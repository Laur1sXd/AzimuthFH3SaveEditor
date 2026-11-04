Azimuth FH3 Save Editor 1.0

Run AzimuthFH3SaveEditor.exe. It is one self-contained Windows x64 executable.
No ForzaCryptoTool.exe, tools folder, .NET installation, or command window is needed.
FH3 decryption, encryption, and authentication run inside the editor.

The editor starts empty. Open folder and Open single file remember their last selected
locations across app restarts. Save and export pickers remember their locations too.
Preferences are stored in %LOCALAPPDATA%\AzimuthFH3SaveEditor\settings.json.
No save is loaded automatically when the app starts.
1. Choose Open save folder or Open single file.
2. Edit values in PlayerDatabase, ProfileData, SaveState, or VersionFlags.
3. Save all (Ctrl+S) writes to the loaded files. Save as (F12) chooses another path.
   Save all to folder and Save as new revision are also available in File.

Supported contents: FH3 encrypted ProfileData and VersionFlags; decrypted packed FH3
profiles; plain four-byte VersionFlags; SQLite/PlayerDatabase; and BXML v2 SaveState.
Individual files can have any filename and be stored in any folder.
Encrypted files remain encrypted when saved. Plain files remain plain.

Close the game before saving. Existing files are backed up automatically under
<Azimuth>\data\titles\4CE1DFAB\saves_backup. The status bar shows the backup location.
Backups are outside active revisions. Automatic save discovery ignores saves_backup.
Standalone files outside an Azimuth title use a saves_backup folder beside the file.
An existing Azimuth commit manifest is regenerated after a save.
Replaced files and the manifest can be restored from the backup while the game is closed.

The built-in crypto was tested against the supplied real FH3 encrypted files.
Unchanged encryption round trips reproduce the originals byte for byte. Editing,
saving, reopening, backup, and authentication checks all passed on workspace copies.
No live save files were edited during development.

See THIRD-PARTY-NOTICES.txt and licenses/ for acknowledgements.
Unofficial software, not affiliated with Microsoft, Playground Games, or Azimuth.
