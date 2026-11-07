# Changelog

## Unreleased

### Added
- Box work areas: mark two opposite corners in the world. The mouse wheel turns the box in the same steps as building pieces. **Set area** asks whether you want a circle or a box
- Farmers with a work area plant the empty cultivated ground in it with seeds from their chests, far enough apart for every crop to grow
- Several farmers can share a field, also with overlapping areas: they never go for the same crop or plant on the same spot, even when other players' games run them
- Works on dedicated servers and when hosting from the game. The server turns away players who don't have the mod or have an incompatible version (telling those with the mod which version to install), and its settings are used by everyone while they're connected, except the hotkey and debug logging
- Changes to the config file take effect as soon as it's saved, without restarting. On a server they're sent to the players who are online
- Guides for singleplayer, hosting, dedicated servers and joining a server in the README

### Changed
- **Breaking:** farmers need a cultivator, like a player planting. They ask for one, take one from their chests, wear it out and have it mended like other tools. A farmer without one stops farming until given one
- Farmers pick all the ripe crops first, then store the harvest and take the seeds in one trip to the chest, and replant them all, instead of walking to the chest for every crop
- `vw_spawn` is a cheat command like the game's `spawn`: type `devcommands` first, and on a server only admins can use it
