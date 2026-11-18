param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'Use Ubuntu to verify the Debian package.' }

$packagePath = (Resolve-Path -LiteralPath $Package).Path
$PSNativeCommandUseErrorActionPreference = $false
$status = dpkg-query -W -f '${db:Status-Status}' plico 2>$null
if ($LASTEXITCODE -eq 0 -and $status -eq 'installed') {
    throw 'Use a clean runner for the Debian package round trip.'
}
$PSNativeCommandUseErrorActionPreference = $true
$stage = [IO.Directory]::CreateTempSubdirectory('plico-deb-test-')
$pdfFolder = (New-Item -ItemType Directory -Force -Path (Join-Path $stage.FullName 'Library/PDFs')).FullName
$libraryFile = Join-Path $pdfFolder 'My document.pdf'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../tests/fixtures/Lease Renewal.pdf') -Destination $libraryFile
$digest = (Get-FileHash -LiteralPath $libraryFile).Hash
$launcher = '/usr/bin/plico'
$application = '/opt/plico/Plico'
$menuEntry = '/usr/share/applications/io.github.itsdanielyc.plico.desktop'
$installed = $false

try {
    sudo apt-get install -y --no-install-recommends $packagePath
    $installed = $true
    if (-not (Test-Path -LiteralPath $launcher) -or -not (Test-Path -LiteralPath $menuEntry)) {
        throw 'The installed package did not provide its launcher and desktop menu entry.'
    }
    $desktopEntry = Get-Content -LiteralPath $menuEntry
    if ($desktopEntry -notcontains 'Exec=plico %F' -or $desktopEntry -notcontains 'MimeType=application/pdf;') {
        throw 'The installed desktop entry did not accept PDF Open With requests.'
    }
    $nativeCheck = Join-Path $PSScriptRoot 'Test-Native.ps1'
    if ($env:DISPLAY) {
        & $nativeCheck -Executable $launcher -EvidenceDirectory $EvidenceDirectory
    } else {
        xvfb-run -a pwsh -NoProfile -File $nativeCheck -Executable $launcher -EvidenceDirectory $EvidenceDirectory
    }

    sudo apt-get remove -y plico
    $installed = $false
    if ((Test-Path -LiteralPath $launcher) -or (Test-Path -LiteralPath $menuEntry) -or (Test-Path -LiteralPath $application)) {
        throw 'Package removal left a desktop entry, application, or launcher.'
    }
    if (-not (Test-Path -LiteralPath $libraryFile) -or (Get-FileHash -LiteralPath $libraryFile).Hash -ne $digest) {
        throw 'Package removal changed the library PDF.'
    }
    'Installed the Debian package with PDF Open With registration, launched Plico, and removed the app while preserving the PDF.' |
        Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'setup-passed.txt')
} finally {
    if ($installed) { sudo apt-get remove -y plico }
    $systemTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ($stage.Parent.FullName -eq $systemTemp -and $stage.Name.StartsWith('plico-deb-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stage.FullName -Recurse -Force
    }
}
