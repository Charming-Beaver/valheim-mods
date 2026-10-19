# Changelog

## Unreleased

- Tidy up brings items together much more completely when chests are nearly full: it merges partial stacks first and keeps going until nothing more can be moved
- The Storage window says how many chests are over the Max chests limit, and Tidy up, Sort all, Gather here and Quick stack say when chests were left out because they were still opening or in use by someone else
- Tidy up and Gather here no longer fill the log with "Trying to add item to occupied slot" warnings when chests are nearly full
- Works with ExtraSlots: Sort, auto-sort, Quick stack and Gather here leave its equipment, food, ammo, misc and quick slots alone, and the Storage window shows only your regular inventory rows
- The buttons next to the inventory and the chest are now in menus that open from a small box above the weight. Ctrl + drag the box to move it out of other mods' way (key and position can be set in the Info window); the menus remember whether they're open
- The Info window opens with Shift + F6 by default, so a menu button moved out of reach can always be reset there. A key you had set for it is kept
- The menus, the Storage window and the Info window use the inventory's darker background
- Find item in the Storage window lights up the slots of matching items without tinting the items themselves, so they keep their full color

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
