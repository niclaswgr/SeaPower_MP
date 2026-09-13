# SeaPower MP

Multiplayer-Mod für **Sea Power: Naval Combat in the Missile Age**: 2–8 Spieler, Co-op und PvP,
host-autoritative Simulation, dazu ein Quick-Start-Task-Force-Modus.

> Status: **Phase 0 – Gerüst.** Noch nicht spielbar. Fahrplan und Designentscheidungen:
> [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

Community-Mod, nicht mit Triassic Games verbunden.

## Voraussetzungen

- Sea Power 0.8.x
- [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757) inkl. Preloader (BepInEx 5.4)
- .NET SDK 8 oder neuer (zum Bauen)

## Bauen

```bash
dotnet build SeaPowerMP.sln
```

Der Build kopiert den Mod nach `<Spiel>/Sea Power_Data/StreamingAssets/SeaPowerMP/`. Danach im
Mod-Menü des Spiels **SeaPower MP** aktivieren und das Spiel komplett neu starten.

Liegt das Spiel woanders als `D:\SteamLibrary\steamapps\common\Sea Power`, lege eine
`Directory.Build.props.user` an:

```xml
<Project>
  <PropertyGroup>
    <GameDir>E:\Steam\steamapps\common\Sea Power</GameDir>
  </PropertyGroup>
</Project>
```

## Tests

```bash
dotnet test
```
