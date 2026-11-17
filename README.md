# JunoVoice

Text-to-speech helper for **Juno**, the voice assistant in [Manydocks](https://denishchai.com/manydocks/).

JunoVoice is a small Windows program that turns text into speech with the
[Kokoro](https://huggingface.co/hexgrad/Kokoro-82M) voice through [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx).
The Kokoro voice needs [espeak-ng](https://github.com/espeak-ng/espeak-ng) (GPL-3.0-or-later), so JunoVoice is free
software under the **GNU General Public License v3.0 or later** (see [COPYING.txt](COPYING.txt)).
Manydocks runs JunoVoice as a separate program and talks to it only through standard input and output.

## Build

```
csc /target:exe /platform:x64 /out:JunoVoice.exe JunoVoice.cs
```

Uses the C# compiler of .NET Framework 4.x (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). No other
dependencies.

## Use

```
JunoVoice.exe <voice folder> [threads]
```

`<voice folder>` contains `bin\onnxruntime.dll`, `tts\sherpa-onnx-c-api.dll` (sherpa-onnx 1.13.8, full build) and
`kokoro-en-v0_19\`. Requests, one per line on standard input:

| Request | Reply |
|---|---|
| `WARM` | `OK <load ms>` |
| `SPEAK <speaker> <speed> <base64 UTF-8 text>` | `WAV <bytes>` followed by a WAV file |

Errors are answered with `ERR <message>`. The program exits when standard input closes.

## Voice pack release

The [voice-pack-1](../../releases/tag/voice-pack-1) release mirrors the files Manydocks downloads for Juno, unchanged,
with the same names as their original publishers. Each is checked by SHA-256 inside Manydocks.

| File | Contents | Licence | Original |
|---|---|---|---|
| `sherpa-onnx-v1.13.8-win-x64-shared-MT-Release-no-tts-lib.tar.bz2` | sherpa-onnx (no TTS) + ONNX Runtime | Apache-2.0, MIT | [sherpa-onnx v1.13.8](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8) |
| `sherpa-onnx-v1.13.8-win-x64-shared-MT-Release-lib.tar.bz2` | sherpa-onnx with espeak-ng + ONNX Runtime | Apache-2.0, GPL-3.0-or-later, MIT | [sherpa-onnx v1.13.8](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8) |
| `small.en-encoder.int8.onnx`, `small.en-decoder.int8.onnx`, `small.en-tokens.txt` | Whisper small.en | MIT | [Hugging Face](https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small.en) |
| `kokoro-en-v0_19.tar.bz2` | Kokoro v0.19 + espeak-ng-data | Apache-2.0, GPL-3.0-or-later | [tts-models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models) |
| `sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2` | keyword spotting model | Apache-2.0 | [kws-models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/kws-models) |

### Corresponding source (GPL)

The release also carries the complete source of the GPL parts it distributes, so they stay available with the binaries:

| File | Contents |
|---|---|
| `sherpa-onnx-1.13.8-source.tar.gz` | sherpa-onnx v1.13.8 source |
| `espeak-ng-ed530aa113046142eb5115cf2fc9157854d0ffe1.zip` | the exact espeak-ng revision sherpa-onnx 1.13.8 builds with (`cmake/espeak-ng-for-piper.cmake`, SHA-256 `e4e262cbe34f7fe21f91f1ba3397f2728e1f30eafbae7853f2b753a9ed13f0dd`) |
| `JunoVoice-source.zip` | this repository at the release |

Copyright (c) 2026 Denish Chairany.
