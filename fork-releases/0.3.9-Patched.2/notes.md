> **AI disclaimer:** this is an unofficial fork of Ranching Together. Every change in it was written with AI (Claude Code, by Anthropic) and tested in-game by the fork's maintainer. It is **not** made, reviewed or supported by the original Ranching Together team, so please don't ask them for help with it.

**Ranching Together** is the Slime Rancher 2 multiplayer mod by **Shark (py8)**, with contributions from **AlchlcSystm** and **PinkTarr**. All credit for the mod goes to them: [original repository](https://github.com/pyeight/SlimeRancher2Multiplayer), [NexusMods](https://www.nexusmods.com/slimerancher2/mods/118), [Discord](https://discord.com/invite/a7wfBw5feU).

## What's fixed in 0.3.9-Patched.2
- **Host sent to the main menu when entering the Sanctuary** ("An error occurred"). A plot's storage could be saved with more slots than the plot has. Saves that already have this now load again, and the cause is fixed.
- **Players stuck when the host's game closes or hangs.** If a player hears nothing from the host for 30 seconds, they now leave the world with a message instead of waiting forever.
- **Better bug reports:** if the game stops responding during multiplayer, the log now says what it was busy with.

Everything else is in the [changelog](https://github.com/Ryuiku/SlimeRancher2Multiplayer/blob/master/CHANGELOG.md).

## Install or update
Needs Slime Rancher 2 **1.3.0**, **MelonLoader 0.7.3** and **[Starlight 4.0.4](https://github.com/ThatFinnDev/Starlight/releases/download/v4.0.4/Starlight.dll)**.
1. Install MelonLoader 0.7.3 with the [MelonLoader Installer](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.1/MelonLoader.Installer.exe).
2. Put `Starlight.dll` into the game's `Mods` folder.
3. Put `SR2MP.dll` from this release into the `Mods` folder, replacing the old one.
4. Start the game. The multiplayer window title shows `SR2MP v0.3.9-Patched.2`.

Everyone in a session needs this same version. Joining a host replaces the save you join with, so join with an empty save.

`SR2MP.dll` sha256: `08969df591d794be82e74fd83522230f34b02ad82486fe4b49bab5bee0316a5f`

Questions or bugs: [open an issue on this fork](https://github.com/Ryuiku/SlimeRancher2Multiplayer/issues). Source code: this repository (GPLv3, like the original).
