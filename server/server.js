'use strict';
// Nevatch server: runs Xray (VLESS + Reality) and a small web console for managing who can connect.
//
//   node server.js                     start the server and console
//   node server.js set-password [pw]   change the console password
//
// Optional environment variables:
//   CONSOLE_PORT  console port (default 8088). Never forward this port on your router.
//   CONSOLE_HOST  console address (default 0.0.0.0 = reachable from your home network;
//                 use 127.0.0.1 to allow this PC only)
//   FVPN_DATA     where keys and people are stored (default ./data)
//   XRAY_LISTEN   address Xray (or the speed-limit relay in front of it) listens on (default: all)
//   FVPN_API_PORT, FVPN_UPDATE_PORT, FVPN_RELAY_PORT   internal ports on 127.0.0.1 (default 10085, 10086,
//                 10443); only needed to run a second copy next to the first
//   VPN_PORT      fixes the port friends connect to (and that goes into their keys); the console can't change
//                 it then. For Docker, where it has to match the published port.
//   PUBLIC_HOST   your public IP or DNS name, used when none is saved in the console yet
//   ADMIN_PASSWORD  console password, used when none is set yet (otherwise one is made and printed)
//   FVPN_CONSOLE_OPEN=1  no console password at all; the console then only answers this PC (Nevatch Server's choice)
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const os = require('os');
const readline = require('readline');
const { spawn, execFile } = require('child_process');
const xray = require('./lib/xray');
const limiter = require('./lib/limiter');
const lagMonitor = require('./lib/lag');

const ROOT = __dirname;
const DATA = path.resolve(process.env.FVPN_DATA || path.join(ROOT, 'data'));
const STATE_FILE = path.join(DATA, 'state.json');
const XRAY_CONFIG = path.join(DATA, 'xray-server.json');
const XRAY_BIN = path.join(ROOT, 'bin', process.platform === 'win32' ? 'xray.exe' : 'xray');
// The friend's program (Xray built in); each download gets that person's key appended to it.
const CLIENT_EXE = path.join(ROOT, '..', 'client', 'dist', 'Nevatch.exe');
const CONSOLE_HOST = process.env.CONSOLE_HOST || '0.0.0.0';
const CONSOLE_PORT = Number(process.env.CONSOLE_PORT) || 8088;
const FIXED_PORT = Number(process.env.VPN_PORT) || 0;
if (process.env.VPN_PORT && !(Number.isInteger(FIXED_PORT) && FIXED_PORT >= 1 && FIXED_PORT <= 65535)) {
  console.error(`VPN_PORT must be a number from 1 to 65535, not "${process.env.VPN_PORT}".`);
  process.exit(1);
}
// The release version, kept in ../VERSION (or next to server.js in the Docker image).
const VERSION = (() => {
  for (const file of [path.join(ROOT, '..', 'VERSION'), path.join(ROOT, 'VERSION')]) {
    try { return fs.readFileSync(file, 'utf8').trim(); } catch {}
  }
  return '';
})();
const SESSION_MS = 12 * 60 * 60 * 1000;
const ACTIVE_MS = 60 * 1000;
const STATS_EVERY_MS = 10 * 1000;
const PING_GONE_MS = 15 * 1000; // Nevatch pings once a second while connected; this long without one means it disconnected
const PINGER_MS = 5 * 60 * 1000; // someone whose Nevatch pinged this recently is judged by its pings alone

// ---------- state (data/state.json) ----------

let state = loadState();

function loadState() {
  let s = {};
  try {
    s = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
  } catch (e) {
    // A damaged file must not silently become new keys: every friend's key would stop working.
    if (e.code !== 'ENOENT') throw new Error(`Cannot read ${STATE_FILE}: ${e.message}`);
  }
  // Xray warns that www.microsoft.com and www.apple.com are overused as Reality targets.
  // downLimit / upLimit: Mbit/s per person, 0 = no limit.
  s.settings = { publicHost: '', port: 443, sni: 'www.samsung.com', downLimit: 0, upLimit: 0, ...s.settings };
  // Older versions had one speedLimit for both directions.
  if (s.settings.speedLimit !== undefined) {
    s.settings.downLimit = s.settings.upLimit = s.settings.speedLimit;
    delete s.settings.speedLimit;
  }
  if (FIXED_PORT) s.settings.port = FIXED_PORT;
  if (!s.settings.publicHost && process.env.PUBLIC_HOST) s.settings.publicHost = process.env.PUBLIC_HOST.trim();
  s.reality ||= xray.newRealityKeys();
  s.users ||= [];
  return s;
}

function saveState() {
  fs.mkdirSync(DATA, { recursive: true });
  fs.writeFileSync(STATE_FILE + '.tmp', JSON.stringify(state, null, 2));
  fs.renameSync(STATE_FILE + '.tmp', STATE_FILE);
}

function hashPassword(pw) {
  const salt = crypto.randomBytes(16);
  return `scrypt$${salt.toString('hex')}$${crypto.scryptSync(pw, salt, 32).toString('hex')}`;
}

function checkPassword(pw, stored) {
  const [, salt, hash] = String(stored).split('$');
  if (!salt || !hash) return false;
  const actual = crypto.scryptSync(String(pw), Buffer.from(salt, 'hex'), 32);
  return crypto.timingSafeEqual(actual, Buffer.from(hash, 'hex'));
}

// ---------- logs ----------

const logs = [];
function log(line) {
  const entry = `${new Date().toLocaleTimeString()}  ${line}`;
  logs.push(entry);
  if (logs.length > 300) logs.shift();
  console.log(entry);
}

const lag = lagMonitor.createLagMonitor(log);

// ---------- Xray process ----------

const proc = { child: null, startedAt: null, stopping: false, failures: 0, timer: null, version: '' };
// Mbit/s -> bytes per millisecond (1 Mbit/s = 125 bytes/ms).
const relay = limiter.createRelay(() => state.settings.downLimit * 125, () => state.settings.upLimit * 125);
const isLimited = s => s.downLimit > 0 || s.upLimit > 0;
const mbit = n => (n > 0 ? `${n} Mbit/s` : 'no limit');
const describeLimit = s => `download ${mbit(s.downLimit)}, upload ${mbit(s.upLimit)} per person`;

async function startXray() {
  if (!fs.existsSync(XRAY_BIN)) {
    log(`Xray is not installed (${path.relative(ROOT, XRAY_BIN)}). Run "node setup.js" first.`);
    return;
  }
  // With a speed limit, the relay takes the public port and Xray sits behind it on 127.0.0.1.
  const limited = isLimited(state.settings);
  if (limited && !relay.listening) {
    try {
      await relay.listen(state.settings.port, process.env.XRAY_LISTEN);
    } catch (err) {
      log(`Speed limit: could not listen on port ${state.settings.port}: ${err.message}`);
    }
  } else if (!limited && relay.listening) {
    await relay.close();
  }
  fs.mkdirSync(DATA, { recursive: true });
  const listen = limited
    ? { listen: '127.0.0.1', port: limiter.INTERNAL_PORT }
    : { listen: process.env.XRAY_LISTEN, port: state.settings.port };
  fs.writeFileSync(XRAY_CONFIG, JSON.stringify(xray.serverConfig(state, listen), null, 2));

  const child = spawn(XRAY_BIN, ['run', '-c', XRAY_CONFIG], { cwd: path.dirname(XRAY_BIN), windowsHide: true });
  proc.child = child;
  proc.startedAt = Date.now();
  // Behind the speed-limit relay Xray listens on an internal port and warns about not being on 443;
  // the public port is still 443, so that warning is left out.
  const forward = data => String(data).split(/\r?\n/).filter(Boolean)
    .filter(l => !(limited && /Listening on non-443 ports/.test(l)))
    .forEach(l => log(`xray: ${l}`));
  child.stdout.on('data', forward);
  child.stderr.on('data', forward);
  child.on('error', err => log(`Could not start Xray: ${err.message}`));
  child.on('exit', code => {
    if (proc.child !== child) return;
    proc.child = null;
    if (proc.stopping) return;
    proc.failures = Date.now() - proc.startedAt > 60000 ? 1 : proc.failures + 1;
    const delay = Math.min(30000, 1000 * 2 ** proc.failures);
    log(`Xray stopped (exit code ${code}). Restarting in ${delay / 1000}s…`);
    proc.timer = setTimeout(() => startXray().catch(err => log(`Could not start Xray: ${err.message}`)), delay);
  });
  const active = state.users.filter(u => u.enabled).length;
  log(`Xray started on port ${state.settings.port} with ${active} active ${active === 1 ? 'person' : 'people'}` +
    (limited ? `, speed limit: ${describeLimit(state.settings)}.` : '.'));
}

async function stopXray() {
  clearTimeout(proc.timer);
  const child = proc.child;
  if (!child) return;
  proc.stopping = true;
  await new Promise(resolve => { child.once('exit', resolve); child.kill(); });
  proc.stopping = false;
}

// Config changes need a restart; queue them so two quick clicks can't overlap.
let restartQueue = Promise.resolve();
function restartXray() {
  restartQueue = restartQueue.then(async () => {
    await pollStats();
    await stopXray();
    await relay.close(); // reopened by startXray if a limit is set, possibly on a new port
    proc.failures = 0;
    await startXray();
  }).catch(err => log(`Restart failed: ${err.message}`));
  return restartQueue;
}

function runXray(args) {
  return new Promise(resolve => {
    execFile(XRAY_BIN, args, { timeout: 8000, windowsHide: true }, (err, stdout) => resolve({ err, stdout }));
  });
}

async function readXrayVersion() {
  if (!fs.existsSync(XRAY_BIN)) return;
  const { stdout } = await runXray(['version']);
  proc.version = /Xray [\d.]+/.exec(stdout || '')?.[0] || '';
}

// Xray counts bytes per person; we read and reset those counters and keep running totals.
async function pollStats() {
  if (!proc.child) return;
  const { err, stdout } = await runXray(['api', 'statsquery', `--server=${xray.API_ADDR}`, '-pattern', 'user>>>', '-reset']);
  if (err) return;
  const stats = xray.parseStats(stdout);
  let changed = false;
  for (const u of state.users) {
    const s = stats[u.id];
    if (!s || (!s.up && !s.down)) continue;
    u.up += s.up;
    u.down += s.down;
    u.lastActive = new Date().toISOString();
    changed = true;
  }
  if (changed) saveState();
}

// ---------- who's connected ----------
// Nevatch (1.6 and newer) pings this server through the VPN once a second while it's connected (see /ping below),
// so when the pings stop, that person has disconnected. Phone apps and older versions don't ping; for them,
// traffic in the last minute counts as connected.

const lastPing = new Map(); // id -> time of their last ping
const connectedSince = new Map(); // id -> since when they're connected
let connectedLine = '';

function isConnected(user, now) {
  if (!user.enabled) return false;
  const pinged = lastPing.get(user.id);
  if (pinged && now - pinged < PINGER_MS) return now - pinged < PING_GONE_MS;
  return !!user.lastActive && now - Date.parse(user.lastActive) < ACTIVE_MS;
}

// Logs who comes and goes. Nevatch Server.exe reads the "@connected" line to show who's connected right now.
function checkConnected() {
  const now = Date.now();
  for (const u of state.users) {
    const on = isConnected(u, now);
    if (on && !connectedSince.has(u.id)) {
      connectedSince.set(u.id, now);
      log(`${u.name} connected.`);
    } else if (!on && connectedSince.has(u.id)) {
      connectedSince.delete(u.id);
      log(`${u.name} disconnected.`);
    }
  }
  for (const id of connectedSince.keys()) {
    if (!state.users.some(u => u.id === id)) connectedSince.delete(id); // removed while connected
  }
  const line = state.users.filter(u => connectedSince.has(u.id)).map(u => u.name).join('\t');
  if (line !== connectedLine && process.env.FVPN_LAUNCHER) console.log(`@connected ${line}`);
  connectedLine = line;
}

// ---------- friend's update package ----------

// Nevatch.exe followed by [key as UTF-8][key length: 4 bytes little-endian]["FVPNKEY1"].
// Windows ignores data after the end of a program, and Nevatch reads its key from there.
function buildPackage(user) {
  if (!fs.existsSync(CLIENT_EXE)) throw new Error('client/dist/Nevatch.exe is missing. Run "node setup.js" first.');
  const key = Buffer.from(xray.shareLink(state, user), 'utf8');
  const length = Buffer.alloc(4);
  length.writeUInt32LE(key.length);
  return Buffer.concat([fs.readFileSync(CLIENT_EXE), key, length, Buffer.from('FVPNKEY1', 'ascii')]);
}

// ---------- updates ----------
// Nevatch asks http://update.nevatch/check through the tunnel (see lib/xray.js) and compares the answer with
// its own SHA-256 (without the key at its end). If they differ it fetches /download and replaces itself.
// So rebuilding client/dist/Nevatch.exe (client\build.cmd) updates everyone.

let clientHash = { mtime: 0, size: 0, sha256: '' };
function currentClientHash() {
  const st = fs.statSync(CLIENT_EXE);
  if (st.mtimeMs !== clientHash.mtime || st.size !== clientHash.size) {
    const sha256 = crypto.createHash('sha256').update(fs.readFileSync(CLIENT_EXE)).digest('hex');
    clientHash = { mtime: st.mtimeMs, size: st.size, sha256 };
  }
  return clientHash.sha256;
}

// Only reachable from this PC (Xray passes the tunnel's update requests to it); still asks for a valid key id.
const updates = http.createServer((req, res) => {
  const url = new URL(req.url, `http://${xray.UPDATE_HOST}`);
  const user = state.users.find(u => u.enabled && u.id === url.searchParams.get('id'));
  try {
    // Nevatch's lag meter asks this every second over one kept-open connection, timing the round trip,
    // and sends along what it measured last time (ms through the VPN and without it, -1 = no answer).
    if (user && url.pathname === '/ping') {
      lastPing.set(user.id, Date.now());
      const ms = name => {
        const n = Number(url.searchParams.get(name));
        return url.searchParams.has(name) && Number.isInteger(n) && n >= -1 && n <= 60000 ? n : null;
      };
      const vpn = ms('vpn'), direct = ms('direct');
      if (vpn !== null && direct !== null) lag.report(user, vpn, direct);
      res.writeHead(200, { 'Content-Type': 'text/plain', 'Content-Length': 2, 'Cache-Control': 'no-store' });
      return res.end('ok');
    }
    if (!user || !fs.existsSync(CLIENT_EXE)) throw new BadRequest('not found');
    if (url.pathname === '/check') return json(res, 200, { sha256: currentClientHash() });
    if (url.pathname === '/download') {
      const exe = buildPackage(user);
      log(`Sending the latest Nevatch to ${user.name}.`);
      res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Length': exe.length });
      return res.end(exe);
    }
    throw new BadRequest('not found');
  } catch (err) {
    if (!(err instanceof BadRequest)) log(`Update service: ${err.message}`);
    res.writeHead(err instanceof BadRequest ? 404 : 500);
    res.end();
  }
});

// ---------- web console ----------

const sessions = new Map(); // token -> expiry time
const loginFailures = new Map(); // ip -> { count, last }
// Nevatch Server.exe starts this process with a random token and sends it along, so its own pages need no
// password. Kept out of the environment Xray and the pings inherit.
const LAUNCHER_TOKEN = Buffer.from(process.env.FVPN_LAUNCHER_TOKEN || '', 'utf8');
delete process.env.FVPN_LAUNCHER_TOKEN;

// This PC's addresses on the home network, real adapters first (Hyper-V, WSL, Docker and VPN
// adapters also have private addresses, but the router can't forward to those).
function localIps() {
  const virtual = /vethernet|hyper-v|wsl|docker|virtualbox|vmware|tailscale|zerotier|loopback|^(br|veth|virbr|lxc)/i;
  const rank = ip => (ip.startsWith('192.168.') ? 0 : ip.startsWith('10.') ? 1 : 2);
  return Object.entries(os.networkInterfaces())
    .flatMap(([name, list]) => (list || []).map(i => ({ ...i, virtual: virtual.test(name) })))
    .filter(i => i.family === 'IPv4' && !i.internal && !i.address.startsWith('169.254.'))
    .sort((a, b) => a.virtual - b.virtual || rank(a.address) - rank(b.address))
    .map(i => i.address);
}

function statusPayload() {
  checkConnected();
  return {
    version: VERSION,
    portFixed: !!FIXED_PORT,
    consoleAccess: consoleOpen() ? 'open' : 'password',
    xray: { installed: fs.existsSync(XRAY_BIN), running: !!proc.child, since: proc.child ? proc.startedAt : null, version: proc.version },
    packageReady: fs.existsSync(CLIENT_EXE),
    settings: state.settings,
    localIps: localIps(),
    users: state.users.map(u => ({
      ...u,
      connected: connectedSince.has(u.id),
      connectedSince: connectedSince.has(u.id) ? new Date(connectedSince.get(u.id)).toISOString() : null,
      link: state.settings.publicHost ? xray.shareLink(state, u) : null,
    })),
    logs: logs.slice(-150),
    lag: lag.snapshot(),
  };
}

function cookies(req) {
  const out = {};
  for (const part of (req.headers.cookie || '').split(';')) {
    const i = part.indexOf('=');
    if (i > 0) out[part.slice(0, i).trim()] = part.slice(i + 1).trim();
  }
  return out;
}

function isLoggedIn(req) {
  // Browsers can't add this header for another website without the console agreeing (CORS), which it never does.
  const launcher = Buffer.from(String(req.headers['x-nevatch-token'] || ''), 'utf8');
  if (LAUNCHER_TOKEN.length >= 32 && launcher.length === LAUNCHER_TOKEN.length && crypto.timingSafeEqual(launcher, LAUNCHER_TOKEN)) return true;
  if (consoleOpen() && isLocalRequest(req)) return true;
  const token = cookies(req).fvpn;
  const expires = token && sessions.get(token);
  return !!expires && expires > Date.now();
}

// An open console (no password) only answers this PC. The Host check stops a website that points its own name at
// 127.0.0.1 (DNS rebinding) from using the visitor's browser to reach it.
function consoleOpen() { return state.consoleOpen === true && !state.adminHash; }

function isLocalRequest(req) {
  const ip = String(req.socket.remoteAddress || '');
  if (!(ip === '127.0.0.1' || ip === '::1' || ip === '::ffff:127.0.0.1')) return false;
  const host = String(req.headers.host || '').toLowerCase().replace(/:\d+$/, '');
  return host === 'localhost' || host === '127.0.0.1' || host === '[::1]';
}

// mode "open": no password, this PC only. mode "password": a password, and other devices at home can log in too.
function setConsoleAccess(body) {
  if (body.mode === 'open') {
    delete state.adminHash;
    state.consoleOpen = true;
    sessions.clear();
    log('Web console: open on this PC, no password.');
  } else if (body.mode === 'password') {
    const pw = String(body.password || '');
    if (pw.length < 8) throw new BadRequest('The password must be at least 8 characters.');
    state.adminHash = hashPassword(pw);
    state.consoleOpen = false;
    sessions.clear();
    log('Web console: protected with a password.');
  } else {
    throw new BadRequest('Choose "open" or "password".');
  }
  saveState();
}

function json(res, code, body, headers = {}) {
  res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8', ...headers });
  res.end(JSON.stringify(body));
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
      if (body.length > 64 * 1024) { reject(new Error('Request too large')); req.destroy(); }
    });
    req.on('end', () => {
      try { resolve(body ? JSON.parse(body) : {}); } catch { reject(new Error('Invalid JSON')); }
    });
  });
}

class BadRequest extends Error {}

async function login(req, res) {
  const ip = req.socket.remoteAddress;
  const now = Date.now();
  const prior = loginFailures.get(ip);
  const recent = prior && now - prior.last < 10 * 60 * 1000 ? prior.count : 0;
  if (recent >= 5) return json(res, 429, { error: 'Too many wrong passwords. Try again in 10 minutes.' });
  const { password } = await readJson(req);
  if (consoleOpen()) {
    return json(res, 403, { error: 'The console has no password, so it only opens on the server PC itself. ' +
      'To use it from here, set a password in Nevatch Server → Settings → Web console.' });
  }
  if (!checkPassword(password || '', state.adminHash)) {
    loginFailures.set(ip, { count: recent + 1, last: now });
    await new Promise(r => setTimeout(r, 800));
    return json(res, 401, { error: 'Wrong password' });
  }
  loginFailures.delete(ip);
  const token = crypto.randomBytes(32).toString('hex');
  sessions.set(token, now + SESSION_MS);
  json(res, 200, { ok: true }, {
    'Set-Cookie': `fvpn=${token}; HttpOnly; SameSite=Strict; Path=/; Max-Age=${SESSION_MS / 1000}`,
  });
}

async function updateSettings(body) {
  const next = { ...state.settings };
  if (body.publicHost !== undefined) {
    const host = String(body.publicHost).trim().replace(/^\[|\]$/g, '');
    if (host && !/^[a-z0-9.:-]{1,253}$/i.test(host)) throw new BadRequest('Public address must be an IP address or a host name.');
    next.publicHost = host;
  }
  if (body.port !== undefined) {
    const port = Number(body.port);
    if (!Number.isInteger(port) || port < 1 || port > 65535) throw new BadRequest('Port must be a number from 1 to 65535.');
    if (FIXED_PORT && port !== FIXED_PORT) {
      throw new BadRequest(`The port is fixed to ${FIXED_PORT} by VPN_PORT (Docker). Change it there, and the published port with it.`);
    }
    if ([CONSOLE_PORT, xray.API_PORT, xray.UPDATE_PORT, limiter.INTERNAL_PORT].includes(port)) {
      throw new BadRequest(`Port ${port} is already used by Nevatch itself.`);
    }
    next.port = port;
  }
  for (const [name, what] of [['downLimit', 'Download'], ['upLimit', 'Upload']]) {
    if (body[name] === undefined) continue;
    const limit = Number(body[name] || 0);
    if (!Number.isFinite(limit) || limit < 0 || limit > 10000) throw new BadRequest(`${what} limit must be a number of Mbit/s, or 0 for no limit.`);
    next[name] = Math.round(limit * 10) / 10;
  }
  if (body.sni !== undefined) {
    const sni = String(body.sni).trim().toLowerCase();
    if (!/^[a-z0-9-]+(\.[a-z0-9-]+)+$/.test(sni)) throw new BadRequest('Camouflage site must be a domain like www.samsung.com.');
    next.sni = sni;
  }
  // A different limit applies to open connections at once; only turning it on or off moves Xray.
  const restart = next.port !== state.settings.port || next.sni !== state.settings.sni ||
    isLimited(next) !== isLimited(state.settings);
  if (next.downLimit !== state.settings.downLimit || next.upLimit !== state.settings.upLimit) {
    log(isLimited(next) ? `Speed limit set: ${describeLimit(next)}.` : 'Speed limit turned off.');
  }
  state.settings = next;
  saveState();
  if (restart) await restartXray();
}

function findUser(id) {
  const user = state.users.find(u => u.id === id);
  if (!user) throw new BadRequest('No such person.');
  return user;
}

function cleanName(name) {
  const n = String(name || '').trim().replace(/\s+/g, ' ');
  if (!n || n.length > 40) throw new BadRequest('Name must be 1–40 characters.');
  return n;
}

async function handle(req, res) {
  const { pathname } = new URL(req.url, 'http://console');
  const method = req.method;

  if (method === 'GET' && (pathname === '/' || pathname === '/index.html')) {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    return res.end(fs.readFileSync(path.join(ROOT, 'public', 'index.html')));
  }
  if (!pathname.startsWith('/api/')) return json(res, 404, { error: 'Not found' });

  // JSON-only POSTs plus SameSite cookies keep other websites from driving the console.
  if (method === 'POST' && !String(req.headers['content-type'] || '').startsWith('application/json')) {
    return json(res, 415, { error: 'Send JSON' });
  }
  if (method === 'POST' && pathname === '/api/login') return login(req, res);
  if (!isLoggedIn(req)) return json(res, 401, { error: 'Please log in' });

  if (method === 'POST' && pathname === '/api/logout') {
    sessions.delete(cookies(req).fvpn);
    return json(res, 200, { ok: true }, { 'Set-Cookie': 'fvpn=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0' });
  }
  if (method === 'GET' && pathname === '/api/status') return json(res, 200, statusPayload());

  if (method === 'POST' && pathname === '/api/settings') {
    await updateSettings(await readJson(req));
    return json(res, 200, statusPayload());
  }
  if (method === 'POST' && pathname === '/api/detect-ip') {
    return json(res, 200, { ip: await detectPublicIp() });
  }
  if (method === 'POST' && pathname === '/api/console-access') {
    setConsoleAccess(await readJson(req));
    return json(res, 200, statusPayload());
  }
  if (method === 'POST' && pathname === '/api/restart') {
    await restartXray();
    return json(res, 200, statusPayload());
  }
  if (method === 'POST' && pathname === '/api/users') {
    const { name } = await readJson(req);
    const user = { id: crypto.randomUUID(), name: cleanName(name), enabled: true, created: new Date().toISOString(), up: 0, down: 0, lastActive: null };
    state.users.push(user);
    saveState();
    log(`Added ${user.name}.`);
    await restartXray();
    return json(res, 200, statusPayload());
  }

  const m = /^\/api\/users\/([0-9a-f-]{36})$/.exec(pathname);
  if (m) {
    const user = findUser(m[1]);
    if (method === 'POST') {
      const body = await readJson(req);
      if (body.name !== undefined) user.name = cleanName(body.name);
      const toggled = typeof body.enabled === 'boolean' && body.enabled !== user.enabled;
      if (toggled) user.enabled = body.enabled;
      saveState();
      if (toggled) {
        log(`${user.enabled ? 'Enabled' : 'Disabled'} ${user.name}.`);
        await restartXray();
      }
      return json(res, 200, statusPayload());
    }
    if (method === 'DELETE') {
      state.users = state.users.filter(u => u !== user);
      saveState();
      log(`Removed ${user.name}.`);
      await restartXray();
      return json(res, 200, statusPayload());
    }
  }
  return json(res, 404, { error: 'Not found' });
}

const server = http.createServer((req, res) => {
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'no-referrer');
  res.setHeader('Cache-Control', 'no-store');
  handle(req, res).catch(err => {
    if (!(err instanceof BadRequest)) log(`Console error: ${err.message}`);
    if (!res.headersSent) json(res, err instanceof BadRequest ? 400 : 500, { error: err.message });
  });
});

// ---------- startup ----------

function ask(question) {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  return new Promise(resolve => rl.question(question, answer => { rl.close(); resolve(answer.trim()); }));
}

async function setPasswordCommand(pw) {
  if (!pw) pw = await ask('Choose a password for the web console (at least 8 characters): ');
  if (!pw || pw.length < 8) {
    console.error('The password must be at least 8 characters.');
    process.exit(1);
  }
  state.adminHash = hashPassword(pw);
  state.consoleOpen = false;
  saveState();
  console.log('✓ Console password saved.');
}

async function detectPublicIp() {
  const r = await fetch('https://api.ipify.org', { signal: AbortSignal.timeout(8000) });
  const ip = (await r.text()).trim();
  if (!r.ok || !/^[0-9a-f.:]+$/i.test(ip)) throw new Error('Could not detect the public IP address.');
  return ip;
}

function main() {
  if (VERSION) log(`Nevatch Server ${VERSION}`);
  if (!state.adminHash && process.env.ADMIN_PASSWORD && process.env.ADMIN_PASSWORD.length >= 8) {
    state.adminHash = hashPassword(process.env.ADMIN_PASSWORD);
    state.consoleOpen = false;
    console.log('  Console password set from ADMIN_PASSWORD.');
  } else if (!state.adminHash && process.env.FVPN_CONSOLE_OPEN === '1') {
    // Chosen in Nevatch Server's first-run question: no password, this PC only.
    state.consoleOpen = true;
  } else if (!state.adminHash && !state.consoleOpen) {
    const pw = crypto.randomBytes(9).toString('base64url');
    state.adminHash = hashPassword(pw);
    console.log(`\n  No console password was set, so one was made for you:  ${pw}\n  Change it any time with:  node server.js set-password\n`);
  }
  saveState();
  readXrayVersion().catch(() => {});
  startXray().catch(err => log(`Could not start Xray: ${err.message}`));
  // No public address yet: fill in this connection's, so downloads work without a trip to Settings.
  if (!state.settings.publicHost) {
    detectPublicIp().then(ip => {
      if (state.settings.publicHost) return;
      state.settings.publicHost = ip;
      saveState();
      log(`Public address set to ${ip} (detected). If your home IP changes, put a dynamic DNS name in Settings instead.`);
    }).catch(err => log(`Could not detect the public address (${err.message}); set it in Settings.`));
  }
  setInterval(() => pollStats().catch(() => {}), STATS_EVERY_MS);
  setInterval(checkConnected, 2000);

  server.on('error', err => {
    log(`Console could not start on port ${CONSOLE_PORT}: ${err.message}`);
    process.exit(1);
  });
  updates.on('error', err => log(`Update service could not start on port ${xray.UPDATE_PORT}: ${err.message}`));
  updates.listen(xray.UPDATE_PORT, '127.0.0.1');
  server.listen(CONSOLE_PORT, CONSOLE_HOST, () => {
    const lan = CONSOLE_HOST === '0.0.0.0' && localIps()[0];
    log(`Console: http://localhost:${CONSOLE_PORT}${lan ? `  (from other devices at home: http://${lan}:${CONSOLE_PORT})` : ''}`);
  });

  let shuttingDown = false;
  const shutdown = async () => {
    if (shuttingDown) return;
    shuttingDown = true;
    await Promise.race([pollStats().catch(() => {}), new Promise(r => setTimeout(r, 2000))]);
    await Promise.race([stopXray(), new Promise(r => setTimeout(r, 3000))]);
    process.exit(0);
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
  // Nevatch Server.exe sends "stop" on stdin, and the pipe closes if the launcher goes away.
  if (process.env.FVPN_LAUNCHER) {
    process.stdin.on('data', data => { if (String(data).trim() === 'stop') shutdown(); });
    process.stdin.on('end', shutdown);
  }
}

if (process.argv[2] === 'set-password') {
  setPasswordCommand(process.argv[3]).then(() => process.exit(0));
} else {
  main();
}
