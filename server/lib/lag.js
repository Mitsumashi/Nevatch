'use strict';
// Lag, for the console: what each person's Nevatch measures (it reports along with its once-a-second ping,
// see /ping in server.js), and this PC's own ping to the router and to the internet. Comparing them shows
// whose side lag comes from: theirs, the way between you, or this PC (a busy Wi-Fi, for example).
const { execFile } = require('child_process');

const SAMPLES = 120; // two minutes at one a second
const SELF_EVERY_MS = 2000; // this PC is checked every 2 s; the last 60 checks (also two minutes) are kept
const SELF_SAMPLES = 60;
const STALE_MS = 15000; // a person whose Nevatch stopped reporting
const LOST = -1;

function createLagMonitor(log) {
  const people = new Map(); // id -> { vpn: [], direct: [], at, lastNote }
  const self = { router: [], internet: [], gateway: null, gatewayAt: 0, lastNote: 0 };
  let viewedAt = 0;
  let timer = null;

  function push(list, value, max) {
    list.push(value);
    if (list.length > max) list.shift();
  }

  // From a person's Nevatch: its previous measurement through the VPN and without it, in ms (-1 = no answer).
  function report(user, vpn, direct) {
    let p = people.get(user.id);
    if (!p) people.set(user.id, p = { vpn: [], direct: [], at: 0, lastNote: 0 });
    push(p.vpn, vpn, SAMPLES);
    push(p.direct, direct, SAMPLES);
    p.at = Date.now();
    const usual = median(p.vpn);
    if ((vpn === LOST || (usual > 0 && vpn > usual * 2 && vpn > usual + 150)) && Date.now() - p.lastNote > 60000) {
      p.lastNote = Date.now();
      log(`Lag for ${user.name}: ${vpn === LOST ? 'no answer' : `${vpn} ms`} through the VPN` +
        `${usual > 0 ? ` (usually ${usual} ms)` : ''}, their own internet ${direct === LOST ? 'not answering' : `${direct} ms`}.`);
    }
    ensureRunning();
  }

  // The console asks for status every few seconds; this PC is only checked while someone looks or is connected.
  function snapshot() {
    viewedAt = Date.now();
    ensureRunning();
    const now = Date.now();
    const out = {};
    for (const [id, p] of people) {
      if (now - p.at > STALE_MS) people.delete(id);
      else out[id] = { vpn: p.vpn, direct: p.direct };
    }
    return { self: { router: self.router, internet: self.internet, gateway: self.gateway }, people: out };
  }

  function wanted() {
    const now = Date.now();
    if (now - viewedAt < 120000) return true;
    for (const p of people.values()) if (now - p.at < STALE_MS) return true;
    return false;
  }

  function ensureRunning() {
    if (!timer) timer = setInterval(checkSelf, SELF_EVERY_MS);
  }

  async function checkSelf() {
    if (!wanted()) {
      clearInterval(timer);
      timer = null;
      return;
    }
    if (Date.now() - self.gatewayAt > 60000) {
      self.gatewayAt = Date.now();
      self.gateway = await defaultGateway();
    }
    const [router, internet] = await Promise.all([self.gateway ? ping(self.gateway) : LOST, ping('1.1.1.1')]);
    push(self.router, router, SELF_SAMPLES);
    push(self.internet, internet, SELF_SAMPLES);
    // A home router normally answers in a few ms; slow answers mean this PC's own connection is clogged.
    const anyoneOn = [...people.values()].some(p => Date.now() - p.at < STALE_MS);
    if (anyoneOn && router > 100 && Date.now() - self.lastNote > 60000) {
      self.lastNote = Date.now();
      log(`Lag on this PC: the router took ${router} ms to answer (normally a few ms). Wi-Fi busy or weak? A network cable fixes it.`);
    }
  }

  return { report, snapshot };
}

function run(cmd, args) {
  return new Promise(resolve =>
    execFile(cmd, args, { timeout: 4000, windowsHide: true }, (err, stdout) => resolve(String(stdout || ''))));
}

// Uses the system's ping (Node can't send pings itself). Reads "time=12ms", "time<1ms", "tijd=12 ms", "time=12.3 ms".
async function ping(ip) {
  const out = process.platform === 'win32'
    ? await run('ping', ['-n', '1', '-w', '1500', ip])
    : await run('ping', ['-c', '1', '-W', '2', ip]);
  const m = /[=<]\s*(\d+(?:[.,]\d+)?)\s*ms/i.exec(out);
  return m ? Math.max(1, Math.round(Number(m[1].replace(',', '.')))) : LOST;
}

// Numbers only, so this works whatever language Windows is in.
async function defaultGateway() {
  if (process.platform === 'win32') {
    const m = /^\s*0\.0\.0\.0\s+0\.0\.0\.0\s+(\d+\.\d+\.\d+\.\d+)\s/m.exec(await run('route', ['print', '-4', '0.0.0.0']));
    return m ? m[1] : null;
  }
  const m = /default via (\d+\.\d+\.\d+\.\d+)/.exec(await run('ip', ['route', 'show', 'default']));
  return m ? m[1] : null;
}

function median(list) {
  const ok = list.filter(v => v !== LOST).sort((a, b) => a - b);
  return ok.length ? ok[Math.floor(ok.length / 2)] : 0;
}

module.exports = { createLagMonitor, ping, defaultGateway };
