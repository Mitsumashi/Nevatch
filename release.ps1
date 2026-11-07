# Makes a release with the version in VERSION. release\<version>\ gets three files:
#   Nevatch-<version>.exe                the friend's program (everyone gets this same file and pastes their own key)
#   Nevatch-Server-<version>.exe         the Windows server in one file: double-click and it runs (Node.js inside)
#   Nevatch-Server-Docker-<version>.zip  the same server for Docker
# Building client\dist\Nevatch.exe is also the publish step: the running server offers it to everyone straight away.
# Nothing here contains server\data (keys, people, password).
# -PackOnly: keep the current Nevatch.exe (a rebuild changes its hash, so everyone would download it again).
param([switch]$PackOnly)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION must look like 1.0.0, not '$version'." }
Write-Host "Nevatch $version"
if (-not (Test-Path (Join-Path $root 'server\bin\xray.exe'))) { throw 'server\bin\xray.exe is missing: run "node setup.js" in the server folder first.' }

$out = Join-Path $root "release\$version"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
$build = Join-Path $out 'build'
New-Item -ItemType Directory -Force $build | Out-Null

if (-not $PackOnly) {
  Write-Host '  Building Nevatch.exe (publishes it to everyone)...'
  & cmd /c "`"$root\client\build.cmd`""
  if ($LASTEXITCODE -ne 0) { throw 'Building Nevatch.exe failed.' }
}
$client = Join-Path $root 'client\dist\Nevatch.exe'

# The server files: no data, no old launcher.
function Copy-Server($dest) {
  New-Item -ItemType Directory -Force $dest | Out-Null
  foreach ($item in 'server.js', 'setup.js', 'package.json', 'lib', 'public') {
    Copy-Item -Recurse (Join-Path $root "server\$item") $dest
  }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Zips a folder's contents. Retries a few times: the antivirus often holds a freshly copied .exe for a moment.
function Pack($stage, $zip) {
  for ($try = 1; ; $try++) {
    try {
      if (Test-Path $zip) { Remove-Item $zip }
      # Built entry by entry: .NET Framework's CreateFromDirectory writes "\" in paths, which Linux unzips wrongly.
      $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
      try {
        $base = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem -Recurse -File -Force $stage) {
          $name = $file.FullName.Substring($base.Length).Replace('\', '/')
          [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
      } finally { $archive.Dispose() }
      break
    } catch {
      if ($try -ge 10) { throw }
      Start-Sleep -Seconds 2
    }
  }
  for ($try = 1; $try -le 10; $try++) {
    try { Remove-Item -Recurse -Force $stage; break } catch { Start-Sleep -Seconds 2 }
  }
}

# Windows server: one exe with the server files, Xray and Nevatch.exe packed inside (unpacked when it starts).
Write-Host '  Building Nevatch-Server.exe...'
$payload = Join-Path $build 'payload'
Copy-Server (Join-Path $payload 'server')
Copy-Item (Join-Path $root 'server\allow-firewall.cmd') (Join-Path $payload 'server')
New-Item -ItemType Directory -Force (Join-Path $payload 'server\bin') | Out-Null
foreach ($item in 'xray.exe', 'LICENSE') { Copy-Item (Join-Path $root "server\bin\$item") (Join-Path $payload 'server\bin') }
# Node.js goes inside too, so the server runs on a PC without Node installed: this PC's node.exe and its license.
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { throw 'Node.js is needed to make a release (its node.exe is packed into the server).' }
$nodeVersion = (& $node --version).Trim()
New-Item -ItemType Directory -Force (Join-Path $payload 'node') | Out-Null
Copy-Item $node (Join-Path $payload 'node\node.exe')
Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/nodejs/node/$nodeVersion/LICENSE" -OutFile (Join-Path $payload 'node\LICENSE')
Write-Host "  Packed Node.js $nodeVersion"
New-Item -ItemType Directory -Force (Join-Path $payload 'client\dist') | Out-Null
Copy-Item $client (Join-Path $payload 'client\dist')
Copy-Item (Join-Path $root 'VERSION') $payload
$payloadZip = Join-Path $build 'payload.zip'
Pack $payload $payloadZip
$serverExe = Join-Path $out "Nevatch-Server-$version.exe"
& cmd /c "`"$root\server\launcher\build.cmd`" `"$serverExe`" `"$payloadZip`""
if ($LASTEXITCODE -ne 0) { throw 'Building Nevatch-Server.exe failed.' }

# Docker: Dockerfile, compose file, the server and Nevatch.exe.
Write-Host '  Making the Docker package...'
$dock = Join-Path $build "docker\nevatch-server-$version"
Copy-Server (Join-Path $dock 'server')
New-Item -ItemType Directory -Force (Join-Path $dock 'client\dist') | Out-Null
Copy-Item $client (Join-Path $dock 'client\dist')
Copy-Item (Join-Path $root 'VERSION'), (Join-Path $root 'CHANGELOG.md') $dock
foreach ($item in 'Dockerfile', '.dockerignore', '.env.example') { Copy-Item (Join-Path $root "docker\$item") $dock }
foreach ($item in 'docker-compose.yml', 'README-DOCKER.md') {
  $text = (Get-Content -Raw (Join-Path $root "docker\$item")).Replace('__VERSION__', $version)
  [IO.File]::WriteAllText((Join-Path $dock $item), $text, (New-Object Text.UTF8Encoding $false))
}
Pack (Join-Path $build 'docker') (Join-Path $out "Nevatch-Server-Docker-$version.zip")

Copy-Item $client (Join-Path $out "Nevatch-$version.exe")
for ($try = 1; $try -le 10; $try++) {
  try { Remove-Item -Recurse -Force $build; break } catch { Start-Sleep -Seconds 2 }
}
Write-Host "`nDone: $out"
Get-ChildItem $out | ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
