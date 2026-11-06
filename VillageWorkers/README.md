# Village Workers

A BepInEx mod for Valheim that adds villagers: viking NPCs you meet in the world, invite to your village, and put to work.

## Quick start

1. **Meet a wanderer.** Wanderers spawn now and then during the day in the Meadows, Black Forest and Plains, 30-60 m away from you and never inside your base. They are rare: each area you walk through rolls once on entering and then every 5 minutes, with an 8% chance, and only one wanderer exists per neighbourhood. Exploring new ground in daylight is the best way to meet them; raise `SpawnChance` or lower `SpawnInterval` in the config to meet more. To get one right away, open the console (F5) and type `devcommands`, then `vw_spawn` (on a server, only admins can).
2. **Invite them.** Look at the wanderer and press **E**. They follow you as a *recruit*.
3. **Give them a bed.** Press **E** on the recruit, or open the village window (**F7**), then choose **Assign bed** and look at a free bed and press **E**. They are now a villager.
4. **Give them chests.** Click **Choose chests**, then look at chests and press **E** to add or remove them. Press **Esc** when you're done.
5. **Give them tasks.** Click **Add task**. Tasks are worked from top to bottom. When one can't continue (chest full, no tool, nothing left to do), the villager moves on to the next one and checks again later.
6. **Give them tools.** Click **Inventory** (the normal container screen), or hover over the villager and press a hotbar key (1-8) to hand them that item. Villagers with no tool ask for one in a speech bubble. When a tool or weapon breaks, the villager carries it to the nearest crafting station that can repair it (the same station and level a player would need, near them or near their bed) and fixes it for free, just like the repair button.

## Controls

| Where | Key | Action |
|---|---|---|
| Wanderer | E | Invite to your village |
| Villager / recruit | E | Open management |
| Villager / recruit | Shift + E | Rename (like a tamed animal) |
| Villager | 1-8 | Give the hotbar item |
| Anywhere | F7 | Village window (configurable) |
| Choosing a bed or chests | E / Esc | Select / finish |
| Choosing a circle area | Mouse wheel, E, Esc | Resize, confirm, cancel |
| Choosing a box area | E, Mouse wheel, Esc | Mark a corner, then the opposite one; turn; cancel |

## Tasks

| Task | What they do | Needs |
|---|---|---|
| Woodcutting | Fells trees, splits logs, clears stumps | Axe (the tool tier must be high enough for the wood) |
| Mining | Rocks, copper, tin, silver, obsidian and so on | Pickaxe (tier high enough) |
| Gathering | Berries, mushrooms, flint, stones, branches, thistle, honey, sap | - |
| Farming | Harvests ripe crops and replants them with seeds from the chests. With an area set, also plants the empty cultivated ground in it | Seeds in a chest |
| Hunting | Hunts deer, boar, necks and similar, then brings the loot home | A weapon (a bow works best against deer) |
| Crafting | Keeps a stock of any recipe ("keep 20 arrows") at the right station | Materials in the chests and the station nearby |
| Smelting & kilns | Fills smelters, charcoal kilns, blast furnaces, windmills, spinning wheels and eitr refineries with ore and fuel, then collects the output | Ore and fuel in the chests |
| Cooking | Runs cooking stations and ovens, and can keep a stock of cauldron food | Raw food (and fuel) in the chests |
| Brewing | Fills and taps fermenters, and can keep a stock of mead bases | Mead bases or their ingredients |
| Tend fires | Keeps hearths, fires, braziers and torches fuelled | Fuel in the chests |
| Guarding | Patrols and fights monsters | A weapon |

### Task settings

- **Set area / Auto area.** Mark the part of the world to work in: a circle (look at its middle and size it with the mouse wheel) or a box (look at one corner and press **E**, then at the opposite corner). The mouse wheel turns a box in the same steps as building pieces, so it lines up with your fences and buildings. Without an area, outdoor tasks start searching close to the bed and widen the search step by step, up to `MaxSearchRadius`. Station tasks use stations within `StationRadius` of the bed.
- **Farming fields.** A farmer with an area also plants the empty cultivated ground in it, with the seeds in their chests (only the crops chosen in Options, if any). They don't plant your carrots, turnips or onions to grow seeds from them, unless you choose that crop in Options. Crops go as close together as lets every one of them grow, also once they're grown, and not under roofs or trees. Several farmers can share a field, and their areas can overlap: they never go for the same crop or plant on the same spot.
- **Own chests.** Give a task its own chests. When they are full, the villager moves on to the next task. Tasks without their own chests use the villager's chests. Materials can be taken from any of the villager's chests.
- **Options.** Limit a task to certain things, such as only fine wood, only copper, only deer, or only the charcoal kiln. You can also edit crafting orders here.
- **Priority.** Use ▲ / ▼ to change the order, the checkbox to pause a task, and X to remove it.

### Daily routine and combat

- Villagers work from sunrise to dusk (`WorkStart`/`WorkEnd`). Then they store the day's loot and go to bed. They open doors on the way.
- **When attacked:** *Fight back* uses the best weapon in their inventory (or bare fists). *Flee* runs away, and they return to work afterwards.
- They wear the best armour in their inventory automatically.
- If a villager dies, everything they carried drops on the ground.

## Statistics

The **Statistics** tab shows:

- Summary tiles
- A ranking (today, last 7 days, total, average per day)
- A daily output line chart (hover for exact values)
- A bar breakdown per villager, per item or per task
- A timeline of what each villager did and when

Click a villager in the ranking to focus all charts on them. Each villager keeps their colour across all charts.

## Console commands

- `vw_spawn`: spawn a wanderer next to you. It's a cheat like the game's own `spawn`: type `devcommands` first, and on a server only admins can use it
- `vw_list`: list loaded villagers and what they are doing
- `vw_window`: open the village window

## Configuration

`BepInEx/config/charmingbeaver.valheim.villageworkers.cfg` is created on first start. Changes to the file take effect as soon as it's saved, without restarting. It includes:

- Hotkey
- Spawn chance, interval and biomes
- Work hours
- Search radii
- Villager health
- Work speed
- Crafting time
- Tool wear
- Default hunting targets
- How many days of detailed statistics to keep

In multiplayer, the server's settings are used for everything except the hotkey and debug logging, which stay your own (see [Multiplayer and servers](#multiplayer-and-servers)).

## Installation

**With r2modman or Thunderstore Mod Manager:** install Village Workers from there; BepInEx is installed automatically.

**Manually:**
1. Install [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
2. Copy `VillageWorkers.dll` into `Valheim/BepInEx/plugins/VillageWorkers/`.
3. Start the game.

## Multiplayer and servers

Villagers are run by whoever is closest to them: usually a nearby player's game, sometimes the server. So in multiplayer **the server and every player need Village Workers**, and the server decides the settings for everyone.

### Singleplayer

Install the mod (see [Installation](#installation)) and play. Nothing else to set up. Your config file is used as it is.

### Hosting from the game

If you start a world with **Start server** ticked, your game is the server:

1. Install the mod as for singleplayer.
2. Your config file's settings (spawning, work hours, health, speed and so on) are sent to everyone who joins. Change them in your config file; players who are online get the changes right away.
3. Every friend who joins needs the mod too (see [Joining a server](#joining-a-server)).

### Dedicated server

1. **Install BepInEx on the server.** Copy the contents of the `BepInExPack_Valheim` folder from [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) into the server's folder (next to `valheim_server.exe`, or `valheim_server.x86_64` on Linux).
   - **Windows:** start the server the way you normally do, for example with `start_headless_server.bat`. BepInEx loads by itself.
   - **Linux:** start the server with `start_server_bepinex.sh` from the pack instead of `start_server.sh`, after putting your server's name, world and password into it.
   - **Server hosts** (rented servers): most have a mod or BepInEx option in their control panel. Otherwise upload the same files through the host's file manager or FTP.
2. **Install Village Workers on the server.** Copy `VillageWorkers.dll` into the server's `BepInEx/plugins/VillageWorkers/` folder.
3. **Start the server once.** The log (`BepInEx/LogOutput.log`, or the console) shows `Village Workers ... loaded on a dedicated server`, and the config file `BepInEx/config/charmingbeaver.valheim.villageworkers.cfg` is created.
4. **Adjust the settings** in that file if you like. They apply to everyone on the server. The server reloads the file when it's saved and sends the new settings to the players who are online, so there's no need to restart.
5. **Tell your players** to install the same version of the mod (see below). The server turns away anyone who doesn't have it.

To spawn a wanderer for testing, add your Steam ID (or your platform ID for crossplay) to the server's `adminlist.txt`, then type `devcommands` and `vw_spawn` in the in-game console.

### Joining a server

Yes, every player who joins a server with Village Workers needs it too:

1. Install Village Workers (see [Installation](#installation)). If you use r2modman or Thunderstore Mod Manager and the server owner shares a mod profile code, importing it gets you exactly the server's mods.
2. Use a **matching version**: the first two numbers have to be the same as the server's. For example, 1.2.0 and 1.2.3 can play together, 1.2.x and 1.3.x can't.
3. Join as usual. You don't need to change any settings: the server's settings are used while you're connected, and your own config file is left alone and used again in singleplayer. Only your village window hotkey and debug logging stay your own.

If you can't join:
- **"Incompatible version"** without anything else: you don't have Village Workers installed (or BepInEx isn't loading). Install it and try again.
- **"This server uses Village Workers 1.2.x, you have 1.1.0"**: update (or downgrade) the mod to the version the server names.

Console players can't install mods, so they can't join a server that uses Village Workers.

### When the server doesn't have the mod

Players who have Village Workers can still join a server without it, and villagers work. But nothing checks that everyone has the mod: players without it don't see villagers at all, and villagers follow the settings of whichever player happens to be running them. Installing it on the server avoids both.

## Limitations

- Like everything in Valheim, villagers only work while their area is loaded, meaning a player is nearby. They do not work while you are far away.
- Villagers that are far away still show up in the window and statistics, but you can only give orders to loaded villagers.
- Statistics are stored on each villager. If a villager dies or is dismissed, their history goes with them.
- In multiplayer, every player needs the mod (see [Multiplayer and servers](#multiplayer-and-servers)).

## Building from source

Run `dotnet build -c Release` in the `VillageWorkers` folder. The DLL is copied to `BepInEx/plugins/VillageWorkers/`. The build needs BepInEx installed in the game folder (set it with `-p:ValheimDir="<path>"` if Valheim isn't in `D:\SteamLibrary\steamapps\common\Valheim`) and uses `Krafs.Publicizer` to access game internals.
