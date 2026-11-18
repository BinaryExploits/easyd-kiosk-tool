# Kiosk Manager

**Kiosk Manager** is a free Windows tool that turns an Android TV or Google TV device into a kiosk that only shows [Easy Display](https://play.google.com/store/apps/details?id=com.taknik.easydplayer). After setup, the TV opens Easy Display at start-up and whenever Home is pressed. Other apps stay installed — nothing is deleted — and you can undo everything later.

## Download

Get the latest Windows build here:

**https://github.com/BinaryExploits/kiosk-manager/releases/latest**

Download `KioskManager.exe` (or the zip that contains the exe and this README) and run it on your PC.

Windows SmartScreen will likely warn that the app is unrecognized, because the exe is not code-signed. Click **More info**, then **Run anyway**. If Windows Firewall asks about `adb`, allow it on private networks so the TV can be found automatically.

## What you need

- A PC running **Windows 10** or **Windows 11**
- An **Android TV** or **Google TV** device on **Android 11 or newer**, on the **same Wi-Fi** as the PC. This includes the Xiaomi Mi TV Stick 4K, Chromecast with Google TV, Google TV Streamer, and most Android TV boxes. The original Xiaomi Mi TV Stick (Android 9) is not supported.

## How to use it

The wizard has four steps: **Prepare → Connect → Easy Display → Make kiosk**.

### 1. Prepare the TV

On the TV, turn on Developer options, then follow the path that matches your device.

**Xiaomi / Android TV** (Mi TV Stick 4K and most boxes):

1. Open **Settings › Device Preferences › About**.
2. Press **Android TV OS build** 7 times (on some devices it is just called **Build**) until it says you are a developer.
3. Go to **Settings › Device Preferences › Developer options** and turn on **USB debugging**. Many Xiaomi sticks have no Wireless debugging entry — USB debugging is enough.
4. Find the TV’s IP address under **Settings › Network & Internet › your Wi-Fi network**.

**Google TV** (Chromecast with Google TV, Google TV Streamer):

1. Open **Settings › System › About**.
2. Press **Android TV OS build** 7 times (on some devices it is just called **Build**; enter your PIN if asked) until it says you are a developer.
3. Go to **Settings › System › Developer options**. Turn on **USB debugging**, then open **Wireless debugging** and turn it on.
4. In Wireless debugging choose **Pair device with pairing code** and leave that screen open.

### 2. Connect this PC to the TV

On the PC, run `KioskManager.exe`. On the Prepare step, pick the menu type that matches your TV, then go to Connect. The default connect method follows that choice.

**Xiaomi / Android TV: Connect by IP (USB debugging)**

1. Type the TV’s IP address (port **5555** is used if you do not add a port; `ip:port` is also accepted) and click **Connect**.
2. When the TV shows **Allow USB debugging?**, tick **Always allow from this computer** and press OK. The tool waits up to about a minute for that.

**Google TV / Chromecast: Pair with code (Wireless debugging)**

1. The tool tries to find the TV automatically. If it does not, type the IP address:port shown under the pairing code.
2. Enter the 6-digit pairing code and click **Pair**, then **Connect**. The connect port at the top of the Wireless debugging screen is different from the pairing port.

### 3. Easy Display

The tool checks whether Easy Display is installed. If it is missing, the Play Store page opens on the TV — press **Install** with the remote, and the tool continues by itself. You can also choose a different app under **Advanced**.

### 4. Make kiosk

Click **Make kiosk**. The TV will then open Easy Display at start-up and whenever Home is pressed. When it finishes, an **All done!** screen shows the TV’s name, IP and Android version, and reminds you how to reach Settings.

**Set up another TV** disconnects this device and returns to Prepare. **Close** exits.

## Hidden way to reach Settings

The kiosk can look locked to Easy Display. To open Android Settings on the TV (no PC needed), press Home, then while the dark Easy Display screen is showing (about 2 seconds), or whenever the “App not available” screen is shown:

- hold **OK** for 3 seconds, or
- press **Up Up Down Down Left Right Left Right**

A factory reset also removes the kiosk.

## Restore original

To put the original home screen back, run Kiosk Manager again, connect to the same TV (USB debugging or Wireless debugging must be on), and click **Restore original** on the last step. That re-enables the stock launcher, uninstalls the kiosk app, and leaves your other apps in place.

## License

Kiosk Manager is released under the [MIT License](LICENSE) © BinaryExploits.

The Windows build bundles Google Android SDK Platform-Tools at build time (Google’s license terms).

## Building from source

For developers: JDK 17+, Android SDK (`build-tools;34.0.0`, `platforms;android-34`), .NET 8 SDK, Python 3, and curl. Builds on Linux, macOS, or Windows (bash); the exe is cross-published for win-x64.

The APK must be signed. **No keystore is in this repo.** To update an already-installed `com.easyd.kiosk`, you need the same signing key; otherwise run Restore first.

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

`win/payload/` is git-ignored. If platform-tools is not embedded, the exe downloads it from Google on first run.

Confirmed on a Xiaomi Mi TV Stick 4K (Android 11) with Easy Display, including Connect by IP and Make kiosk. The Google TV pairing path was also used on a Chromecast with Google TV. The Windows UI was built on Linux.
