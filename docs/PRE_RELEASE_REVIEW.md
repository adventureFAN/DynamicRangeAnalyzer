# Pre-Release Code Review

Review basis: the user's full source snapshot `DRAnalyzer-current-2026-08-17-0641.zip`, SHA-256:

```text
ed872e51dfd76dfb475cf484e2c8faf981298dbab937906d903a80a9cf684d88
```

The user reported the Stage 1-3 build/test checkpoints as green on the Windows development machine. Stage 4 was subsequently applied there as well: the clean build checkpoint and the portable synthetic test suite (`Category!=ExternalReference`) were both confirmed green by the user. The review environment used to prepare these files did not itself run .NET, so those green results are explicitly user-side Windows results, not claims about the review container.

## Review Pass 1

Addressed:
- removed development analysis diagnostics file generation;
- separated exact owned DR fields from compatible foreign aliases;
- made recursive folder scanning more tolerant of inaccessible subfolders;
- aggregated metadata errors;
- corrected write/remove completion wording;
- removed empty Visual Studio template files.

## Review Pass 2

Addressed:
- M4A temporary output flush before replacement;
- simultaneous stdout/stderr draining for ffprobe paths;
- empty but exact owned DR fields remain removable;
- external/reference tests no longer silently succeed when required environment data is missing.

## Review Pass 3

Addressed:
- FLAC write-path preservation validation hardened to the same safety philosophy as remove;
- global WPF fatal exception fallback added;
- ffmpeg/ffprobe watchdogs added;
- reference-sensitive single-precision DR rounding documented and protected by regression test.

A proposed ID3 TXXX parser refactor was deliberately deferred. MP3/WAV/AIFF have separately proven writer implementations; deduplicating them immediately before release would increase the regression radius without changing user-visible behavior.

## Review Pass 4

**Status: green on the Windows development machine.**

Confirmed after applying Stage 4:
- clean build: green;
- portable synthetic test suite (`Category!=ExternalReference`): green.

Stage 4 changes:
- writer temp/backup deletion is now best-effort cleanup;
- a cleanup failure after successful `File.Replace` can no longer masquerade as a failed tag operation;
- if `File.Replace` fails, its backup is intentionally not removed;
- all tests that depend on local/private reference assets are categorized `ExternalReference`;
- public/release/developer documentation and `.gitignore` added.

## No new metadata-corruption finding in Pass 4

The Pass 4 audit did not identify a new code path that intentionally bypasses the format-specific writer validation/replacement model or modifies foreign metadata fields.

## Release identity / legal decision after Pass 4

The following release decisions were made after Pass 4:

- final public product name: **Dynamic Range Analyzer**;
- first public version: **1.0.0**;
- source-code license: **MIT**;
- copyright holder text: **Copyright (c) 2026 adventureFAN**;
- planned repository: `https://github.com/adventureFAN/DynamicRangeAnalyzer`;
- FFmpeg/ffprobe will remain clearly separate third-party executables; the release target is an FFmpeg configuration without GPL or nonfree components, so the exact runtime can remain under FFmpeg's LGPL terms.

The exact FFmpeg binary build is still a release blocker and must be recorded/tested before publishing a binary package.

## Remaining release work

The following are intentional open decisions, not forgotten findings:

1. Produce and verify the exact distributable FFmpeg/ffprobe runtime build and corresponding source/build information.
2. Final package choice/details and clean-machine test.
3. Decide whether `%LocalAppData%\DRAnalyzer` should remain as the stable internal settings identifier (preferred for 1.0 to avoid migration risk).
4. [x] English error-surface pass: analysis/ffprobe messages are English and format-specific writer exceptions are normalized before they reach the GUI. Low-level parser safety text may remain internal without surfacing to users.
5. Add final repository/project links to About/Help once the repository exists.
6. Final release archive/source hygiene verification and checksum.
7. Optional future cleanup: shared ID3v2 TXXX parser only after a dedicated regression campaign.

Raw AAC and WMA tag writing are intentionally out of scope unless future format-specific preservation work justifies enabling them.
