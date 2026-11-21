# Ranching Together (Patched): changelog

> **AI disclaimer:** every change in this fork was written with AI (Claude Code, by Anthropic) and tested in-game by the fork's maintainer. It is not made, reviewed or supported by the original Ranching Together team.

This fork builds on [Ranching Together](https://github.com/pyeight/SlimeRancher2Multiplayer) 0.3.9 by Shark (py8), with contributions from AlchlcSystm and PinkTarr. It targets Slime Rancher 2 1.3.0, MelonLoader 0.7.3 and Starlight 4.0.4.

## 0.3.9-Patched.2

**Fixes**
- **Host sent to the main menu when entering the Sanctuary.** A plot's storage could be saved with more slots than the plot really has, and the game failed to load the area with "An error occurred". The extra slots are now cut off when the area loads, so saves that already have this load again (anything in the extra slots is listed in the log). The cause is fixed too: when a player replaced a plot in an area the host didn't have loaded, the old plot's storage was kept. It now starts empty, like the game does.
- **Players stuck when the host's game closes or hangs.** The host now sends a heartbeat every few seconds. If a player hears nothing from the host for 30 seconds, they leave the world with a message instead of waiting forever. Leaving after a lost connection also happens on the game's main thread now, which avoids errors while cleaning up.

**For bug reports**
- If the game stops finishing frames for 8 seconds or more during multiplayer, the log now says what the mod was doing at the time (or that the game itself was busy) and how many network messages were waiting. This helps track down a host that stops responding.

## 0.3.9-Patched

The first public release of the fork. It contains everything from the private test builds below, plus:

- **Own version and update check.** The mod reports itself as `0.3.9-Patched` and checks this fork for updates, not the original mod. A newer release shows a warning with a link to the download page and no longer closes the game.
- **Version guard when joining.** Everyone in a session needs the same version. A host now turns away a player on a different version (including the original 0.3.9) with a message that names both versions, instead of letting the session desync.
- **Clearer join refusals.** The "Connection was denied!" message now says why.
- The multiplayer window title shows the version, for example `SR2MP v0.3.9-Patched`.

What the test builds changed, newest first:

### Test build 6.12

**Fixes**
- **Corral auto-feeders losing food or ignoring their speed.** Changes made by a joining player did reach the host. The real problem was the feeding timer: only one game runs a feeder at a time, and it shared the timer only when food came out. When the other game took over, it fed the "missed" feedings again and drained food on both screens. The timer is now shared after every feeding, it no longer depends on each PC's own clock (which could stall a feeder and then dump food all at once), and quick speed clicks arrive in order.

**Cheat menu**
- **Weather buttons are cleaner.** There is one button per weather type in your area, with the map's weather icon. Clicking it steps that type through Off, Light, Medium and Heavy, and the other types keep running. "Clear all weather here" turns everything off. The buttons update by themselves when the weather changes. Pollen Heavy works now.
- **Spawn tornado works anywhere.** The player who presses it finds the spot. If the host is far away, that player's game runs the tornado and everyone sees it. If that player leaves while the host is nearby, the host takes it over.
- **Sloomber yawns show for everyone.** Pressing the button makes every nearby sloomber yawn, including ones another player's game runs. The other player sees the whole yawn: wind-up, face and sleepy cloud. Sleeping sloombers' yawns are shown too.
- **Grow and ripen gardens covers every garden.** One press from anyone now grows and ripens all ranch gardens, including ones another player's game runs (like the mint mango tree).

### Test build 6.11

**Fixes**
- **Map, slimepedia or pause menu stuck on screen after arriving in a new area.** This was the mod, not the game. About a second after an area finishes loading, the game checks whether a menu is open by looking at whether time is paused. The mod keeps time running in multiplayer, so the game decided no menu was open and took the controls away from it. The mod now hands the controls back to an open menu, and the mouse cursor stays free while a menu is open. F6 can also give an open menu its controls back if this ever happens again.
- **Fruit dropping off plants on other players' screens.** For everyone except the player running the garden, fruit fell off the plant as soon as it became ripe, instead of hanging until it was ready to fall like it normally does. It now stays on the plant.
- **Cheat-ripened fruit showed as unripe to the other players.** Fixed.
- **No more fake "zone load error" lines in the log.** The game runs its load-error check after every area load. The mod now only logs it when there really was an error.

**Cheat menu**
- **Weather can stack.** There are now separate buttons:
  - "Add weather here" puts a weather on top of what is already running, for example wind during a storm.
  - "Set weather here (replaces)" swaps all running weather for the one you picked.
  - "Clear weather here" stops all weather.
  - A label shows what is running in your area right now.
- **Spawn tornado** now picks a spot of open ground near where you look, so the tornado travels and lasts instead of fading at once. Both players see it in the same place, moving the same way, and it fades out smoothly instead of popping away. A tornado normally circles in place for a few seconds before it heads off. Players who join while a tornado is out see it in the right place, and the other player's copy no longer spawns extra slimes.
- **Grow and ripen gardens** now does something you can see right away, at any time of day:
  - every empty spot on a plant gets a ripe fruit
  - every unripe fruit ripens.

  The log line "Cheat menu: gardens" says how many it filled and ripened.

### Test build 6.10
- Other players' vac streams now bend the grass, like your own does.

### Test build 6.9
- You can see another player's vac swirl even when you are not vaccing yourself. Before, it only showed while both players were vaccing.

### Test build 6.8
- More work on making another player's vac swirl visible without vaccing yourself. This attempt did not fix it; 6.9 did.
- Tangle stalk copies:
  - A stalk that withers while it is still growing in no longer stays forever.
  - A withering copy is cleaned up after a short time.
  - Grown stalks no longer show an extra fake bud.

### Test build 6.7
- The tangle bud cheat now shows the plant. Before, it could hide itself straight away.
- Another player's vac swirl points where they aim, not straight up.

### Test build 6.6
- **New cheat menu buttons** (in Starlight's cheat menu, only when the host allows cheats):
  - rest until morning, noon, evening or midnight, without going to the ranch house
  - real, synced weather for your area
  - spawn a tornado
  - grow a tangle bud stalk where you look
  - grow and ripen ranch gardens
  - make nearby sloombers yawn
  - start dervish cyclones
  - burst the gordo you look at

  Clients ask the host, so the result is the same for everyone.
- Several fixes to show another player's vac swirl.

### Test build 6.5
- **Garden fixes.** Planted crops now show up properly for everyone:
  - Only the player who planted something plants it. The other players get the host's result, so a crop no longer goes missing on one screen.
  - Deluxe garden plots keep their deluxe crops after a reload.
  - Gardens pause together with their ranch area, so a garden no longer freezes for the others while the host is away.
  - The host decides who runs each garden, so two players can no longer both claim it and leave it frozen.
  - Fruit that shows up a moment before its plant is attached once the plant arrives.
- Connection fix: a reliable message that had to be resent is no longer thrown away as out of order.
- Another player's vac now shows sucking or shooting correctly.

### Test build 6.4
- You can see other players':
  - vac stream and Pulse Wave
  - tangle bud stalks and grab vines
- Weather icons on the map match the host's for clients.

### Test builds 6.1 to 6.3: start-up crash fixes
- Builds 5 and 6 crashed the game while it started. The cause was two game functions the mod hooked into, whose code the game shares with hundreds of other functions. Those hooks are gone, and the game starts normally again from 6.3.
- 6.1 and 6.2 were diagnostic steps to find this.

### Test build 6
- **Starlight Strand showing as unknown on the map.** Older saves could have the area's map revealed without the zone being unlocked. The host repairs this when it starts the server and before each join, and joiners repair it after getting the host's map.

### Test build 5: the "polish everything" build
- **Shared progress.** The host's save is the source of truth, and anything anyone unlocks is shared live. Each player still picks their own look. This covers:
  - vac palettes
  - Sanctuary room themes and rooms
  - slime appearances and secret styles
  - rewards kiosk progress
- **Other players' avatars:**
  - Each avatar shows that player's own vac colour, vac stream and Pulse Wave.
  - You walk through other players instead of getting stuck on them.
- **Sanctuary:**
  - Room themes, sprinkle canisters and the Caretaker shop are shared.
  - Sprinkles are no longer produced twice.
  - The boat ride no longer freezes.
- **Every currency** comes from the host when you join, sprinkles included.
- **Things that happened twice now happen once:**
  - Crates break, and give their rewards, once.
  - Gordo rewards come from the player who fed the last piece.
  - Treasure pods and Labyrinth rewards give their loot once.
  - Geysers and linked cannons fire once for everyone.
  - Explorer drones, Quantum Cloud extraction and drone fuel run on one game, so income is not counted once per player.
- **Toys bought from the shop** appear for everyone.
- **Ranch while you are away:**
  - The catch-up for areas nobody was in runs once, not once per player.
  - Every slime gets fed during catch-up, including slimes another player owns.
  - Auto-feeders run on one game.
- **Your actions reach the host:**
  - Garden growth reaches the host's save.
  - Plort sales lower the market price for everyone.
- **Storage, coops and plots** stay correct when an area is not loaded on your game, and a joiner's coop collector gets the host's eggs.
- **Labyrinth:** everyone shares the host's prisma disruption forecast, and barrier times no longer fight between players.
- **Slime behaviours** are shown to everyone:
  - Tabby stalking and stealth.
  - Sloombers show their yawn wind-up, and nearby slimes get sleepy once.
  - Tangle buds grow once.
- **Weather and map:**
  - Joiners get the host's whole weather forecast, so the map shows the same weather icons.
  - Weather changes settle instead of restarting every state.
  - Zones unlocked by another player unlock properly without a reload.
- **Getting stuck in place:**
  - A frozen player is released automatically when nothing should be holding them.
  - Very long area loads stop holding time at zero.
  - F6 reports more and frees more.

### Test build 4
- **Map sharing.** Joiners get the host's revealed areas and unlocked zones, and new ones are shared live.
- **Ringtails** no longer turn into two stacked statues.
- A ringtail statue that lost its slime is replaced with a ringtail instead of breaking.

### Test build 3
- **Treasure pods fixed.** Pods whose id starts with a zero were saved twice by older versions. That made reward pods not open for the other player, and a save that joined with an old version and later hosted could not send its world to anyone. Old saves are repaired automatically.
- Joining no longer fails on plots whose storage the host never built.

### Test build 2
- **Joining and resyncing:**
  - The world data is sent in a steady stream instead of one burst that tunnels and routers dropped, so joins and resyncs complete.
  - Only missing pieces are resent.
- **Slimes and items:**
  - Slimes are no longer deleted for everyone by one player's out-of-date copy.
  - Only the player running a slime lets it eat, so food and plorts are not eaten twice.
- **Carried items** stay with the player holding them, and anyone can pick up items owned by another player.
- **Join codes** also work for players on the host's home network.
- **Things that happen in places you are not:** treasure pods, resource nodes, map unlocks, puzzle slots, plort depositors, prisma barriers and gordos are recorded even when that area is not loaded on your game.
- **Time skips:** a second player sleeping or getting knocked out can no longer cut someone else's time skip short.
- **F6 "unstuck" key:** logs what is stopping you from moving and frees you.
- **Solo testing:** two copies of the game on one PC can play together.
- **Test build tag:** the window title shows the test build, so you can check everyone matches.
