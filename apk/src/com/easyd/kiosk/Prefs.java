package com.easyd.kiosk;

import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;

final class Prefs {
    static final String FILE = "kiosk";
    static final String KEY_TARGET = "target";
    static final String DEFAULT_TARGET = "com.taknik.easydplayer";

    static SharedPreferences sp(Context c) {
        // Device-protected storage so the target is readable during direct boot too.
        Context dc = c.createDeviceProtectedStorageContext();
        return dc.getSharedPreferences(FILE, Context.MODE_PRIVATE);
    }

    static String target(Context c) { return sp(c).getString(KEY_TARGET, DEFAULT_TARGET); }

    static boolean setTarget(Context c, String pkg) {
        return sp(c).edit().putString(KEY_TARGET, pkg).commit();
    }

    static Intent launchIntent(Context c, String pkg) {
        if (pkg == null || pkg.isEmpty()) return null;
        PackageManager pm = c.getPackageManager();
        Intent i = pm.getLeanbackLaunchIntentForPackage(pkg);
        if (i == null) i = pm.getLaunchIntentForPackage(pkg);
        if (i != null) {
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);
        }
        return i;
    }

    static void setHomeEnabled(Context c, boolean enabled) {
        c.getPackageManager().setComponentEnabledSetting(
                new ComponentName(c, "com.easyd.kiosk.HomeAlias"),
                enabled ? PackageManager.COMPONENT_ENABLED_STATE_ENABLED
                        : PackageManager.COMPONENT_ENABLED_STATE_DISABLED,
                PackageManager.DONT_KILL_APP);
    }
}
