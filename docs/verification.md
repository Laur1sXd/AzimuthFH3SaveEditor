# Version 1.0 verification

## UI performance checks

The grid update was tested with a workspace copy of a current FH3 revision containing 446 profile values, 1,297 SaveState properties, and 32 database tables.

- 80 rapid tab switches completed; the slowest switch in the automated check took 24 ms.
- All 32 database tables loaded and scrolled without display errors; column widths remained stable while scrolling.
- Filtering and editing Credits and a SaveState property, then saving and reopening, preserved both edits and a valid manifest.
- Switching tabs and scrolling without edits did not mark the save as changed.
- Visible-window scrolling measurements improved from 1,630 ms to 224 ms for 31 SaveState positions, and from 2,462 ms to 275 ms for 32 ProfileData positions on the test machine. Timings vary by machine and other running applications.
- The self-contained EXE passed all 48 existing save-format, encryption, backup, and round-trip checks.

Only workspace copies were edited during these checks.

## Save-format checks

The editor was checked against workspace copies of a real Azimuth FH3 save. Personal save fixtures, account settings, and backups are not included in this repository.

The development verification suite passed 48 checks covering:

- Parsing the packed profile, BXML SaveState, and SQLite database.
- Byte-identical unchanged encryption round trips for ProfileData and VersionFlags.
- Editing and reopening profile numbers, Unicode strings, state properties, database cells, and flags.
- Standalone renamed files, Save as, in-place Save, and automatic backups.
- Title-level `saves_backup` paths, ignored backup revisions, and unchanged active revision names.
- SHA-256 commit manifest updates and preservation of unopened sibling files.
- Detection of external changes and refusal to install stale revisions.
- Empty, partial, exact, and multiple crypto blocks.
- Rejection of damaged headers, payloads, authentication tags, and truncated files.

Picker preferences were separately confirmed to persist and restore from a fresh process. The executable reports file version `1.0.0.0`, with the interface labeled **1.0**.

GitHub Actions publishes the self-contained EXE, ZIP, and checksums attached to [v1.0.0](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases/tag/v1.0.0). Current results are listed on the repository's [Actions page](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/actions).

These checks establish file-format round trips and save integrity. The game may still require related progression values to remain consistent after manual edits.
