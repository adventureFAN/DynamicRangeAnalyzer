# Testing

## Build checkpoint

From the repository root:

```powershell
dotnet build --no-incremental
```

The project workflow treats a build as "green" only when it completes with **0 warnings and 0 errors**.

## Portable synthetic suite

Tests that require private/local reference music are categorized as `ExternalReference`.

Run the portable synthetic suite with:

```powershell
dotnet test .\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj `
    --filter "Category!=ExternalReference"
```

This is the appropriate starting point for a clean development machine or future CI that does not have access to the private reference corpus.

## External-reference / preservation suite

Run the external suite explicitly with:

```powershell
dotnet test .\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj `
    --filter "Category=ExternalReference"
```

These tests intentionally **fail loudly** when their required environment variable or reference file/folder is missing. They must not silently report success without performing the intended preservation/reference check.

### Environment variables referenced by the current tests

FLAC:
- `DRANALYZER_REFERENCE_DISCOVERY_FLAC_DIR`
- `DRANALYZER_MANUAL_FLAC_ORIGINAL`
- `DRANALYZER_MANUAL_FLAC_COPY`
- `DRANALYZER_MANUAL_FLAC_ALBUM_ORIGINAL`
- `DRANALYZER_MANUAL_FLAC_ALBUM_COPY`

Opus:
- `DRANALYZER_REFERENCE_LEDZEPPELIN4_DIR`
- `DRANALYZER_REFERENCE_TOOL_DIR`
- `DRANALYZER_MANUAL_OPUS_FILE`
- `DRANALYZER_MANUAL_OPUS_ORIGINAL`
- `DRANALYZER_MANUAL_OPUS_COPY`
- `DRANALYZER_MANUAL_OPUS_ORIGINAL_SHA256`
- `DRANALYZER_MANUAL_OPUS_ALBUM_ORIGINAL`
- `DRANALYZER_MANUAL_OPUS_ALBUM_COPY`

MP3:
- `DRANALYZER_REFERENCE_INVINCIBLE_MP3_DIR`

Ogg Vorbis:
- `DRANALYZER_REFERENCE_DISCOVERY_OGG_DIR`

M4A:
- `DRANALYZER_REFERENCE_M4A_DIR`

WAV:
- `DRANALYZER_REFERENCE_WAV_DIR`

AIFF:
- `DRANALYZER_REFERENCE_AIFF_DIR`

APE:
- `DRANALYZER_REFERENCE_APE_DIR`
- `DRANALYZER_REFERENCE_DISCOVERY_APE_DIR`

WavPack:
- `DRANALYZER_REFERENCE_WAVPACK_DIR`

## Reference DR policy

The classic DR implementation is validated against:

```text
foo_dr_meter 1.0.8
```

Do not change the algorithm merely to match pre-existing tags in arbitrary music files. Newly calculated reference results are the truth source for this project.

The public rounded track DR deliberately includes a single-precision step before rounding; a regression test protects that boundary behavior.

Album DR is the arithmetic mean of rounded integer track DR values, rounded with `MidpointRounding.AwayFromZero`.

## Runtime regression checklist

Before a public release, test at minimum:

- Load individual files.
- Add a recursively scanned folder.
- Drag and drop files/folders from non-elevated Explorer.
- Large collection load (hundreds/thousands of files), watching RAM and UI responsiveness.
- Analyze complete collection.
- Cancel analysis and resume in the same loaded session.
- Metadata-error aggregation with a deliberately unreadable/broken file.
- Remove selected rows and verify album DR recalculation.
- Write / update / remove on test copies for every writer-enabled format.
- Verify foreign metadata and embedded artwork remain intact.
- Verify a failed/unsupported structure leaves the source unchanged.
- Verify themes, custom font, table layout persistence and Reset Table Layout.
- Verify About dialog/icon/version display.
- Verify app close is blocked while a safe file-boundary operation is still active.

## Process watchdogs

The production analyzer currently protects external processes with:

- ffprobe: 30 second timeout per metadata/audio-info query;
- ffmpeg analysis: 60 minute timeout per individual file.

Timeouts are a last-resort hang guard. They are not the user Cancel mechanism; Cancel remains file-boundary-safe.
