# Using Azimuth FH3 Save Editor 1.0

## Open a save

1. Close Forza Horizon 3.
2. Run `AzimuthFH3SaveEditor.exe`. It starts with an empty editor.
3. Choose **Open save folder** or **Open single file**.

For an Azimuth revision, the `data` folder contains the save files and its parent contains `commit`. You can select that `data` folder directly. If several revisions are present, use the latest completed revision.

**Open single file** can load encrypted FH3 `ProfileData` or `VersionFlags` independently. It also accepts decrypted packed FH3 profiles, plain four-byte flags, SQLite/PlayerDatabase, and BXML v2 SaveState. Identification is based on file contents, so a supported file can have a different filename.

The file and folder pickers remember their locations after closing and reopening the application. Remembering a picker location does not automatically load a save.

## Edit values

| Tab | How to edit |
| --- | --- |
| PlayerDatabase | Select a table, then edit an existing cell. Binary columns and tables without a usable rowid are read-only. |
| ProfileData | Filter by a name such as `Credits`, then edit the Value column. The current credits property is `Main/Credits`. |
| SaveState | Filter by object, property, or value, then edit the Value column. |
| VersionFlags | Change the decrypted unsigned 32-bit number. |

Press Enter or leave the cell to apply an edit. Use a decimal point for decimal numbers. Invalid numeric values are rejected. The opaque packed binary-state section is preserved.

## Save

| Command | Result |
| --- | --- |
| Save all (`Ctrl+S`) | Writes to the loaded files. An existing Azimuth commit manifest is updated. |
| Save as (`F12`) | Chooses another file for a single-file session, or another folder for a folder session. The new copy becomes the open session. |
| Save all to folder | Writes the loaded save files to a selected folder. |
| Save as new revision | Creates the next hexadecimal revision and keeps the loaded revision. Available when an Azimuth revision is loaded. |
| Export revision to new folder | Creates a separate complete revision folder containing `data` and `commit`. |

Encrypted files remain encrypted, and plain files remain plain. Rebuilt encrypted files are decrypted and compared before writing. Editing a file inside an Azimuth revision preserves its unopened sibling files.

If another process changes the loaded files, reopen them before saving. If a newer revision appears, reopen the latest save before using **Save as new revision**.

## Restore a backup

The editor backs up files before replacing them. The status bar shows the backup directory under:

```text
<Azimuth>\data\titles\4CE1DFAB\saves_backup\
```

The title-level backup folder is separate from active revisions and is ignored by automatic save discovery. Standalone files outside an Azimuth title use a `saves_backup` folder beside the file. Existing backups from earlier builds remain in their previous location.

1. Close the game and editor.
2. Open the relevant backup directory.
3. Read `restore-info.txt` to find the original locations.
4. Copy the backed-up files to those locations, including the saved `commit` when present.

A backup contains the files replaced by that operation; keep a separate full backup of the save container as well.

## Downloads and post text

- [Version 1.0 release](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases/tag/v1.0.0)
- [Copy the mod description](discord-description.md)
