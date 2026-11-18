package com.easyd.kiosk;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.WindowManager;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * Full-screen 4-digit code so the user can tell TVs apart from the Windows wizard.
 * Started over adb (shell can launch this non-exported activity). Auto-closes.
 */
public class IdentifyActivity extends Activity {
    private static final long VISIBLE_MS = 20000;
    private final Handler h = new Handler(Looper.getMainLooper());
    private final Runnable close = this::finish;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        String code = getIntent() != null ? getIntent().getStringExtra("code") : null;
        if (code == null || code.trim().isEmpty()) code = "----";
        code = code.trim();

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        root.setBackgroundColor(Color.rgb(8, 28, 64));
        root.setPadding(48, 48, 48, 48);
        root.setFocusable(true);
        root.setFocusableInTouchMode(true);

        TextView kicker = new TextView(this);
        kicker.setText("This is your TV");
        kicker.setTextColor(Color.WHITE);
        kicker.setTextSize(TypedValue.COMPLEX_UNIT_SP, 28);
        kicker.setGravity(Gravity.CENTER);
        root.addView(kicker);

        TextView digits = new TextView(this);
        digits.setText(code);
        digits.setTextColor(Color.WHITE);
        digits.setTextSize(TypedValue.COMPLEX_UNIT_SP, 96);
        digits.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        digits.setGravity(Gravity.CENTER);
        digits.setPadding(0, 24, 0, 24);
        root.addView(digits);

        TextView hint = new TextView(this);
        hint.setText("The same code is shown in Kiosk Manager on your PC.\nPress Back or OK to close.");
        hint.setTextColor(0xFFB0BEC5);
        hint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        hint.setGravity(Gravity.CENTER);
        root.addView(hint);

        setContentView(root);
        root.requestFocus();
    }

    @Override
    protected void onResume() {
        super.onResume();
        h.removeCallbacks(close);
        h.postDelayed(close, VISIBLE_MS);
    }

    @Override
    protected void onPause() {
        super.onPause();
        h.removeCallbacks(close);
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK
                || keyCode == KeyEvent.KEYCODE_ESCAPE
                || keyCode == KeyEvent.KEYCODE_DPAD_CENTER
                || keyCode == KeyEvent.KEYCODE_ENTER
                || keyCode == KeyEvent.KEYCODE_BUTTON_A) {
            finish();
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }
}
