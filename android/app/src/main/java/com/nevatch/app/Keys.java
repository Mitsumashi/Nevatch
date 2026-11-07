package com.nevatch.app;

import android.content.Context;

/** The one key Nevatch keeps, in the app's private storage. */
final class Keys {
    private static final String PREFS = "nevatch";
    private static final String KEY = "key";

    static String load(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY, null);
    }

    static void save(Context c, String link) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY, link).apply();
    }

    /** The saved key, or null if there is none (or it no longer parses). */
    static NevatchKey current(Context c) {
        try {
            String s = load(c);
            return s == null ? null : NevatchKey.parse(s);
        } catch (NevatchKey.BadKey e) {
            return null;
        }
    }

    private static final String DEVICE_ID = "device";

    /** A random ID for this install (16 hex characters), so the server can tell one person's devices apart. */
    static synchronized String deviceId(Context c) {
        android.content.SharedPreferences p = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String id = p.getString(DEVICE_ID, null);
        if (id == null || !id.matches("[0-9a-f]{16}")) {
            byte[] b = new byte[8];
            new java.security.SecureRandom().nextBytes(b);
            StringBuilder sb = new StringBuilder();
            for (byte x : b) sb.append(String.format(java.util.Locale.ROOT, "%02x", x & 0xff));
            id = sb.toString();
            p.edit().putString(DEVICE_ID, id).apply();
        }
        return id;
    }

    /** "Samsung SM-S911B", "Google Pixel 8": the maker, unless the model name already starts with it. */
    static String deviceName() {
        String maker = android.os.Build.MANUFACTURER == null ? "" : android.os.Build.MANUFACTURER.trim();
        String model = android.os.Build.MODEL == null ? "" : android.os.Build.MODEL.trim();
        if (maker.isEmpty() || model.regionMatches(true, 0, maker, 0, maker.length())) return model;
        // Samsung reports "samsung"; show it capitalised.
        return Character.toUpperCase(maker.charAt(0)) + maker.substring(1) + " " + model;
    }

    /** "&dev=...&dn=...&os=...&app=...", added to each presence ping so the server can show which devices are connected. */
    static String deviceQuery(Context c) {
        String version;
        try {
            version = c.getPackageManager().getPackageInfo(c.getPackageName(), 0).versionName;
        } catch (Exception e) {
            version = "?";
        }
        return "&dev=" + android.net.Uri.encode(deviceId(c))
            + "&dn=" + android.net.Uri.encode(deviceName())
            + "&os=" + android.net.Uri.encode("Android " + android.os.Build.VERSION.RELEASE)
            + "&app=" + android.net.Uri.encode("Nevatch " + version);
    }

    private Keys() { }
}
