# Ranching Together (Patched)

> **AI disclaimer:** this is an unofficial fork of Ranching Together. Every change in it was written with AI (Claude Code, by Anthropic) and tested in-game by the fork's maintainer in two-player sessions. It is **not** made, reviewed or supported by the original Ranching Together team, so please don't ask them for help with it.

**Ranching Together** is the Slime Rancher 2 multiplayer mod by **Shark (py8)**, with contributions from **AlchlcSystm** and **PinkTarr**. All credit for the mod itself goes to them: [original repository](https://github.com/pyeight/SlimeRancher2Multiplayer), [NexusMods](https://www.nexusmods.com/slimerancher2/mods/118), [Discord](https://discord.com/invite/a7wfBw5feU).

This fork, **0.3.9-Patched** (latest release: 0.3.9-Patched.2), builds on their version 0.3.9 and fixes a long list of sync problems found while playing: gardens, auto-feeders, weather, tornadoes, sloombers, vac streams, menus getting stuck after a zone load, and more. See the [changelog](CHANGELOG.md) for everything that changed.

- **Contact:** open an issue on [this fork's issue tracker](https://github.com/Ryuiku/SlimeRancher2Multiplayer/issues).
- **License:** GPLv3, like the original. The full source of every release is in this repository.
- If the Ranching Together developers ask for this fork to be taken down, it will be.

### Installing the Patched version
1. Install **MelonLoader 0.7.3** onto Slime Rancher 2 **1.3.0** with the [MelonLoader Installer](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.1/MelonLoader.Installer.exe).
2. Put [Starlight 4.0.4](https://github.com/ThatFinnDev/Starlight/releases/download/v4.0.4/Starlight.dll) (`Starlight.dll`) into the game's `Mods` folder.
3. Download `SR2MP.dll` from the [latest release of this fork](https://github.com/Ryuiku/SlimeRancher2Multiplayer/releases/latest) and put it into the `Mods` folder. If you had the original mod, replace its `SR2MP.dll`.
4. Start the game. The multiplayer window title shows the version, for example `SR2MP v0.3.9-Patched.2`.

Everyone in a session needs **the same Patched version**. It can't play together with the original 0.3.9, and a host turns away a player on a different version. Joining a host still replaces the save you join with, exactly as described below, so join with an empty save.

---

*The original Ranching Together README follows. Links in it point to the original mod.*

# Slime Rancher 2 Multiplayer Mod - <a href="https://discord.com/invite/a7wfBw5feU" target="_blank" rel="noopener noreferrer">Join our Discord</a>

### You can also get the mod on <a href="https://www.nexusmods.com/slimerancher2/mods/118" target="_blank" rel="noopener noreferrer">NexusMods</a>

**LEGEND:**
- 🟩 fully synced
- 🟨 partially synced
- 🟧 in progress
- 🟥 not synced

|      Feature      | Status |                                  Details about Feature                                  |
|:-----------------:|:------:|:---------------------------------------------------------------------------------------:|
|  Player Movement  |   🟩   |                                    Fully functional                                     |
| Player Animations |   🟩   |                                    Fully functional                                     |
|  Player Sound FX  |   🟩   |                                    Fully functional                                     |
| Player Visual FX  |   🟨   |   Vac suction stream & Pulse Wave added (untested); remote vac spin/ammo dial missing   |
| Player Inventory  |   🟥   |                  Not stored on the server, use the same save to rejoin                  |
| Initial Save load |   🟩   |      Everything that is ___currently___ synchronised will be loaded on Player join      |
|      Slimes       |   🟩   |                          Fully functional (Including Radiant)                           |
|  Slime behaviour  |   🟨   |   Partially functional (Yolky, Dervish, Sloomber, Gold, Lucky, Tabby), Tangle partial   |
|   Gordo Slimes    |   🟩   |                                    Fully functional                                     |
|  Actors (Items)   |   🟩   |                                    Fully functional                                     |
|  Resource Nodes   |   🟩   |                                    Fully functional                                     |
|     Landplots     |   🟩   |        Fully functional (Gardens, Silos, etc. + Plort collectors & Auto-feeders)        |
| Landplot upgrades |   🟩   |                                    Fully functional                                     |
|      Gadgets      |   🟩   |                                    Fully functional                                     |
|      Drones       |   🟩   |                                    Fully functional                                     |
|     Upgrades      |   🟩   |                                    Fully functional                                     |
|     Currency      |   🟩   |                                    Fully functional                                     |
|   Market Prices   |   🟩   |                                    Fully functional                                     |
|     Sprinkles     |   🟩   |                                    Fully functional                                     |
|     Refinery      |   🟩   |                                    Fully functional                                     |
|     World FX      |   🟩   |                                    Fully functional                                     |
|    Slimepedia     |   🟩   |                                    Fully functional                                     |
|        Map        |   🟩   |                 Fully functional (Including player icons & locator bar)                 |
|  Map Expansions   |   🟩   |                                    Fully functional                                     |
|    Teleporters    |   🟩   |                                    Fully functional                                     |
|      Geysers      |   🟩   |                                    Fully functional                                     |
|      Weather      |   🟩   |                                    Fully functional                                     |
|     Lightning     |   🟩   |                                    Fully functional                                     |
|     Tornados      |   🟩   |                                    Fully functional                                     |
|       Time        |   🟩   |                                    Fully functional                                     |
|  Gray Labyrinth   |   🟨   | Switches, Barriers, Depositors, Gates, Rewards, Disruption; Stabilizers & Beams missing |
|   The Sanctuary   |   🟨   |  Unlock, sprinkles, canisters, themes, rooms, shop; Caretaker & decorations per player  |
| NPC Conversations |   🟩   |                                    Fully functional                                     |
|  Multiplayer API  |   🟩   |                                    Fully functional                                     |

**Known gaps:**
- Toy shop: the queue of bought toys still waiting at the kiosk comes from each player's own save, so a joining player whose save still has undelivered toys gets them dispensed into the host's world.

# ⚠ MAKE SURE TO BACK UP YOUR SAVES ⚠

The mod does this for you as a safety net, but it is no substitute for your own backups.

## Save Backups
Just before the game writes an autosave, the mod copies the loaded save's autosaves 
(including your profile and settings files) into `UserData/SR2MP/SaveBackups/`:

```
SaveBackups/
  2026-07-30_08-36-59/              <- when the backup was taken
    20260722122725_36/              <- the save
      beforehost/                   <- what you were doing at the time
        Steam/76561199228048027/    <- copy this folder into AppData/LocalLow/MonomiPark/SlimeRancher2/
          20260722122725_36_0.sav
          ...
          slimerancher.prf
          slimerancher.cfg
```

The top level is a list of every backup, 
and the inner folders mirror the actual game's structure, 
so you can easily drag and drop them in.
The backup phase is one of the following:

| Phase             | Taken when                                                 |
|-------------------|------------------------------------------------------------|
| `offline`         | autosave in singleplayer                                   |
| `beforehost`      | the moment you open your world to others                   |
| `whilehost`       | autosaves while you are hosting a world                    |
| `beforeconnect`   | the moment before you connect, before anything is replaced |
| `whileconnect`    | autosaves while you are connected to a world               |
| `afterdisconnect` | you left a host's session                                  |
| `manual`          | you ran the `backupsave` console command                   |

The `beforehost`, `beforeconnect` and `afterdisconnect` backups always run,
even with `save_backups` off so joining with the wrong save can always be undone.
Turning on `save_backup_disable_forced` skips those three as well, leaving you with no safety net at all.
The autosave phases are skipped when the files are identical to the newest backup already stored for that save,

Backups are **never deleted** unless you turn on `save_backup_pruning`,
which then keeps the newest `save_backup_count` (10) backups **per phase** and deletes the rest.
Loading a save and quitting without playing does not create a redundant backup.
Run `backupsave` in the console (F11 -> backupsave) to back up your save on demand,
or turn the whole thing off with the `save_backups` option.

To restore a backup, close the game and copy the store folder inside it (`Steam/...`) back into
`AppData/LocalLow/MonomiPark/SlimeRancher2` on Windows, overwriting when asked.
Only the backed up save's files are overwritten, your other saves are left alone.

## Installation
1. Download and run the [MelonLoader Installer](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.1/MelonLoader.Installer.exe) and install **MelonLoader 0.7.3** onto Slime Rancher 2.
2. Download [Starlight 4.0.3](https://github.com/ThatFinnDev/Starlight/releases/download/v4.0.3/Starlight.dll) and put `Starlight.dll` into the `Mods` folder of your game install.
3. Download `Ranching Together` from [Nexusmods](https://www.nexusmods.com/slimerancher2/mods/118) or our [Discord Server](https://discord.com/invite/a7wfBw5feU) and put it into the `Mods` folder.
4. Launch the game, the multiplayer menu appears on the main menu screen.
5. Configure the settings in the first time settings screen.

Every player needs the **same version** of all mods being played with.

The host's save is the one being played on, everyone else joins it.
If you wanna host, join the save file you wanna play on.

If you wanna connect, join an **EMPTY SAVE FILE** and always use this one to connect to this hosts save.
Once you join the host, **ALL YOUR PROGRESS ON THE SAVE FILE YOU JOINED WITH IS ERASED AND REPLACED!**

### Requirements
- [MelonLoader 0.7.3](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.1/MelonLoader.Installer.exe)
- [Starlight 4.0.3](https://github.com/ThatFinnDev/Starlight/releases/download/v4.0.3/Starlight.dll)

## Configuration
All settings are in MelonPreferences (`UserData/MelonPreferences.cfg`) under the `SR2MP` category.
They are also editable via the *Mod Settings* screen (`ESC -> Mods -> Mods Settings -> Scroll down`).
All settings you change via the Multiplayer GUI are automatically saved.

|            Setting            | Default  |                                      Description                                       |
|:-----------------------------:|:--------:|:--------------------------------------------------------------------------------------:|
|          `username`           | `Player` |                          Your name as shown to other players                           |
|       `username_color`        | `FFFFFF` |                            Hex color of your username label                            |
|         `allow_cheats`        | `false`  |                         Allows cheats in multiplayer sessions                          |
|        `streamer_mode`        | `false`  |                     Hides IPs and join codes in the multiplayer UI                     |
|          `host_port`          |  `1919`  |                               UDP port used when hosting                               |
|   `recent_ip` / `recent_port` | *empty*  |                       Last used connection, saved automatically                        |
|     `firewall_exceptions`     | *empty*  |                Firewall rules created by the mod, managed automatically                |
|       `packet_size_log`       | `false`  |                           Logs packet sizes (debug setting)                            |
|        `packet_ack_log`       |  `true`  |                 Logs reliable-packet acknowledgements (debug setting)                  |
|        `save_backups`         |  `true`  | Backs up your save files once per play session (hosting and connecting always back up) |
|     `save_backup_pruning`     | `false`  |                    Deletes old backups instead of keeping them all                     |
|      `save_backup_count`      |   `10`   |                    Backups kept per save (when pruning is enabled)                     |
| `save_backup_disable_forced`  | `false`  | Also skips the host, connect and disconnect backups, **leaves you with no safety net** |
|    `internal_setup_ui_new`    |  `true`  |                        Internal first time settings screen flag                        |
| `the_rock_plorts_are_coming`  | `false`  |               Rock Plort Mode, a troll mode, **FULLY BREAKS SAVES!**                   |

## Official Mod Support
These mods have dedicated Ranching Together integration, they are fully working in multiplayer when installed:

|                             Mod                             | Version |                             Nexus                             |                           Source Code                           |                         Support                         |
|:-----------------------------------------------------------:|:-------:|:-------------------------------------------------------------:|:---------------------------------------------------------------:|:-------------------------------------------------------:|
| **Better Drones**: explorer drones really fly to resources  | `0.1.1` | [NexusMods](https://www.nexusmods.com/slimerancher2/mods/177) |        [GitHub](https://github.com/pyeight/BetterDrones)        | [Discord Server](https://discord.com/invite/a7wfBw5feU) |
| **Placement Improvements**: custom placement rules & colors | `1.0.0` | [NexusMods](https://www.nexusmods.com/slimerancher2/mods/179) | [GitHub](https://github.com/BlackthornZZ/PlacementImprovements) |    Discord: `lunar_snail` (ID: `426024775333314570`)    |
| **Vacuum Modifications**: custom limits & instant transfers | `2.3.7` | [NexusMods](https://www.nexusmods.com/slimerancher2/mods/45)  |  [GitHub](https://github.com/Bread-Ch4n/Vacuum-Modifications)   |   Discord: `.bread_chan.` (ID: `212243828831289344`)    |

### Mod Integration Requirements
If you want your mod to be supported by us, it has to meet a few requirements so we can build and maintain an integration for it:
- **It must depend on [Starlight](https://github.com/ThatFinnDev/Starlight)**, this gives us reliable mod detection and versioning to work with.
- **It must be somewhat structured.** Our integrations find your mod's members via reflection, 
  so the logic has to live in stable, reasonable named classes and methods (helpers, preference entries).
  Renaming or restructuring these breaks the integration, so let us know before you do it.
- Your mod must be uploaded on [NexusMods](https://www.nexusmods.com/games/slimerancher2/mods)
- The source code must be publicly available so we can review what actually needs syncing.

If your mod fits (or you need help making it fit) the requirements, reach out on the [Discord Server](https://discord.com/invite/a7wfBw5feU).

## Forks
As a community policy, we ask that forks of this project:
- provide a clearly visible way to contact their developer (e.g. a Discord handle or e-mail) in their README,
- are taken down if the Ranching Together developers request it.

Note that we may cherry-pick content from your fork back into Ranching Together if we want to.  
(forks **MUST BE** GPLv3 like this project, which allows this).

If you want to build on this project, you can reach out to us on the [Discord Server](https://discord.com/invite/a7wfBw5feU) to discuss first.

## Contributing
Contributions are welcome! A few ground rules:
- **Fully vibe-coded commits will be declined and not merged.** AI assistance is fine,
  but you need to understand every line you submit, have tested it in-game, and be able to explain
  and defend it in review. PRs that are clearly unreviewed AI output waste everyone's time.  
- Match the existing code style and architecture (see the sync pattern used across the codebase).
- Test your changes in an actual multiplayer session (host + multiple clients) before opening a PR.
- Keep the build warning-clean.

If you are unsure whether a feature fits or a practise is good, you can ask on the [Discord Server](https://discord.com/invite/a7wfBw5feU) before you start.

## Acknowledgements
Special thanks to:
- [ThatFinn](https://github.com/ThatFinnDev) for developing and maintaining their [Starlight Core Essentials Mod](https://github.com/ThatFinnDev/Starlight)
- [Lachee](https://github.com/Lachee/) for developing and maintaining their [Discord RPC C# library](https://github.com/Lachee/discord-rpc-csharp)
- Each and every one of you for supporting us!
