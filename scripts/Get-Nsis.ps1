param([Parameter(Mandatory)][string]$DestinationDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$version = '3.13'
$digest = 'BA63DFFC4410EE89193E1CB5A41989991BD77C61068DA17E3156D136B7B0B3D8'
$directory = (New-Item -ItemType Directory -Force -Path $DestinationDirectory).FullName
$archive = Join-Path $directory "nsis-$version.zip"
$uri = "https://downloads.sourceforge.net/project/nsis/NSIS%203/$version/nsis-$version.zip"
curl.exe --fail --silent --show-error --location --retry 2 --max-time 60 --output $archive $uri
if ((Get-FileHash -LiteralPath $archive).Hash -ne $digest) {
    throw "NSIS $version failed its SHA-256 check."
}
Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force
(Resolve-Path -LiteralPath (Join-Path $directory "nsis-$version/makensis.exe")).Path
