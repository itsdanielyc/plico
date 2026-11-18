param(
    [Parameter(Mandatory)][string]$Compiler,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$compilerPath = (Resolve-Path -LiteralPath $Compiler).Path
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifest = New-TemporaryFile

try {
    $removeFiles = Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
        $path = [IO.Path]::GetRelativePath($payload, $_.FullName)
        'Delete "$INSTDIR\{0}"' -f $path.Replace('$', '$$')
    }
    $removeEmptyDirectories = Get-ChildItem -LiteralPath $payload -Recurse -Directory |
        Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
            $path = [IO.Path]::GetRelativePath($payload, $_.FullName)
            'RMDir "$INSTDIR\{0}"' -f $path.Replace('$', '$$')
        }
    [IO.File]::WriteAllLines($manifest.FullName, [string[]](@($removeFiles) + @($removeEmptyDirectories)), [Text.UTF8Encoding]::new($true))
    $recipe = Join-Path $PSScriptRoot 'windows/Plico.nsi'
    & $compilerPath /V3 /WX "/DVERSION=$Version" "/DPAYLOAD=$payload" "/DOUTPUT=$output" "/DUNINSTALL_MANIFEST=$($manifest.FullName)" $recipe
} finally {
    Remove-Item -LiteralPath $manifest.FullName -Force
}
