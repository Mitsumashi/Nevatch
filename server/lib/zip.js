'use strict';
// Minimal ZIP reader (no dependencies). Enough to unpack the Xray release.
const zlib = require('zlib');

function readZip(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('Not a ZIP file');

  const files = new Map();
  let p = buf.readUInt32LE(eocd + 16);
  for (let n = buf.readUInt16LE(eocd + 10); n > 0; n--) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('Damaged ZIP file');
    const method = buf.readUInt16LE(p + 10);
    const size = buf.readUInt32LE(p + 20);
    const nameLen = buf.readUInt16LE(p + 28);
    const skip = buf.readUInt16LE(p + 30) + buf.readUInt16LE(p + 32);
    const localAt = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nameLen);
    p += 46 + nameLen + skip;
    if (name.endsWith('/')) continue;
    const start = localAt + 30 + buf.readUInt16LE(localAt + 26) + buf.readUInt16LE(localAt + 28);
    files.set(name, { method, body: buf.subarray(start, start + size) });
  }

  return {
    names: [...files.keys()],
    read(name) {
      const f = files.get(name);
      if (!f) return null;
      if (f.method === 0) return Buffer.from(f.body);
      if (f.method === 8) return zlib.inflateRawSync(f.body);
      throw new Error(`Unsupported compression for ${name}`);
    },
  };
}

module.exports = { readZip };
