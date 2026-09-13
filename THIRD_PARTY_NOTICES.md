# Third-party notices

Soundswap is MIT-licensed (see LICENSE). It builds on the following work.

## VFXEditor (MIT)

The layout of the game's music files (SCD audio entries, the Vorbis sub-header, the seek table rules, loop
markers) and the de-obscuring table in `src/Soundswap.Core/Scd/ScdXor.cs` follow VFXEditor.

> Copyright 2021 Michael Kaminsky. https://github.com/0ceal0t/Dalamud-VFXEditor
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without
> limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the
> Software, and to permit persons to whom the Software is furnished to do so, subject to the following
> conditions: The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

## Orchestrion (MIT)

The song names in `src/Soundswap/Data/xiv_bgm_*.csv` come from the Orchestrion plugin's song list, used when
Orchestrion itself isn't installed. https://github.com/perchbirdd/OrchestrionPlugin (MIT, originally by Meli).

## Libraries

| Library | Licence | Used for |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | MIT | WAV/AIFF/MP3 reading, resampling, Media Foundation decoding, WASAPI previews |
| [NVorbis](https://github.com/NVorbis/NVorbis) | MIT | Ogg Vorbis decoding |
| [NLayer](https://github.com/naudio/NLayer) | MIT | MP3 decoding |
| [OggVorbisEncoder](https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder) | MIT | Ogg Vorbis encoding when the native encoder is missing (stereo only) |
| [libogg](https://github.com/xiph/ogg) 1.3.5 and [libvorbis](https://github.com/xiph/vorbis) 1.3.7 | BSD-3-Clause, Xiph.Org Foundation | Ogg Vorbis encoding and decoding, compiled into soundswap_vorbis.dll by tools/build-vorbis.cmd |

Copyright (c) 2002-2020 Xiph.org Foundation. Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the conditions of the BSD 3-Clause licence are met: source
redistributions retain this notice; binary redistributions reproduce it in the documentation; neither the name of
the Xiph.org Foundation nor its contributors may be used to endorse products derived from this software without
permission. The software is provided "as is", without warranty of any kind.
