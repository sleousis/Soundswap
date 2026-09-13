/*
 * soundswap_vorbis: a one-call Ogg Vorbis encoder for Soundswap, linking the reference libogg and libvorbis
 * (Xiph.Org, BSD licence) statically. One function crosses into .NET, with plain pointers and two callbacks,
 * so no library structure layout ever has to be mirrored in C#.
 *
 * Header pages and audio pages are reported apart (the game stores them apart), and a page is closed every
 * `page_every_blocks` blocks of 1024 frames so loop points and seeking stay precise.
 */
#include <string.h>
#include <vorbis/vorbisenc.h>
#include <vorbis/vorbisfile.h>

#define SW_API __declspec(dllexport)
#define SW_CALL __cdecl

/* Receives page bytes. Return non-zero to abort. */
typedef int (SW_CALL *sw_write_fn)(const unsigned char *data, int length, int is_header, void *user);
/* Receives progress 0..1. Return non-zero to cancel. */
typedef int (SW_CALL *sw_progress_fn)(float fraction, void *user);

enum { SW_OK = 0, SW_BAD_SETUP = -1, SW_WRITE_FAILED = -2, SW_CANCELLED = -3, SW_BAD_ARGS = -4 };

/* Receives decoded planar floats. Return non-zero to cancel. */
typedef int (SW_CALL *sw_pcm_fn)(const float *const *pcm, int channels, int frames, void *user);

/*
 * 2: sw_decode. The reference decoder handles every channel layout, where managed decoders disagree on some.
 * 3: six channels are encoded uncoupled, never with the 5.1 template's low-pass subwoofer channel.
 */
SW_API int SW_CALL sw_version(void) { return 3; }

typedef struct
{
    const unsigned char *data;
    long long length;
    long long position;
} memory_file;

static size_t memory_read(void *ptr, size_t size, size_t count, void *source)
{
    memory_file *f = (memory_file *)source;
    long long left = f->length - f->position;
    long long want = (long long)(size * count);
    if (want > left) want = left;
    if (want <= 0) return 0;
    memcpy(ptr, f->data + f->position, (size_t)want);
    f->position += want;
    return (size_t)(want / (long long)size);
}

static int memory_seek(void *source, ogg_int64_t offset, int whence)
{
    memory_file *f = (memory_file *)source;
    long long to = whence == SEEK_SET ? offset : whence == SEEK_CUR ? f->position + offset : f->length + offset;
    if (to < 0 || to > f->length) return -1;
    f->position = to;
    return 0;
}

static long memory_tell(void *source) { return (long)((memory_file *)source)->position; }

/*
 * Decodes an Ogg Vorbis stream held in memory. Reports the layout first through the out parameters, then the
 * audio in chunks. Returns 0, SW_BAD_ARGS for bad input, SW_BAD_SETUP when it isn't Vorbis, SW_CANCELLED.
 */
SW_API int SW_CALL sw_decode(const unsigned char *data, long long length, int *channels, int *rate, long long *total_frames,
                             sw_pcm_fn pcm, void *user)
{
    OggVorbis_File vf;
    memory_file file;
    ov_callbacks callbacks;
    vorbis_info *info;
    float **buffer;
    int section = 0, result = SW_OK;
    long n;

    if (!data || length <= 0 || !channels || !rate || !total_frames || !pcm) return SW_BAD_ARGS;
    file.data = data;
    file.length = length;
    file.position = 0;
    callbacks.read_func = memory_read;
    callbacks.seek_func = memory_seek;
    callbacks.close_func = NULL;
    callbacks.tell_func = memory_tell;
    if (ov_open_callbacks(&file, &vf, NULL, 0, callbacks) < 0) return SW_BAD_SETUP;

    info = ov_info(&vf, -1);
    *channels = info->channels;
    *rate = (int)info->rate;
    *total_frames = ov_pcm_total(&vf, -1);

    while ((n = ov_read_float(&vf, &buffer, 4096, &section)) != 0)
    {
        if (n < 0) continue; /* a hole in the data: skip it, as players do */
        if (ov_info(&vf, section)->channels != *channels) break; /* a chained stream with another layout: stop */
        if (pcm((const float *const *)buffer, *channels, (int)n, user)) { result = SW_CANCELLED; break; }
    }
    ov_clear(&vf);
    return result;
}

static int write_page(const ogg_page *page, int is_header, sw_write_fn write, void *user)
{
    if (write(page->header, (int)page->header_len, is_header, user)) return 1;
    if (write(page->body, (int)page->body_len, is_header, user)) return 1;
    return 0;
}

SW_API int SW_CALL sw_encode(const float *const *channels, int channel_count, long long frames, int rate, float quality,
                             const char *const *comments, int comment_count, int page_every_blocks,
                             sw_write_fn write, sw_progress_fn progress, void *user)
{
    vorbis_info vi;
    vorbis_comment vc;
    vorbis_dsp_state vd;
    vorbis_block vb;
    ogg_stream_state os;
    ogg_page og;
    ogg_packet op, header, header_comments, header_books;
    const int chunk = 1024;
    long long pos = 0;
    int result = SW_OK, eos = 0, ended = 0, blocks = 0, i, c;

    if (!channels || channel_count < 1 || channel_count > 255 || frames < 0 || rate < 8000 || !write) return SW_BAD_ARGS;

    vorbis_info_init(&vi);
    if (vorbis_encode_setup_vbr(&vi, channel_count, rate, quality) != 0)
    {
        vorbis_info_clear(&vi);
        return SW_BAD_SETUP;
    }
    /*
     * A six-channel stream would otherwise get libvorbis' 5.1 template, which codes the sixth channel as a
     * subwoofer (LFE) with almost no treble. The game's layered songs are three stereo layers, so layer 3's right
     * side would lose its high end. With coupling off, only the uncoupled templates match: every channel full
     * bandwidth, like the game's own files. Stereo keeps its coupled template.
     */
    if (channel_count != 2)
    {
        int coupling = 0;
        vorbis_encode_ctl(&vi, OV_ECTL_COUPLING_SET, &coupling);
    }
    if (vorbis_encode_setup_init(&vi) != 0)
    {
        vorbis_info_clear(&vi);
        return SW_BAD_SETUP;
    }
    vorbis_comment_init(&vc);
    for (i = 0; i < comment_count; i++)
        if (comments && comments[i]) vorbis_comment_add(&vc, comments[i]);

    vorbis_analysis_init(&vd, &vi);
    vorbis_block_init(&vd, &vb);
    ogg_stream_init(&os, 0x53574150); /* "SWAP": the same input always gives the same bytes */

    vorbis_analysis_headerout(&vd, &vc, &header, &header_comments, &header_books);
    ogg_stream_packetin(&os, &header);
    ogg_stream_packetin(&os, &header_comments);
    ogg_stream_packetin(&os, &header_books);
    while (ogg_stream_flush(&os, &og))
    {
        if (write_page(&og, 1, write, user)) { result = SW_WRITE_FAILED; goto done; }
    }

    while (!eos)
    {
        if (!ended)
        {
            long long n = frames - pos;
            if (n > chunk) n = chunk;
            if (n <= 0)
            {
                vorbis_analysis_wrote(&vd, 0);
                ended = 1;
            }
            else
            {
                float **buffer = vorbis_analysis_buffer(&vd, (int)n);
                for (c = 0; c < channel_count; c++) memcpy(buffer[c], channels[c] + pos, (size_t)n * sizeof(float));
                vorbis_analysis_wrote(&vd, (int)n);
                pos += n;
            }
        }

        while (vorbis_analysis_blockout(&vd, &vb) == 1)
        {
            vorbis_analysis(&vb, NULL);
            vorbis_bitrate_addblock(&vb);
            while (vorbis_bitrate_flushpacket(&vd, &op))
            {
                ogg_stream_packetin(&os, &op);
                while (!eos && ogg_stream_pageout(&os, &og))
                {
                    if (write_page(&og, 0, write, user)) { result = SW_WRITE_FAILED; goto done; }
                    if (ogg_page_eos(&og)) eos = 1;
                }
            }
        }

        if (!eos && page_every_blocks > 0 && ++blocks % page_every_blocks == 0)
        {
            while (!eos && ogg_stream_flush(&os, &og))
            {
                if (write_page(&og, 0, write, user)) { result = SW_WRITE_FAILED; goto done; }
                if (ogg_page_eos(&og)) eos = 1;
            }
        }

        if (progress && progress(frames > 0 ? (float)((double)pos / (double)frames) : 1.0f, user)) { result = SW_CANCELLED; goto done; }

        if (ended && !eos)
        {
            /* Everything is analysed: whatever is still pending goes out now. */
            while (!eos && ogg_stream_flush(&os, &og))
            {
                if (write_page(&og, 0, write, user)) { result = SW_WRITE_FAILED; goto done; }
                if (ogg_page_eos(&og)) eos = 1;
            }
            break;
        }
    }

done:
    ogg_stream_clear(&os);
    vorbis_block_clear(&vb);
    vorbis_dsp_clear(&vd);
    vorbis_comment_clear(&vc);
    vorbis_info_clear(&vi);
    return result;
}
