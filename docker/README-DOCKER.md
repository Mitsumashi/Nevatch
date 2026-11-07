# Nevatch Server __VERSION__ on Docker

Runs the same server as the Windows launcher: Xray (VLESS + Reality), the web console, and the update
service that hands out new versions of Nevatch.exe. Works on any Linux machine or NAS with Docker (x64 or arm64).

## Start

1. Unzip this package on the Docker machine and open a terminal in the folder.
2. `cp .env.example .env` and fill it in:
   - `VPN_PORT`: the port your friends connect to (443 unless it's taken).
   - `PUBLIC_HOST`: your public IP, or better a dynamic DNS name (e.g. `myname.duckdns.org`), since home IPs change.
   - `ADMIN_PASSWORD`: the console password.
3. `docker compose up -d --build`
4. **Router:** forward TCP `VPN_PORT` to this machine's local IP. Do **not** forward the console port (8088).
5. Open the console at `http://<this-machine>:8088`, log in, and add people.

## Adding a friend

In the console, *People* → type a name → **Add**. Then click **Copy key**. It gives a `vless://…` link (it holds
your address, the port and their personal ID). Send them that key and `Nevatch-<version>.exe` from the Releases
page. They paste the key in Nevatch with **Add key**, or in a phone app such as Hiddify, v2rayNG or Streisand.

Each person gets their own key, so you can disable or remove one person without affecting the others.

## Things to mind on Docker

- **The port is fixed by `VPN_PORT`.** It's published with the same number on both sides because it also goes into
  every key; the console's port field is locked. Changing it means every key changes: send fresh downloads, or
  have people paste their new key with *Add key*.
- **Moving from the Windows server?** Copy its `server/data/state.json` into `./data/` here *before* the first start.
  It holds the server keys and people; without it everyone needs a new key.
- **Back up `./data`.** Same reason.
- **Speed limit per person** counts per internet address. On Linux, Docker keeps your friends' real addresses.
  Docker Desktop (Windows/Mac) shows them all as one address, so they'd share one limit there.
- **The lag card's "router" ping** measures Docker's internal gateway, not your real router, so it stays low even
  when your Wi-Fi is busy. The internet ping and each person's numbers are accurate.
- **The server log** is capped at 3 files of 10 MB (see `logging` in `docker-compose.yml`).
- **Console from outside your home:** don't. Use a VPN into your home network, or set `CONSOLE_BIND=127.0.0.1` and
  an SSH tunnel.

## Updating

Unzip the new package over this folder (`.env` and `data/` stay), then `docker compose up -d --build`.
Everyone's Nevatch updates itself to the Nevatch.exe inside the package the next time it connects, or within
6 hours while it stays connected.

Useful commands: `docker compose logs -f` (server log), `docker compose restart`, `docker compose down`.
