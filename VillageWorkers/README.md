# Village Workers

A BepInEx mod for Valheim that adds villagers: viking NPCs you meet in the world, invite to your village, and put to work.

## Quick start

1. **Meet a wanderer.** Wanderers spawn now and then during the day in the Meadows, Black Forest and Plains, 30-60 m away from you and never inside your base. They are rare: each area you walk through rolls once on entering and then every 5 minutes, with an 8% chance, and only one wanderer exists per neighbourhood. Exploring new ground in daylight is the best way to meet them; raise `SpawnChance` or lower `SpawnInterval` in the config to meet more. To get one right away, open the console (F5) and type `vw_spawn`.
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
| Choosing an area | Mouse wheel, E, Esc | Resize, confirm, cancel |

## Tasks

| Task | What they do | Needs |
|---|---|---|
| Woodcutting | Fells trees, splits logs, clears stumps | Axe (the tool tier must be high enough for the wood) |
| Mining | Rocks, copper, tin, silver, obsidian and so on | Pickaxe (tier high enough) |
| Gathering | Berries, mushrooms, flint, stones, branches, thistle, honey, sap | - |
| Farming | Harvests ripe crops and replants them with seeds from the chests | Seeds in a chest |
| Hunting | Hunts deer, boar, necks and similar, then brings the loot home | A weapon (a bow works best against deer) |
| Crafting | Keeps a stock of any recipe ("keep 20 arrows") at the right station | Materials in the chests and the station nearby |
| Smelting & kilns | Fills smelters, charcoal kilns, blast furnaces, windmills, spinning wheels and eitr refineries with ore and fuel, then collects the output | Ore and fuel in the chests |
| Cooking | Runs cooking stations and ovens, and can keep a stock of cauldron food | Raw food (and fuel) in the chests |
| Brewing | Fills and taps fermenters, and can keep a stock of mead bases | Mead bases or their ingredients |
| Tend fires | Keeps hearths, fires, braziers and torches fuelled | Fuel in the chests |
| Guarding | Patrols and fights monsters | A weapon |

### Task settings

- **Set area / Auto area.** Pick a circle in the world to work in. Without an area, outdoor tasks start searching close to the bed and widen the search step by step, up to `MaxSearchRadius`. Station tasks use stations within `StationRadius` of the bed.
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

- `vw_spawn`: spawn a wanderer next to you
- `vw_list`: list loaded villagers and what they are doing
- `vw_window`: open the village window

## Configuration

`BepInEx/config/charmingbeaver.valheim.villageworkers.cfg` is created on first start. It includes:

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

## Limitations

- Like everything in Valheim, villagers only work while their area is loaded, meaning a player is nearby. They do not work while you are far away.
- Villagers that are far away still show up in the window and statistics, but you can only give orders to loaded villagers.
- Statistics are stored on each villager. If a villager dies or is dismissed, their history goes with them.
- In multiplayer, every player needs the mod.

## Building from source

Run `dotnet build -c Release` in the `VillageWorkers` folder. The DLL is copied to `BepInEx/plugins/VillageWorkers/`. The build needs BepInEx installed in the game folder (set it with `-p:ValheimDir="<path>"` if Valheim isn't in `D:\SteamLibrary\steamapps\common\Valheim`) and uses `Krafs.Publicizer` to access game internals.
