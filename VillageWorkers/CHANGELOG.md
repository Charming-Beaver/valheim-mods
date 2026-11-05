# Changelog

## Unreleased

### Added
- Works on dedicated servers and when hosting from the game. The server turns away players who don't have the mod or have an incompatible version (telling those with the mod which version to install), and its settings are used by everyone while they're connected, except the hotkey and debug logging
- Changes to the config file take effect as soon as it's saved, without restarting. On a server they're sent to the players who are online
- Guides for singleplayer, hosting, dedicated servers and joining a server in the README

### Changed
- `vw_spawn` is a cheat command like the game's `spawn`: type `devcommands` first, and on a server only admins can use it
