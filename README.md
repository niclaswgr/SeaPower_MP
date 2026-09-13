# SeaPower MP

Multiplayer-Mod für **Sea Power: Naval Combat in the Missile Age**: 2–8 Spieler, Co-op und PvP,
host-autoritative Simulation, dazu ein Quick-Start-Task-Force-Modus.

> Status: **Phase 1 – Verbindung & Lobby.** Spieler können sich verbinden (Steam oder direkte IP,
> bis zu 8), eine Mission wird noch nicht synchronisiert. Fahrplan und Designentscheidungen:
> [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Bedienung

**Strg+F8** öffnet das Multiplayer-Fenster (auch im Hauptmenü).

- **Steam:** *Host session*, dann *Invite Steam friends*. Freunde nehmen die Einladung in Steam an und
  werden automatisch verbunden – auch wenn ihr Spiel noch nicht lief.
- **Direkte IP:** Der Host klickt *Host on this port* (Standard 7777, UDP muss erreichbar sein),
  die anderen tragen seine Adresse ein und klicken *Join*.

Beim Beitritt müssen Sea-Power-Version, SeaPower-MP-Version und die aktivierten Mods übereinstimmen,
sonst lehnt der Host mit einer genauen Begründung ab. Im Fenster wechseln Spieler ihr Team, der Host
kann Teams zuweisen und Spieler entfernen.

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

### Bot-Spieler zum Testen ohne Mitspieler

`tools/SeaPowerMP.TestClient` verbindet beliebig viele Bots über echtes UDP mit einem Host – mit dem
Spiel (Host per *Host on this port*) oder mit einem Stand-in-Host ohne Spiel:

```bash
dotnet run --project tools/SeaPowerMP.TestClient -- --count 3 --switch-team --mods "Anchor Chain"
```

```bash
dotnet run --project tools/SeaPowerMP.TestClient -- --host --port 7790 --seconds 60
```

Weitere Optionen: `--address`, `--port`, `--name`, `--seconds`, `--game-version`, `--mod-version`.
