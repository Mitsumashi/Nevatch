'use strict';
// One-time setup:
//   1. downloads Xray from the official GitHub release and checks its SHA-256 checksum:
//      one for this server (bin/) and the Windows one that goes inside Nevatch.exe (../client/xray/),
//      plus wintun.dll from wintun.net for Nevatch's whole-PC mode (../client/wintun/)
//   2. builds the friend's Windows program (../client/build.cmd) and the server window (launcher/build.cmd)
//   3. asks for a password for the web console
// Safe to run again: finished steps are skipped.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const { readZip } = require('./lib/zip');

const ROOT = __dirname;
const XRAY_VERSION = process.env.XRAY_VERSION || 'v26.10.21';
const RELEASE = `https://github.com/XTLS/Xray-core/releases/download/${XRAY_VERSION}/`;
const SERVER_BIN = path.join(ROOT, 'bin');
const CLIENT_DIR = path.join(ROOT, '..', 'client');
const CLIENT_XRAY = path.join(CLIENT_DIR, 'xray');
const CLIENT_EXE = path.join(CLIENT_DIR, 'dist', 'Nevatch.exe');
const LAUNCHER_EXE = path.join(ROOT, 'Nevatch Server.exe');
// Wintun (the virtual network adapter behind whole-PC mode), checksum as published on wintun.net.
const WINTUN_URL = 'https://www.wintun.net/builds/wintun-0.14.1.zip';
const WINTUN_SHA256 = '07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51';
const CLIENT_WINTUN = path.join(CLIENT_DIR, 'wintun');

function serverAsset() {
  if (process.platform === 'win32' && process.arch === 'x64') return 'Xray-windows-64.zip';
  if (process.platform === 'linux' && process.arch === 'x64') return 'Xray-linux-64.zip';
  if (process.platform === 'linux' && process.arch === 'arm64') return 'Xray-linux-arm64-v8a.zip';
  throw new Error(`No Xray build picked for ${process.platform}/${process.arch} — add it to serverAsset() in setup.js.`);
}

async function download(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Download failed (${res.status}): ${url}`);
  return Buffer.from(await res.arrayBuffer());
}

const zips = new Map();
async function verifiedRelease(asset) {
  if (!zips.has(asset)) {
    console.log(`  Downloading ${asset} (${XRAY_VERSION}) from GitHub…`);
    const zip = await download(RELEASE + asset);
    const digest = (await download(RELEASE + asset + '.dgst')).toString();
    const expected = /SHA2-256\s*=\s*([0-9a-f]{64})/i.exec(digest)?.[1]?.toLowerCase();
    const actual = crypto.createHash('sha256').update(zip).digest('hex');
    if (!expected || expected !== actual) throw new Error(`Checksum mismatch for ${asset}; not using it.`);
    console.log('  Checksum OK.');
    zips.set(asset, readZip(zip));
  }
  return zips.get(asset);
}

// files: { 'name in the release zip': 'destination path' }; the first one is the program.
async function installXray(asset, files) {
  const [exe] = Object.values(files);
  const shown = path.relative(path.dirname(ROOT), exe);
  if (fs.existsSync(exe)) {
    console.log(`✓ ${shown} already installed`);
    return;
  }
  const zip = await verifiedRelease(asset);
  for (const [name, dest] of Object.entries(files)) {
    const data = zip.read(name);
    if (!data) throw new Error(`${name} not found in ${asset}`);
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, data, { mode: 0o755 });
  }
  console.log(`✓ Installed ${shown}`);
}

async function installWintun() {
  const dll = path.join(CLIENT_WINTUN, 'wintun.dll');
  if (fs.existsSync(dll)) {
    console.log('✓ client/wintun/wintun.dll already installed');
    return;
  }
  console.log('  Downloading Wintun from wintun.net…');
  const zip = await download(WINTUN_URL);
  if (crypto.createHash('sha256').update(zip).digest('hex') !== WINTUN_SHA256) throw new Error('Checksum mismatch for Wintun; not using it.');
  const files = readZip(zip);
  // Unmodified, as its license asks.
  for (const [name, dest] of [['wintun/bin/amd64/wintun.dll', dll], ['wintun/LICENSE.txt', path.join(CLIENT_WINTUN, 'LICENSE.txt')]]) {
    const data = files.read(name);
    if (!data) throw new Error(`${name} not found in the Wintun download`);
    fs.mkdirSync(CLIENT_WINTUN, { recursive: true });
    fs.writeFileSync(dest, data);
  }
  console.log('✓ Installed client/wintun/wintun.dll');
}

// Builds a Windows program with its build.cmd, unless it's already there (or --rebuild is given).
function buildExe(exe, buildCmd, shown) {
  if (fs.existsSync(exe) && !process.argv.includes('--rebuild')) {
    console.log(`✓ ${shown} already built`);
    return;
  }
  if (process.platform !== 'win32') {
    console.log(`! ${shown} can only be built on Windows; copy it from a Windows PC if you need it.`);
    return;
  }
  // Quoted because the folder path may contain spaces.
  const result = spawnSync(`"${buildCmd}"`, { shell: true, encoding: 'utf8' });
  if (result.status !== 0) throw new Error(`Building ${shown} failed:\n${result.stdout}${result.stderr}`);
  console.log(`✓ Built ${shown}`);
}

function setPassword() {
  const statePath = path.resolve(process.env.FVPN_DATA || path.join(ROOT, 'data'), 'state.json');
  try {
    if (JSON.parse(fs.readFileSync(statePath, 'utf8')).adminHash) {
      console.log('✓ Console password already set (change it with: node server.js set-password)');
      return;
    }
  } catch {}
  // Nevatch Server asks open-or-password itself and hands the choice to server.js.
  if (process.env.FVPN_LAUNCHER && !process.env.ADMIN_PASSWORD) return;
  const args = [path.join(ROOT, 'server.js'), 'set-password'];
  if (process.env.ADMIN_PASSWORD) args.push(process.env.ADMIN_PASSWORD);
  const result = spawnSync(process.execPath, args, { stdio: 'inherit' });
  if (result.status !== 0) throw new Error('Setting the password failed.');
}

(async () => {
  console.log('Nevatch setup\n');
  const exe = process.platform === 'win32' ? 'xray.exe' : 'xray';
  await installXray(serverAsset(), { [exe]: path.join(SERVER_BIN, exe), LICENSE: path.join(SERVER_BIN, 'LICENSE') });
  // --server-only (Docker): just the server's Xray; Nevatch.exe comes ready-built and the password from ADMIN_PASSWORD.
  if (process.argv.includes('--server-only')) {
    console.log('\nDone (server only).');
    return;
  }
  await installXray('Xray-windows-64.zip', {
    'xray.exe': path.join(CLIENT_XRAY, 'xray.exe'),
    LICENSE: path.join(CLIENT_XRAY, 'LICENSE'),
  });
  await installWintun();
  buildExe(CLIENT_EXE, path.join(CLIENT_DIR, 'build.cmd'), 'client/dist/Nevatch.exe');
  // The launcher can't replace itself while it's the one running setup, so it's never rebuilt then.
  if (!process.env.FVPN_LAUNCHER) buildExe(LAUNCHER_EXE, path.join(ROOT, 'launcher', 'build.cmd'), 'server/Nevatch Server.exe');
  setPassword();
  console.log(process.platform === 'win32'
    ? '\nDone. Start the server with "Nevatch Server.exe".'
    : '\nDone. Start the server with: node server.js');
})().catch(err => {
  console.error(`\nSetup failed: ${err.message}`);
  process.exit(1);
});
