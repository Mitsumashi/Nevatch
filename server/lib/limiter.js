'use strict';
// Speed limit. Xray has none of its own, so while a limit is set, people connect to this relay instead of
// straight to Xray, and the relay passes their traffic on at no more than the limit.
// "Per person" means per internet address: all connections from one address share one allowance,
// download and upload separately, each with its own limit. The relay only copies bytes; the encrypted Reality stream is untouched.
const net = require('net');

const INTERNAL_PORT = Number(process.env.FVPN_RELAY_PORT) || 10443; // while the relay is on, Xray listens here on 127.0.0.1 only

// Token bucket; rate() gives the current limit in bytes per millisecond, so a changed limit applies at once.
class Bucket {
  constructor(rate) {
    this.rate = rate;
    this.tokens = null;
    this.last = Date.now();
  }

  // Spends n bytes and returns how many ms to wait before reading more.
  take(n) {
    const perMs = this.rate();
    if (!(perMs > 0)) return 0;
    const now = Date.now();
    const burst = Math.max(64 * 1024, perMs * 250); // a quarter second of slack keeps TCP flowing smoothly
    this.tokens = this.tokens === null ? burst : Math.min(burst, this.tokens + (now - this.last) * perMs);
    this.last = now;
    this.tokens -= n;
    return this.tokens < 0 ? Math.ceil(-this.tokens / perMs) : 0;
  }
}

// Copies from -> to, pausing `from` while `to` is full or the bucket is empty.
function pump(from, to, bucket) {
  let full = false, waiting = false;
  const resume = () => { if (!full && !waiting) from.resume(); };
  from.on('data', chunk => {
    if (!to.write(chunk)) {
      full = true;
      from.pause();
      to.once('drain', () => { full = false; resume(); });
    }
    const ms = bucket.take(chunk.length);
    if (ms > 0) {
      waiting = true;
      from.pause();
      setTimeout(() => { waiting = false; resume(); }, ms);
    }
  });
  from.on('end', () => to.end());
}

// downRate / upRate: functions giving each direction's limit in bytes per ms (0 = none), as seen by the person.
function createRelay(downRate, upRate) {
  const peers = new Map(); // address -> { down, up, connections }
  const sockets = new Set();
  let server = null;

  function onConnection(client) {
    const address = String(client.remoteAddress || '').replace(/^::ffff:/, '');
    let peer = peers.get(address);
    if (!peer) peers.set(address, peer = { down: new Bucket(downRate), up: new Bucket(upRate), connections: 0 });
    peer.connections++;

    const upstream = net.connect({ host: '127.0.0.1', port: INTERNAL_PORT, allowHalfOpen: true });
    for (const socket of [client, upstream]) {
      sockets.add(socket);
      socket.setNoDelay(true);
      socket.on('error', () => socket.destroy());
    }
    client.on('close', () => {
      sockets.delete(client);
      upstream.destroy();
      if (--peer.connections === 0) peers.delete(address);
    });
    upstream.on('close', () => {
      sockets.delete(upstream);
      client.destroy();
    });
    pump(client, upstream, peer.up);
    pump(upstream, client, peer.down);
  }

  return {
    get listening() { return !!server; },

    listen(port, host) {
      return new Promise((resolve, reject) => {
        const s = net.createServer({ allowHalfOpen: true }, onConnection);
        s.once('error', reject);
        s.listen(port, host, () => {
          s.removeListener('error', reject);
          s.on('error', () => {});
          server = s;
          resolve();
        });
      });
    },

    // Stops listening and drops open connections (Nevatch reconnects by itself).
    close() {
      const s = server;
      server = null;
      if (!s) return Promise.resolve();
      for (const socket of sockets) socket.destroy();
      return new Promise(resolve => s.close(() => resolve()));
    },
  };
}

module.exports = { INTERNAL_PORT, createRelay };
