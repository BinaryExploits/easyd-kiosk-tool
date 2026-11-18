package com.easyd.kiosk;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.Settings;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.ArrayDeque;

/**
 * Home screen that only ever opens the configured target app.
 *
 * Hidden escape hatches (so the device can never be locked out):
 *  - Hold OK / Enter for ~3 seconds while this screen is visible (shown briefly after Home is pressed,
 *    or permanently when the target app is missing) -> opens Android Settings.
 *  - Key sequence Up Up Down Down Left Right Left Right -> opens Android Settings.
 *  - Over adb: Kiosk Manager's "Restore original" (or `adb uninstall com.easyd.kiosk` plus `pm enable`).
 */
public class KioskActivity extends Activity {
    private static final long SPLASH_MS = 2000;
    private static final long HOLD_FOR_SETTINGS_MS = 3000;
    private static final long LOOP_WINDOW_MS = 20000;
    private static final int LOOP_MAX_LAUNCHES = 4;
    private static final int[] SEQ = {
            KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN, KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT,
            KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT};

    private static final ArrayDeque<Long> launches = new ArrayDeque<>();

    private final Handler h = new Handler(Looper.getMainLooper());
    private final Runnable autoLaunch = this::launchTarget;
    private TextView title, msg;
    private int seqPos = 0;
    private boolean okDown = false, settingsOpenedByHold = false;
    private boolean autoEnabled = true;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        root.setBackgroundColor(Color.BLACK);
        root.setFocusable(true);
        title = new TextView(this);
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 34);
        title.setGravity(Gravity.CENTER);
        msg = new TextView(this);
        msg.setTextColor(0xFFB0BEC5);
        msg.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        msg.setGravity(Gravity.CENTER);
        msg.setPadding(48, 32, 48, 0);
        root.addView(title);
        root.addView(msg);
        setContentView(root);
        root.requestFocus();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        // Home pressed again: give auto-launch another chance even after a crash-loop stop.
        autoEnabled = true;
    }

    @Override
    protected void onResume() {
        super.onResume();
        okDown = false;
        settingsOpenedByHold = false;
        seqPos = 0;
        String target = Prefs.target(this);
        Intent li = Prefs.launchIntent(this, target);
        if (li == null) {
            showMessage("App not available",
                    (Prefs.DEFAULT_TARGET.equals(target) ? "Easy Display Player" : target) + " is not installed or cannot be opened."
                            + "\n\nInstall it, then press OK.");
            return;
        }
        if (isLooping()) {
            showMessage(appLabel(target), "The app closed several times in a row.\nPress OK to open it again.");
            return;
        }
        if (!autoEnabled) {
            showMessage(appLabel(target), "Press OK to open.");
            return;
        }
        title.setText(appLabel(target));
        msg.setText("");
        h.postDelayed(autoLaunch, SPLASH_MS);
    }

    @Override
    protected void onPause() {
        super.onPause();
        h.removeCallbacks(autoLaunch);
    }

    private void showMessage(String t, String m) {
        h.removeCallbacks(autoLaunch);
        title.setText(t);
        msg.setText(m);
    }

    private String appLabel(String pkg) {
        try {
            return getPackageManager().getApplicationLabel(getPackageManager().getApplicationInfo(pkg, 0)).toString();
        } catch (Exception e) {
            return pkg == null ? "" : pkg;
        }
    }

    private boolean isLooping() {
        long now = SystemClock.elapsedRealtime();
        while (!launches.isEmpty() && now - launches.peekFirst() > LOOP_WINDOW_MS) launches.pollFirst();
        return launches.size() >= LOOP_MAX_LAUNCHES;
    }

    private void launchTarget() {
        h.removeCallbacks(autoLaunch);
        String target = Prefs.target(this);
        Intent li = Prefs.launchIntent(this, target);
        if (li == null) { onResume(); return; }
        launches.addLast(SystemClock.elapsedRealtime());
        try {
            startActivity(li);
        } catch (Exception e) {
            showMessage("Could not open the app", String.valueOf(e.getMessage()) + "\n\nPress OK to try again.");
        }
    }

    private void openSettings() {
        h.removeCallbacks(autoLaunch);
        autoEnabled = false;
        Intent[] tries = {
                new Intent(Settings.ACTION_SETTINGS),
                new Intent().setClassName("com.android.tv.settings", "com.android.tv.settings.MainSettings"),
                new Intent().setPackage("com.android.tv.settings").setAction(Intent.ACTION_MAIN)};
        for (Intent i : tries) {
            try {
                startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                return;
            } catch (ActivityNotFoundException ignored) {
            } catch (SecurityException ignored) {
            }
        }
        showMessage("Settings not found", "Use Kiosk Manager's Restore button over adb.");
    }

    private static boolean isOk(int k) {
        return k == KeyEvent.KEYCODE_DPAD_CENTER || k == KeyEvent.KEYCODE_ENTER
                || k == KeyEvent.KEYCODE_NUMPAD_ENTER || k == KeyEvent.KEYCODE_BUTTON_A;
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent e) {
        // Any key pauses the auto-launch countdown while the user is interacting.
        h.removeCallbacks(autoLaunch);
        if (isOk(keyCode)) {
            if (e.getRepeatCount() == 0) { okDown = true; settingsOpenedByHold = false; }
            else if (okDown && !settingsOpenedByHold
                    && e.getEventTime() - e.getDownTime() >= HOLD_FOR_SETTINGS_MS) {
                settingsOpenedByHold = true;
                openSettings();
            }
            return true;
        }
        if (e.getRepeatCount() == 0) {
            if (keyCode == SEQ[seqPos]) {
                seqPos++;
                if (seqPos == SEQ.length) { seqPos = 0; openSettings(); }
            } else {
                seqPos = (keyCode == SEQ[0]) ? 1 : 0;
            }
        }
        if (keyCode == KeyEvent.KEYCODE_BACK) return true; // home never closes
        return super.onKeyDown(keyCode, e);
    }

    @Override
    public boolean onKeyUp(int keyCode, KeyEvent e) {
        if (isOk(keyCode)) {
            boolean wasHold = settingsOpenedByHold;
            okDown = false;
            settingsOpenedByHold = false;
            if (!wasHold) {
                // Short press = open the app now (also clears a crash-loop stop).
                launches.clear();
                autoEnabled = true;
                launchTarget();
            }
            return true;
        }
        if (keyCode == KeyEvent.KEYCODE_BACK) return true;
        // Resume the countdown after a non-OK key (longer while the user may be typing the sequence).
        if (autoEnabled && hasWindowFocus() && Prefs.launchIntent(this, Prefs.target(this)) != null && !isLooping()) {
            h.postDelayed(autoLaunch, seqPos == 0 ? SPLASH_MS * 2 : SPLASH_MS * 4);
        }
        return super.onKeyUp(keyCode, e);
    }

    @Override
    public void onBackPressed() { /* ignore: home screen */ }
}
