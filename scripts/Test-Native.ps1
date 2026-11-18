param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$evidencePath = (New-Item -ItemType Directory -Force -Path $EvidenceDirectory).FullName
$passed = Join-Path $evidencePath 'passed.txt'
$failure = Join-Path $evidencePath 'error.txt'
foreach ($marker in @($passed, $failure)) {
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
}

$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executablePath
$start.WorkingDirectory = Split-Path -Parent $executablePath
$start.ArgumentList.Add('--verify')
$start.ArgumentList.Add($evidencePath)
$process = [System.Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(60000)) {
        $process.Kill($true)
        throw 'The native window did not finish verification within one minute.'
    }
    if (Test-Path -LiteralPath $failure) { throw (Get-Content -Raw -LiteralPath $failure) }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $passed)) {
        throw "Plico did not complete the native journey (exit $($process.ExitCode))."
    }
    Get-Content -LiteralPath $passed
} finally {
    $process.Dispose()
}
