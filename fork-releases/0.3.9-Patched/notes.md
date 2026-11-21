> **AI disclaimer:** this is an unofficial fork of Ranching Together. Every change in it was written with AI (Claude Code, by Anthropic) and tested in-game by the fork's maintainer in two-player sessions. It is **not** made, reviewed or supported by the original Ranching Together team, so please don't ask them for help with it.

**Ranching Together** is the Slime Rancher 2 multiplayer mod by **Shark (py8)**, with contributions from **AlchlcSystm** and **PinkTarr**. All credit for the mod goes to them: [original repository](https://github.com/pyeight/SlimeRancher2Multiplayer), [NexusMods](https://www.nexusmods.com/slimerancher2/mods/118), [Discord](https://discord.com/invite/a7wfBw5feU). This release builds on their version 0.3.9.

## What's fixed
- **Gardens:** crops show up for everyone, deluxe plots keep their crops, fruit hangs on the plant instead of dropping early.
- **Auto-feeders** keep their food and speed, even after a re-host.
- **Stuck menus:** the map, slimepedia or pause menu no longer gets stuck on screen after arriving in a new area.
- **Things that happened twice now happen once:** crates, gordo rewards, treasure pods, geysers, drone income, plort sales.
- **Shared progress:** vac palettes, Sanctuary rooms, slime appearances and rewards kiosk progress are shared with the host's save.
- **Seeing each other:** other players' vac streams, Pulse Waves, tangle stalks and grab vines; you walk through other players.
- **Map and weather:** joiners get the host's revealed map and weather, and Starlight Strand no longer shows as unknown.
- **Joining:** big worlds load reliably, and a host now turns away a player on a different version with a clear message.
- **Cheat menu** (when the host allows cheats): rest until a time of day, per-type weather buttons, tornadoes, tangle stalks, grow gardens, sloomber yawns, dervish cyclones, burst gordos. Everyone sees the results.

The full list is in the [changelog](https://github.com/Ryuiku/SlimeRancher2Multiplayer/blob/master/CHANGELOG.md).

## Install
Needs Slime Rancher 2 **1.3.0**, **MelonLoader 0.7.3** and **[Starlight 4.0.4](https://github.com/ThatFinnDev/Starlight/releases/download/v4.0.4/Starlight.dll)**.
1. Install MelonLoader 0.7.3 with the [MelonLoader Installer](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.1/MelonLoader.Installer.exe).
2. Put `Starlight.dll` into the game's `Mods` folder.
3. Put `SR2MP.dll` from this release into the `Mods` folder. If you had the original mod, replace its `SR2MP.dll`.
4. Start the game. The multiplayer window title shows `SR2MP v0.3.9-Patched`.

Everyone in a session needs this same version. It can't play with the original 0.3.9. Joining a host replaces the save you join with, so join with an empty save.

`SR2MP.dll` sha256: `c38dde3097b91bdb4bb9589d63fe85da8ed9885c7fc1581a9c204ebc428e46f9`

Questions or bugs: [open an issue on this fork](https://github.com/Ryuiku/SlimeRancher2Multiplayer/issues). Source code: this repository (GPLv3, like the original).
