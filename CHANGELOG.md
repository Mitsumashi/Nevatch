# Changelog

The version lives in `VERSION`; both programs and the server read it from there. To release: raise it here and in
`VERSION`, then run `release.cmd`. Updates reach everyone by file hash, so the number is for people, not the updater.

## 1.4.2 — 2026-11-07

**Nevatch**
- The slogan is gone from the sidebar. The About page now says what Nevatch is.

## 1.4.1 — 2026-11-07

**Nevatch Server**
- A clearer Lag page: each card shows the name and a short verdict ("All good.") on one line, the graph, and the
  latest value of each line. The long "usually / worst" lines and the avg, min, max and jitter numbers are gone.
- The same for the Lag card in the web console.

The friend's app is the same as in 1.4.0.

## 1.4.0 — 2026-11-07

**Nevatch Server**
- No Node.js needed: `Nevatch-Server-<version>.exe` now has Node.js packed inside. Download it and double-click.
- One app for everyone: the per-person *Download for Windows* button is gone. Send your friends `Nevatch-<version>.exe`
  and their key (*Copy key*); they paste it with *Add key*. Friends who already have Nevatch don't need to do anything.

**Both**
- The shape behind the shield keeps turning slowly while connected (Nevatch) or running (Nevatch Server), not only
  while connecting. It rests while the window is minimized or hidden next to the clock.

## 1.3.2 — 2026-11-07

**Nevatch**
- *Only chosen apps* no longer cuts off the other apps' connections when you connect, disconnect, or while it
  reconnects. Nevatch's network adapter now stays on until you quit or change mode or server; disconnecting just
  sends the chosen apps direct again. An app that was already open when you connect keeps its direct connections
  until it reconnects (restart it if needed).
- Adding or removing an app while connected takes effect straight away, without reconnecting.

## 1.3.1 — 2026-11-07

**Nevatch Server**
- The single-file `Nevatch-Server-<version>.exe` no longer makes new keys when it can't find the server folder's
  data in the registry: it also looks for `server\data` up to four folders above itself (so a release in
  `release\<version>\` keeps the keys everyone already has). Before, it quietly started with a fresh set and every
  key handed out stopped working.
- The Log says which data folder (keys and people) the server is using when it starts.

## 1.3.0 — 2026-11-07

**Nevatch**
- Updates on connect: every time you connect, Nevatch asks the server for a newer version. If there is one, it
  downloads it first ("Downloading the new Nevatch from your server…"), installs it, restarts and connects again
  by itself. If the download fails, it stays connected on the old version and tries again next time.

**Both**
- Slim, rounded scroll bars instead of Windows' grey ones: on the server's Settings and Lag pages, the Log, the
  Monitor list and every other list.
- The line under the logo in the sidebar now lines up with the line under each page's title.

**Nevatch Server**
- Opening Nevatch Server no longer turns the server on by itself; click **Start server**. With *Start with Windows*
  on, it still starts the server when you sign in.
- The shield on Home sits lower, with room above it, instead of touching the top edge.
- Lag page: a taller graph that zooms in on small numbers (down to a 5 ms scale), ms labels and gridlines, a
  minute scale underneath, and for each line now / average / min / max / jitter / lost.

## 1.2.0 — 2026-11-07

**Nevatch Server**
- One file for Windows: `Nevatch-Server-<version>.exe` carries the server, Xray and Nevatch.exe inside. Double-click
  and it runs; nothing to unzip or download (Node.js is still needed). On a PC that ran the server from its folder
  before, it uses the same keys and people.
- The server starts as soon as Nevatch Server opens.
- Web console: on the first start you choose **open** (no password, works on this PC only) or a **password** (also
  works from other devices at home). Change it any time in *Settings → Web console*. The window itself never asks.
- The public address is detected by itself on the first start.
- A release is now three files: `Nevatch-<version>.exe`, `Nevatch-Server-<version>.exe` and
  `Nevatch-Server-Docker-<version>.zip`.

## 1.1.0 — 2026-11-07

- New look: deep navy colors and a calmer Home page — a soft shape behind the shield instead of the world map and
  ring, a flatter Connect button, and an *Activity* card with the last log lines (click it for the full log).
  Nevatch Server's Home shows the same activity card.
- Home shows the live ping in a small badge under the status ("48 ms · good"). When answers get lost or the ping
  stays high, the badge, the shield and the sidebar dot turn orange ("Connected, unstable").
- Reconnecting: first try after 2 s, then every 10 s. After 10 minutes Nevatch stops and shows Disconnected, and
  starts trying again by itself when the network comes back or the PC wakes. A failed connect at startup keeps
  trying the same way.
- Sidebar order: Home, Apps, Lag, Servers, Log, Settings, Monitor, About.

## 1.0.1 — 2026-11-07

- Nevatch's own popups in the corner of the screen replace Windows' balloons: when the window goes to the tray,
  when the connection is lost or comes back, and after an update. Dark card with the logo, a colored accent, a
  bar that runs down until it closes; it waits while the mouse is on it, and clicking it opens Nevatch.
- The sidebar no longer has the separate logo strip; the logo sits next to the name, and the sidebar is narrower.

## 1.0.0 — 2026-11-07

First numbered release (earlier builds were numbered 1.1 to 1.7 for Nevatch and 1.3 for Nevatch Server).

**Nevatch**
- Reconnects by itself when the connection to the server breaks: tries again after 2, 5, 10 and 20 s, then every
  30 s, until it works or you click *Stop trying*. The internet works without the VPN in between. Tries straight
  away when the network comes back or the PC wakes from sleep.
- A red bar at the top says what went wrong (for example "No answer from the server for 18 s"), with
  *Reconnect now*; failed connects show the reason with *Show log*.
- New dark look in the style of Discord: a logo rail, channel-style pages, a status panel with a colored dot,
  green switches, a dark title bar.

**Nevatch Server**
- Same dark look in the Windows launcher.
- Docker package: `VPN_PORT`, `PUBLIC_HOST` and `ADMIN_PASSWORD` settings, `setup.js --server-only`.
- The server log and the console's status pill (hover it) show the version; the console locks the port field
  when `VPN_PORT` sets it.
