# Charming Beaver – Valheim mods

Client-side [BepInEx](https://github.com/BepInEx/BepInEx) mods for Valheim.

| Mod | What it does |
|---|---|
| [Storage Master](StorageMaster) | Inventory sorting, locked slots, quick stack and a window that opens every nearby chest at once to move, find, gather and tidy up items. |

## Building

Requires the .NET SDK and Valheim with BepInEx installed.

```
cd StorageMaster
dotnet build -c Release
```

The build copies the mod into `BepInEx/plugins/`. If Valheim isn't installed at the default path, pass it in:
`dotnet build -c Release -p:ValheimDir="C:\path\to\Valheim"`

## License

[MIT](LICENSE) © Charming Beaver
