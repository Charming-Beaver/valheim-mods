# Charming Beaver – Valheim mods

Client-side [BepInEx](https://github.com/BepInEx/BepInEx) mods for Valheim.

| Mod | What it does |
|---|---|
| [Storage Master](StorageMaster) ([Thunderstore](https://thunderstore.io/package/CharmingBeaver/StorageMaster/), [Nexus](https://www.nexusmods.com/valheim/mods/4479)) | Inventory sorting, locked slots, quick stack and a window that opens every nearby chest at once to move, find, gather and tidy up items. |

## Building

Requires the .NET SDK and Valheim with BepInEx installed.

```
cd StorageMaster
dotnet build -c Release
```

The build copies the mod into `BepInEx/plugins/`. If Valheim isn't installed at the default path, pass it in:
`dotnet build -c Release -p:ValheimDir="C:\path\to\Valheim"`

### Testing

```
.\tests\run-tests.ps1 StorageMaster
```

The tests run inside the game. The script builds the mod and `tests/StorageMaster.Tests` (a BepInEx plugin), starts Valheim, and prints each result as it comes in. The plugin loads a local test character and world called `modtest`, creating both on the first run, then runs every `[Test]` and quits the game. Steam must be running, and the game takes over the screen for a minute or two.

The script removes the test plugin again afterwards. The plugin also only runs when the script has created its marker file in `BepInEx/config`, so a leftover copy doesn't affect normal games. Screenshots of the mod's windows end up in `BepInEx/StorageMaster.Tests.screenshots` in the Valheim folder.

To check compatibility with other mods, add them from Thunderstore by their `Namespace-Name`:

```
.\tests\run-tests.ps1 StorageMaster -With seneaL-SeneaL_UI
```

They're downloaded with their dependencies into `tests/.mods` (once, ignored by git) and installed only for that run.

To try something by hand, `-Play` loads the test world with a few chests and leaves the game to you instead of running the tests (also works with `-With`). The test plugin and any `-With` mods are removed when you close the game.

```
.\tests\run-tests.ps1 StorageMaster -Play -With seneaL-SeneaL_UI
```

### Releasing

1. Raise `Version` in the mod's `src/Plugin.cs` and rename the `## Unreleased` section of its `CHANGELOG.md` to that version. Commit and push.
2. Run `.\release.ps1 StorageMaster`. It builds both zips (GitHub/Nexus and Thunderstore) and creates a **draft** GitHub release.
3. Test the build in game, then publish the draft on GitHub. The **Publish to Thunderstore** and **Publish to Nexus Mods** workflows upload it to both sites. On Nexus it becomes the new version of the mod's main file, with the changelog section and the old version archived.

The Nexus upload needs the `NEXUSMODS_API_KEY` secret (a personal API key from [Nexus](https://www.nexusmods.com/settings/api-keys)) and the mod's `nexus.json` with its mod and file ID. To check the setup without uploading, run **Publish to Nexus Mods** by hand with a release tag; it does a dry run by default.

`.\package.ps1 StorageMaster` only builds the zips, without making a release.

## Support

If these mods make your game better, you can buy me a coffee on [Ko-fi](https://ko-fi.com/charmingbeaver). Thank you!

[![Support me on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/charmingbeaver)

## License

[MIT](LICENSE) © Charming Beaver
