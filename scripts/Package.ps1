param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'osx-arm64', 'osx-x64', 'linux-x64', 'linux-arm64')][string]$Runtime,
    [string]$OutputDirectory = 'artifacts/archives',
    [string]$WindowsInstallerCompiler
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repository = Split-Path -Parent $PSScriptRoot
$output = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$buildProperties = [xml](Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props'))
$version = $buildProperties.Project.PropertyGroup.Version
$stage = [IO.Directory]::CreateTempSubdirectory('plico-package-')
$macRuntime = $Runtime.StartsWith('osx-', [StringComparison]::Ordinal)
$linuxRuntime = $Runtime.StartsWith('linux-', [StringComparison]::Ordinal)

try {
    $publish = Join-Path $stage.FullName 'Plico'
    $resources = $publish
    $bundle = Join-Path $stage.FullName 'Plico.app'
    if ($macRuntime) {
        if (-not $IsMacOS) { throw 'Package the macOS app on macOS so it can be signed.' }
        $publish = Join-Path $bundle 'Contents/MacOS'
        $resources = Join-Path $bundle 'Contents/Resources'
    }

    dotnet publish (Join-Path $repository 'src/Plico.Desktop/Plico.Desktop.csproj') -c Release -r $Runtime --self-contained -o $publish -p:NuGetLockFilePath=obj/publish-packages.lock.json
    # Some native packages ship large PDBs for debugging. The release payload only needs the runtime files.
    foreach ($symbol in Get-ChildItem -LiteralPath $publish -File -Recurse -Filter '*.pdb') {
        Remove-Item -LiteralPath $symbol.FullName -Force
    }
    New-Item -ItemType Directory -Force -Path $resources | Out-Null
    & (Join-Path $PSScriptRoot 'Copy-Notices.ps1') -AssetsFile (Join-Path $repository 'src/Plico.Desktop/obj/project.assets.json') -InstallDirectory $resources
    foreach ($file in @('README.md', 'THIRD-PARTY.md')) {
        $notice = Join-Path $repository $file
        if (Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination $resources }
    }

    if ($macRuntime) {
        $plist = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'macos/Info.plist')
        $plist.Replace('@VERSION@', $version) | Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $bundle 'Contents/Info.plist')
        Get-ChildItem -LiteralPath $publish -File -Recurse -Filter 'tesseract' | ForEach-Object { chmod +x $_.FullName }
        codesign --force --deep --sign - $bundle
        codesign --verify --deep --strict $bundle
        ditto -c -k --sequesterRsrc --keepParent $bundle (Join-Path $output "Plico-$version-$Runtime.zip")
        $applicationsLink = Join-Path $stage.FullName 'Applications'
        New-Item -ItemType SymbolicLink -Path $applicationsLink -Target '/Applications' | Out-Null
        try {
            hdiutil create -quiet -srcfolder $stage.FullName -volname Plico -fs HFS+ -format UDZO (Join-Path $output "Plico-$version-$Runtime.dmg")
        } finally {
            Remove-Item -LiteralPath $applicationsLink -Force
        }
    } elseif ($linuxRuntime) {
        Get-ChildItem -LiteralPath $publish -File -Recurse -Filter 'tesseract' | ForEach-Object { chmod +x $_.FullName }
        tar -czf (Join-Path $output "Plico-$version-$Runtime.tar.gz") -C $stage.FullName Plico
        & (Join-Path $PSScriptRoot 'Package-LinuxDeb.ps1') -Payload $publish -OutputDirectory $output -Version $version -Runtime $Runtime
    } else {
        Compress-Archive -Force -LiteralPath $publish -DestinationPath (Join-Path $output "Plico-$version-$Runtime.zip")
        if ($WindowsInstallerCompiler) {
            & (Join-Path $PSScriptRoot 'Package-WindowsInstaller.ps1') -Compiler $WindowsInstallerCompiler -PayloadDirectory $publish -OutputDirectory $output -Version $version -Runtime $Runtime
        }
    }
} finally {
    $systemTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ($stage.Parent.FullName -eq $systemTemp -and $stage.Name.StartsWith('plico-package-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stage.FullName -Recurse -Force
    }
}
