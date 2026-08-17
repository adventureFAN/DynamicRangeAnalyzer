# Release Checklist

This checklist is intentionally conservative because the application can modify music files in place.

## A. Identity and legal

- [x] Final public product name: **Dynamic Range Analyzer**.
- [x] First public version: **1.0.0**.
- [x] Set explicit assembly/file/product version metadata to 1.0.0 / 1.0.0.0.
- [x] Window/About/README branding uses **Dynamic Range Analyzer**.
- [x] Public source-code license: **MIT**.
- [x] Added final root `LICENSE` file.
- [x] Copyright/author: **Copyright (c) 2026 adventureFAN**.
- [x] Third-party policy documented; exact FFmpeg binary notice remains tied to the final runtime build.

## B. FFmpeg / runtime / packaging

- [x] Distribution direction: bundle `ffmpeg.exe`/`ffprobe.exe` as separate third-party executables for a simple end-user package.
- [ ] Produce and review the exact FFmpeg runtime build: no `--enable-gpl`, no `--enable-nonfree`, target LGPL v2.1+.
- [x] `docs/THIRD_PARTY.md` defines the release policy; update it again with exact FFmpeg version/config/source location when the runtime build is frozen.
- [x] Packaging choice: framework-dependent .NET 10 `win-x64` publish; requires Microsoft .NET 10 Desktop Runtime (x64).
- [x] 1.0.0 packaging choice: portable ZIP; no installer for the first release.
- [x] Runtime discovery: prefer `runtime\ffmpeg\ffmpeg.exe` / `ffprobe.exe` beside the app, then fall back to PATH for development.
- [x] Missing-runtime errors remain English and now cover reinstalling the packaged runtime or PATH fallback.
- [ ] Test install/uninstall or portable extraction on a clean Windows machine.
- [ ] Test paths containing spaces, Unicode and non-ASCII characters.

## C. User-visible polish

- [x] Stage 5C normalizes low-level writer/analysis errors before they reach the English GUI.
- [ ] Recheck File / View / Help menus.
- [ ] Add project/repository link to Help/About if available.
- [ ] Confirm application icon at all packaged sizes.
- [ ] Confirm Light/Dark/System theme behavior.
- [ ] Confirm custom font and Reset Font.
- [ ] Confirm table layout persistence and Reset Table Layout.
- [ ] Confirm no development/debug text appears in the UI.

## D. Build and test gates

- [x] Clean `dotnet build --no-incremental`: 0 warnings, 0 errors. (Stage 4 checkpoint, 2026-08-17)
- [x] Portable synthetic suite passes:
  `dotnet test .\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj --filter "Category!=ExternalReference"`
  (Stage 4 checkpoint, 2026-08-17)
- [ ] External-reference/preservation suite passes with all required local assets.
- [ ] Re-run complete classic DR reference corpus; do not change algorithm to match arbitrary old tags.
- [ ] Large-collection load/analysis/cancel/resume smoke test.
- [ ] Metadata-error aggregation smoke test.
- [ ] ffprobe/ffmpeg normal-path smoke test.
- [ ] Verify fatal-error handler does not interfere with normal shutdown.

## E. Writer safety matrix

Using **test copies only**:

- [ ] FLAC write/update/remove + preservation.
- [ ] Opus write/update/remove + preservation.
- [ ] MP3 write/update/remove + preservation.
- [ ] Ogg Vorbis write/update/remove + preservation.
- [ ] M4A write/update/remove + preservation.
- [ ] WAV write/update/remove + preservation.
- [ ] AIFF/AIF write/update/remove + preservation.
- [ ] APE write/update/remove + preservation.
- [ ] WavPack write/update/remove + preservation.
- [ ] Raw AAC remains writer-disabled.
- [ ] WMA remains writer-disabled.
- [ ] Unsupported/ambiguous writer structures fail without replacing the source.

For every format, verify:
- [ ] `DYNAMIC RANGE` correct.
- [ ] `ALBUM DYNAMIC RANGE` correct when applicable.
- [ ] foreign tags preserved.
- [ ] ReplayGain/loudness metadata preserved.
- [ ] embedded artwork preserved.
- [ ] audio still decodes.
- [ ] source/reference original was never modified during testing.
- [ ] no stale `.dranalyzer.tmp`/`.dranalyzer.backup` artifacts under normal successful operation.

## F. Release artifact hygiene

- [ ] Source archive contains README/changelog/docs.
- [ ] Source archive excludes `bin`, `obj`, `.vs`, `.git` metadata when making a clean handoff archive, local `backup-*` folders and test output.
- [ ] Release binary package excludes private test/reference music.
- [ ] Release binary package excludes development-only tools not covered by the final distribution plan.
- [ ] Generate SHA-256 checksums for published artifacts.
- [ ] Test the exact artifact that will be uploaded; do not rebuild after the final smoke test without retesting.
- [ ] Update `docs/HANDOFF.md` to the exact released commit/snapshot/version.
- [ ] Create final release notes from `CHANGELOG.md`.

## Final UI language pass

- [x] English user-facing error surface: analysis/metadata errors translated; low-level tagging safety exceptions normalized before GUI display.
- [x] Final Stage 5C runtime smoke test passed on 2026-08-17: analyze, write/update, repeat update, remove, metadata preservation and About/version.
