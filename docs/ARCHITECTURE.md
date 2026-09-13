# SeaPower MP – Architektur

Multiplayer-Mod für *Sea Power: Naval Combat in the Missile Age* (getestet gegen 0.8.2, Unity 6 / Mono,
BepInEx 5.4 + Anchor Chain). Ein Mod, zwei Teile: ein **Multiplayer-Kern** und darauf aufbauend
**Spielmodi** (als erster: ein Quick-Start/Task-Force-Modus).

Eigenständige Neuentwicklung. Die bestehenden Mods *Seapower Multiplayer* und *Quick Start* dienten nur
als Referenz dafür, welche Spielsysteme berührt werden müssen. Kein Code wurde übernommen.

## Ziele

1. **Stabilität:** Keine Desyncs durch unterschiedliche Simulationen. Spiel-Updates sollen laut und
   früh auffallen, nicht leise die Synchronisation brechen.
2. **Mehr als 2 Spieler:** 2–8 Spieler, frei auf Teams verteilt (Co-op, PvP, 2v2, alle gegen KI).
3. **Offene Schnittstelle für Spielmodi:** Quick-Start und spätere Modi nutzen eine öffentliche API
   statt Reflection auf private Member.

## Warum kein Lockstep?

Deterministischer Lockstep (alle Rechner simulieren identisch, nur Eingaben werden getauscht) wäre am
bandbreitensparendsten. Sea Power ist dafür aber nicht geeignet:

- ~86 Stellen nutzen den globalen `UnityEngine.Random`, den auch Kamera/Rendering verbrauchen.
- Die Simulation hängt an `Update()` (framerate-abhängig) und am Zeitraffer.
- Sensoren laufen als Unity-ECS-Systeme (`SystemBase`) mit Multithread-Jobs.

→ Kleine Abweichungen würden sich aufschaukeln. Deshalb: **Host-autoritativ.**

## Grundmodell: Host simuliert, Clients zeigen an

```
            Befehle (reliable)                     Snapshots (unreliable, 10–25 Hz)
 Client A ───────────────────────►  ┌────────┐  ─────────────────────────────►  Client A
 Client B ───────────────────────►  │  HOST  │  ─────────────────────────────►  Client B
 Client C ───────────────────────►  │ (volle │  Ereignisse (reliable, geordnet)
                                    │  Sim)  │  ─────────────────────────────►  alle
                                    └────────┘  Kontaktbild pro Team  ───────►  je Team
```

- **Nur der Host simuliert.** Physik, KI, Waffen, Schaden, Sensoren, Flugdeck laufen ausschließlich
  beim Host. Es gibt keine „Wer hat recht?“-Konflikte, weil es nur eine Wahrheit gibt.
- **Clients sind Thin Clients.** Rendering, UI, Kamera, Eingabe und Umgebung laufen lokal. Die
  Simulation ist an den **zentralen Taktgebern** abgeschaltet, nicht an hunderten Einzelmethoden:
  - `GameMain.PerformFixedUpdate` / `GameMain.Update` → Objektschleife `_objects[i].OnFixedUpdate /
    OnUpdateEveryFrame / OnLazyUpdate`
  - `GameFixedUpdater.fixedUpdate` → `ProjectileManager`, `SensorManager`, `TaskforceManager`,
    `AircraftManager`
  - `GameUpdater.update` → u. a. `AIPlayer`, `AIController`, `MissionManager`, `TaskforceManager`
  Pro Objekttyp entscheidet eine **Replikations-Policy**, welche Teile weiterlaufen dürfen (z. B.
  rein visuelle Animationen) und welche vom Host gesteuert werden.
- **Befehle** der Clients gehen als Nachricht an den Host, werden dort gegen die Einheiten-Zuteilung
  geprüft und über die normale Spiel-API ausgeführt. Das Ergebnis kommt über die Replikation zurück.
  Der Host selbst ist ein normaler Spieler mit denselben Regeln.
- **Fog of War / Anti-Cheat:** Der Host berechnet das Kontaktbild pro Team und schickt jedem Client
  nur, was sein Team sieht (Interest Management). Gegnerische Einheiten, die ein Team nicht entdeckt
  hat, existieren auf dessen Client gar nicht erst. Nebeneffekt: Track-Nummern und Klassifizierung
  sind für Teamkameraden identisch.

## Netzwerk-Schichten

| Schicht | Aufgabe |
|---|---|
| **Transport** | `ITransport` mit zwei gleichwertigen Implementierungen: Steam P2P (`SteamNetworkingMessages`, Relay, Einladungen) und LiteNetLib (direkte IP/LAN). Kanäle: reliable-ordered, unreliable-sequenced. Fragmentierung großer Nachrichten (Savegames). |
| **Session** | Lobby, Slots (0 = Host), Teams, Handshake mit Protokoll-/Spiel-/Mod-Versionsprüfung, Mod-Liste-Fingerprint, Late Join, Reconnect. |
| **Replikation** | Entity-IDs (Host vergibt), Snapshot-Stream mit Delta-Kompression gegen den letzten bestätigten Stand, Priorisierung (Sichtfeld > Nähe > Rest), Interpolationspuffer beim Client. |
| **Ereignisse** | Spawn/Despawn, Abschuss, Treffer, Schaden, Munition, Flugdeck – reliable mit Sequenznummer, damit nichts verloren oder doppelt ankommt. |
| **Spielmodus-API** | Öffentliche Schnittstelle: eigene Nachrichtenkanäle registrieren, Lobby-Hooks, Missionsstart. |

## Robustheit gegen Spiel-Updates

- **Ein Adapter für alles Spielspezifische** (`Game/`): Alle Harmony-Patches und Zugriffe auf
  Spiel-Interna liegen an einer Stelle.
- **Selbsttest beim Start:** Jeder Patch-Zielpunkt wird geprüft. Fehlt einer nach einem Spiel-Update,
  wird Multiplayer mit einer klaren Meldung deaktiviert, statt mit halben Patches zu laufen.
- **Versions-Handshake:** Host und Clients müssen Protokoll, Spielversion und Mod-Liste teilen.

## Projektstruktur

```
src/
  SeaPowerMP.Core/        netstandard2.1, KEINE Unity-Abhängigkeit
                          Protokoll, Serialisierung, Delta-Kompression, Session-Zustandsautomat,
                          Interest-Management-Logik → vollständig unit-testbar
  SeaPowerMP/             Plugin (Unity, Harmony, Anchor Chain)
    Game/                 Adapter + Harmony-Patches (einzige Stelle mit Spiel-Interna)
    Net/                  Transports (Steam, LiteNetLib)
    Sync/                 Host-Streamer, Client-Replikation, Befehle
    Modes/QuickStart/     Quick-Start-Spielmodus
    UI/                   Lobby & Overlay
tests/
  SeaPowerMP.Core.Tests/  xUnit
mod/                      Workshop-Ordnerinhalt (_info.ini, Missionen, Assets)
```

## Fahrplan

| Phase | Inhalt | Ergebnis |
|---|---|---|
| 0 | Gerüst, Build, Deploy in lokalen Mod-Ordner | Mod lädt im Spiel, Logzeile erscheint |
| 1 | Transport + Lobby (2–8 Spieler), Handshake | Spieler verbinden sich, Roster sichtbar |
| 2 | Session-Sync: Savegame-Transfer, Laden, Zeit/Pause | Alle sind in derselben Mission |
| 3 | **Thin-Client-Spike:** Client-Simulation abschalten, Schiffspositionen replizieren | Kernhypothese bewiesen oder verworfen |
| 4 | Befehle (Kurs, Fahrt, Wegpunkte, Formationen, Waffen) | Clients steuern ihre Einheiten |
| 5 | Waffen, Schaden, Ereignisse, Flugzeuge & Träger | Gefecht spielbar |
| 6 | Kontaktbild pro Team, Fog of War | PvP fair |
| 7 | Quick-Start-Modus (Flottenbau, Kampfgebiete, Einsatz) | Missionen ohne Editor |
| 8 | UI-Politur, Workshop-Veröffentlichung | Release |

## Größtes Risiko

Beim Thin Client zeigt die Spiel-UI Details (Munition, Schadenszonen, Sensorstatus) direkt aus den
lokalen Objekten an. Werden sie nicht simuliert, müssen diese Zustände repliziert werden. Phase 3
klärt früh, wie viel davon nötig ist und ob die zentrale Abschaltung sauber funktioniert, bevor
darauf aufgebaut wird.
