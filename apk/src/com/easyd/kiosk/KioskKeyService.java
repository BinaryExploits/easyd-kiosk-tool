package com.easyd.kiosk;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.AccessibilityServiceInfo;
import android.os.Handler;
import android.os.Looper;
import android.view.KeyEvent;
import android.view.accessibility.AccessibilityEvent;

/**
 * Sees key events even when Easy Display Player (or any other app) is in the foreground.
 * Hold OK / Enter for ~3 s, or press Up Up Down Down Left Right Left Right, to open
 * the Kiosk menu. Normal key presses are never consumed.
 */
public class KioskKeyService extends AccessibilityService {
    private static final long HOLD_FOR_MENU_MS = 3000;
    private static final int[] SEQ = {
            KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN, KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT,
            KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT};

    private final Handler h = new Handler(Looper.getMainLooper());
    private final Runnable holdFire = this::onHoldComplete;
    private boolean holdingOk, openedThisHold;
    private int seqPos;

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) { }

    @Override
    public void onInterrupt() { }

    @Override
    protected void onServiceConnected() {
        AccessibilityServiceInfo info = getServiceInfo();
        if (info != null) {
            info.flags |= AccessibilityServiceInfo.FLAG_REQUEST_FILTER_KEY_EVENTS;
            setServiceInfo(info);
        }
    }

    @Override
    protected boolean onKeyEvent(KeyEvent e) {
        int k = e.getKeyCode();
        if (isOk(k)) {
            int action = e.getAction();
            if (action == KeyEvent.ACTION_DOWN) {
                if (e.getRepeatCount() == 0) {
                    holdingOk = true;
                    openedThisHold = false;
                    h.removeCallbacks(holdFire);
                    h.postDelayed(holdFire, HOLD_FOR_MENU_MS);
                } else if (holdingOk && !openedThisHold
                        && e.getEventTime() - e.getDownTime() >= HOLD_FOR_MENU_MS) {
                    onHoldComplete();
                }
            } else if (action != KeyEvent.ACTION_DOWN) {
                holdingOk = false;
                h.removeCallbacks(holdFire);
                openedThisHold = false;
            }
            return false;
        }
        if (e.getAction() == KeyEvent.ACTION_DOWN && e.getRepeatCount() == 0) {
            if (k == SEQ[seqPos]) {
                seqPos++;
                if (seqPos == SEQ.length) {
                    seqPos = 0;
                    KioskActivity.openMenu(this);
                }
            } else {
                seqPos = (k == SEQ[0]) ? 1 : 0;
            }
        }
        return false; // never consume — the foreground app must still get every key
    }

    private void onHoldComplete() {
        if (openedThisHold) return;
        openedThisHold = true;
        h.removeCallbacks(holdFire);
        KioskActivity.openMenu(this);
    }

    private static boolean isOk(int k) {
        return k == KeyEvent.KEYCODE_DPAD_CENTER || k == KeyEvent.KEYCODE_ENTER
                || k == KeyEvent.KEYCODE_NUMPAD_ENTER || k == KeyEvent.KEYCODE_BUTTON_A;
    }
}
