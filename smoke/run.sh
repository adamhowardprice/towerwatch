#!/bin/sh
# Boots the dedicated server with BepInEx and only the TowerWatch plugin, in three phases:
#   first:     place a sign, then test the archer.
#   reload:    restart the server, then test the saved world.
#   crossplay: restart with -crossplay, and report what the PlayFab backend does.
set -eu

SERVER=/work/.valheim-server
CACHE=/work/.valheim-server/.smoke-cache
BEPINEX_VERSION=5.4.2360
WAIT_SECONDS=${WAIT_SECONDS:-480}
LOG="$SERVER/BepInEx/LogOutput.log"
LOGS=/work/smoke-logs

mkdir -p "$CACHE" "$LOGS"
rm -rf "$CACHE/saves" "$LOGS"/* "$CACHE/scenario-state.txt"
if [ ! -f "$CACHE/bepinex.zip" ]; then
    curl -fsSL -o "$CACHE/bepinex.zip" "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$BEPINEX_VERSION/"
fi

unzip -qo "$CACHE/bepinex.zip" "BepInExPack_Valheim/*" -d "$CACHE/pack"
cp -R "$CACHE/pack/BepInExPack_Valheim/." "$SERVER/"
rm -rf "$SERVER/BepInEx/plugins"
mkdir -p "$SERVER/BepInEx/plugins/TowerWatch"
cp /work/src/TowerWatch/bin/Release/TowerWatch.dll "$SERVER/BepInEx/plugins/TowerWatch/"

cd "$SERVER"
chmod +x valheim_server.x86_64 start_server_bepinex.sh 2>/dev/null || true

# These values match start_server_bepinex.sh from the BepInEx pack.
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll
export LD_LIBRARY_PATH="./doorstop_libs:./linux64:${LD_LIBRARY_PATH:-}"
export LD_PRELOAD="libdoorstop_x64.so"
export SteamAppId=892970
export TOWERWATCH_STATE="$CACHE/scenario-state.txt"

status=0

run_phase() {
    phase=$1
    shift
    phase_log="$LOGS/LogOutput-$phase.log"
    stdout_log="$LOGS/stdout-$phase.log"
    rm -f "$LOG"
    export TOWERWATCH_PHASE="$phase"

    ./valheim_server.x86_64 -nographics -batchmode -name TowerWatchSmoke -port 2456 \
        -world TowerWatchSmoke -password smoketest123 -public 0 \
        -savedir "$CACHE/saves" "$@" >"$stdout_log" 2>&1 &
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
        # An exception ends the scenario coroutine, so DONE can never appear.
        if grep -q "at TowerWatch\." "$stdout_log" 2>/dev/null; then
            break
        fi
        sleep 5
        elapsed=$((elapsed + 5))
    done

    sleep 20
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
    cp "$LOG" "$phase_log" 2>/dev/null || true

    echo "=== Phase $phase"
    grep -E "SCENARIO|TowerWatch\]" "$phase_log" | sed -E 's/^\[[^]]*\] *//' || true

    if grep -q "at TowerWatch\." "$phase_log" "$stdout_log"; then
        echo "FAIL: an exception passed through plugin code:"
        grep -B3 "at TowerWatch\." "$stdout_log" | head -n 24 || true
        status=1
    fi
    if ! grep -q "SCENARIO DONE phase=$phase failures=0" "$phase_log"; then
        echo "FAIL: phase $phase did not finish with zero failures."
        status=1
    fi
}

run_phase first
if [ -f "$TOWERWATCH_STATE" ]; then
    run_phase reload
else
    echo "FAIL: the first phase did not save its state, so the reload phase did not run."
    status=1
fi

run_phase crossplay -crossplay
echo "=== Crossplay backend lines"
grep -iE "playfab|party|join code|crossplay|backend|Game server connected|Session" "$LOGS/stdout-crossplay.log" | cut -c1-200 | head -n 40 || true
if grep -qE "Failed to open plugin.*libparty|DllNotFoundException: libParty" "$LOGS/stdout-crossplay.log"; then
    echo "FAIL: the crossplay library did not load."
    ldd "$SERVER/valheim_server_Data/Plugins/libparty.so" | grep "not found" || true
    status=1
fi

if [ "$status" -eq 0 ]; then
    echo "PASS: all phases finished with zero failures."
fi
exit "$status"
