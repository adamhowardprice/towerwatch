# TowerWatch

A server-only Valheim mod that adds friendly archers to your tower. Only the dedicated server has the mod, so players install nothing, and Xbox players can join.

Write `[archer]` on a vanilla sign on your tower. The server spawns a tamed vanilla `Draugr_Ranged` that stands about 0.8 m in front of the sign and fires fire arrows. Remove the sign to remove the archer.

## Use

Place the sign on top of a platform, near the middle. On the side of a platform, the archer can land on the ground below. Clear the trees around the tower, because archers cannot shoot through them.

## Known behavior

- **One base:** a server has one active area, and the first sign selects it. Archers work only there.
- **Appearance:** archers look like Draugr, and they use the Draugr name.
- **Wild animals:** archers shoot wild animals. They do not target players, Dverger, or tamed animals.
- **Death:** a dead archer does not respawn. Replace the sign to get a new archer.
- **Balance:** this is not tuned. An arrow did about 90 damage in testing.
- **Support:** a sign or a floor with no support collapses, and the archer goes with the sign.

## Install on a server

The server needs a dedicated Linux server with SSH access, Valheim 1.0.21, and [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

1. Install `libatomic1`, `libpulse0`, and `libpulse-mainloop-glib0`. Crossplay does not load without the last one.
2. Put `TowerWatch.dll` in `BepInEx/plugins/TowerWatch/`.
3. Set the variables from `start_server_bepinex.sh`, and start the server with `-crossplay`.
4. Open UDP ports 2456 to 2458.

The join code can change when the server restarts.

## Test status

| Item | Result |
|---|---|
| Xbox client joins over crossplay | Worked in a real session |
| Sign placed on Xbox spawns an archer that the player can see | Worked in a real session |
| Arrows kill a monster that the Xbox runs | Worked in a real session |
| Server owns only the archers | Passes in CI, with no player |
| Archer survives a server restart, and removing the sign despawns it | Passes in CI, with no player |
| Hit rate on a still target | CI: 10/10 at 15 m, 9/12 at 30 m, 5/12 at 45 m |
| Archer ignores a real player | **Not verified** |
| Normal combat feel near the tower | **Not verified** |
| Log out and log in with a real player | **Not verified** |

## Build and test

Docker is the only requirement. The commands download the Valheim dedicated server with an anonymous Steam login, and compile against its assemblies.

```bash
make fetch
```

```bash
make build
```

The DLL is at `src/TowerWatch/bin/Release/TowerWatch.dll`.

The `Smoke` GitHub Actions workflow boots the real server with the plugin in test mode, and uploads the logs. Test mode runs only when `TOWERWATCH_PHASE` is set. The workflow needs an x86-64 host, because the server crashes under Rosetta in Docker on Apple Silicon.

Do not commit or redistribute `.valheim-server/` or `.decompiled/`. They contain game files.
