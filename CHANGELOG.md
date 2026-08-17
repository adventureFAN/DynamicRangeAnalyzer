# Changelog

All notable changes to the project are documented here.

The first public release is **1.0.0**, published on 2026-08-17.

## [1.0.0] - 2026-08-17

### Analysis
- Classic Dynamic Range analysis with track DR, peak and RMS.
- Album DR calculated from rounded integer track DR values.
- DR calculation reference-validated against `foo_dr_meter 1.0.8`.
- Analysis support for FLAC, WAV, MP3, Ogg Vorbis, Opus, M4A/AAC, AIFF/AIF, WMA, APE and WavPack.
- Per-file FFmpeg/ffprobe watchdogs to prevent indefinite hangs.
- Same-session resume after cancellation: successful analyzed tracks are skipped, failed tracks are retried.

### Metadata and album handling
- Reads common artist, album, album artist, title and track metadata.
- Existing compatible DR aliases can be displayed without being treated as fields owned by this application.
- Ownership is restricted to the exact fields `DYNAMIC RANGE` and `ALBUM DYNAMIC RANGE`.
- Album grouping uses AlbumArtist/Artist + Album, with parent-directory fallback when album metadata is missing.
- Track number is displayed independently and is never required for grouping.

### Safe DR tag operations
- Write/update/remove support for FLAC, Opus, MP3, Ogg Vorbis, M4A, WAV, AIFF/AIF, APE and WavPack.
- Format-specific writers preserve foreign metadata, artwork/container data and audio payload according to each format's tested safety model.
- Temporary copies are validated before the source file is replaced.
- Replacement uses a same-directory temporary file and transient backup.
- Cleanup of temporary artifacts is best-effort and can no longer turn an already successful replacement into a false write failure.
- Raw AAC and WMA remain intentionally analysis-only for tag operations.

### User interface
- Windows WPF GUI with file/folder loading and drag & drop.
- Responsive background loading with progress reporting.
- File-boundary-safe cancel behavior.
- System, Light and Dark themes.
- Configurable font.
- Persisted table column widths/order and reset-to-default command.
- Compact DR/peak/RMS columns and flexible Artist/Album/Title columns.
- Dedicated About dialog with application icon and format/safety information.
- Primary Analyze/Cancel action styling.
- Collected metadata/read errors rather than one dialog per bad file.

### Robustness and review hardening
- Global fatal WPF exception fallback with controlled shutdown.
- ffmpeg and ffprobe output/error pipes drained safely.
- Writer ownership distinguishes exact application-owned fields from compatible foreign aliases.
- FLAC write validation now applies preservation checks before replacement, not only value checks.
- M4A temporary output is flushed to disk before replacement.
- Reference/manual tests fail clearly when required reference data is missing.
- External-reference tests are categorized as `ExternalReference` so a portable synthetic suite can be run independently.
- Removed development diagnostics and unused template source files.


### Project identity / licensing
- Final public product name set to **Dynamic Range Analyzer**.
- First public version metadata set to **1.0.0**.
- Source code licensed under the MIT License.
- Copyright notice set to `Copyright (c) 2026 adventureFAN`.
- Public repository: `adventureFAN/DynamicRangeAnalyzer`.
- FFmpeg/ffprobe explicitly documented as separately licensed third-party runtime components; the exact distributable runtime build is produced and reviewed as part of the release workflow.


### User-facing error language
- Normalized the complete GUI error surface to English without changing format-specific writer/parser logic.
- Analysis and metadata FFmpeg/ffprobe failures now use English messages.
- Low-level tagging safety rejections are presented as clear English categories while the conservative format-specific checks remain untouched.
- Fatal error UI no longer exposes raw OS/localized exception text.

### Release packaging automation
- Added deterministic bundled-runtime discovery with PATH fallback for development.
- Added a pinned .NET 10.0.400 `global.json`.
- Added GitHub Actions CI for Release build + portable tests.
- Added manual release workflow that builds FFmpeg 9.0 from its signed official source with GPL/nonfree components disabled, then creates a framework-dependent Windows x64 portable ZIP requiring Microsoft .NET 10 Desktop Runtime (x64).
- Release workflow publishes candidate artifacts only; public publication follows review and smoke-testing of the exact generated artifact.

### Documentation / source hygiene
- Added README, changelog, testing, format support, metadata safety, pre-release review, release checklist, third-party notes and development handoff.
- Added `.gitignore` for build/test/editor artifacts and local review backups.
