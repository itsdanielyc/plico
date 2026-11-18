param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Use Windows to verify the setup executable.' }

$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Plico'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Plico.lnk'
if ((Test-Path -LiteralPath $registryKey) -or (Test-Path -LiteralPath $shortcut)) {
    throw 'Use a clean Windows user profile for the setup round trip.'
}

$stage = [IO.Directory]::CreateTempSubdirectory('plico-setup-test-')
$installed = Join-Path $stage.FullName 'Plico'
$executable = Join-Path $installed 'Plico.exe'
$uninstaller = Join-Path $installed 'Uninstall-Plico.exe'

function Invoke-SetupProcess([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    try {
        if ($process.ExitCode -ne 0) { throw "$([IO.Path]::GetFileName($File)) exited with code $($process.ExitCode)." }
    } finally {
        $process.Dispose()
    }
}

try {
    Invoke-SetupProcess $installerPath @('/S', "/D=$installed")
    $registration = Get-ItemProperty -LiteralPath $registryKey
    if ($registration.InstallLocation -ne $installed -or -not (Test-Path -LiteralPath $shortcut -PathType Leaf)) {
        throw 'Setup did not register the installed path and Start menu shortcut.'
    }
    $uninstallCommand = '"{0}"' -f $uninstaller
    if ($registration.UninstallString -ne $uninstallCommand -or $registration.QuietUninstallString -ne "$uninstallCommand /S") {
        throw 'Setup registered invalid uninstall commands.'
    }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcut).TargetPath -ne $executable) {
        throw 'The Start menu shortcut did not target the installed application.'
    }
    & (Join-Path $PSScriptRoot 'Test-Native.ps1') -Executable $executable -EvidenceDirectory $EvidenceDirectory

    $pdfFolder = (New-Item -ItemType Directory -Force -Path (Join-Path $installed 'Library/PDFs')).FullName
    $libraryFile = Join-Path $pdfFolder 'My document.pdf'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../tests/fixtures/Lease Renewal.pdf') -Destination $libraryFile
    $libraryDigest = (Get-FileHash -LiteralPath $libraryFile).Hash

    Invoke-SetupProcess $uninstaller @('/S')
    if ((Test-Path -LiteralPath $executable) -or (Test-Path -LiteralPath $uninstaller) -or
        (Test-Path -LiteralPath $registryKey) -or (Test-Path -LiteralPath $shortcut)) {
        throw 'Uninstall left an executable, registration, or Start menu shortcut.'
    }
    if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf) -or
        (Get-FileHash -LiteralPath $libraryFile).Hash -ne $libraryDigest) {
        throw 'Uninstall changed a PDF that belonged to the user.'
    }
    $result = 'Setup registered Plico, launched the native journey, and removed the app while preserving an existing PDF.'
    Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'setup-passed.txt') -Value $result
    Write-Output $result
} finally {
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) { Invoke-SetupProcess $uninstaller @('/S') }
    $systemTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ($stage.Parent.FullName -eq $systemTemp -and $stage.Name.StartsWith('plico-setup-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stage.FullName -Recurse -Force
    }
}
