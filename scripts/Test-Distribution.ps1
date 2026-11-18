param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'

function Find-Bundle([string]$Pattern) {
    $paths = @(Get-ChildItem -LiteralPath $Directory -File -Filter $Pattern)
    if ($paths.Count -ne 1) { throw "Expected one bundle matching $Pattern." }
    $paths[0].FullName
}

$archive = Find-Bundle $(if ($IsLinux) { '*.tar.gz' } else { '*.zip' })
$portableEvidence = Join-Path $EvidenceDirectory 'portable'
$installedEvidence = Join-Path $EvidenceDirectory 'installed'
& (Join-Path $PSScriptRoot 'Test-Archive.ps1') -Archive $archive -EvidenceDirectory $portableEvidence
if ($IsWindows) {
    & (Join-Path $PSScriptRoot 'Test-WindowsInstaller.ps1') -Installer (Find-Bundle '*-setup.exe') -EvidenceDirectory $installedEvidence
} elseif ($IsMacOS) {
    & (Join-Path $PSScriptRoot 'Test-MacDiskImage.ps1') -Image (Find-Bundle '*.dmg') -EvidenceDirectory $installedEvidence
} else {
    & (Join-Path $PSScriptRoot 'Test-DebPackage.ps1') -Package (Find-Bundle '*.deb') -EvidenceDirectory $installedEvidence
}
