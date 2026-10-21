# URAH Kingdoms

A mod for **Knights of Honor II: Sovereign**: play your own kingdom. When a campaign starts, the
kingdom a player picked takes the name, adjective, colour, arms and ruler name of the player's own
profile, made in the main menu. Purely cosmetic: lands, culture, religion, units and everything else
stay those of the replaced kingdom. Works in singleplayer and multiplayer, with any map.

**Work in progress**: nothing playable yet.

It is a mod (`URAH_Kingdoms/`) plus a BepInEx 5 plugin (`plugin/`) that does the work. The mod holds
no game data, only `Defs/URAH_Kingdoms.def` with the plugin version: without a def file the game
treats the mod as texts-only and leaves it out of the multiplayer mod check, and the version changes
the mod checksum with every release. The plugin only patches the game while the mod is active.

Profiles are kept outside the mod in `BepInEx/config/urah.kingdoms/`, so they are not part of the mod
checksum.

## Building

```sh
git clone --recursive <this repo>      # external/URAH_shared is a submodule
cd plugin && dotnet build -c Release -p:Deploy=true
```

Compiles against your own game install with BepInEx 5 (`GameDir`, default: Steam on Linux; pass
`-p:GameDir=...`). `Deploy=true` copies the DLL into `BepInEx/plugins/`.

## License

The code, defs and texts of this repository are under the MIT License (`LICENSE`).
