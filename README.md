# Charming Beaver – Valheim mods

Client-side [BepInEx](https://github.com/BepInEx/BepInEx) mods for Valheim.

| Mod | What it does |
|---|---|
| [Storage Master](StorageMaster) ([Nexus](https://www.nexusmods.com/valheim/mods/4479)) | Inventory sorting, locked slots, quick stack and a window that opens every nearby chest at once to move, find, gather and tidy up items. |

## Building

Requires the .NET SDK and Valheim with BepInEx installed.

```
cd StorageMaster
dotnet build -c Release
```

The build copies the mod into `BepInEx/plugins/`. If Valheim isn't installed at the default path, pass it in:
`dotnet build -c Release -p:ValheimDir="C:\path\to\Valheim"`

### Releasing

1. Raise `Version` in the mod's `src/Plugin.cs` and add a section for it to its `CHANGELOG.md`. Commit and push.
2. Run `.elease.ps1 StorageMaster`. It builds both zips (GitHub/Nexus and Thunderstore) and creates a **draft** GitHub release.
3. Test the build in game, then publish the draft on GitHub. The **Publish to Thunderstore** workflow uploads it to Thunderstore.
4. Upload `dist/StorageMaster-<version>.zip` to Nexus by hand.

`.\package.ps1 StorageMaster` only builds the zips, without making a release.

## Support

If these mods make your game better, you can buy me a coffee on [Ko-fi](https://ko-fi.com/charmingbeaver). Thank you!

[![Support me on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/charmingbeaver)

## License

[MIT](LICENSE) © Charming Beaver
