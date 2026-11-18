param(
    [Parameter(Mandatory)][string]$Image,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $IsMacOS) { throw 'Use macOS to verify a disk image.' }

$imagePath = (Resolve-Path -LiteralPath $Image).Path
$stage = [IO.Directory]::CreateTempSubdirectory('plico-dmg-test-')
$mount = (New-Item -ItemType Directory -Path (Join-Path $stage.FullName 'Mounted')).FullName
$applications = (New-Item -ItemType Directory -Path (Join-Path $stage.FullName 'Applications')).FullName
$application = Join-Path $applications 'Plico.app'
$attached = $false

try {
    hdiutil verify -quiet $imagePath
    hdiutil attach -readonly -nobrowse -mountpoint $mount $imagePath
    $attached = $true
    try {
        $shortcut = Get-Item -LiteralPath (Join-Path $mount 'Applications')
        if ($shortcut.LinkTarget -ne '/Applications') { throw 'The disk image did not provide its Applications shortcut.' }
        ditto (Join-Path $mount 'Plico.app') $application
    } finally {
        hdiutil detach $mount
        $attached = $false
    }
    codesign --verify --deep --strict $application
    & (Join-Path $PSScriptRoot 'Test-Native.ps1') -Executable (Join-Path $application 'Contents/MacOS/Plico') -EvidenceDirectory $EvidenceDirectory
    'Mounted the disk image, copied Plico.app, verified its signature, and completed the native journey.' |
        Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'setup-passed.txt')
} finally {
    $systemTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if (-not $attached -and $stage.Parent.FullName -eq $systemTemp -and
        $stage.Name.StartsWith('plico-dmg-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stage.FullName -Recurse -Force
    }
}
