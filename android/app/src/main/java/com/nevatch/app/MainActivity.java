package com.nevatch.app;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.VpnService;
import android.os.Build;
import android.os.Bundle;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.animation.AlphaAnimation;
import android.view.animation.Animation;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.google.zxing.integration.android.IntentIntegrator;
import com.google.zxing.integration.android.IntentResult;

/** The one screen: the key's name, a shield, and one big Connect / Disconnect button. */
public class MainActivity extends Activity {
    private static final int REQ_VPN = 1;
    private static final int COLOR_TEXT = 0xFF1B2433;
    private static final int COLOR_MUTED = 0xFF6B7785;
    private static final int COLOR_BLUE = 0xFF2F7BEA;
    private static final int COLOR_GREEN = 0xFF1DB954;
    private static final int COLOR_DARK = 0xFF3A4452;

    private TextView serverName, status, detail, changeKey;
    private ImageView shield;
    private Button mainButton, scanButton;
    private boolean pulsing;
    private final Runnable onStateChange = this::refresh;

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        buildUi();
        handleLink(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleLink(intent);
    }

    @Override
    protected void onStart() {
        super.onStart();
        NevatchVpnService.listen(onStateChange);
        refresh();
    }

    @Override
    protected void onStop() {
        NevatchVpnService.unlisten(onStateChange);
        super.onStop();
    }

    // ---------- screen ----------

    private void buildUi() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setGravity(Gravity.CENTER_HORIZONTAL);
        col.setPadding(dp(24), dp(24), dp(24), dp(24));
        col.setBackgroundColor(Color.WHITE);

        col.addView(space(), weight());

        TextView title = text("Nevatch", 34, COLOR_TEXT, true);
        col.addView(title);
        serverName = text("", 15, COLOR_MUTED, false);
        col.addView(serverName);

        shield = new ImageView(this);
        shield.setAdjustViewBounds(true);
        LinearLayout.LayoutParams sp = new LinearLayout.LayoutParams(dp(190), dp(190));
        sp.topMargin = dp(36);
        sp.bottomMargin = dp(20);
        col.addView(shield, sp);

        status = text("", 22, COLOR_TEXT, true);
        col.addView(status);
        detail = text("", 14, COLOR_MUTED, false);
        detail.setPadding(dp(8), dp(6), dp(8), 0);
        col.addView(detail);

        mainButton = bigButton();
        LinearLayout.LayoutParams bp = new LinearLayout.LayoutParams(dp(260), dp(62));
        bp.topMargin = dp(36);
        col.addView(mainButton, bp);
        mainButton.setOnClickListener(v -> onMainButton());

        scanButton = bigButton();
        LinearLayout.LayoutParams sb = new LinearLayout.LayoutParams(dp(260), dp(62));
        sb.topMargin = dp(14);
        col.addView(scanButton, sb);
        scanButton.setText("Scan QR code");
        style(scanButton, Color.WHITE, COLOR_BLUE, true);
        scanButton.setOnClickListener(v -> scan());

        col.addView(space(), weight());

        changeKey = text("Change key", 15, COLOR_BLUE, false);
        changeKey.setPadding(dp(16), dp(12), dp(16), dp(4));
        changeKey.setOnClickListener(v -> askChangeKey());
        col.addView(changeKey);

        FrameLayout root = new FrameLayout(this);
        root.addView(col);
        // Android 15+ draws apps under the status and navigation bars; keep the content clear of them.
        root.setOnApplyWindowInsetsListener((v, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(android.view.WindowInsets.Type.systemBars());
                v.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            } else {
                v.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            }
            return insets;
        });
        setContentView(root);
    }

    private void refresh() {
        NevatchKey key = Keys.current(this);
        NevatchVpnService.State st = NevatchVpnService.state();
        String msg = NevatchVpnService.message();

        if (key == null) {
            serverName.setText("");
            setShield(R.drawable.shield_gray, false);
            status.setText("Add your key");
            status.setTextColor(COLOR_TEXT);
            detail.setText("Paste the key you received (it starts with vless://), or scan its QR code.");
            mainButton.setText("Paste key");
            style(mainButton, COLOR_BLUE, Color.WHITE, false);
            scanButton.setVisibility(View.VISIBLE);
            changeKey.setVisibility(View.GONE);
            return;
        }

        serverName.setText(key.displayName());
        scanButton.setVisibility(View.GONE);
        changeKey.setVisibility(View.VISIBLE);
        switch (st) {
            case ON:
                setShield(R.drawable.shield_green, false);
                status.setText("Connected");
                status.setTextColor(COLOR_GREEN);
                detail.setText("Everything on this phone goes through Nevatch.");
                mainButton.setText("Disconnect");
                style(mainButton, COLOR_DARK, Color.WHITE, false);
                break;
            case CONNECTING:
                setShield(R.drawable.shield_blue, true);
                status.setText("Connecting…");
                status.setTextColor(COLOR_BLUE);
                detail.setText(msg.equals("Connecting…") ? "" : msg);
                mainButton.setText("Cancel");
                style(mainButton, COLOR_DARK, Color.WHITE, false);
                break;
            default:
                setShield(R.drawable.shield_gray, false);
                status.setText("Not connected");
                status.setTextColor(COLOR_TEXT);
                detail.setText(msg);
                mainButton.setText("Connect");
                style(mainButton, COLOR_BLUE, Color.WHITE, false);
                break;
        }
    }

    private void setShield(int res, boolean pulse) {
        shield.setImageResource(res);
        if (pulse == pulsing) return;
        pulsing = pulse;
        if (pulse) {
            AlphaAnimation a = new AlphaAnimation(1f, 0.45f);
            a.setDuration(700);
            a.setRepeatMode(Animation.REVERSE);
            a.setRepeatCount(Animation.INFINITE);
            shield.startAnimation(a);
        } else {
            shield.clearAnimation();
        }
    }

    // ---------- actions ----------

    private void onMainButton() {
        if (Keys.current(this) == null) {
            askPaste();
            return;
        }
        if (NevatchVpnService.state() == NevatchVpnService.State.OFF) connect();
        else disconnect();
    }

    private void connect() {
        Intent ask = VpnService.prepare(this);
        if (ask != null) startActivityForResult(ask, REQ_VPN);
        else startVpn();
    }

    private void startVpn() {
        Intent i = new Intent(this, NevatchVpnService.class).setAction(NevatchVpnService.ACTION_CONNECT);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(i);
        else startService(i);
        if (Build.VERSION.SDK_INT >= 33
            && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            // Only for the "Connected" notification with its Disconnect button; the VPN works without it.
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, 2);
        }
    }

    private void disconnect() {
        startService(new Intent(this, NevatchVpnService.class).setAction(NevatchVpnService.ACTION_DISCONNECT));
    }

    private void askChangeKey() {
        new AlertDialog.Builder(this)
            .setTitle("Change key")
            .setItems(new String[] { "Paste key", "Scan QR code" }, (d, which) -> {
                if (which == 0) askPaste();
                else scan();
            })
            .show();
    }

    private void askPaste() {
        EditText box = new EditText(this);
        box.setHint("vless://…");
        box.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        box.setMinLines(3);
        box.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        String clip = clipboardText();
        if (clip != null && clip.trim().regionMatches(true, 0, "vless://", 0, 8)) box.setText(clip.trim());
        FrameLayout wrap = new FrameLayout(this);
        wrap.setPadding(dp(20), dp(8), dp(20), 0);
        wrap.addView(box);
        new AlertDialog.Builder(this)
            .setTitle("Paste the key you received")
            .setView(wrap)
            .setPositiveButton("Add", (d, w) -> importKey(box.getText().toString()))
            .setNegativeButton("Cancel", null)
            .show();
    }

    private void scan() {
        IntentIntegrator scanner = new IntentIntegrator(this);
        scanner.setDesiredBarcodeFormats(IntentIntegrator.QR_CODE);
        scanner.setPrompt("Point the camera at the Nevatch QR code");
        scanner.setBeepEnabled(false);
        scanner.setOrientationLocked(true);
        scanner.initiateScan();
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == REQ_VPN) {
            if (resultCode == RESULT_OK) startVpn();
            return;
        }
        IntentResult scanned = IntentIntegrator.parseActivityResult(requestCode, resultCode, data);
        if (scanned != null) {
            if (scanned.getContents() != null) importKey(scanned.getContents());
            return;
        }
        super.onActivityResult(requestCode, resultCode, data);
    }

    private void handleLink(Intent intent) {
        if (intent == null || !Intent.ACTION_VIEW.equals(intent.getAction()) || intent.getDataString() == null) return;
        String link = intent.getDataString();
        intent.setData(null);
        importKey(link);
    }

    private void importKey(String text) {
        NevatchKey key;
        try {
            key = NevatchKey.parse(text);
        } catch (NevatchKey.BadKey e) {
            new AlertDialog.Builder(this).setTitle("That didn't work").setMessage(e.getMessage())
                .setPositiveButton("OK", null).show();
            return;
        }
        NevatchKey old = Keys.current(this);
        boolean wasOn = NevatchVpnService.state() != NevatchVpnService.State.OFF;
        if (old != null && old.link.equals(key.link)) {
            refresh();
            return;
        }
        Keys.save(this, key.link);
        // The running VPN uses the old key; start over with the new one.
        if (wasOn) disconnect();
        refresh();
    }

    private String clipboardText() {
        try {
            ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            ClipData clip = cm.getPrimaryClip();
            if (clip == null || clip.getItemCount() == 0) return null;
            CharSequence t = clip.getItemAt(0).coerceToText(this);
            return t == null ? null : t.toString();
        } catch (Exception e) {
            return null;
        }
    }

    // ---------- little view helpers ----------

    private TextView text(String s, int sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(color);
        t.setGravity(Gravity.CENTER);
        if (bold) t.setTypeface(Typeface.DEFAULT_BOLD);
        return t;
    }

    private Button bigButton() {
        Button b = new Button(this);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 19);
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setStateListAnimator(null);
        return b;
    }

    private void style(Button b, int fill, int textColor, boolean outlined) {
        GradientDrawable bg = new GradientDrawable();
        bg.setCornerRadius(dp(31));
        bg.setColor(fill);
        if (outlined) bg.setStroke(dp(2), textColor);
        b.setBackground(bg);
        b.setTextColor(textColor);
    }

    private View space() { return new View(this); }

    private static LinearLayout.LayoutParams weight() {
        return new LinearLayout.LayoutParams(1, 0, 1f);
    }

    private int dp(int v) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics()));
    }
}
