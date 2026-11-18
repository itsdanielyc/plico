param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$Fixture = (Join-Path $PSScriptRoot '../tests/fixtures/Hardware Store Receipt.png')
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$evidencePath = (New-Item -ItemType Directory -Force -Path $EvidenceDirectory).FullName
$passed = Join-Path $evidencePath 'passed.txt'
$failure = Join-Path $evidencePath 'error.txt'
$progress = Join-Path $evidencePath 'progress.txt'
foreach ($marker in @($passed, $failure, $progress)) {
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
}

$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executablePath
$start.WorkingDirectory = Split-Path -Parent $executablePath
$start.ArgumentList.Add('--verify')
$start.ArgumentList.Add($evidencePath)
$start.ArgumentList.Add((Resolve-Path -LiteralPath $Fixture).Path)
$pollMilliseconds = 1000
$inactivityLimit = [TimeSpan]::FromMinutes(1)
$verificationLimit = [TimeSpan]::FromMinutes(3)
$lastCaptureAt = [TimeSpan]::Zero
$progressLength = 0L
$process = [System.Diagnostics.Process]::Start($start)
$clock = [System.Diagnostics.Stopwatch]::StartNew()
try {
    while (-not $process.WaitForExit($pollMilliseconds)) {
        $elapsed = $clock.Elapsed
        $progressFile = Get-Item -LiteralPath $progress -ErrorAction SilentlyContinue
        if ($null -ne $progressFile -and $progressFile.Length -gt $progressLength) {
            $progressLength = $progressFile.Length
            $lastCaptureAt = $elapsed
        }
        $stalled = ($elapsed - $lastCaptureAt) -ge $inactivityLimit
        if (-not $stalled -and $elapsed -lt $verificationLimit) { continue }
        try { $process.Kill($true) } catch [InvalidOperationException] {
            if ($process.HasExited) { break }
            throw
        }
        $lastCapture = Get-Content -LiteralPath $progress -Tail 1 -ErrorAction SilentlyContinue
        if (-not $lastCapture) { $lastCapture = 'none' }
        if ($stalled) { throw "Native verification made no progress for one minute. Last capture was $lastCapture." }
        throw "Native verification exceeded three minutes. Last capture was $lastCapture."
    }
    if (Test-Path -LiteralPath $failure) { throw (Get-Content -Raw -LiteralPath $failure) }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $passed)) {
        throw "Plico did not complete the native journey (exit $($process.ExitCode))."
    }
    Get-Content -LiteralPath $passed
} finally {
    $process.Dispose()
}
