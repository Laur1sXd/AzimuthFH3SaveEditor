**Author:** Laur1sXd
**Version:** v1.0
**Category:** Utility / QoL / Cheats

A small Windows desktop app (C# WinForms, .NET 10) for viewing and editing the save files of the Azimuth PC build of **Forza Horizon 3**. Saves live under:
`...\data\titles\4CE1DFAB\saves\player-1\User_...\revisions\<revision>\data\`.

> **Unofficial and experimental.** Keep your own copy of your saves before editing. This project is not affiliated with Microsoft, Playground Games, Turn 10, or the Azimuth project.

## Download
https://github.com/Laur1sXd/AzimuthFH3SaveEditor/releases

Self-contained Windows x64 executable. No separate .NET installation, external crypto tool, or CMD popups.

Open a save folder or single supported file, edit, and save. Includes automatic backups, Azimuth commit updates, and remembered file/folder picker locations.

## Known values

**SaveState**

`TombolaState | TombolaState | Spins` = Wheelspins
`skills_save_state | SkillsState | CurrentSkillTokensTotal` = Skill tokens / perk points
`skills_save_state | SkillsState | UnspentSkillPointsTotal` = Unspent skill points

**ProfileData**

`Main/Credits` = Game credits
`Main/XP` = XP
`Main/Level` = Player level
`Main/NumSkillTokens` = Skill tokens

**Credits**

AnthemSaveEditor / qiangqiang101 for the interface reference; Doliman100 and DVS for FH3 crypto research and compatibility data; Nenkai / ForzaTools for BXML research.
