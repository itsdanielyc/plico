param(
    [Parameter(Mandatory)][string]$AssetsFile,
    [Parameter(Mandatory)][string]$InstallDirectory
)

$ErrorActionPreference = 'Stop'
$assets = Get-Content -Raw -LiteralPath $AssetsFile | ConvertFrom-Json -AsHashtable
$noticeDirectory = Join-Path $InstallDirectory 'licenses'
$supplementDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/notices'
foreach ($library in $assets.libraries.GetEnumerator()) {
    if ($library.Value.type -ne 'package') { continue }
    $packagePath = $null
    foreach ($cache in $assets.packageFolders.Keys) {
        $candidate = Join-Path $cache $library.Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $packagePath = $candidate
            break
        }
    }
    if ($null -eq $packagePath) { continue }
    $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object {
        $_.Name -match '^(license|third.party.notices|notice|ofl|copying)([.-_]|$)'
    })
    $packageName = $library.Key -replace '/', '-'
    $supplement = Join-Path $supplementDirectory "$packageName.txt"
    if (Test-Path -LiteralPath $supplement -PathType Leaf) { $notices += (Get-Item -LiteralPath $supplement) }
    if ($notices.Count -eq 0) { continue }
    $packageNotices = New-Item -ItemType Directory -Force -Path (Join-Path $noticeDirectory $packageName)
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination $packageNotices.FullName
    }
}
