#!/bin/sh
# Creates dist/TowerArchers-<version>.zip in the layout that r2modman imports.
set -eu

cd /work
version=$(sed -n 's/.*"version_number": "\(.*\)",/\1/p' thunderstore/manifest.json)
stage=$(mktemp -d)

mkdir -p "$stage/plugins/TowerArchers" dist
cp thunderstore/manifest.json thunderstore/icon.png README.md "$stage/"
cp src/TowerArchers/bin/Release/TowerArchers.dll "$stage/plugins/TowerArchers/"

rm -f "dist/TowerArchers-$version.zip"
(cd "$stage" && zip -qr "/work/dist/TowerArchers-$version.zip" .)
rm -rf "$stage"
echo "Created dist/TowerArchers-$version.zip"
