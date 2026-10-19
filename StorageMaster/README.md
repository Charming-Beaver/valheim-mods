# Storage Master

Sort your inventory, lock slots, quick stack into nearby chests, and open **every chest around you in one window** to move, find, gather and tidy up your stuff.

Everything looks and feels like Valheim's own UI, and the whole mod runs on your side only: it works on any server, and nobody else needs to install it.

**Download:** [Thunderstore](https://thunderstore.io/c/valheim/p/CharmingBeaver/StorageMaster/) (r2modman) · [Nexus Mods](https://www.nexusmods.com/valheim/mods/4479) · [GitHub releases](https://github.com/Charming-Beaver/valheim-mods/releases/tag/storagemaster-v1.0.1)

![The Storage window: your inventory next to every chest around you](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/overview.jpg)

---

## Features

### Inventory sorting

![The buttons next to your inventory, and naming a chest in the normal chest window](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/inventory-buttons.jpg)

- The buttons are in a **menu next to your inventory**: click the box with three bars (above your weight) to open or close it. **Ctrl + drag** the box to move it, for example out of another mod's way (the key can be changed in Options).
- **Sort** tidies everything **below the hotbar**. Your hotbar row is never moved.
- Partial stacks of the same item are merged first, so three stacks of 30 wood become one stack of 90.
- **By: …** switches what Sort orders by:
  - **Name A–Z** and **Name Z–A**
  - **Type**: weapons, ammo, armor, tools, food, materials, trophies
  - **Quality**: best first
  - **Weight**: heaviest first
  - **Amount**: biggest stacks first
- **Sort chest** is in the menu next to any chest you open and sorts it the same way.
- **Auto-sort** (optional) sorts your inventory every time you pick something up.

### Locked slots
- **Alt + click** a slot in your inventory to lock it. Locked slots are tinted orange and get a small lock badge.
- Sort, auto-sort and quick stack never touch locked slots, and neither does the game's own **Place stacks** button.
- Locks are saved with your character.

### Quick stack
- One click (or a hotkey) puts your items into **nearby chests that already hold the same kind of item**.
- Your hotbar, equipped gear and locked slots always stay with you.
- A short summary shows how many items went into how many chests.

### Storage window
Opens every chest around you at once, next to your inventory, using the game's own item grids.

![Hide inventory gives the chests the whole window](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/hide-inventory.jpg)

- **Move items freely** between your inventory and all chests:
  - **Click** to pick up and put down
  - **Shift + click** to take half a stack
  - **Ctrl + click** to send an item straight to you, or from you to a chest that already has it
  - **Esc** puts a carried item back
- **Find item:** press **F** (as in the build menu) or click the field, type a name and every matching slot in every chest lights up; the rest fade out.

![Find item highlights every match in every chest](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/find-item.jpg)

- **Choose an item:** right-click any item to see how many you have in total, how many chests it's spread over, and where every stack is.

![A chosen item: every stack lights up, with totals in the panel](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/chosen-item.jpg)

- **Gather here:** pulls all of the chosen item into one chest. Whatever doesn't fit flows into the next chests. Optionally it also takes them from your inventory (never from your hotbar or locked slots).

| Before | After Gather here |
|---|---|
| ![Bone fragments spread over six chests](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/gather-before.jpg) | ![All bone fragments in one chest](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/gather-after.jpg) |

- **Tidy up:** every item that's split over several chests moves into the chest that already holds the most of it. Then all chests get sorted.

| Before | After Tidy up |
|---|---|
| ![Items scattered over many chests](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/tidy-before.jpg) | ![Each item brought together and every chest sorted](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/tidy-after.jpg) |

- **Sort all**, plus **Sort** and **Take all** on each chest.
- **Quick stack** into all the open chests at once.
- **Name your chests:** click a chest's title to name it. The name appears when you look at the chest in the world. The **X** button removes it.
- **Skip a chest:** tick **Skip** on a chest (for example your friend's) and Quick stack, Gather, Tidy up and Sort all will leave it alone. You can still move items by hand. Only you see this setting.
- **Hide inventory:** gives the chests the whole window, so many more fit side by side on large screens.
- **Settings:** choose how far away (5–50 m) and how many chests (4–60) the window opens. Up to **20 m and about 30 chests** is recommended.

![The Settings popup](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/settings.jpg)

### Name chests anywhere
The title of the normal chest window (opened with **E**) is a text field too. Click it, type a name and press **Enter**. While you type, your keys only type: E won't close the chest and WASD won't move you.

### Info and Options
The **Info** button in the inventory menu explains every feature and lists your hotkeys.

**Options** (next to it, or **Shift + F6**) lets you **change hotkeys** (with warnings when Valheim already uses a key), adjust the main settings, and choose the drag key and the menu buttons' positions. **Reset** there puts a menu button back if you've moved it out of reach.

![The Info window with clickable hotkeys](https://raw.githubusercontent.com/Charming-Beaver/valheim-mods/main/StorageMaster/images/info-window.jpg)

---

## Hotkeys

| Action | Default |
|---|---|
| Open the Storage window | `Shift + F1` |
| Sort inventory | `Shift + F4` |
| Quick stack | `Shift + F3` |
| Options | `Shift + F6` |
| Auto-sort on / off | not set |
| Info window | not set |
| Lock / unlock a slot | `Alt + click` |

Change them in **Options**. Click a key, then press the new one; Esc cancels and Backspace removes the key. You can also edit them in the config file.

---

## Multiplayer

- **Client-side only.** The server and other players don't need the mod.
- **Safe with other players.** Before touching a chest, Storage Master asks for it the same way the game's own **Place stacks** button does. The chest is handed over properly, and chests someone else has open are skipped ("In use by someone else").
- While the Storage window is open, the chests it shows count as **in use**, just like a normal open chest. Close the window and they're free again.
- Chest names are stored on the chest itself, so everyone with the mod sees them. **Skip** and **locked slots** are personal and saved with your character.
- Respects wards: you only see chests you're allowed to open. Tombstones are never touched.

---

## Installation

**With Vortex:** click **Mod Manager Download** on the [Nexus page](https://www.nexusmods.com/valheim/mods/4479).

**With r2modman or Thunderstore Mod Manager:** install it from there; BepInEx is installed automatically.

**Manually:**
1. Install [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
2. Copy `StorageMaster.dll` into `Valheim/BepInEx/plugins/StorageMaster/`.
3. Start the game.

## Configuration

All settings live in `BepInEx/config/charmingbeaver.valheim.storagemaster.cfg`. Most of them can also be changed in game from **Options** and the Storage window's **Settings**.

| Section | Setting | Default | |
|---|---|---|---|
| Sorting | SortMode | NameAsc | What Sort orders by |
| Sorting | AutoSort | false | Sort on every pickup |
| QuickStack | Range | 10 | Meters around you |
| QuickStack | SkipHotbar | true | Keep the hotbar out of quick stack |
| Storage | Range | 15 | Meters around you |
| Storage | MaxChests | 24 | Nearest chests first |
| Storage | ShowInventory | true | Show your inventory in the Storage window |

## Compatibility

- Made for keyboard and mouse. Controllers aren't supported in the mod's windows.
- Other mods that change the inventory screen or chest handling (sorting, auto-storing, quick stacking) may clash with this one. If something behaves oddly, try without them first.

## Support

If Storage Master saves you some chest-digging, you can buy me a coffee on [Ko-fi](https://ko-fi.com/charmingbeaver). It's completely optional and very appreciated.

[![Support me on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/charmingbeaver)

## Issues and source

Bug reports and ideas: [GitHub issues](https://github.com/Charming-Beaver/valheim-mods/issues).
Source code: [Charming-Beaver/valheim-mods](https://github.com/Charming-Beaver/valheim-mods), [MIT licensed](https://github.com/Charming-Beaver/valheim-mods/blob/main/LICENSE).
