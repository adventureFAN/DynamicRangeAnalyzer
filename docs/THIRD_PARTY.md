# Third-Party / Runtime Notices

Dynamic Range Analyzer itself is licensed under the MIT License. See the repository root `LICENSE` file.

Third-party components retain their own licenses.

## FFmpeg / ffprobe

Dynamic Range Analyzer invokes `ffmpeg` and `ffprobe` as separate executable processes for audio decoding and stream/metadata inspection. It does not link FFmpeg libraries into the Dynamic Range Analyzer executable.

### Release policy

For the public 1.0.0 binary package, do **not** reuse or silently bundle the current development FFmpeg installation.

The 1.0.0 release packaging is as follows:

- ship `ffmpeg.exe` and `ffprobe.exe` as clearly separate third-party executables;
- build FFmpeg **9.0** from the official FFmpeg release source tarball in GitHub Actions;
- verify the official release signature before compiling;
- cross-compile Windows x64 with MinGW-w64 and `--disable-autodetect`;
- explicitly use `--disable-gpl` and `--disable-nonfree`;
- explicitly enable the zlib cross-build dependency required by FFmpeg's PNG encoder so the release-runtime artwork/metadata safety tests can use PNG fixtures;
- include the zlib copyright/license notice alongside the FFmpeg notices;
- keep FFmpeg under its LGPL v2.1-or-later terms;
- record the exact FFmpeg version and configure/build options used;
- include the applicable FFmpeg/LGPL license notices in the binary package;
- make the exact corresponding FFmpeg source and any build scripts/configuration used for the distributed binaries available as required by the applicable FFmpeg license;
- do not describe FFmpeg as being licensed under Dynamic Range Analyzer's MIT license.

The build recipe lives in `scripts/build-ffmpeg-runtime.sh` and the release workflow in `.github/workflows/build-release-package.yml`. The workflow publishes the exact FFmpeg source archive and signature next to the Windows release artifact.

For every public binary release, the exact generated FFmpeg runtime artifact, configure flags, corresponding source archive, signature and checksums are reviewed before publication. A rebuilt runtime is treated as a new artifact and must be reviewed again.

The development machine may use a different FFmpeg build for testing; that development binary must not be copied into a public release merely because the application works with it.

## foo_dr_meter 1.0.8

`foo_dr_meter 1.0.8` is used only as a development/reference target for validating the classic DR calculation. It is not a runtime dependency, is not distributed with Dynamic Range Analyzer, and is not part of this source tree.

## .NET

Dynamic Range Analyzer targets .NET 10 / WPF.

The Windows 1.0.0 release uses a **framework-dependent .NET 10 win-x64 publish** inside a portable ZIP. The **Microsoft .NET 10 Desktop Runtime (x64) must be installed separately** on the target system; the full .NET runtime is not bundled with Dynamic Range Analyzer.

The package retains the .NET product license and third-party notices for transparency. Microsoft .NET and its components retain their own distribution terms and are not relicensed under Dynamic Range Analyzer's MIT license.
