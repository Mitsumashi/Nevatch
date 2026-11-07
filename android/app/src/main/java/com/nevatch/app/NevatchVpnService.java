package com.nevatch.app;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.VpnService;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.util.Log;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import libv2ray.CoreCallbackHandler;
import libv2ray.CoreController;
import libv2ray.Libv2ray;

/**
 * The VPN: Android hands every app's traffic to a tun device, and Xray (libv2ray) reads it from there and sends it
 * to the Nevatch server. Nevatch itself is left out of the VPN so Xray's own connection to the server goes direct.
 */
public class NevatchVpnService extends VpnService {
    static final String TAG = "Nevatch";
    static final String ACTION_CONNECT = "com.nevatch.app.CONNECT";
    static final String ACTION_DISCONNECT = "com.nevatch.app.DISCONNECT";

    public enum State { OFF, CONNECTING, ON }

    // The app and the service run in one process, so the screen reads this directly.
    private static volatile State state = State.OFF;
    private static volatile String message = "";
    private static final CopyOnWriteArrayList<Runnable> listeners = new CopyOnWriteArrayList<>();
    private static final Handler main = new Handler(Looper.getMainLooper());

    public static State state() { return state; }
    public static String message() { return message; }
    public static void listen(Runnable r) { listeners.add(r); }
    public static void unlisten(Runnable r) { listeners.remove(r); }

    private static void setState(State s, String msg) {
        state = s;
        message = msg == null ? "" : msg;
        main.post(() -> { for (Runnable r : listeners) r.run(); });
    }

    // The server answers http://update.nevatch/ping?id=... itself, through the VPN (server/server.js).
    // Nevatch pings once a second so the server's console knows who is online, and so the app knows the VPN works.
    private static final String PING_HOST = "update.nevatch";
    private static final int NOTIFICATION_ID = 1;
    private static final String CHANNEL_ID = "vpn";
    private static final long LOST_AFTER_MS = 10_000;

    private final Object lock = new Object();
    private ParcelFileDescriptor tun;
    private CoreController core;
    private Thread worker;
    private volatile boolean running;

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent != null ? intent.getAction() : null;
        if (ACTION_DISCONNECT.equals(action)) {
            disconnect();
            return START_NOT_STICKY;
        }
        // Started by the app (ACTION_CONNECT) or by Android's always-on VPN (android.net.VpnService).
        startInForeground("Connecting…");
        connect();
        return START_NOT_STICKY;
    }

    @Override
    public void onRevoke() {
        // Another VPN took over, or the user turned Nevatch off in Android's settings.
        disconnect();
    }

    @Override
    public void onDestroy() {
        stopEverything();
        super.onDestroy();
    }

    private void connect() {
        synchronized (lock) {
            if (running) return;
            running = true;
        }
        setState(State.CONNECTING, "Connecting…");
        worker = new Thread(this::run, "nevatch-vpn");
        worker.start();
    }

    private void disconnect() {
        stopEverything();
        stopForeground(true);
        stopSelf();
    }

    private void stopEverything() {
        synchronized (lock) {
            running = false;
            if (worker != null) worker.interrupt();
            worker = null;
            if (core != null) {
                try { core.stopLoop(); } catch (Exception e) { Log.w(TAG, "stopLoop", e); }
                core = null;
            }
            if (tun != null) {
                try { tun.close(); } catch (IOException ignored) { }
                tun = null;
            }
        }
        if (state != State.OFF) setState(State.OFF, "");
    }

    private void run() {
        NevatchKey key;
        try {
            key = NevatchKey.parse(Keys.load(this));
        } catch (NevatchKey.BadKey e) {
            fail("Add your key first.");
            return;
        }
        int socksPort;
        try {
            socksPort = freePort();
            Builder b = new Builder()
                .setSession("Nevatch")
                .setMtu(1500)
                .addAddress("172.19.77.2", 30)
                .addRoute("0.0.0.0", 0)
                // IPv6 goes into the VPN too, where Xray refuses it, so nothing leaks around the VPN.
                .addAddress("fdfe:dcba:9876::2", 126)
                .addRoute("::", 0);
            for (String dns : NevatchKey.TUN_DNS) b.addDnsServer(dns);
            try {
                b.addDisallowedApplication(getPackageName());
            } catch (Exception ignored) { }
            if (Build.VERSION.SDK_INT >= 29) b.setMetered(false);
            ParcelFileDescriptor fd = b.establish();
            if (fd == null) {
                fail("Android didn't allow the VPN. Open Nevatch and tap Connect.");
                return;
            }
            synchronized (lock) {
                if (!running) { fd.close(); return; }
                tun = fd;
                Libv2ray.initCoreEnv(getFilesDir().getAbsolutePath(), "");
                core = Libv2ray.newCoreController(new Callbacks());
                core.startLoop(key.xrayConfig(socksPort), fd.getFd());
            }
        } catch (Exception e) {
            Log.e(TAG, "start", e);
            fail("Couldn't start: " + e.getMessage());
            return;
        }
        Log.i(TAG, "Xray started for " + key.host + ":" + key.port);
        pingLoop("id=" + android.net.Uri.encode(key.id) + Keys.deviceQuery(this), socksPort);
    }

    private void fail(String why) {
        Log.w(TAG, why);
        stopEverything();
        setState(State.OFF, why);
        main.post(() -> { stopForeground(true); stopSelf(); });
    }

    // ---------- presence pings ----------

    private void pingLoop(String query, int socksPort) {
        Log.i(TAG, "presence ping: /ping?" + query.replaceFirst("^id=[^&]*", "id=…"));
        Socket s = null;
        long lastAnswer = 0, started = SystemClock.elapsedRealtime();
        while (running) {
            long t0 = SystemClock.elapsedRealtime();
            try {
                if (s == null) s = openTunnel(socksPort);
                if (!ping(s, query)) { closeQuietly(s); s = null; }
                lastAnswer = SystemClock.elapsedRealtime();
                if (state != State.ON && running) {
                    setState(State.ON, "");
                    updateNotification("Connected");
                }
            } catch (IOException e) {
                closeQuietly(s);
                s = null;
                long since = SystemClock.elapsedRealtime() - (lastAnswer == 0 ? started : lastAnswer);
                if (running && since > LOST_AFTER_MS) {
                    String msg = lastAnswer == 0 ? "Can't reach the server yet. Still trying…" : "Lost the connection. Reconnecting…";
                    if (state != State.CONNECTING || !msg.equals(message)) {
                        setState(State.CONNECTING, msg);
                        updateNotification(msg);
                    }
                }
            }
            long wait = 1000 - (SystemClock.elapsedRealtime() - t0);
            if (wait > 0) {
                try { Thread.sleep(wait); } catch (InterruptedException e) { break; }
            }
        }
        closeQuietly(s);
    }

    /** A SOCKS5 connection through Xray to update.nevatch:80, which the server answers itself. */
    private static Socket openTunnel(int socksPort) throws IOException {
        Socket s = new Socket();
        try {
            s.setTcpNoDelay(true);
            s.setSoTimeout(5000);
            s.connect(new InetSocketAddress("127.0.0.1", socksPort), 2000);
            OutputStream out = s.getOutputStream();
            InputStream in = s.getInputStream();
            out.write(new byte[] { 5, 1, 0 });
            byte[] hello = readExactly(in, 2);
            if (hello[0] != 5 || hello[1] != 0) throw new IOException("SOCKS refused");
            byte[] host = PING_HOST.getBytes(StandardCharsets.US_ASCII);
            byte[] connect = new byte[7 + host.length];
            connect[0] = 5; connect[1] = 1; connect[2] = 0; connect[3] = 3; connect[4] = (byte) host.length;
            System.arraycopy(host, 0, connect, 5, host.length);
            connect[5 + host.length] = 0;
            connect[6 + host.length] = 80;
            out.write(connect);
            byte[] reply = readExactly(in, 4);
            if (reply[1] != 0) throw new IOException("SOCKS connect failed");
            int rest = reply[3] == 1 ? 4 : reply[3] == 4 ? 16 : (readExactly(in, 1)[0] & 0xff);
            readExactly(in, rest + 2);
            return s;
        } catch (IOException e) {
            closeQuietly(s);
            throw e;
        }
    }

    private static final Pattern CONTENT_LENGTH = Pattern.compile("Content-Length:\\s*(\\d+)", Pattern.CASE_INSENSITIVE);

    /** One ping over the kept-open connection. Any answer means the server is reachable; false = close the connection. */
    private static boolean ping(Socket s, String query) throws IOException {
        String req = "GET /ping?" + query + " HTTP/1.1\r\nHost: " + PING_HOST + "\r\n\r\n";
        s.getOutputStream().write(req.getBytes(StandardCharsets.US_ASCII));
        InputStream in = s.getInputStream();
        StringBuilder head = new StringBuilder();
        while (head.length() < 4 || !head.substring(head.length() - 4).equals("\r\n\r\n")) {
            int c = in.read();
            if (c < 0) throw new IOException("closed");
            head.append((char) c);
            if (head.length() > 4096) throw new IOException("bad answer");
        }
        if (!head.toString().startsWith("HTTP/1.1 ")) throw new IOException("bad answer");
        Matcher m = CONTENT_LENGTH.matcher(head);
        if (!head.toString().startsWith("HTTP/1.1 200") || !m.find()) return false;
        readExactly(in, Integer.parseInt(m.group(1)));
        return true;
    }

    private static byte[] readExactly(InputStream in, int n) throws IOException {
        byte[] buf = new byte[n];
        for (int got = 0; got < n; ) {
            int r = in.read(buf, got, n - got);
            if (r < 0) throw new IOException("closed");
            got += r;
        }
        return buf;
    }

    private static void closeQuietly(Socket s) {
        if (s != null) try { s.close(); } catch (IOException ignored) { }
    }

    private static int freePort() throws IOException {
        try (ServerSocket ss = new ServerSocket(0)) { return ss.getLocalPort(); }
    }

    // ---------- notification ----------

    private Notification notification(String text) {
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            if (nm.getNotificationChannel(CHANNEL_ID) == null) {
                NotificationChannel ch = new NotificationChannel(CHANNEL_ID, "VPN", NotificationManager.IMPORTANCE_LOW);
                ch.setShowBadge(false);
                nm.createNotificationChannel(ch);
            }
            b = new Notification.Builder(this, CHANNEL_ID);
        } else {
            b = new Notification.Builder(this);
        }
        int pi = PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE;
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), pi);
        PendingIntent stop = PendingIntent.getService(this, 1,
            new Intent(this, NevatchVpnService.class).setAction(ACTION_DISCONNECT), pi);
        return b.setSmallIcon(R.drawable.ic_notification)
            .setContentTitle("Nevatch")
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .addAction(new Notification.Action.Builder(null, "Disconnect", stop).build())
            .build();
    }

    private void startInForeground(String text) {
        Notification n = notification(text);
        if (Build.VERSION.SDK_INT >= 34)
            startForeground(NOTIFICATION_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        else
            startForeground(NOTIFICATION_ID, n);
    }

    private void updateNotification(String text) {
        if (!running) return;
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        nm.notify(NOTIFICATION_ID, notification(text));
    }

    // Xray's callbacks; nothing to do with them.
    private static final class Callbacks implements CoreCallbackHandler {
        @Override public long onEmitStatus(long code, String s) { Log.i(TAG, "xray: " + s); return 0; }
        @Override public long shutdown() { return 0; }
        @Override public long startup() { return 0; }
    }
}
