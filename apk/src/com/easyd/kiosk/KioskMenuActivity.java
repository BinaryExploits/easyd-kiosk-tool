package com.easyd.kiosk;

import android.app.Activity;
import android.app.ActivityManager;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.graphics.Color;
import android.graphics.drawable.ColorDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.AbsListView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ScrollView;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * D-pad menu opened from the hidden escape (hold OK 3 s, or Up Up Down Down Left Right Left Right).
 * Launching Settings or another app from here leaves the menu under it; Home still returns to the
 * kiosk, which then opens the target app. Back closes the menu (or returns from All apps).
 */
public class KioskMenuActivity extends Activity {
    private boolean showingApps = false;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        KioskActivity.suppressAutoLaunch();
        showMainMenu();
    }

    @Override
    public void onBackPressed() {
        if (showingApps) showMainMenu();
        else finish();
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent e) {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            onBackPressed();
            return true;
        }
        return super.onKeyDown(keyCode, e);
    }

    private void showMainMenu() {
        showingApps = false;
        String label = targetLabel();
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.BLACK);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        root.setPadding(dp(32), dp(36), dp(32), dp(36));
        root.setBackgroundColor(Color.BLACK);

        TextView title = new TextView(this);
        title.setText("Kiosk menu");
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 34);
        title.setGravity(Gravity.CENTER);
        title.setPadding(0, 0, 0, dp(8));
        root.addView(title, lp());

        TextView hint = new TextView(this);
        hint.setText("Choose an option, or press Back to close this menu.");
        hint.setTextColor(0xFFB0BEC5);
        hint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        hint.setGravity(Gravity.CENTER);
        hint.setPadding(0, 0, 0, dp(20));
        root.addView(hint, lp());

        Button first = addBtn(root, "Restart " + label, v -> restartTarget());
        addBtn(root, "App settings / Clear data", v -> openAppSettings());
        addBtn(root, "TV Settings", v -> openTvSettings());
        addBtn(root, "All apps", v -> showAllApps());
        addBtn(root, "Back to " + label, v -> backToTarget());

        scroll.addView(root, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        setContentView(scroll);
        first.post(first::requestFocus);
    }

    private void showAllApps() {
        showingApps = true;
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.BLACK);
        root.setPadding(dp(24), dp(24), dp(24), dp(16));

        TextView title = new TextView(this);
        title.setText("All apps");
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 28);
        title.setPadding(0, 0, 0, dp(8));
        root.addView(title, lp());

        TextView hint = new TextView(this);
        hint.setText("Press Back to return to the Kiosk menu.");
        hint.setTextColor(0xFFB0BEC5);
        hint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        hint.setPadding(0, 0, 0, dp(12));
        root.addView(hint, lp());

        List<AppEntry> apps = listLaunchableApps();
        ListView list = new ListView(this);
        list.setBackgroundColor(Color.BLACK);
        list.setDivider(new ColorDrawable(0xFF37474F));
        list.setDividerHeight(1);
        list.setSelector(new ColorDrawable(0xFF1565C0));
        list.setAdapter(new AppAdapter(this, apps));
        list.setOnItemClickListener((parent, view, position, id) -> launchApp(apps.get(position)));
        LinearLayout.LayoutParams listLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        root.addView(list, listLp);
        setContentView(root);
        if (!apps.isEmpty()) list.post(() -> list.requestFocus());
    }

    private Button addBtn(LinearLayout root, String text, View.OnClickListener click) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        b.setPadding(dp(28), dp(22), dp(28), dp(22));
        b.setTextColor(Color.WHITE);
        b.setFocusable(true);
        b.setFocusableInTouchMode(true);
        b.setBackgroundColor(0xFF37474F);
        b.setOnFocusChangeListener((v, has) ->
                v.setBackgroundColor(has ? 0xFF1565C0 : 0xFF37474F));
        b.setOnClickListener(click);
        LinearLayout.LayoutParams p = lp();
        p.width = ViewGroup.LayoutParams.MATCH_PARENT;
        p.topMargin = dp(8);
        p.bottomMargin = dp(8);
        root.addView(b, p);
        return b;
    }

    private void restartTarget() {
        String pkg = Prefs.target(this);
        try {
            ActivityManager am = (ActivityManager) getSystemService(ACTIVITY_SERVICE);
            if (am != null) am.killBackgroundProcesses(pkg);
        } catch (Exception ignored) {
        }
        try {
            ActivityManager am = (ActivityManager) getSystemService(ACTIVITY_SERVICE);
            ActivityManager.class.getMethod("forceStopPackage", String.class).invoke(am, pkg);
        } catch (Exception ignored) {
        }
        Intent li = Prefs.launchIntent(this, pkg);
        if (li == null) {
            toast("Could not open " + targetLabel());
            return;
        }
        li.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        try {
            startActivity(li);
            finish();
        } catch (Exception e) {
            toast("Could not open " + targetLabel());
        }
    }

    private void backToTarget() {
        Intent li = Prefs.launchIntent(this, Prefs.target(this));
        if (li == null) {
            toast("Could not open " + targetLabel());
            return;
        }
        try {
            startActivity(li);
            finish();
        } catch (Exception e) {
            toast("Could not open " + targetLabel());
        }
    }

    private void openAppSettings() {
        KioskActivity.suppressAutoLaunch();
        String pkg = Prefs.target(this);
        Intent i = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        i.setData(Uri.fromParts("package", pkg, null));
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        if (startSafely(i)) return;
        // Fallbacks if the TV settings app does not handle the details action.
        Intent manage = new Intent(Settings.ACTION_APPLICATION_SETTINGS);
        manage.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        if (startSafely(manage)) return;
        toast("App settings not found");
    }

    private void openTvSettings() {
        KioskActivity.suppressAutoLaunch();
        Intent[] tries = {
                new Intent(Settings.ACTION_SETTINGS),
                new Intent().setClassName("com.android.tv.settings", "com.android.tv.settings.MainSettings"),
                new Intent().setPackage("com.android.tv.settings").setAction(Intent.ACTION_MAIN)
        };
        for (Intent i : tries) {
            if (startSafely(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))) return;
        }
        toast("Settings not found");
    }

    private void launchApp(AppEntry app) {
        if (app == null || app.launch == null) return;
        KioskActivity.suppressAutoLaunch();
        Intent i = new Intent(app.launch);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        if (!startSafely(i)) toast("Could not open " + app.label);
    }

    private boolean startSafely(Intent i) {
        try {
            startActivity(i);
            return true;
        } catch (ActivityNotFoundException e) {
            return false;
        } catch (SecurityException e) {
            return false;
        } catch (Exception e) {
            return false;
        }
    }

    private void toast(String m) {
        android.widget.Toast.makeText(this, m, android.widget.Toast.LENGTH_SHORT).show();
    }

    private String targetLabel() {
        String pkg = Prefs.target(this);
        try {
            return getPackageManager().getApplicationLabel(getPackageManager().getApplicationInfo(pkg, 0)).toString();
        } catch (Exception e) {
            return Prefs.DEFAULT_TARGET.equals(pkg) ? "Easy Display Player" : pkg;
        }
    }

    private List<AppEntry> listLaunchableApps() {
        PackageManager pm = getPackageManager();
        Map<String, AppEntry> out = new LinkedHashMap<>();
        for (String cat : new String[]{Intent.CATEGORY_LEANBACK_LAUNCHER, Intent.CATEGORY_LAUNCHER}) {
            Intent q = new Intent(Intent.ACTION_MAIN).addCategory(cat);
            List<ResolveInfo> resolved = pm.queryIntentActivities(q, 0);
            for (ResolveInfo ri : resolved) {
                if (ri.activityInfo == null) continue;
                String p = ri.activityInfo.packageName;
                if (out.containsKey(p) || p.equals(getPackageName())) continue;
                AppEntry e = new AppEntry();
                e.pkg = p;
                CharSequence lb = ri.activityInfo.applicationInfo != null
                        ? ri.activityInfo.applicationInfo.loadLabel(pm) : p;
                e.label = lb == null ? p : lb.toString();
                e.launch = pm.getLeanbackLaunchIntentForPackage(p);
                if (e.launch == null) e.launch = pm.getLaunchIntentForPackage(p);
                if (e.launch == null) {
                    e.launch = new Intent(Intent.ACTION_MAIN)
                            .addCategory(cat)
                            .setClassName(p, ri.activityInfo.name)
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                }
                out.put(p, e);
            }
        }
        return new ArrayList<>(out.values());
    }

    private LinearLayout.LayoutParams lp() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private int dp(int v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private static final class AppEntry {
        String pkg, label;
        Intent launch;
        @Override public String toString() { return label; }
    }

    private static final class AppAdapter extends ArrayAdapter<AppEntry> {
        AppAdapter(Context c, List<AppEntry> items) {
            super(c, 0, items);
        }

        @Override
        public View getView(int position, View convert, ViewGroup parent) {
            TextView tv = convert instanceof TextView ? (TextView) convert : new TextView(getContext());
            AppEntry e = getItem(position);
            tv.setText(e == null ? "" : e.label);
            tv.setTextColor(Color.WHITE);
            tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
            tv.setPadding(48, 36, 48, 36);
            tv.setLayoutParams(new AbsListView.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            return tv;
        }
    }
}
