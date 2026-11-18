#!/bin/bash
# Builds a stand-in "Easy Display Player" (package com.taknik.easydplayer) for emulator tests.
# Uses the same env vars as apk/build.sh (ANDROID_SDK_ROOT, JAVA_HOME, KIOSK_KEYSTORE, KIOSK_KEYSTORE_PASS).
set -euo pipefail
cd "$(dirname "$0")"
: "${ANDROID_SDK_ROOT:?}"; : "${KIOSK_KEYSTORE:?}"; : "${KIOSK_KEYSTORE_PASS:?}"
[ -n "${JAVA_HOME:-}" ] && export PATH="$JAVA_HOME/bin:$PATH"
BT="$ANDROID_SDK_ROOT/build-tools/34.0.0"; AJ="$ANDROID_SDK_ROOT/platforms/android-34/android.jar"
rm -rf b && mkdir -p b/c b/d
"$BT/aapt2" link -o b/u.apk -I "$AJ" --manifest AndroidManifest.xml
javac -source 11 -target 11 -nowarn -cp "$AJ" -d b/c src/com/taknik/easydplayer/Main.java 2>/dev/null || true
"$BT/d8" --min-api 30 --lib "$AJ" --output b/d b/c/com/taknik/easydplayer/*.class
python3 -c "import zipfile;z=zipfile.ZipFile('b/u.apk','a');z.write('b/d/classes.dex','classes.dex')"
"$BT/zipalign" -f 4 b/u.apk b/a.apk
"$BT/apksigner" sign --ks "$KIOSK_KEYSTORE" --ks-pass env:KIOSK_KEYSTORE_PASS --key-pass env:KIOSK_KEYSTORE_PASS --out stub.apk b/a.apk
ls -la stub.apk
