#!/bin/bash
# Puts the files that get embedded into EasyDKiosk.exe into win/payload/ (not committed):
#   payload/EasyDKiosk.apk                      from apk/build.sh
#   payload/platform-tools-latest-windows.zip   Google's adb for Windows
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p payload
cp ../apk/build/EasyDKiosk.apk payload/
curl -fL https://dl.google.com/android/repository/platform-tools-latest-windows.zip -o payload/platform-tools-latest-windows.zip
ls -la payload
