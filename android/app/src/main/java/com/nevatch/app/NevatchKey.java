package com.nevatch.app;

import android.net.Uri;

import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

/**
 * A Nevatch key, a VLESS + Reality share link:
 * vless://ID@HOST:PORT?security=reality&sni=...&pbk=...&sid=...&fp=...&flow=...#NAME
 * Parsed the same way as VlessKey in client/Nevatch.cs.
 */
public final class NevatchKey {
    public String id, host, sni, publicKey, shortId, fingerprint, flow, name;
    public int port;
    public String link; // the key as it was given, trimmed

    public static final class BadKey extends Exception {
        BadKey(String message) { super(message); }
    }

    public static NevatchKey parse(String text) throws BadKey {
        String s = text == null ? "" : text.trim();
        if (!s.regionMatches(true, 0, "vless://", 0, 8))
            throw new BadKey("This is not a Nevatch key. It should start with vless://");
        NevatchKey key = new NevatchKey();
        key.link = s;
        s = s.substring(8);

        int hash = s.indexOf('#');
        if (hash >= 0) {
            key.name = Uri.decode(s.substring(hash + 1));
            s = s.substring(0, hash);
        }

        Map<String, String> query = new HashMap<>();
        int q = s.indexOf('?');
        if (q >= 0) {
            for (String pair : s.substring(q + 1).split("&")) {
                int eq = pair.indexOf('=');
                if (eq > 0) query.put(pair.substring(0, eq).toLowerCase(Locale.ROOT), Uri.decode(pair.substring(eq + 1)));
            }
            s = s.substring(0, q);
        }
        while (s.endsWith("/")) s = s.substring(0, s.length() - 1);

        int at = s.lastIndexOf('@');
        if (at <= 0) throw incomplete();
        key.id = Uri.decode(s.substring(0, at));
        String hostPort = s.substring(at + 1);
        String portText;
        if (hostPort.startsWith("[")) {
            int close = hostPort.indexOf(']');
            if (close < 0) throw incomplete();
            key.host = hostPort.substring(1, close);
            portText = hostPort.substring(close + 1);
            if (portText.startsWith(":")) portText = portText.substring(1);
        } else {
            int colon = hostPort.lastIndexOf(':');
            if (colon < 0) throw incomplete();
            key.host = hostPort.substring(0, colon);
            portText = hostPort.substring(colon + 1);
        }
        try {
            key.port = Integer.parseInt(portText);
        } catch (NumberFormatException e) {
            throw incomplete();
        }
        if (key.port < 1 || key.port > 65535) throw incomplete();

        if (!"reality".equalsIgnoreCase(get(query, "security")))
            throw new BadKey("This key does not use Reality, which Nevatch needs.");
        key.sni = get(query, "sni");
        key.publicKey = get(query, "pbk");
        key.shortId = orEmpty(get(query, "sid"));
        key.fingerprint = get(query, "fp") != null ? get(query, "fp") : "chrome";
        key.flow = orEmpty(get(query, "flow"));
        if (key.host.isEmpty() || key.id.length() < 8 || key.sni == null || key.publicKey == null) throw incomplete();
        return key;
    }

    /** The name to show: the key's #NAME, or the server address. */
    public String displayName() {
        return name != null && !name.trim().isEmpty() ? name.trim() : host;
    }

    private static BadKey incomplete() {
        return new BadKey("The key is incomplete. Copy the whole key again.");
    }

    private static String get(Map<String, String> query, String name) {
        String v = query.get(name);
        return v != null && !v.isEmpty() ? v : null;
    }

    private static String orEmpty(String s) { return s == null ? "" : s; }

    // ---------- Xray config ----------

    // Russian sites go direct: many of them (banks, Gosuslugi) refuse visitors from abroad. Same list as on Windows.
    private static final String[] DIRECT_DOMAINS = {
        "domain:ru", "domain:su", "domain:xn--p1ai", "domain:xn--80adxhks",
        "domain:vk.com", "domain:vk.me", "domain:userapi.com", "domain:vkuser.net",
        "domain:yandex.com", "domain:yandex.net", "domain:yastatic.net", "domain:mycdn.me",
    };
    private static final String[] LOCAL_NETS = {
        "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.168.0.0/16", "::1/128", "fc00::/7", "fe80::/10",
        "224.0.0.0/4", "255.255.255.255/32", "ff00::/8",
    };
    /** The DNS servers the phone is told to use; Xray answers those lookups itself, resolving through the VPN. */
    public static final String[] TUN_DNS = { "1.1.1.1", "8.8.8.8" };

    /**
     * The whole phone goes through the VPN (the tun inbound, fed by Android's VpnService), like "whole PC" on Windows.
     * socksPort: a local SOCKS inbound the app uses for its presence pings (update.nevatch, answered by the server).
     */
    public String xrayConfig(int socksPort) {
        String sniff = "{\"enabled\": true, \"destOverride\": [\"http\", \"tls\", \"quic\"], \"routeOnly\": true}";
        StringBuilder sb = new StringBuilder();
        sb.append("{\n");
        // No access log: it would list every site visited.
        sb.append("  \"log\": {\"loglevel\": \"warning\", \"access\": \"none\"},\n");
        sb.append("  \"inbounds\": [\n");
        sb.append("    {\"tag\": \"tun\", \"protocol\": \"tun\", \"settings\": {\"name\": \"nevatch0\", \"mtu\": 1500}, \"sniffing\": ")
          .append(sniff).append("},\n");
        sb.append("    {\"tag\": \"socks\", \"listen\": \"127.0.0.1\", \"port\": ").append(socksPort)
          .append(", \"protocol\": \"socks\", \"settings\": {\"udp\": false}}\n");
        sb.append("  ],\n");
        sb.append("  \"outbounds\": [\n");
        sb.append("    {\"tag\": \"proxy\", \"protocol\": \"vless\",\n");
        sb.append("     \"settings\": {\"vnext\": [{\"address\": ").append(j(host)).append(", \"port\": ").append(port)
          .append(", \"users\": [{\"id\": ").append(j(id)).append(", \"encryption\": \"none\", \"flow\": ").append(j(flow)).append("}]}]},\n");
        sb.append("     \"streamSettings\": {\"network\": \"tcp\", \"security\": \"reality\", \"realitySettings\": {")
          .append("\"serverName\": ").append(j(sni)).append(", \"fingerprint\": ").append(j(fingerprint))
          .append(", \"publicKey\": ").append(j(publicKey)).append(", \"shortId\": ").append(j(shortId))
          .append(", \"spiderX\": \"/\"}}},\n");
        sb.append("    {\"tag\": \"direct\", \"protocol\": \"freedom\"},\n");
        // A (IPv4) lookups go to the built-in resolver below; AAAA (IPv6) get an empty answer;
        // anything else is passed on through the VPN.
        sb.append("    {\"tag\": \"dns-out\", \"protocol\": \"dns\", \"settings\": {\"rules\": [")
          .append("{\"action\": \"return\", \"qType\": 28, \"rCode\": 0}, ")
          .append("{\"action\": \"direct\", \"qType\": \"2-27,29-65535\"}]}, ")
          .append("\"streamSettings\": {\"sockopt\": {\"dialerProxy\": \"proxy\"}}},\n");
        sb.append("    {\"tag\": \"block\", \"protocol\": \"blackhole\"}\n");
        sb.append("  ],\n");
        sb.append("  \"dns\": {\"servers\": ").append(jList(TUN_DNS)).append(", \"queryStrategy\": \"UseIPv4\", \"tag\": \"dns-in\"},\n");
        sb.append("  \"routing\": {\"domainStrategy\": \"AsIs\", \"rules\": [\n");
        sb.append("    {\"inboundTag\": [\"tun\"], \"port\": \"53\", \"outboundTag\": \"dns-out\"},\n");
        sb.append("    {\"inboundTag\": [\"dns-in\"], \"outboundTag\": \"proxy\"},\n");
        sb.append("    {\"ip\": ").append(jList(LOCAL_NETS)).append(", \"outboundTag\": \"direct\"},\n");
        sb.append("    {\"domain\": ").append(jList(DIRECT_DOMAINS)).append(", \"outboundTag\": \"direct\"},\n");
        // Apps only get IPv4 addresses, but one could still use an IPv6 address it knows; refuse that
        // (the server may have no IPv6, and IPv6 must not leak around the VPN).
        sb.append("    {\"inboundTag\": [\"tun\"], \"ip\": [\"::/0\"], \"outboundTag\": \"block\"}\n");
        sb.append("  ]}\n");
        sb.append("}\n");
        return sb.toString();
    }

    private static String j(String s) {
        StringBuilder sb = new StringBuilder("\"");
        for (char c : (s == null ? "" : s).toCharArray()) {
            if (c == '"' || c == '\\') sb.append('\\').append(c);
            else if (c < 0x20) sb.append(String.format(Locale.ROOT, "\\u%04x", (int) c));
            else sb.append(c);
        }
        return sb.append('"').toString();
    }

    private static String jList(String[] items) {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < items.length; i++) {
            if (i > 0) sb.append(", ");
            sb.append(j(items[i]));
        }
        return sb.append(']').toString();
    }
}
