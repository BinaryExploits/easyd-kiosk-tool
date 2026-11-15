package com.easyd.kiosk;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * adb-only configuration (protected by android.permission.DUMP):
 *  am broadcast --include-stopped-packages -n com.easyd.kiosk/.ConfigReceiver -a com.easyd.kiosk.SET_TARGET --es target PKG
 *  am broadcast --include-stopped-packages -n com.easyd.kiosk/.ConfigReceiver -a com.easyd.kiosk.GET_TARGET
 *  am broadcast --include-stopped-packages -n com.easyd.kiosk/.ConfigReceiver -a com.easyd.kiosk.LIST_APPS
 *  am broadcast --include-stopped-packages -n com.easyd.kiosk/.ConfigReceiver -a com.easyd.kiosk.DISABLE_HOME
 */
public class ConfigReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent intent) {
        String a = intent.getAction();
        if (a == null) return;
        switch (a) {
            case "com.easyd.kiosk.SET_TARGET": {
                String pkg = intent.getStringExtra("target");
                if (pkg == null || pkg.trim().isEmpty()) { reply(0, "ERR:no_target_extra"); return; }
                pkg = pkg.trim();
                if (Prefs.launchIntent(c, pkg) == null) { reply(0, "ERR:not_launchable:" + pkg); return; }
                if (!Prefs.setTarget(c, pkg)) { reply(0, "ERR:save_failed"); return; }
                Prefs.setHomeEnabled(c, true);
                reply(1, "OK:" + pkg);
                return;
            }
            case "com.easyd.kiosk.GET_TARGET":
                reply(1, "TARGET:" + Prefs.target(c));
                return;
            case "com.easyd.kiosk.DISABLE_HOME":
                Prefs.setHomeEnabled(c, false);
                reply(1, "OK:home_disabled");
                return;
            case "com.easyd.kiosk.LIST_APPS":
                reply(1, "APPS:" + listApps(c));
                return;
            default:
        }
    }

    private void reply(int code, String data) {
        if (isOrderedBroadcast()) { setResultCode(code); setResultData(data); }
    }

    /** Base64(UTF-8) of lines "package\tlabel\tsys(0|1)" for every launchable app. */
    private static String listApps(Context c) {
        PackageManager pm = c.getPackageManager();
        Map<String, String> out = new LinkedHashMap<>();
        for (String cat : new String[]{Intent.CATEGORY_LEANBACK_LAUNCHER, Intent.CATEGORY_LAUNCHER}) {
            Intent q = new Intent(Intent.ACTION_MAIN).addCategory(cat);
            List<ResolveInfo> l = pm.queryIntentActivities(q, 0);
            for (ResolveInfo ri : l) {
                String p = ri.activityInfo.packageName;
                if (out.containsKey(p) || p.equals(c.getPackageName())) continue;
                ApplicationInfo ai = ri.activityInfo.applicationInfo;
                boolean sys = (ai.flags & (ApplicationInfo.FLAG_SYSTEM | ApplicationInfo.FLAG_UPDATED_SYSTEM_APP)) != 0;
                CharSequence label = ai.loadLabel(pm);
                String lb = label == null ? p : label.toString().replace('\t', ' ').replace('\n', ' ');
                out.put(p, p + "\t" + lb + "\t" + (sys ? "1" : "0"));
            }
        }
        StringBuilder sb = new StringBuilder();
        for (String s : out.values()) sb.append(s).append('\n');
        return Base64.encodeToString(sb.toString().getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
    }
}
