'use strict';
// Everything Xray-specific: keys, the server config, share links and traffic stats.
const crypto = require('crypto');

const API_PORT = Number(process.env.FVPN_API_PORT) || 10085;
const API_ADDR = `127.0.0.1:${API_PORT}`;
// Nevatch asks for updates at http://update.nevatch/ through the tunnel; the server hands those requests
// (and only those) to the update service on this port. Nothing extra is opened to the internet.
const UPDATE_HOST = 'update.nevatch';
const UPDATE_PORT = Number(process.env.FVPN_UPDATE_PORT) || 10086;

// Addresses on your own network. VPN users must never reach these through your server.
const LOCAL_NETS = [
  '0.0.0.0/8', '10.0.0.0/8', '100.64.0.0/10', '127.0.0.0/8', '169.254.0.0/16',
  '172.16.0.0/12', '192.168.0.0/16', '224.0.0.0/4', '240.0.0.0/4',
  '::1/128', 'fc00::/7', 'fe80::/10',
];

// Reality uses an X25519 key pair; Xray expects both halves as unpadded base64url,
// which is exactly how Node exports them as JWK.
function newRealityKeys() {
  const { privateKey, publicKey } = crypto.generateKeyPairSync('x25519');
  return {
    privateKey: privateKey.export({ format: 'jwk' }).d,
    publicKey: publicKey.export({ format: 'jwk' }).x,
    shortId: crypto.randomBytes(8).toString('hex'),
  };
}

// listen/port: where Xray itself listens; with a speed limit that's 127.0.0.1 behind the relay (lib/limiter.js).
function serverConfig(state, { listen, port }) {
  const { settings, reality, users } = state;
  return {
    // access: 'none', or Xray prints every site each person visits (it's on by default, even at 'warning').
    log: { loglevel: 'warning', access: 'none' },
    api: { tag: 'api', listen: API_ADDR, services: ['StatsService'] },
    stats: {},
    policy: { levels: { 0: { statsUserUplink: true, statsUserDownlink: true } } },
    inbounds: [{
      tag: 'vless-in',
      ...(listen ? { listen } : {}),
      port,
      protocol: 'vless',
      settings: {
        decryption: 'none',
        clients: users.filter(u => u.enabled).map(u => ({ id: u.id, email: u.id, flow: 'xtls-rprx-vision', level: 0 })),
      },
      streamSettings: {
        network: 'tcp',
        security: 'reality',
        realitySettings: {
          target: `${settings.sni}:443`,
          serverNames: [settings.sni],
          privateKey: reality.privateKey,
          shortIds: [reality.shortId],
        },
      },
      sniffing: { enabled: true, destOverride: ['http', 'tls', 'quic'], routeOnly: true },
    }],
    outbounds: [
      { tag: 'direct', protocol: 'freedom' },
      { tag: 'block', protocol: 'blackhole' },
      // Xray refuses local addresses for VPN users by itself; this one exception lets the update requests through.
      {
        tag: 'update', protocol: 'freedom',
        settings: {
          redirect: `127.0.0.1:${UPDATE_PORT}`,
          finalRules: [{ action: 'allow', ip: ['127.0.0.1/32'], port: String(UPDATE_PORT) }],
        },
      },
    ],
    routing: {
      domainStrategy: 'IPIfNonMatch',
      rules: [
        { domain: [`full:${UPDATE_HOST}`], outboundTag: 'update' },
        { ip: LOCAL_NETS, outboundTag: 'block' },
        { protocol: ['bittorrent'], outboundTag: 'block' }, // torrents would be traced to your home IP
        { port: '25', outboundTag: 'block' }, // no outgoing mail spam from your IP
      ],
    },
  };
}

function shareLink(state, user) {
  const { settings, reality } = state;
  const host = settings.publicHost.includes(':') ? `[${settings.publicHost}]` : settings.publicHost;
  const query = new URLSearchParams({
    encryption: 'none', flow: 'xtls-rprx-vision', security: 'reality', sni: settings.sni,
    fp: 'chrome', pbk: reality.publicKey, sid: reality.shortId, type: 'tcp', headerType: 'none',
  });
  return `vless://${user.id}@${host}:${settings.port}?${query}#${encodeURIComponent(user.name)}`;
}

// Output of `xray api statsquery`: { "stat": [{ "name": "user>>>ID>>>traffic>>>uplink", "value": "123" }] }
function parseStats(text) {
  const out = {};
  let data;
  try { data = JSON.parse(text); } catch { return out; }
  for (const s of data.stat || []) {
    const m = /^user>>>(.+)>>>traffic>>>(uplink|downlink)$/.exec(s.name || '');
    if (!m) continue;
    const entry = (out[m[1]] ||= { up: 0, down: 0 });
    entry[m[2] === 'uplink' ? 'up' : 'down'] += Number(s.value || 0);
  }
  return out;
}

module.exports = { API_PORT, API_ADDR, UPDATE_HOST, UPDATE_PORT, newRealityKeys, serverConfig, shareLink, parseStats };
