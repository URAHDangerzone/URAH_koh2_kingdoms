# URAH Kingdoms

A mod for **Knights of Honor II: Sovereign**: play your own kingdom. When a campaign starts, the
kingdom a player picked takes the name, adjective, colour, arms and ruler name of the player's own
profile. Purely cosmetic: lands, culture, religion, units and everything else
stay those of the replaced kingdom. Works in singleplayer and multiplayer, with any map.

**Work in progress**, not released yet.

## Playing

- **Singleplayer**: click a kingdom in the campaign setup; "Override kingdom" (where the multiplayer
  lobby has "Pick kingdom") opens the list of your profiles, "+ New profile" and "Edit profile". The
  map shows the selected kingdom with the chosen profile.
- **Multiplayer**: pick a kingdom as usual; the same button then takes the place of "picked by you".
  The other players see your kingdom with your profile on the map and in the player list. The host
  checks every profile when the game starts; `[Multiplayer] AllowOtherProfiles = false` in the host's
  config shows only the host's own. Players without the plugin see the kingdoms as they are.
- **Saves** keep the profiles they were started with (in the kingdoms and in the campaign: the save
  list shows the campaign's name and arms), whatever profile is chosen later.

It is a mod (`URAH_Kingdoms/`) plus a BepInEx 5 plugin (`plugin/`) that does the work. The mod holds
no game data, only `Defs/URAH_Kingdoms.def` with the plugin version: without a def file the game
treats the mod as texts-only and leaves it out of the multiplayer mod check, and the version changes
the mod checksum with every release. The plugin only patches the game while the mod is active.

## Profiles

A profile is a JSON file in `BepInEx/config/urah.kingdoms/` (outside the mod, so not part of the mod
checksum); its file name without `.json` is its id (`a-z`, `0-9`, `_`, `-`). The editor writes them; they can
also be edited by hand. The profile to play is `[General] Profile` in
`BepInEx/config/urah.koh2.kingdoms.cfg` (the button sets it); empty plays the picked kingdom as it is.

```json
{
  "format": 1,
  "name": "Ostmark",
  "adjective": { "ian": "Ostmarkian", "anian": "Ostmarkian", "ians": "Ostmarkians" },
  "color": "#A0B0C0",
  "arms": "Hungary",
  "ruler": "Otto"
}
```

- `name` (required) and `ruler`: up to 32 characters, shown as typed in every language
- `adjective` (optional): the English adjective forms; a missing one falls back to the game's default
- `color`: map colour, `#RRGGBB`
- `arms`: key of an existing kingdom (`kingdoms.csv`) whose coat of arms to show
- No text may contain `{ } [ ] | ; " # @ \`: the game reads them as text markup. Unknown fields are
  an error, so a typo doesn't go unnoticed; an empty field keeps the replaced kingdom's value.

A **kingdom code** is the same profile as one line (`URAHK1:` + base64 of the JSON, under 1 KB), for
sharing (the editor copies and pastes it) and for sending in multiplayer.

## Building

```sh
git clone --recursive <this repo>      # external/URAH_shared is a submodule
cd plugin && dotnet build -c Release -p:Deploy=true
```

Compiles against your own game install with BepInEx 5 (`GameDir`, default: Steam on Linux; pass
`-p:GameDir=...`). `Deploy=true` copies the DLL into `BepInEx/plugins/`.

## License

The code, defs and texts of this repository are under the MIT License (`LICENSE`).
