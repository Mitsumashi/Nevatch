# Nevatch Server in Docker: Xray (VLESS + Reality), the web console, and the update service for Nevatch.exe.
# Build from the unpacked Docker package (release.cmd makes it), which holds server/, client/dist/Nevatch.exe and VERSION.
FROM node:20-bookworm-slim

# ping and ip: the console's lag card pings the router and the internet.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates iputils-ping iproute2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY VERSION ./VERSION
COPY server/ ./server/
# Nevatch.exe is built on Windows; the server appends each person's key to it on download and offers it as the update.
COPY client/dist/Nevatch.exe ./client/dist/Nevatch.exe

# Downloads the Linux Xray from GitHub (checksum-verified) into server/bin.
RUN cd server && node setup.js --server-only

ENV FVPN_DATA=/data \
    CONSOLE_HOST=0.0.0.0 \
    CONSOLE_PORT=8088 \
    VPN_PORT=443
VOLUME /data
EXPOSE 443/tcp 8088/tcp

WORKDIR /app/server
CMD ["node", "server.js"]
