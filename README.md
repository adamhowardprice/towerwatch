# TowerArchers

A Valheim mod that adds friendly stationary archers. Build a Watch Post at the top of a tower, and an archer on the post fires fire arrows at monsters.

Install the mod on the server and on every client. Peers without the mod, or with a different minor version, cannot connect.

## Contents

| Prefab | Base prefab | Purpose |
|---|---|---|
| `TowerArcher` | `Draugr_Ranged` | Tamed archer in the Players faction. It has no drops. |
| `ArcherBowFire` | `BowFineWood` | NPC bow that needs no ammo and fires `bow_projectile_fire`. |
| `WatchPost` | `wood_floor` | Hammer piece in the Misc tab. It spawns one archer. |

## Configuration

The file is `BepInEx/config/com.adamprice.towerarchers.cfg`. The server values override client values.

| Section | Key | Default |
|---|---|---|
| Archer | `Health` | 150 |
| Archer | `ViewRange` | 50 |
| Archer | `HearRange` | 50 |
| Archer | `LeashDistance` | 6 |
| Bow | `AttackRange` | 50 |
| Bow | `AttackInterval` | 2.5 |
| WatchPost | `FineWoodCost` | 10 |

## Build

Docker is the only requirement. The commands download the Valheim dedicated server with an anonymous Steam login, and compile against its assemblies.

```bash
make fetch
```

```bash
make build
```

```bash
make package
```

The DLL is at `src/TowerArchers/bin/Release/TowerArchers.dll`. The r2modman package is at `dist/TowerArchers-0.1.0.zip`.

Other commands:

- `make decompile` writes the game source to `.decompiled/`, to check names.
- `make fetch-full` downloads the whole server (about 2 GB). `make smoke` then boots it with the mod. This needs an x86-64 host, so the `Smoke` GitHub Actions workflow runs it. The workflow uploads the logs and the package as artifacts.

To build with a local .NET SDK and deploy to a game folder:

```bash
VALHEIM_INSTALL="/path/to/Valheim" dotnet build -c Release
```

The post-build step copies the DLL to `$VALHEIM_INSTALL/BepInEx/plugins/TowerArchers/`.

Do not commit or redistribute `.valheim-server/` or `.decompiled/`. They contain game files.

## Test in game

Enable the console with the `-console` launch option. Press F5, then run `devcommands`.

**Spawn from the console**

1. Run `spawn TowerArcher`. An archer appears near you, and stays at that position.

**Place a Watch Post**

1. Get Fine Wood with `spawn FineWood 10`, or run `nocost`.
2. Equip the Hammer. Open the Misc tab, and place Watch Post at the top of a tower.
3. An archer appears on the post.

**Check these results**

- [ ] The archer stays on the tower. It does not walk off to chase a target.
- [ ] Run `spawn Greydwarf` near the base of the tower. The archer fires fire arrows, and the Greydwarf takes fire damage and burns.
- [ ] Arrows reach a target 40 to 50 m away. The attack has no gravity compensation, so long shots can fall short.
- [ ] The archer does not attack you or other players.
- [ ] Run `spawn Boar`, then `tame`. The archer does not attack the tamed boar.
- [ ] Log out and log in, or restart the server. There is one archer on the post, at the same position.
- [ ] Walk far away and return, to unload and reload the area. There is still one archer.
- [ ] Destroy the Watch Post with the Hammer. The archer despawns. Repeat after a reload.
- [ ] On a dedicated server, repeat the checks with a second client.

## Differences from the original specification

Decompiled code showed that these parts of the specification would not work as written.

| Specification | Finding | Implementation |
|---|---|---|
| `m_aiPrioritizeMeleeTarget` | The field does not exist. | Omitted. The bow is the only weapon. |
| `arrow_fire` item | The prefab name is `ArrowFire`. | Uses `ArrowFire`, and reads the projectile from it. |
| `EnforceModStrictness` | The name does not exist. | `CompatibilityLevel.EveryoneMustHaveMod`, `VersionStrictness.Minor`. |
| AI tuning keeps the archer in place | `UpdateAI` walks to a target that it hears but cannot see. Random movement with range 0 never returns to the patrol point. | A postfix on `MonsterAI.UpdateAI` holds the archer. |
| Clone the bow attack | The player attack uses `m_bowDraw`. NPCs have no draw value, so damage is zero. | Uses the attack from the Draugr bow, with the fire projectile. |
| No ammo item | The arrow damage comes from the ammo item. | Adds `ArrowFire` damage and status effect to the bow. |
| Store the ZDO ID | `ZDO.Load` renumbers IDs on each world load. | Uses the `Spawned` ZDO connection, as `CreatureSpawner` does. The position is a ZDO value. |

Additions that the specification did not request:

- `m_viewAngle = 360`, because a stationary archer must see in all directions.
- `m_alertRange` follows `ViewRange`. Vanilla drops the target of a tamed creature beyond this range.
- `LeashDistance` teleports an archer back when a hit knocks it off the tower. An archer cannot climb back.

## Known behavior

- Archers attack wild animals such as boars and deer. Vanilla `BaseAI.IsEnemy` makes a tamed creature hostile to all creatures that are not tamed, except players and Dverger.
- A dead archer does not respawn. Rebuild the Watch Post to get a new archer.
- Line of sight starts at the eye of the archer. A wall or the floor of the tower can block a target directly below.

## Verification status

Built against Valheim 1.0.21 (dedicated server), Jotunn 2.30.2, and BepInEx 5.4.2360.

| Item | Status |
|---|---|
| Class, field, and method names | Compiled against the publicized game assembly, and read in decompiled code. |
| Prefab names | Found in the game asset files. |
| Plugin loads on a server | The `Smoke` workflow checks this. The server crashes under Rosetta in Docker on Apple Silicon (`tramp-amd64.c:641`), even with no mods, so `make smoke` cannot run there. |
| All behavior in [Test in game](#test-in-game) | **Not verified.** |
| Draugr bow attack values | **Not verified.** They exist only in game data, and the plugin logs them at startup. |
| Bow model on the Draugr body | **Not verified.** Check it visually. |
