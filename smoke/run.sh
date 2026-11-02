#!/bin/sh
# Boots the dedicated server with BepInEx, Jotunn, and this mod, then checks the log.
# This proves that the mod loads and registers. It cannot prove in-game behavior.
set -eu

SERVER=/work/.valheim-server
CACHE=/work/.valheim-server/.smoke-cache
BEPINEX_VERSION=5.4.2360
JOTUNN_VERSION=2.30.2
WAIT_SECONDS=${WAIT_SECONDS:-420}
LOG="$SERVER/BepInEx/LogOutput.log"

collect_logs() {
    mkdir -p /work/smoke-logs
    cp "$LOG" "$CACHE/stdout.log" /work/smoke-logs/ 2>/dev/null || true
}
trap collect_logs EXIT

mkdir -p "$CACHE"
if [ ! -f "$CACHE/bepinex.zip" ]; then
    curl -fsSL -o "$CACHE/bepinex.zip" "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$BEPINEX_VERSION/"
fi
if [ ! -f "$CACHE/jotunn.zip" ]; then
    curl -fsSL -o "$CACHE/jotunn.zip" "https://thunderstore.io/package/download/ValheimModding/Jotunn/$JOTUNN_VERSION/"
fi

unzip -qo "$CACHE/bepinex.zip" "BepInExPack_Valheim/*" -d "$CACHE/pack"
cp -R "$CACHE/pack/BepInExPack_Valheim/." "$SERVER/"
# The Jotunn zip uses backslash paths, so unzip exits with a warning status. The test below checks the result.
mkdir -p "$SERVER/BepInEx/plugins/Jotunn" "$SERVER/BepInEx/plugins/TowerArchers"
unzip -qoj "$CACHE/jotunn.zip" -d "$CACHE/jotunn" 2>/dev/null || true
test -f "$CACHE/jotunn/Jotunn.dll"
cp "$CACHE/jotunn/Jotunn.dll" "$SERVER/BepInEx/plugins/Jotunn/"
cp /work/src/TowerArchers/bin/Release/TowerArchers.dll "$SERVER/BepInEx/plugins/TowerArchers/"
rm -f "$LOG"

cd "$SERVER"
chmod +x valheim_server.x86_64 start_server_bepinex.sh 2>/dev/null || true

# These values match start_server_bepinex.sh from the BepInEx pack.
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll
export LD_LIBRARY_PATH="./doorstop_libs:./linux64:${LD_LIBRARY_PATH:-}"
export LD_PRELOAD="libdoorstop_x64.so"
export SteamAppId=892970

./valheim_server.x86_64 -nographics -batchmode -name TowerArchersSmoke -port 2456 \
    -world TowerArchersSmoke -password smoketest123 -public 0 \
    -savedir "$CACHE/saves" >"$CACHE/stdout.log" 2>&1 &
PID=$!

elapsed=0
while [ "$elapsed" -lt "$WAIT_SECONDS" ]; do
    if ! kill -0 "$PID" 2>/dev/null; then
        echo "FAIL: the server exited early."
        tail -n 40 "$CACHE/stdout.log"
        exit 1
    fi
    if grep -q "Registered ArcherBowFire, TowerArcher, and WatchPost" "$LOG" 2>/dev/null \
        && grep -q "Game server connected" "$LOG" "$CACHE/stdout.log" 2>/dev/null; then
        break
    fi
    sleep 5
    elapsed=$((elapsed + 5))
done

# Give the managers time to finish, then stop the server.
sleep 20
kill "$PID" 2>/dev/null || true
wait "$PID" 2>/dev/null || true

echo "--- TowerArchers log lines"
grep -nE "TowerArcher|ArcherBowFire|WatchPost" "$LOG" || true
echo "--- Errors and warnings from mods"
grep -nE "^\[(Error|Fatal|Warning) *: *(Jotunn|TowerArchers|BepInEx|HarmonyX)" "$LOG" || true

echo "--- Exceptions in mod code"
grep -n "at TowerArchers\." "$LOG" "$CACHE/stdout.log" || true
echo "--- Server start"
grep -n "Game server connected" "$LOG" "$CACHE/stdout.log" || true

status=0
if ! grep -q "Registered ArcherBowFire, TowerArcher, and WatchPost" "$LOG"; then
    echo "FAIL: the mod did not register its prefabs."
    status=1
fi
if ! grep -q "Game server connected" "$LOG" "$CACHE/stdout.log"; then
    echo "FAIL: the server did not finish its start."
    status=1
fi
if grep -qE "^\[(Error|Fatal) *: *(Jotunn|TowerArchers)" "$LOG"; then
    echo "FAIL: Jotunn or the mod logged an error."
    status=1
fi
if grep -q "at TowerArchers\." "$LOG" "$CACHE/stdout.log"; then
    echo "FAIL: an exception passed through mod code."
    status=1
fi
if [ "$status" -eq 0 ]; then
    echo "PASS: the server started, and the mod registered with no errors."
fi
exit "$status"
