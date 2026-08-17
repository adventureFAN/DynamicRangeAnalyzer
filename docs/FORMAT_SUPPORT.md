# Format Support

This document describes the source tree reviewed on 2026-08-17. Writer support is intentionally narrower than analysis support.

## Support matrix

| Format | Extension(s) | Analysis | DR write/update/remove | Notes |
|---|---|---:|---:|---|
| FLAC | `.flac` | Yes | Yes | Native FLAC metadata-block editing; Vorbis comments only |
| Opus | `.opus` | Yes | Yes | Ogg/OpusTags editing |
| MP3 | `.mp3` | Yes | Yes | Conservative ID3v2 TXXX editing |
| Ogg Vorbis | `.ogg` | Yes | Yes | Vorbis comment-header editing |
| M4A | `.m4a` | Yes | Yes | Direct MP4 atom editing for supported normal M4A layouts |
| WAV | `.wav` | Yes | Yes | Classic RIFF/WAVE + ID3 chunk |
| AIFF / AIF | `.aiff`, `.aif` | Yes | Yes | Classic FORM/AIFF + ID3 chunk |
| APE / Monkey's Audio | `.ape` | Yes | Yes | APEv2 tail metadata |
| WavPack | `.wv` | Yes | Yes | Validated WavPack stream + APEv2 tail metadata |
| Raw AAC | `.aac` | Yes | No | Analysis-only |
| WMA | `.wma` | Yes | No | Analysis-only |

## General writer policy

The writer facade only exposes formats that have a dedicated implementation. There is no generic "remux and hope" path.

For every tag operation, only these fields are owned by the application:

- `DYNAMIC RANGE`
- `ALBUM DYNAMIC RANGE`

Compatible aliases such as `DR`, `DYNAMIC_RANGE`, `ALBUM DR` or `ALBUMDR` may be read for display, but they are not considered owned and are not removed merely because they look similar.

## Format-specific safety limits

These limits are deliberate. A rejected file should remain unchanged.

### FLAC
- Edits the FLAC Vorbis-comment metadata block only.
- More than one `VORBIS_COMMENT` block is rejected.
- Foreign comments, vendor data, other metadata blocks and audio frames are preservation-validated.
- Write and remove paths validate the generated copy before replacement.

### Opus
- Edits `OpusTags` in the Ogg stream.
- Ogg page structure, sequence numbers and checksums are handled explicitly.
- Foreign comments and audio packets are preserved.
- Unsupported or structurally ambiguous streams are rejected.

### Ogg Vorbis
- Edits the Vorbis comment header only.
- Header-page granule-position rules are validated.
- Vendor, foreign comments, setup packet and audio packets are preserved.

### MP3
- Uses ID3v2 TXXX fields for the two owned DR keys.
- Existing ID3v2.3 and ID3v2.4 are supported within the conservative parser limits.
- ID3v2.2 and problematic/special ID3 structures are rejected.
- If a tagless MP3 needs DR fields, a minimal supported ID3v2 tag can be created.
- If removal leaves only a DRAnalyzer-created empty ID3v2 tag, that tag can be removed so a write/remove round trip can return a previously tagless file to its original payload.

### M4A
- Uses direct MP4/M4A atom editing, not a remux.
- Own fields are stored as iTunes-style freeform `----` metadata entries.
- Normal supported `moov/udta/meta/ilst` layouts are required.
- Fragmented MP4/M4A is rejected.
- `moov` and chunk offsets are adjusted only in supported validated layouts.
- Exotic/ambiguous atom layouts and unsupported large/size-zero structures are rejected.

### WAV
- Supports classic RIFF/WAVE.
- RF64/BW64/RIFX are not writer targets.
- Uses an `ID3 ` chunk with supported ID3v2.3/v2.4 TXXX fields.
- Multiple ID3 chunks and unsafe ID3 structures are rejected.
- The classic RIFF 4 GiB size boundary is enforced.

### AIFF / AIF
- Supports classic uncompressed FORM/AIFF.
- AIFC is not a writer target.
- Uses an ID3 chunk with supported ID3v2.3/v2.4 TXXX fields.
- Multiple ID3 chunks and unsafe ID3 structures are rejected.
- The IFF/AIFF 4 GiB size boundary is enforced.
- Real-world all-zero trailing padding after an ID3 payload is handled conservatively.

### APE / Monkey's Audio
- Uses APEv2 metadata.
- APEv1 is rejected.
- Text/binary foreign items are preserved.
- Legacy contradictory footer flags seen in real files are accepted only when the physical structure validates.

### WavPack
- Uses an APEv2 metadata tail while validating the underlying WavPack stream.
- Supported WavPack stream versions in the writer are `0x0402` through `0x0410`.
- APEv1 is rejected.
- Existing trailing ID3v1 data is preserved.
- Legacy APEv2 footer-flag handling follows the same conservative validation model as APE.

## Analysis-only formats

Raw AAC and WMA are intentionally analysis-only at this stage. Do not advertise DR tag writing/removal for them unless a future format-specific preservation test campaign explicitly frees them for writing.
