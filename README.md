# TowerWatch: server-only proof of concept

**This is throwaway code. It answers one question: can archers work when only the dedicated server has a mod, so that console players can join?**

The server has one DLL, and players install nothing. Write `[archer]` on a vanilla sign on your tower. The server spawns a tamed vanilla `Draugr_Ranged` that stands on the floor below the sign and fires fire arrows. Remove the sign to remove the archer.

## Limits

- One active area, so archers work at one base.
- Archers look like Draugr.
- Dedicated server only.

## Open questions

| Question | How to answer it |
|---|---|
| Can a server with no client fire arrows? | `Smoke` workflow |
| Can the server own only the archers? | `Smoke` workflow |
| Does a server with BepInEx start with `-crossplay`? | `Smoke` workflow |
| Do console players see the archers, and can they connect? | **Only a real console can test this** |
| Do arrows hit monsters that a player's game runs? | Needs a real client |

## Run

The `Smoke` GitHub Actions workflow boots the real server with the plugin in test mode, and uploads the logs. It needs an x86-64 host, because the server crashes under Rosetta in Docker on Apple Silicon.

```bash
make fetch
```

```bash
make build
```

Do not commit or redistribute `.valheim-server/` or `.decompiled/`. They contain game files.
