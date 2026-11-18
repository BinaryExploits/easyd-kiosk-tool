# EasyDisplay Kiosk Manager

**EasyDisplay Kiosk Manager** (short name: **Kiosk Manager**) is a Windows tool that turns an **Android TV 11+**
device (Xiaomi Mi TV Stick 4K, Chromecast with Google TV, Google TV Streamer, most Android TV boxes)
into a kiosk that only shows **EasyD Player** (`com.taknik.easydplayer`). After setup the TV opens
EasyD Player at start-up and whenever Home is pressed. Other apps stay installed; nothing is deleted,
and everything can be undone.

This repository is [`BinaryExploits/kiosk-manager`](https://github.com/BinaryExploits/kiosk-manager)
(previously `easyd-kiosk-tool`).

It has two parts:

| Part | What it is |
|------|------------|
| `win/` | `KioskManager.exe`: a .NET 8 WinForms wizard (self-contained single file, win-x64) that bundles adb and the kiosk APK |
| `apk/` | `com.easyd.kiosk`: a tiny home-screen app, labelled **Kiosk Manager**, that opens the target app and nothing else |

A prebuilt exe is in [`release/KioskManager.exe`](release/KioskManager.exe) and on the Releases page.

> **Status:** tested on an Android TV 11 emulator with a stand-in app. **Untested on real hardware.**
> The exe is not code-signed, so Windows SmartScreen will warn ("More info" → "Run anyway").

## Using it

1. On the TV turn on Developer options:
   - **Android TV (Xiaomi Mi TV Stick 4K and most boxes):** Settings › Device Preferences › About › press
     **Android TV OS build** 7 times (on some devices it's just called **Build**), then Device Preferences ›
     Developer options › **USB debugging**. Note the TV's IP address under Settings › Network & Internet ›
     your Wi-Fi network. Many Xiaomi sticks have no **Wireless debugging** entry; USB debugging is enough.
     When the TV shows **Allow USB debugging?**, tick **Always allow from this computer** and press OK.
   - **Google TV (Chromecast):** Settings › System › About › press **Android TV OS build** 7 times
     (on some devices it's just called **Build**), then System › Developer options › **Wireless debugging**.
     In Wireless debugging choose **Pair device with pairing code** and keep that screen open.
2. On a Windows PC on the same Wi-Fi, run `KioskManager.exe` and follow the 4 steps:
   **Prepare → Connect → EasyD Player** (if it's missing, its Play Store page opens on the TV; press Install
   and the tool continues by itself) **→ Make kiosk**.
   On Connect, choose how to reach the TV (the default follows the menu type you picked in Prepare):
   - **Xiaomi / Android TV: Connect by IP (USB debugging)** — type the TV's IP (port 5555 is used if you
     don't add a port) and click Connect. The tool waits up to about a minute for you to allow debugging
     on the TV.
   - **Google TV / Chromecast: Pair with code (Wireless debugging)** — auto-discovers the TV over mDNS;
     type the 6-digit pairing code, then Connect.
3. To undo, run the tool again and click **Restore original** on the last step.

"Advanced: choose a different app" on the EasyD Player step lets you pick any launchable app instead of EasyD Player.

### What "Make kiosk" does (in order)

1. Checks the target app is installed.
2. Installs `com.easyd.kiosk` (its HOME entry ships **disabled**, so installing alone changes nothing).
3. Sets the target via an adb-only broadcast, which also enables the HOME entry.
4. Verifies Android lists the kiosk as a HOME activity. **If not, it stops before disabling anything.**
5. Saves the list of packages it will disable (on the PC and in `/data/local/tmp` on the TV), then
   `pm disable-user` on the stock launchers that are present: `com.google.android.tvlauncher`,
   `com.google.android.apps.tv.launcherx`, `com.google.android.tungsten.setupwraith`, plus any other
   HOME-resolving package you leave ticked. Settings/setup/boot packages are never disabled.
6. `cmd package set-home-activity` to the kiosk, then presses Home.

**Restore original** re-enables everything recorded (plus any disabled known stock launcher),
uninstalls the kiosk and makes the stock launcher the default again.

### Escape keys on the TV (no PC needed)

Right after pressing Home, while the dark "EasyD Player" screen shows (about 2 s), or any time the
"App not available" screen is shown:

- hold **OK** for 3 seconds, or
- press **Up Up Down Down Left Right Left Right**

to open Android Settings. A factory reset also removes the kiosk.

### Kiosk app behaviour

- Back/Home always return to the target app; a crash loop (4 launches in 20 s) stops auto-launch and shows "Press OK to open".
- If the target is missing, a full-screen message is shown.
- Config receiver `com.easyd.kiosk/.ConfigReceiver` is protected by `android.permission.DUMP`, so only adb (shell) can change it:

```
adb shell am broadcast --include-stopped-packages -n com.easyd.kiosk/.ConfigReceiver \
    -a com.easyd.kiosk.SET_TARGET --es target com.taknik.easydplayer
# other actions: com.easyd.kiosk.GET_TARGET, com.easyd.kiosk.LIST_APPS, com.easyd.kiosk.DISABLE_HOME
```

## Building

Requirements: JDK 17+, Android SDK (`build-tools;34.0.0`, `platforms;android-34`), .NET 8 SDK, Python 3, curl.
Builds on Linux, macOS, or Windows (bash); the exe is cross-published for win-x64.

### Signing key

The APK must be signed. **No keystore is in this repo.** Either use your own keystore or keep the existing
release keystore private. Note Android only lets you update an installed `com.easyd.kiosk` with an APK
signed by the **same** key, so to update devices already set up with the released APK you need the
original keystore; otherwise run Restore (uninstall) first.

```bash
export ANDROID_SDK_ROOT=/path/to/android-sdk
export JAVA_HOME=/path/to/jdk
export KIOSK_KEYSTORE=/secure/path/kiosk.keystore
export KIOSK_KEYSTORE_PASS='...'          # never commit this
# first time only, to create a new key:  export KIOSK_GENERATE_KEY=1

./apk/build.sh                    # -> apk/build/EasyDKiosk.apk
./win/fetch-payload.sh            # copies the APK + downloads Google platform-tools into win/payload/
cd win && dotnet publish -c Release -o out   # -> win/out/KioskManager.exe (~80 MB, self-contained)
```

`win/payload/` is git-ignored: the APK and Google's platform-tools are embedded into the exe at publish time.
If platform-tools is not embedded, the exe downloads it from Google on first run.

### Tests

- `test/dummy/` – a stand-in app with package `com.taknik.easydplayer` (`./test/dummy/build.sh`).
- `test/harness/` – runs the real `KioskOps`/`Adb` code against an emulator or device:
  `ADB_PATH=$(which adb) ANDROID_SERIAL=emulator-5554 dotnet run --project test/harness`
  (needs `win/payload/EasyDKiosk.apk`).

## Limitations

- **Untested on real hardware** (Xiaomi Mi TV Stick 4K, Chromecast with Google TV) and with the real EasyD Player.
  Verified on an Android TV 11 x86 emulator: install, set target, HOME resolution, disabling the stock launcher,
  relaunch on Home/Back, the key-sequence escape, the missing-app screen, and Restore.
- The Windows UI has not been visually checked on Windows (built on Linux).
- Hold-OK escape and the 2-second splash timing were not testable on the slow emulator.
- Google TV specifics (`launcherx` / `setupwraith`) follow a manual run on a Chromecast, not an automated test.
- Requires Android 11+. Xiaomi Mi TV Stick 4K (Android 11) is connected by IP on port 5555 after turning on USB debugging (it has no Wireless debugging). Google TV uses Wireless debugging and a pairing code. The original Mi TV Stick (Android 9) is not supported.
- Unsigned exe: SmartScreen warning expected. Windows Firewall may ask about adb (allow on Private networks for auto-discovery).

## License

MIT © BinaryExploits. Bundled at build time: Google Android SDK Platform-Tools (Google's license terms).
