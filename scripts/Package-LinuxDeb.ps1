param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidateSet('linux-x64', 'linux-arm64')][string]$Runtime
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$workspace = Split-Path -Parent (Resolve-Path -LiteralPath $Payload).Path
$debianArchitecture = if ($Runtime -eq 'linux-arm64') { 'arm64' } else { 'amd64' }
$root = Join-Path $workspace 'deb'
foreach ($directory in @('DEBIAN', 'opt', 'usr/bin', 'usr/share/applications')) {
    New-Item -ItemType Directory -Path (Join-Path $root $directory) -Force | Out-Null
}

# The tarball is complete before the publish directory is moved into the package.
Move-Item -LiteralPath $Payload -Destination (Join-Path $root 'opt/plico')
$control = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'linux/control.in') -Raw
$control.Replace('@VERSION@', $Version).Replace('@ARCH@', $debianArchitecture) |
    Set-Content -LiteralPath (Join-Path $root 'DEBIAN/control') -Encoding utf8NoBOM
$launcher = Join-Path $root 'usr/bin/plico'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux/plico') -Destination $launcher
chmod 755 $launcher
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux/io.github.itsdanielyc.plico.desktop') -Destination (Join-Path $root 'usr/share/applications')

dpkg-deb --root-owner-group -Zzstd --build $root (Join-Path $OutputDirectory "Plico-$Version-$Runtime.deb")
