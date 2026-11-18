param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'osx-arm64', 'linux-x64')][string]$Runtime,
    [string]$OutputDirectory = 'artifacts/archives'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repository = Split-Path -Parent $PSScriptRoot
$output = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$buildProperties = [xml](Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props'))
$version = $buildProperties.Project.PropertyGroup.Version
$stage = [IO.Directory]::CreateTempSubdirectory('plico-package-')

try {
    $publish = Join-Path $stage.FullName 'Plico'
    $resources = $publish
    $bundle = Join-Path $stage.FullName 'Plico.app'
    if ($Runtime -eq 'osx-arm64') {
        if (-not $IsMacOS) { throw 'Package the macOS app on macOS so it can be signed.' }
        $publish = Join-Path $bundle 'Contents/MacOS'
        $resources = Join-Path $bundle 'Contents/Resources'
    }

    dotnet publish (Join-Path $repository 'src/Plico.Desktop/Plico.Desktop.csproj') -c Release -r $Runtime --self-contained -o $publish -p:NuGetLockFilePath=obj/publish-packages.lock.json
    New-Item -ItemType Directory -Force -Path $resources | Out-Null
    & (Join-Path $PSScriptRoot 'Copy-Notices.ps1') -AssetsFile (Join-Path $repository 'src/Plico.Desktop/obj/project.assets.json') -InstallDirectory $resources
    foreach ($file in @('README.md', 'THIRD-PARTY.md')) {
        $notice = Join-Path $repository $file
        if (Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination $resources }
    }

    if ($Runtime -eq 'osx-arm64') {
        $plist = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'macos/Info.plist')
        $plist.Replace('@VERSION@', $version) | Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $bundle 'Contents/Info.plist')
        Get-ChildItem -LiteralPath $publish -File -Recurse -Filter 'tesseract' | ForEach-Object { chmod +x $_.FullName }
        codesign --force --deep --sign - $bundle
        codesign --verify --deep --strict $bundle
        ditto -c -k --sequesterRsrc --keepParent $bundle (Join-Path $output "Plico-$version-$Runtime.zip")
    } elseif ($Runtime -eq 'linux-x64') {
        Get-ChildItem -LiteralPath $publish -File -Recurse -Filter 'tesseract' | ForEach-Object { chmod +x $_.FullName }
        tar -czf (Join-Path $output "Plico-$version-$Runtime.tar.gz") -C $stage.FullName Plico
    } else {
        Compress-Archive -Force -LiteralPath $publish -DestinationPath (Join-Path $output "Plico-$version-$Runtime.zip")
    }
} finally {
    $systemTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ($stage.Parent.FullName -eq $systemTemp -and $stage.Name.StartsWith('plico-package-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stage.FullName -Recurse -Force
    }
}
