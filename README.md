# Dynamic Range Analyzer

> **Dynamic Range Analyzer 1.0.0** is the first public release target. `DRAnalyzer` remains the internal solution/namespace/settings identifier.

Dynamic Range Analyzer is a Windows WPF application for classic Dynamic Range (DR) analysis of music files. It analyzes track DR, album DR, peak and RMS values, reads existing DR metadata, and can safely write/update/remove its own DR fields for explicitly supported formats.

## Core safety rule

Tag operations are allowed to modify **only**:

- `DYNAMIC RANGE`
- `ALBUM DYNAMIC RANGE`

All other metadata, custom tags, ReplayGain/loudness data, embedded artwork and audio payload must remain untouched. Writers are deliberately format-specific and conservative; unsupported or ambiguous structures are rejected rather than rewritten generically.

## Supported formats

| Format | Analysis | DR tag write/update/remove |
|---|---:|---:|
| FLAC | Yes | Yes |
| Opus (`.opus`) | Yes | Yes |
| MP3 | Yes | Yes |
| Ogg Vorbis (`.ogg`) | Yes | Yes |
| M4A (AAC/ALAC in normal MP4/M4A containers) | Yes | Yes |
| WAV | Yes | Yes |
| AIFF / AIF | Yes | Yes |
| APE / Monkey's Audio | Yes | Yes |
| WavPack (`.wv`) | Yes | Yes |
| Raw AAC (`.aac`) | Yes | No |
| WMA | Yes | No |

See [docs/FORMAT_SUPPORT.md](docs/FORMAT_SUPPORT.md) for the intentionally conservative writer limits.

## Analysis behavior

The classic DR calculation is reference-validated against `foo_dr_meter 1.0.8`. The public rounded track DR deliberately preserves the validated single-precision rounding behavior. Album DR is calculated from the mean of the rounded integer track DR values using midpoint rounding away from zero.

Album grouping:

- album metadata present: `AlbumArtist + Album`, falling back to `Artist + Album`;
- album metadata missing: parent directory;
- track number is display metadata only and is never an album-grouping requirement.

## Application behavior

- Add individual files or recursively scan folders.
- Explorer drag & drop.
- Responsive background metadata loading and analysis.
- Cancel at file boundaries; a file currently being analyzed/written is allowed to finish safely.
- Resume analysis in the same loaded session: already successful tracks are skipped; failed tracks are retried.
- System / Light / Dark themes.
- Configurable font.
- Persisted table column widths and order, plus **Reset Table Layout**.
- Metadata errors are collected instead of producing a dialog storm.

## Development

Requirements for the current source tree:

- Windows
- .NET 10 SDK / Windows Desktop workload
- `ffmpeg` and `ffprobe` available via `PATH` for source/development runs; packaged builds use the bundled `runtime\ffmpeg` tools

Build:

```powershell
dotnet build
```

Run:

```powershell
dotnet run --project .\DRAnalyzer.App\DRAnalyzer.App.csproj
```

Portable synthetic test suite:

```powershell
dotnet test .\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj --filter "Category!=ExternalReference"
```

The external-reference/preservation suite intentionally requires local reference music and environment variables. See [docs/TESTING.md](docs/TESTING.md).

## Credits

Dynamic Range Analyzer is developed by **adventureFAN** with extensive development assistance from **OpenAI ChatGPT**, including software architecture, implementation, testing strategy, code review, documentation, metadata-safety hardening and release engineering.
## License

Dynamic Range Analyzer is open source under the [MIT License](LICENSE).

Copyright (c) 2026 adventureFAN.

FFmpeg/ffprobe are separate third-party programs and are **not** covered by the MIT license of Dynamic Range Analyzer. See [docs/THIRD_PARTY.md](docs/THIRD_PARTY.md).

## Windows release packaging

The 1.0.0 Windows release is prepared as a framework-dependent `win-x64` portable ZIP. **Microsoft .NET 10 Desktop Runtime (x64) is required** and is not bundled with the application. Packaged builds include and prefer the bundled `runtime/ffmpeg` tools; source/development runs can still use `ffmpeg` and `ffprobe` from `PATH`. The release-candidate workflow builds FFmpeg 9.0 from the official signed source with GPL/nonfree components disabled and keeps the exact source/signature alongside the candidate artifact.

## Release status

The codebase has undergone multiple pre-release review passes, including metadata ownership, process timeouts, FLAC write preservation validation, exception handling and writer cleanup hardening.

Before a public release, the remaining decisions/tasks are tracked in [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md). The public name, version and source-code license are fixed. Do **not** publish a binary package until the exact FFmpeg/ffprobe runtime build and redistribution package have been finalized and the exact release artifact has passed its final smoke tests.

## Documentation

- [Format support](docs/FORMAT_SUPPORT.md)
- [Metadata safety](docs/METADATA_SAFETY.md)
- [Testing](docs/TESTING.md)
- [Pre-release review](docs/PRE_RELEASE_REVIEW.md)
- [Release checklist](docs/RELEASE_CHECKLIST.md)
- [Third-party/runtime notes](docs/THIRD_PARTY.md)
- [Development handoff](docs/HANDOFF.md)
- [Changelog](CHANGELOG.md)
