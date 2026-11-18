#!/bin/bash
# Builds and signs the kiosk launcher APK (no Gradle needed).
# Required env:
#   KIOSK_KEYSTORE        path to your signing keystore (never commit it)
#   KIOSK_KEYSTORE_PASS   its password (store and key)
# Optional env:
#   KIOSK_KEY_ALIAS       key alias (default: kiosk)
#   JAVA_HOME             JDK 17+
#   ANDROID_SDK_ROOT      SDK with build-tools;34.0.0 and platforms;android-34
#   KIOSK_GENERATE_KEY=1  create a new keystore at $KIOSK_KEYSTORE if it does not exist
set -euo pipefail
cd "$(dirname "$0")"
: "${KIOSK_KEYSTORE:?Set KIOSK_KEYSTORE to the path of your signing keystore}"
: "${KIOSK_KEYSTORE_PASS:?Set KIOSK_KEYSTORE_PASS to the keystore password}"
ALIAS="${KIOSK_KEY_ALIAS:-kiosk}"
: "${ANDROID_SDK_ROOT:?Set ANDROID_SDK_ROOT}"
[ -n "${JAVA_HOME:-}" ] && export PATH="$JAVA_HOME/bin:$PATH"
BT="$ANDROID_SDK_ROOT/build-tools/34.0.0"
AJ="$ANDROID_SDK_ROOT/platforms/android-34/android.jar"

rm -rf build && mkdir -p build/res build/classes build/dex
"$BT/aapt2" compile --dir res -o build/res/res.zip
"$BT/aapt2" link -o build/unsigned.apk -I "$AJ" --manifest AndroidManifest.xml build/res/res.zip \
  --java build/gen --min-sdk-version 30 --target-sdk-version 34
javac -source 11 -target 11 -nowarn -classpath "$AJ" -d build/classes $(find src build/gen -name '*.java') 2>&1 | grep -v "bootstrap classpath" || true
"$BT/d8" --release --min-api 30 --lib "$AJ" --output build/dex $(find build/classes -name '*.class')
cp build/unsigned.apk build/withdex.apk
python3 -c "import zipfile;z=zipfile.ZipFile('build/withdex.apk','a',zipfile.ZIP_DEFLATED);z.write('build/dex/classes.dex','classes.dex');z.close()"
"$BT/zipalign" -f -p 4 build/withdex.apk build/aligned.apk

if [ ! -f "$KIOSK_KEYSTORE" ]; then
  if [ "${KIOSK_GENERATE_KEY:-0}" = "1" ]; then
    keytool -genkeypair -keystore "$KIOSK_KEYSTORE" -storepass:env KIOSK_KEYSTORE_PASS -keypass:env KIOSK_KEYSTORE_PASS \
      -alias "$ALIAS" -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Kiosk Manager, O=EasyDisplay" >/dev/null
  else
    echo "Keystore $KIOSK_KEYSTORE not found (set KIOSK_GENERATE_KEY=1 to create one)" >&2; exit 1
  fi
fi
"$BT/apksigner" sign --v2-signing-enabled true --v3-signing-enabled true \
  --ks "$KIOSK_KEYSTORE" --ks-pass env:KIOSK_KEYSTORE_PASS --key-pass env:KIOSK_KEYSTORE_PASS --ks-key-alias "$ALIAS" \
  --out build/EasyDKiosk.apk build/aligned.apk
"$BT/apksigner" verify build/EasyDKiosk.apk
ls -la build/EasyDKiosk.apk
