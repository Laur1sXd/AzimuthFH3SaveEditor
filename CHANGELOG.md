# Changelog

## 1.0

- Four editor tabs: PlayerDatabase, ProfileData, SaveState, and VersionFlags.
- Open save folders or individual supported files from any location.
- Built-in FH3 encryption, decryption, and authentication in a single Windows x64 executable.
- Save all, Save as, Save all to folder, and Save as new revision.
- Automatic backups and Azimuth commit manifest updates.
- File and folder pickers remember their locations across restarts.
- SQLite export and preservation of opaque binary state.

### Backup location update

- Store FH3 save backups in `data/titles/4CE1DFAB/saves_backup`.
- Ignore `saves_backup` during automatic revision discovery.
- Keep active revision folder names unchanged during Save all.

### UI performance update

- Buffer grid painting for smoother ProfileData, SaveState, and PlayerDatabase scrolling.
- Set column widths when loading data or resizing the window, avoiding repeated content measurement while scrolling.
