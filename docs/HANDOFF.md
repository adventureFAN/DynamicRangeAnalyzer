# DRAnalyzer / Dynamic Range Analyzer – Living Handoff

**Stand:** 2026-08-17, Stage 5C green / Stage 5D packaging automation prepared  
**Public product name:** not final  
**Final public product name:** `Dynamic Range Analyzer`  
**First public version:** `1.0.0`  
**Internal solution/namespaces/settings identifier:** `DRAnalyzer`
**Source license:** `MIT`  
**Copyright:** `Copyright (c) 2026 adventureFAN`  
**Planned GitHub repository:** `https://github.com/adventureFAN/DynamicRangeAnalyzer`  

This file is the development continuity document. Public-facing documentation is in the repository root and the other files under `docs/`.

---

## 1. Source of truth

Pass-4 review input was the user's complete snapshot:

```text
DRAnalyzer-current-2026-08-17-0641.zip
SHA-256:
ed872e51dfd76dfb475cf484e2c8faf981298dbab937906d903a80a9cf684d88
```

It contained 85 relevant source/assets/test files and no `bin`, `obj`, `.vs`, `.git` or local backup folders.

The new post-Stage-4 source-of-truth snapshot is:

```text
DRAnalyzer-current-pre-release-stage4-green-2026-08-17.zip
```

It is reconstructed from the complete `0641` snapshot plus the exact Stage-4 replacement that the user then built and portable-tested successfully. Its SHA-256 is distributed as a companion checksum file rather than embedded here, because embedding a ZIP's own checksum inside that ZIP would change the checksum.

That snapshot already included the successfully built/tested Stage 1-3 review changes:
- metadata ownership correction;
- diagnostics removal;
- robust folder/metadata-error handling;
- M4A flush-to-disk;
- ffprobe pipe handling;
- global fatal exception fallback;
- ffmpeg/ffprobe watchdogs;
- strict FLAC write preservation validation;
- public DR single-precision rounding regression test.

**Stage 4 was applied on the Windows development machine and the user confirmed both the clean build checkpoint and the portable synthetic test suite as green.** A new complete clean source snapshot is generated from that exact Stage-4 code/document set and is the next source of truth.

Known development environment at review time:
- Windows 11 x64
- .NET 10 project (`net10.0`, WPF app `net10.0-windows`)
- known user SDK/runtime during development: SDK 10.0.400, runtime 10.0.11
- project path on the development PC: `C:\Users\Alex\DRAnalyzer`

Run:

```powershell
Set-Location "$HOME\DRAnalyzer"
dotnet run --project ".\DRAnalyzer.App\DRAnalyzer.App.csproj"
```

Build:

```powershell
Set-Location "$HOME\DRAnalyzer"
dotnet build --no-incremental
```

"Grün" / "tiefgrün" in this project means the user observed a successful requested build/test with 0 warnings and 0 errors where applicable. Do not claim a build was run in an environment that cannot run .NET.

---

## 1A. Release identity / legal status

Stage 5A was applied on the Windows development machine and the user confirmed the clean build as green.

Final public identity:
- product: **Dynamic Range Analyzer**
- version: **1.0.0**
- executable/assembly name: `DynamicRangeAnalyzer`
- internal namespaces/project/settings identifier remain `DRAnalyzer`

Stage 5B establishes:
- source-code license: MIT;
- copyright notice: `Copyright (c) 2026 adventureFAN`;
- planned repository: `adventureFAN/DynamicRangeAnalyzer`;
- commit identity should use GitHub handle `adventureFAN` and the user's GitHub noreply address locally; do not publish private email addresses in source merely for commit attribution;
- FFmpeg remains separately licensed third-party software.

FFmpeg release direction:
- bundle separate `ffmpeg.exe` / `ffprobe.exe` for usability;
- do not copy the development FFmpeg binaries blindly;
- target a verified build without `--enable-gpl` and without `--enable-nonfree`;
- record exact version/config/build scripts/source availability and include applicable notices;
- binary release remains blocked until that exact runtime is built and tested.


## 1B. Stage 5C - English error surface

Prepared after Stage 5B green:
- no format writer/parser logic is changed;
- `DynamicRangeAnalyzer` and `AudioMetadataReader` user-relevant FFmpeg/ffprobe messages are English;
- `DRAnalyzer.App.UserFacingErrorFormatter` prevents low-level German parser/safety exception text from leaking into the English GUI;
- tagging failures are intentionally summarized by exception category (unsupported structure, safety validation rejection, I/O/access error) rather than mechanically rewriting hundreds of proven parser strings;
- fatal-error UI no longer displays raw potentially localized exception messages.

Stage 5C still requires Alex's Windows clean build and runtime smoke test before it can be marked green.


## 1B. Stage 5C / Stage 5D release packaging status

Stage 5C is green on the Windows development machine after the one-line `System.IO` compile fix. The user confirmed the final mixed runtime smoke test: load/analyze, write/update, repeat update, remove, foreign metadata preservation, and About showing Dynamic Range Analyzer 1.0.0.

Stage 5D prepares deterministic release packaging without changing the DR algorithm or any format writer:
- `ExternalToolLocator` first looks for `runtime/ffmpeg/ffmpeg.exe` and `ffprobe.exe` beside the packaged application; if absent it falls back to PATH for development.
- `global.json` pins .NET SDK 10.0.400.
- `.github/workflows/ci.yml` builds and runs the portable non-ExternalReference suite on Windows.
- `.github/workflows/build-release-package.yml` builds a release candidate only on explicit manual dispatch.
- `scripts/build-ffmpeg-runtime.sh` builds FFmpeg 9.0 from the official signed source tarball using MinGW-w64, without GPL/nonfree components and without auto-detected external libraries.
- `scripts/package-windows.ps1` publishes Dynamic Range Analyzer self-contained for `win-x64`, adds the verified FFmpeg runtime and license/source notices, produces a portable ZIP and SHA-256 checksum.
- The workflow deliberately creates an Actions artifact only; it does **not** publish a GitHub Release automatically. The exact artifact must first pass the clean-machine test.

Binary release remains blocked until the GitHub Actions release candidate is green and that exact downloaded artifact passes the clean-machine/package smoke test.

## 2. Absolute safety rule

DR tag operations may alter **only** these exact owned fields:

```text
DYNAMIC RANGE
ALBUM DYNAMIC RANGE
```

Never modify:
- Title
- Artist
- Album
- Album Artist
- Track / Disc numbers
- Genre
- Date / Year
- Composer
- ReplayGain / loudness fields
- comments
- arbitrary custom tags
- embedded artwork
- unrelated container metadata
- audio payload

Read compatibility is broader than ownership. `DR`, `DYNAMIC_RANGE`, `ALBUM DR`, `ALBUMDR`, etc. may be read/displayed, but they are not application-owned and must not be removed merely because they are aliases.

When safety and convenience conflict, refuse to write.

---

## 3. DR algorithm – do not casually change

Reference target is exactly:

```text
foo_dr_meter 1.0.8
```

The core was validated across a broad reference corpus (historically 119/119 exact on the established cross-format set). Do not "fix" the algorithm because an old embedded DR tag disagrees.

Track public rounding is deliberately:

1. internal DR as `double`;
2. reduce to `float`;
3. `+ 0.5f`, truncate to int.

This apparently odd sequence is reference-sensitive. A regression test protects a boundary where direct `Math.Round(double, AwayFromZero)` would differ.

Album DR:
- use each track's rounded integer DR;
- arithmetic mean;
- `Math.Round(..., MidpointRounding.AwayFromZero)`.

Known AAC/M4A reference nuance from development: a direct M4A decode can disagree by one DR unit with another decoder path while the same decoded PCM reproduces the analyzer result. Do not modify the core merely to force that one container/decoder discrepancy to match an old tag.

---

## 4. Current architecture

```text
DRAnalyzer/
├── DRAnalyzer.slnx
├── README.md
├── CHANGELOG.md
├── .gitignore
├── docs/
│   ├── FORMAT_SUPPORT.md
│   ├── HANDOFF.md
│   ├── METADATA_SAFETY.md
│   ├── PRE_RELEASE_REVIEW.md
│   ├── RELEASE_CHECKLIST.md
│   ├── TESTING.md
│   └── THIRD_PARTY.md
├── DRAnalyzer.App/
│   ├── App.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   ├── AboutWindow.xaml(.cs)
│   ├── FontSettingsWindow.xaml(.cs)
│   ├── Assets/
│   ├── Models/
│   └── Settings/
├── DRAnalyzer.Core/
│   ├── Analysis/
│   ├── Metadata/
│   ├── Models/
│   ├── Processes/
│   └── Tagging/
└── DRAnalyzer.Tests/
```

Core responsibilities:
- `DynamicRangeAnalyzer`: ffprobe stream info + ffmpeg f64le PCM analysis.
- `DynamicRangeResult`: calculated result and reference-sensitive public rounding.
- `AlbumDynamicRangeCalculator`: album mean/rounding.
- `AlbumGroupingKey`: metadata album grouping + parent-directory fallback.
- `AudioMetadataReader`: ffprobe metadata parsing and compatible DR aliases.
- `DynamicRangeTagWriter`: format facade.
- individual `*DynamicRangeTagWriter`: format-specific safe writing/removal.
- `ProcessTimeoutGuard`: last-resort external-process watchdog.
- `WriterFileCleanup` (Stage 4): best-effort temp/backup cleanup that must not mask the actual writer result.

GUI should remain comparatively thin; Core should remain reusable for a possible future plugin.

---

## 5. Current format support

### Analysis

Supported by the GUI:
- FLAC
- WAV
- MP3
- Ogg Vorbis
- Opus
- M4A / AAC in M4A
- raw AAC
- AIFF / AIF
- WMA
- APE
- WavPack `.wv`

### Write / Update / Remove

GUI-freed writer formats:
- FLAC
- Opus
- MP3
- Ogg Vorbis
- M4A
- WAV
- AIFF / AIF
- APE
- WavPack

Intentionally analysis-only:
- raw `.aac`
- WMA

Do not enable new writer formats through the facade until they have format-specific synthetic + real-file preservation verification.

Detailed limits are in `docs/FORMAT_SUPPORT.md`.

---

## 6. Writer design / safety

Common pattern:
- inspect original structure;
- generate a same-directory unique `.dranalyzer.tmp`;
- validate generated copy;
- `File.Replace(..., backupPath, ignoreMetadataErrors: true)`;
- only after successful replacement, best-effort delete temp/transient backup;
- if `File.Replace` fails, do not delete its backup.

Stage 4 specifically fixes a subtle reporting issue: before this change, `File.Delete(backupPath)` in a `finally` could throw after a successful replacement and make the GUI report "Write error" even though the file had already been safely changed.

Format notes:

FLAC:
- native FLAC metadata blocks;
- only Vorbis comment owned fields;
- vendor/foreign comments/other blocks/audio preserved;
- strict write and remove validation.

Opus:
- direct Ogg/OpusTags;
- vendor/foreign comments/audio packets preserved;
- page sequence/CRC managed explicitly.

MP3:
- conservative ID3v2.3/v2.4 TXXX;
- unsafe/special ID3 structures rejected;
- minimal supported tag can be created for tagless MP3;
- write→remove can restore a previously tagless payload when only application-created ID3 data remains.

Ogg Vorbis:
- Vorbis comment packet only;
- correct header granule-position rules;
- setup/audio preserved.

M4A:
- direct MP4 atom editing, not remux;
- iTunes-style freeform `----` fields;
- normal existing metadata layout required;
- chunk offsets adjusted in supported layouts;
- fragmented/exotic/ambiguous structures rejected.

WAV:
- classic RIFF/WAVE only;
- ID3 chunk with ID3v2.3/v2.4 TXXX;
- RF64/BW64/RIFX not writer-supported;
- 4 GiB boundary enforced.

AIFF:
- classic FORM/AIFF, not AIFC;
- ID3v2.3/v2.4 chunk;
- known real-world zero padding after ID3 payload supported conservatively;
- 4 GiB boundary enforced.

APE:
- APEv2;
- APEv1 rejected;
- foreign text/binary items preserved;
- validated support for a known contradictory legacy footer flag.

WavPack:
- validates WavPack blocks and version 0x0402–0x0410;
- APEv2 metadata tail;
- trailing ID3v1 preserved;
- APEv1 rejected.

Do not perform a broad ID3 parser deduplication immediately before release. MP3/WAV/AIFF were separately proven; a shared-parser refactor is deferred until it can receive its own regression campaign.

---

## 7. GUI state

Visible UI language: English.

Main window:
- title: `Dynamic Range Analyzer`;
- application icon: chosen dark rounded-square DR bars + magnifying glass icon;
- System / Light / Dark native WPF theme;
- configurable font;
- non-elevated launch is important for normal Explorer drag & drop.

Top:
- `Add Files`
- `Add Folder`
- `Clear List` aligned right

Menu:
- File
  - Add Files… `Ctrl+O`
  - Add Folder… `Ctrl+Shift+O`
  - Remove Selected `Delete`
  - Clear List `Ctrl+L`
  - Exit `Alt+F4`
- View
  - Theme > System / Light / Dark
  - Change Font…
  - Reset Font
  - Reset Table Layout
- Help
  - About…

The old redundant Tags menu is gone. About no longer uses F1.

Table:
```text
Artist | Album | # | Title | Track Tag DR | Track DR |
Album Tag DR | Album DR | Peak | RMS | Status
```

Artist/Album/Title are flexible; metric columns are compact. User-adjusted column widths and order persist to LocalAppData. Reset Table Layout restores defaults. Sorting is deliberately disabled to keep natural load/album order.

Bottom buttons:
```text
Remove DR Tags | Write/Update DR Tags | Analyze
```

Analyze is the primary/accent action.

Empty state:
```text
Drop files or folders here
```

Status area shows collection counts and current operation rather than duplicated success text.

---

## 8. Loading / grouping / analysis behavior

Folder loading:
- recursive;
- `IgnoreInaccessible = true`;
- unsupported extensions ignored;
- duplicate paths ignored;
- metadata errors collected and summarized rather than one MessageBox per file.

Large collection behavior was runtime-tested during development with roughly 1700 files and low memory growth; keep sequential/controlled processing rather than decoding an entire collection into RAM.

Album grouping:
- Album present → AlbumArtist + Album;
- if AlbumArtist empty → Artist + Album;
- Album missing → parent directory;
- track number never determines album membership.

Folder fallback does not fabricate visible Album metadata.

Analysis:
- runs background work so WPF stays responsive;
- already successful analyzed tracks are skipped on a subsequent Analyze in the same loaded session;
- error tracks are retried;
- reload/Clear List resets that session state.

Cancel:
- no permanent extra Cancel button;
- active operation button changes to Cancel;
- cancellation is checked at file boundaries;
- current file finishes safely;
- FFmpeg/ffprobe watchdogs are hang guards, not the user cancellation mechanism.

Current watchdogs:
- ffprobe 30 s per call;
- ffmpeg analysis 60 min per file.

---

## 9. Settings

Current settings path uses the work name:

```text
%LocalAppData%\DRAnalyzer\
```

Files include:
- `theme.txt`
- `font.txt`
- `table-layout.json`

Do not rename/migrate this piecemeal. Resolve it together with the final public product-name decision so existing development settings are handled intentionally.

---

## 10. Testing

Stage 4 categorizes every test class that reads a local environment variable as:

```text
Category = ExternalReference
```

Portable/synthetic command:

```powershell
dotnet test ".\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj" `
    --filter "Category!=ExternalReference"
```

External/reference command:

```powershell
dotnet test ".\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj" `
    --filter "Category=ExternalReference"
```

Missing required environment variables must fail loudly when those tests are run.

Environment variables currently referenced:

```text
DRANALYZER_MANUAL_FLAC_ALBUM_COPY
DRANALYZER_MANUAL_FLAC_ALBUM_ORIGINAL
DRANALYZER_MANUAL_FLAC_COPY
DRANALYZER_MANUAL_FLAC_ORIGINAL
DRANALYZER_MANUAL_OPUS_ALBUM_COPY
DRANALYZER_MANUAL_OPUS_ALBUM_ORIGINAL
DRANALYZER_MANUAL_OPUS_COPY
DRANALYZER_MANUAL_OPUS_FILE
DRANALYZER_MANUAL_OPUS_ORIGINAL
DRANALYZER_MANUAL_OPUS_ORIGINAL_SHA256
DRANALYZER_REFERENCE_AIFF_DIR
DRANALYZER_REFERENCE_APE_DIR
DRANALYZER_REFERENCE_DISCOVERY_APE_DIR
DRANALYZER_REFERENCE_DISCOVERY_FLAC_DIR
DRANALYZER_REFERENCE_DISCOVERY_OGG_DIR
DRANALYZER_REFERENCE_INVINCIBLE_MP3_DIR
DRANALYZER_REFERENCE_LEDZEPPELIN4_DIR
DRANALYZER_REFERENCE_M4A_DIR
DRANALYZER_REFERENCE_TOOL_DIR
DRANALYZER_REFERENCE_WAVPACK_DIR
DRANALYZER_REFERENCE_WAV_DIR
```

Known real/reference corpus used during development included:
- Daft Punk – Discovery (FLAC and other format copies where applicable)
- Led Zeppelin IV (Opus)
- Tool – 10,000 Days (Opus/reference)
- Invincible MP3 reference
- format-specific WAV/AIFF/M4A/APE/WavPack copies

Original music references are always read-only truth sources; tests operate on copies.

---

## 11. Review history

### Pass 1
- removed hidden analysis diagnostics;
- corrected exact owned-field logic vs. foreign aliases;
- improved recursive scan resilience;
- aggregated metadata errors;
- corrected operation wording;
- removed template junk.

### Pass 2
- M4A flush-to-disk;
- ffprobe stdout/stderr concurrency;
- exact empty owned fields remain removable;
- reference tests fail when assets/env vars are missing.

### Pass 3
- strict FLAC write preservation validation;
- global fatal exception fallback;
- ffmpeg/ffprobe watchdogs;
- protected reference-sensitive rounded DR behavior.

External independent static review agreed with the conservative writer design and raised:
- FLAC validation asymmetry → fixed;
- global exception fallback → fixed;
- process timeouts → fixed;
- duplicated ID3 parser → deliberately deferred;
- DR rounding style → deliberately not changed because reference semantics are more important;
- silently passing manual tests → fixed.

### Pass 4
- no new foreign-metadata/audio corruption path identified;
- cleanup after successful writer replacement made best-effort;
- external-reference tests categorized;
- release/source documentation added.

---

## 12. Known release blockers / deliberate open work

Do not call the project publicly released until these are resolved:

1. public license + `LICENSE`;
2. FFmpeg/ffprobe packaging and exact redistribution notices;
3. framework-dependent vs self-contained .NET packaging;
4. portable ZIP / installer choice;
5. clean-machine packaging smoke test;
6. English review/translation of low-level German writer safety-error messages that can surface through GUI error dialogs;
7. final GitHub/project URL in About/Help if desired;
8. final source/release artifact checksums.

Not release blockers unless deliberately promoted:
- raw AAC writer support;
- WMA writer support;
- generic ID3 parser refactor;
- MusicBee plugin idea.

---

## 13. Release documentation created in Stage 4

Public/developer files:
- `README.md`
- `CHANGELOG.md`
- `.gitignore`
- `docs/FORMAT_SUPPORT.md`
- `docs/METADATA_SAFETY.md`
- `docs/TESTING.md`
- `docs/PRE_RELEASE_REVIEW.md`
- `docs/RELEASE_CHECKLIST.md`
- `docs/THIRD_PARTY.md`
- `docs/HANDOFF.md`

No `LICENSE` was invented because the user has not selected one.

---

## 14. Next steps after Stage 4 green

Completed:
- `dotnet build --no-incremental` → green on the Windows development machine;
- portable synthetic tests (`Category!=ExternalReference`) → green;
- clean Stage-4 source snapshot created.

Next:
1. preferably perform a short writer smoke test on test copies because Stage 4 mechanically touched writer cleanup `finally` blocks;
2. **completed:** final public product name = `Dynamic Range Analyzer`; first public version = `1.0.0`; explicit product/file/assembly metadata set;
3. decide public license and add `LICENSE`;
4. handle packaging + FFmpeg/ffprobe licensing/distribution;
5. final user-visible English error-message pass;
6. clean-machine release-candidate test;
7. freeze the exact artifact, calculate checksums and publish only that tested artifact.

---

## 15. Release identity finalized

Public identity is now fixed:
- product name: **Dynamic Range Analyzer**;
- first public version: **1.0.0**;
- executable assembly name: `DynamicRangeAnalyzer`;
- internal solution/project namespaces remain `DRAnalyzer.*`;
- existing `%LocalAppData%\DRAnalyzer` settings location remains unchanged intentionally.

The About dialog reads the assembly version dynamically and therefore displays `Version 1.0.0` after this change.

## 16. Development philosophy

Keep:
- preservation before convenience;
- conservative rejection rather than generic rewriting;
- reference-calculated DR over old embedded tags;
- format-specific writer testing;
- current source inspection before large changes;
- full replacement files/ZIPs for larger changes;
- build checkpoint before runtime testing;
- one test too many rather than one too few;
- update this living handoff after relevant changes.

**Do not casually refactor proven metadata writers just for stylistic elegance immediately before release.**
