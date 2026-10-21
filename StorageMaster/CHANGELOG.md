# Changelog

## Unreleased

### Added
- Works with Adventure Backpacks: Quick stack also puts items from the backpack you're wearing into nearby chests (setting "Quick stack from the backpack" in Options; when it's off, the open backpack gets a Quick stack button of its own), and Sort chest becomes Sort backpack when the backpack is open
- The Storage window shows the backpack you're wearing under your inventory (scrolling when both don't fit). Ctrl + click moves its items into the chests, and Tidy up, Sort all and Gather here leave it alone unless you tick "Also gather from my backpack"

### Changed
- The Storage window opens right away and fills in nearest chest first, instead of freezing the game for a moment in bases with many chests

### Fixed
- With Quick Stack Store Sort Trash Restock, opening the Storage window, Quick stack and Gather here no longer quick stack your inventory into the chests
- With SeneaL UI, Quick stack and Place stacks keep your hotbar and locked slots again, both Storage Master's and SeneaL UI's, and Quick stack counts only the items it moved
- With SeneaL UI, Sort and auto-sort leave its equipment and quick slots alone and no longer put items in slots it doesn't let you use, and the Storage window shows only your regular inventory rows
- With SeneaL UI, Alt + click locks a slot only once, with SeneaL UI's lock: Sort, Quick stack and the Storage window keep SeneaL UI's locked slots, and slots you locked with Storage Master before become SeneaL UI's locks

## 1.2.0

### Added
- Works with ExtraSlots: Sort, auto-sort, Quick stack and Gather here leave its equipment, food, ammo, misc and quick slots alone, and the Storage window shows only your regular inventory rows
- New Options window (button in the inventory menu, Shift + F6) for hotkeys, settings and the menu buttons, with Reset to bring back a menu button moved out of reach
- F jumps into Find item in the Storage window, as in the build menu
- The Storage window says how many chests are over the Max chests limit, and Tidy up, Sort all, Gather here and Quick stack say when chests were left out because they were still opening or in use by someone else

### Changed
- The buttons next to the inventory and the chest are now in menus that open from a small box above the weight. Ctrl + drag the box to move it out of other mods' way (key and position can be set in Options); the menus remember whether they're open
- Info now explains the features and lists your hotkeys; changing them and the settings moved to Options
- The menus, the Storage window, Info and Options use the inventory's darker background

### Fixed
- Tidy up brings items together much more completely when chests are nearly full: it merges partial stacks first and keeps going until nothing more can be moved
- Tidy up and Gather here no longer fill the log with "Trying to add item to occupied slot" warnings when chests are nearly full
- Find item lights up the slots of matching items without tinting the items themselves, so they keep their full color
- Text fields other than Find item no longer show an F key that does nothing
- The By and Auto-sort buttons update when their settings are changed in the config file or a config manager

## 1.0.1

- Pressing Quick stack again within a second no longer replaces the summary with a wrong "the chests are full" message
- The mouse wheel scrolls the Storage window and the Info window at a normal speed. One notch used to move them only a couple of pixels
- Window titles and headers no longer turn into magenta blocks when another mod loads its own Norse font
- No more "LiberationSans SDF Font Asset was not found" warnings in the log
- With SeneaL UI, items in the Storage window show their tooltip (name and description) again, and the buttons' tooltips work

## 1.0.0

First release.

- Inventory sorting below the hotbar, merging partial stacks, by name, type, quality, weight or amount
- Sort chest button for the open chest, and optional auto-sort on pickup
- Locked slots (Alt + click) that sorting, quick stack and Place stacks leave alone
- Quick stack into nearby chests that already hold the same items
- Storage window that opens every nearby chest at once: move items, find items, gather an item into one chest, tidy up, sort all, take all
- Chest names, set in the Storage window or the normal chest window, shown when you look at a chest
- Skip chests so Quick stack, Gather, Tidy up and Sort all leave them alone
- Hide inventory mode and Settings (range and number of chests) in the Storage window
- Info window with every feature explained, in-game hotkey changes and settings
- Multiplayer safe and client-side only
