using OggVorbisEncoder;

namespace Soundswap.Core.Audio;

/// <summary>
/// The fallback when soundswap_vorbis.dll isn't there: OggVorbisEncoder, a managed port of libvorbis. It has
/// setup templates for stereo only, and it swallows the first 1024 frames it is given, so 1024 frames of
/// silence go in first and the song comes out whole and on time.
/// </summary>
internal static class ManagedVorbis
{
    private const int Serial = 0x53574150; // "SWAP"
    private const int SwallowedFrames = 1024;

    public static EncodedVorbis Encode(float[][] channels, int sampleRate, float quality, IReadOnlyDictionary<string, string>? tags,
        Action<float>? progress, CancellationToken ct)
    {
        if (channels.Length != 2) throw new NotSupportedException("The managed Vorbis encoder only handles stereo.");
        var frames = channels[0].Length;

        var info = VorbisInfo.InitVariableBitRate(2, sampleRate, quality);
        var stream = new OggStream(Serial);
        var comments = new Comments();
        comments.AddTag("ENCODER", "Soundswap");
        if (tags is not null)
        {
            foreach (var (key, value) in tags) comments.AddTag(key, value);
        }
        stream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        stream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        stream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));

        using var header = new MemoryStream();
        while (stream.PageOut(out var page, true)) Write(header, page);

        var state = ProcessingState.Create(info);
        using var audio = new MemoryStream(frames / 2);
        const int block = 1024;
        var buffer = new[] { new float[block], new float[block] };
        var total = frames + SwallowedFrames;
        var blocks = 0;
        for (var pos = 0; pos < total; pos += block)
        {
            ct.ThrowIfCancellationRequested();
            var n = Math.Min(block, total - pos);
            for (var c = 0; c < 2; c++)
            {
                Array.Clear(buffer[c]);
                var from = pos - SwallowedFrames;
                var start = Math.Max(0, -from);
                var count = Math.Min(n, frames - Math.Max(0, from)) - start;
                if (count > 0) Array.Copy(channels[c], Math.Max(0, from), buffer[c], start, count);
            }
            state.WriteData(buffer, n, 0);
            Drain(state, stream, audio, false);
            if (++blocks % VorbisEncoder.PageEveryBlocks == 0)
            {
                while (stream.PageOut(out var page, true)) Write(audio, page);
            }
            if (blocks % 64 == 0) progress?.Invoke((float)(pos + n) / total);
        }

        state.WriteEndOfStream();
        Drain(state, stream, audio, true);
        progress?.Invoke(1f);
        return new EncodedVorbis(header.ToArray(), audio.ToArray(), 2, sampleRate, frames);
    }

    private static void Drain(ProcessingState state, OggStream stream, Stream output, bool final)
    {
        // The stream counts as finished once the end-of-stream packet is in, before its pages are out, so the
        // last round must not stop on Finished or it drops the tail of the song.
        while (state.PacketOut(out var packet))
        {
            stream.PacketIn(packet);
            while (stream.PageOut(out var page, false)) Write(output, page);
        }
        if (!final) return;
        while (stream.PageOut(out var page, true)) Write(output, page);
    }

    private static void Write(Stream output, OggPage page)
    {
        output.Write(page.Header, 0, page.Header.Length);
        output.Write(page.Body, 0, page.Body.Length);
    }
}
