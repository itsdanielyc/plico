param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$stage = [IO.Directory]::CreateTempSubdirectory('plico-install-')

try {
    if ($IsMacOS) {
        ditto -x -k $archivePath $stage.FullName
        $app = Join-Path $stage.FullName 'Plico.app'
        codesign --verify --deep --strict $app
        $executable = Join-Path $app 'Contents/MacOS/Plico'
    } elseif ($IsLinux) {
        tar -xzf $archivePath -C $stage.FullName
        $executable = Join-Path $stage.FullName 'Plico/Plico'
    } else {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $stage.FullName
        $executable = Join-Path $stage.FullName 'Plico/Plico.exe'
    }

    $nativeCheck = Join-Path $PSScriptRoot 'Test-Native.ps1'
    if ($IsLinux -and -not $env:DISPLAY) {
        xvfb-run -a pwsh -NoProfile -File $nativeCheck -Executable $executable -EvidenceDirectory $EvidenceDirectory
    } else {
        & $nativeCheck -Executable $executable -EvidenceDirectory $EvidenceDirectory
    }
} finally {
    $systemTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ($stage.Parent.FullName -eq $systemTemp -and $stage.Name.StartsWith('plico-install-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stage.FullName -Recurse -Force
    }
}
