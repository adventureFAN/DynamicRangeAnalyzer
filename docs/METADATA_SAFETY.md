# Metadata Safety Model

Metadata preservation is the highest-priority design constraint of this project.

## Owned fields

The application may create, update or remove only:

- `DYNAMIC RANGE`
- `ALBUM DYNAMIC RANGE`

It must not alter unrelated metadata, including title, artist, album, album artist, track/disc numbers, genre, date/year, composer, ReplayGain/loudness fields, comments, arbitrary custom tags or embedded artwork.

Read compatibility is intentionally broader than write ownership. Similar aliases can be displayed without becoming removable application-owned fields.

## Transaction model

Writer implementations follow a conservative same-directory replacement model:

1. Parse and validate the original file/metadata structure.
2. Create a uniquely named hidden temporary file in the same directory.
3. Write only the format-specific owned-field change.
4. Flush output where required.
5. Validate the generated temporary file before touching the original.
6. Replace the original with `File.Replace`, requesting a transient backup.
7. After a successful replacement, delete temporary artifacts on a best-effort basis.
8. If `File.Replace` itself fails, any backup it may have produced is intentionally retained rather than deleted.

The Stage 4 cleanup hardening is important: failure to delete a temporary/backup artifact after an already successful replacement must not turn that successful tag operation into a false "write failed" result.

## Preservation testing policy

A format should only be exposed through the GUI writer facade after:

1. a format-specific writer exists;
2. synthetic/edge tests pass;
3. a real reference file is modified only through a test copy;
4. original vs. modified preservation checks pass;
5. audio remains decodable/preserved;
6. foreign tags and artwork/container payload remain preserved;
7. the original reference file remains unchanged;
8. write/update/remove is tested through the facade/GUI path.

The source tree contains synthetic and real-reference preservation/regression tests for the formats that are currently writer-enabled.

## Conservative rejection is expected

A safe refusal is preferable to rewriting a structure the writer does not fully understand. Unsupported ID3 structures, fragmented or exotic M4A layouts, unsupported WAV/AIFF container variants, APEv1 and other ambiguous cases should fail without replacing the source file.

## Cancellation

Tag operations are not cancelled in the middle of a file rewrite. User cancellation is observed at file boundaries. The current file is allowed to finish its validated operation before processing stops.
