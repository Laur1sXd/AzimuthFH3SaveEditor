# Azimuth FH3 Save Editor

A Windows desktop save editor for the **Azimuth build of Forza Horizon 3**, by [Laur1sXd](https://github.com/Laur1sXd). Version **1.0**.

Open a save folder or individual file, edit its values, and save. The editor starts empty and remembers the locations used by its file and folder pickers. FH3 encryption runs inside the application: no external crypto tool, command windows, or separate .NET installation is needed for the Windows x64 build.

Inspired by the layout of [AnthemSaveEditor](https://github.com/qiangqiang101/AnthemSaveEditor), with support for FH3's encrypted, packed profile format.

> Close the game before editing. Keep a separate backup of your save container. This is an unofficial editor; individual progression and garage changes may require other values to remain consistent.

## Download

Version **1.0** is available:

- [Download the Windows x64 EXE](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases/download/v1.0.0/AzimuthFH3SaveEditor.exe)
- [Download the ZIP with instructions and notices](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases/download/v1.0.0/Azimuth-FH3-Save-Editor-win-x64.zip)
- [SHA-256 checksums](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases/download/v1.0.0/SHA256SUMS.txt)
- [All releases](https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases)

Run `AzimuthFH3SaveEditor.exe`. The executable is self-contained and can be used on its own. The ZIP also includes usage instructions and third-party notices.

## What it edits

| Tab | Contents | Editing |
| --- | --- | --- |
| **PlayerDatabase** | The SQLite career database embedded in FH3 `ProfileData` | Browse tables, edit existing cells, export as `.sqlite` |
| **ProfileData** | Typed profile properties | Filter and edit values such as Credits, XP, Level, and NumSkillTokens |
| **SaveState** | BXML objects and properties packed inside `ProfileData` | Filter by object, property, or value; edit property values |
| **VersionFlags** | Encrypted or plain little-endian `uint32` | Edit the decrypted number |

The packed binary-state section is preserved unchanged. Binary database columns are read-only.

Individual-file loading recognizes the contents, so supported files can have different filenames and live outside the original save folder. Supported standalone formats include encrypted FH3 `ProfileData` and `VersionFlags`, decrypted packed FH3 profiles, plain four-byte flags, SQLite/PlayerDatabase files, and BXML v2 SaveState files.

## Known values

These entries are present in the FH3 save used for verification. SaveState entries are listed as `Object | Type | Property`.

| Tab | Entry | Meaning |
| --- | --- | --- |
| ProfileData | `Main/Credits` | Game credits |
| ProfileData | `Main/XP` | XP |
| ProfileData | `Main/Level` | Player level |
| ProfileData | `Main/NumSkillTokens` | Skill tokens |
| SaveState | `TombolaState \| TombolaState \| Spins` | Wheelspins |
| SaveState | `skills_save_state \| SkillsState \| CurrentSkillTokensTotal` | Skill tokens / perk points |
| SaveState | `skills_save_state \| SkillsState \| UnspentSkillPointsTotal` | Unspent skill points |

## Usage

1. Close Forza Horizon 3 and run the editor.
2. Choose **File > Open save folder** or **Open single file**.
3. Edit values in the four tabs. Press Enter or leave the cell to apply an edit. Use a decimal point for decimal numbers.
4. Choose a save operation:
   - **Save all** (`Ctrl+S`): write back to the loaded files.
   - **Save as** (`F12`): choose another filename or folder.
   - **Save all to folder**: write the loaded files to another folder.
   - **Save as new revision**: create the next numbered Azimuth revision and retain the loaded revision.
5. Start FH3 and check the result.

For folder loading, select the `data` folder inside a save revision, a revision folder, or the Azimuth `titles` folder. A typical FH3 path is:

```text
<Azimuth>\data\titles\4CE1DFAB\saves\player-1\User_<account>\revisions\<revision>\data\
```

You can also pass a file or folder explicitly:

```powershell
.\AzimuthFH3SaveEditor.exe "<path to save file or folder>"
```

The editor does not automatically open the last save on startup. Only picker locations are remembered.

See the [usage and backup guide](docs/usage.md) for a walkthrough. A [Discord-ready mod description](docs/discord-description.md) is also included.

## Backups and save verification

Before replacing existing files, the editor backs them up to:

```text
<Azimuth>\data\titles\4CE1DFAB\saves_backup\
```

Backups are stored outside the active `saves` and revision folders, and automatic save discovery skips `saves_backup`. For a standalone file outside an Azimuth title, the editor uses an adjacent `saves_backup` folder.

The status bar shows the backup location. Each backup contains `restore-info.txt` with the original paths. Close the game and editor before copying backed-up files to those locations.

Edited encrypted files are decrypted again and compared with the intended plaintext before they are written. Existing Azimuth `commit` manifests are rebuilt with SHA-256 hashes and sizes. In-place saves refuse to overwrite source changes made by another process.

Picker preferences are stored in `%LOCALAPPDATA%\AzimuthFH3SaveEditor\settings.json`.

## Build from source

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build src/AzimuthFH3SaveEditor.csproj -c Release
.\build.ps1
```

`build.ps1` publishes the self-contained Windows x64 application into `dist/` and includes the usage instructions and notices. The crypto implementation and FH3 compatibility data are embedded at build time; no external executable is downloaded or launched by the editor.

GitHub Actions builds pushes and pull requests. When a source or release-workflow update is pushed to `main`, it publishes the project version (such as `v1.0.0`) or refreshes that version's executable, ZIP, checksums, release notes, and source tag. Publishing a GitHub Release manually also builds and attaches the executable, ZIP, and SHA-256 checksums. The release workflow can be run manually to produce a downloadable build artifact.

## Verification

The included verification suite uses copies of a real FH3 save. It checks unchanged byte-identical encryption round trips; edits to profile values, Unicode strings, BXML properties, SQLite cells, and flags; backup behavior; commit updates; standalone renamed files; and rejection of damaged encrypted files. No personal save fixture is included in this repository.

To run it, provide your own compatible revision `data` folder and a new test workspace:

```powershell
.\dist\AzimuthFH3SaveEditor.exe --self-test "<FH3 revision data folder>" "<new test folder>"
```

The test suite writes to copied saves and temporary working folders. It produces `validation.json` in the test workspace. It expects a populated FH3 career save with the usual profile properties and garage data.

## Project layout

| File | Purpose |
| --- | --- |
| `src/EditorForm.cs` | Four-tab interface and file dialogs |
| `src/FileSession.cs` | File detection, SQLite editing, save operations, backups, and manifests |
| `src/Formats.cs` | Typed profile properties and BXML parsing/writing |
| `src/Fh3Crypto.cs` | In-process FH3 TFIT/CBC/CMAC encryption and authentication |
| `src/Fh3Tfit.bin.gz` | Embedded FH3 profile compatibility constants |
| `src/DialogLocations.cs` | Persistent file and folder picker locations |
| `src/SelfTest.cs`, `FileTests.cs`, `CryptoTests.cs` | Save and crypto verification |

## Credits and licensing

- [qiangqiang101 / AnthemSaveEditor](https://github.com/qiangqiang101/AnthemSaveEditor): interface and manifest reference.
- [Doliman100 / ForzaTech-crypto-tool](https://github.com/Doliman100/ForzaTech-crypto-tool): FH3 crypto format and round-layout research.
- [DVS / Forza-Crypto-Tool](https://github.com/DVS-code/Forza-Crypto-Tool): legacy FH3 compatibility data used during development. This editor does not depend on its executable or assemblies.
- [Nenkai / ForzaTools](https://github.com/Nenkai/ForzaTools): BXML format research.

Original editor code is provided under the [MIT license](LICENSE). Third-party components and compatibility data retain their own terms; the original-code license does not grant additional rights to third-party data. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and [licenses/](licenses/).

Not affiliated with Microsoft, Playground Games, Turn 10, or the Azimuth project.
