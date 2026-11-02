#!/bin/sh
# Boots the dedicated server with BepInEx, Jotunn, the mod, and the scenario plugin.
# The first boot places a post and tests it. The second boot tests the saved world.
# This cannot test the Hammer menu, visuals, or a real player connection.
set -eu

SERVER=/work/.valheim-server
CACHE=/work/.valheim-server/.smoke-cache
BEPINEX_VERSION=5.4.2360
JOTUNN_VERSION=2.30.2
WAIT_SECONDS=${WAIT_SECONDS:-600}
LOG="$SERVER/BepInEx/LogOutput.log"
LOGS=/work/smoke-logs

mkdir -p "$CACHE" "$LOGS"
rm -rf "$CACHE/saves" "$LOGS"/*
if [ ! -f "$CACHE/bepinex.zip" ]; then
    curl -fsSL -o "$CACHE/bepinex.zip" "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$BEPINEX_VERSION/"
fi
if [ ! -f "$CACHE/jotunn.zip" ]; then
    curl -fsSL -o "$CACHE/jotunn.zip" "https://thunderstore.io/package/download/ValheimModding/Jotunn/$JOTUNN_VERSION/"
fi

unzip -qo "$CACHE/bepinex.zip" "BepInExPack_Valheim/*" -d "$CACHE/pack"
cp -R "$CACHE/pack/BepInExPack_Valheim/." "$SERVER/"
# The Jotunn zip uses backslash paths, so unzip exits with a warning status. The test below checks the result.
mkdir -p "$SERVER/BepInEx/plugins/Jotunn" "$SERVER/BepInEx/plugins/TowerArchers" "$SERVER/BepInEx/plugins/TowerArchersScenario"
unzip -qoj "$CACHE/jotunn.zip" -d "$CACHE/jotunn" 2>/dev/null || true
test -f "$CACHE/jotunn/Jotunn.dll"
cp "$CACHE/jotunn/Jotunn.dll" "$SERVER/BepInEx/plugins/Jotunn/"
cp /work/src/TowerArchers/bin/Release/TowerArchers.dll "$SERVER/BepInEx/plugins/TowerArchers/"
cp /work/smoke/Scenario/bin/Release/TowerArchersScenario.dll "$SERVER/BepInEx/plugins/TowerArchersScenario/"

cd "$SERVER"
chmod +x valheim_server.x86_64 start_server_bepinex.sh 2>/dev/null || true

# These values match start_server_bepinex.sh from the BepInEx pack.
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll
export LD_LIBRARY_PATH="./doorstop_libs:./linux64:${LD_LIBRARY_PATH:-}"
export LD_PRELOAD="libdoorstop_x64.so"
export SteamAppId=892970
export SCENARIO_STATE="$CACHE/scenario-state.txt"

status=0

# Boots the server for one phase, and stops it when the scenario reports DONE.
run_phase() {
    phase=$1
    phase_log="$LOGS/LogOutput-$phase.log"
    stdout_log="$LOGS/stdout-$phase.log"
    rm -f "$LOG"
    export SCENARIO_PHASE="$phase"

    ./valheim_server.x86_64 -nographics -batchmode -name TowerArchersSmoke -port 2456 \
        -world TowerArchersSmoke -password smoketest123 -public 0 \
        -savedir "$CACHE/saves" >"$stdout_log" 2>&1 &
    pid=$!

    elapsed=0
    while [ "$elapsed" -lt "$WAIT_SECONDS" ]; do
        if ! kill -0 "$pid" 2>/dev/null; then
            echo "FAIL: the server exited early in phase $phase."
            tail -n 40 "$stdout_log"
            status=1
            break
        fi
        if grep -q "SCENARIO DONE" "$LOG" 2>/dev/null; then
            break
        fi
        sleep 5
        elapsed=$((elapsed + 5))
    done

    # The world save runs on a background thread after the scenario finishes.
    sleep 20
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
    cp "$LOG" "$phase_log" 2>/dev/null || true

    echo "=== Phase $phase"
    grep -E "SCENARIO" "$phase_log" | sed 's/^\[[^]]*\] *//' || true

    if ! grep -q "Registered ArcherBowFire, TowerArcher, and WatchPost" "$phase_log"; then
        echo "FAIL: the mod did not register its prefabs."
        status=1
    fi
    if ! grep -q "Game server connected" "$phase_log"; then
        echo "FAIL: the server did not finish its start."
        status=1
    fi
    if grep -qE "^\[(Error|Fatal) *: *(Jotunn|TowerArchers)" "$phase_log"; then
        echo "FAIL: Jotunn or the mod logged an error:"
        grep -E "^\[(Error|Fatal) *: *(Jotunn|TowerArchers)" "$phase_log" || true
        status=1
    fi
    if grep -q "at TowerArchers\." "$phase_log" "$stdout_log"; then
        echo "FAIL: an exception passed through mod code:"
        grep -B3 "at TowerArchers\." "$phase_log" | head -n 20 || true
        status=1
    fi
    if grep -q "at Scenario\." "$phase_log" "$stdout_log"; then
        echo "FAIL: the scenario plugin threw an exception:"
        grep -B3 "at Scenario\." "$phase_log" | head -n 20 || true
        status=1
    fi
    if ! grep -q "SCENARIO DONE phase=$phase failures=0" "$phase_log"; then
        echo "FAIL: phase $phase did not finish with zero failures."
        status=1
    fi
}

run_phase first
if [ -f "$SCENARIO_STATE" ]; then
    run_phase reload
else
    echo "FAIL: the first phase did not save its state, so the reload phase did not run."
    status=1
fi

if [ "$status" -eq 0 ]; then
    echo "PASS: all scenario phases finished with zero failures."
fi
exit "$status"
