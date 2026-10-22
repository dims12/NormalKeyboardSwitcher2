[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',
    [string]$InnoCompiler,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\installer' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $InnoCompiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $InnoCompiler = $command.Source }
    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )) {
        if (-not $InnoCompiler -and (Test-Path -LiteralPath $candidate)) { $InnoCompiler = $candidate }
    }
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Install Inno Setup 6.7.3 or newer, or pass -InnoCompiler with the path to ISCC.exe.'
}
$InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path

# A fresh directory prevents obsolete files from a previous publish entering setup.
$stage = Join-Path $repoRoot ('artifacts\installer-stage\' + [Guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $stage 'app'
New-Item -ItemType Directory -Path $publishDir, $OutputDirectory -Force | Out-Null
Push-Location $repoRoot
try {
    & dotnet build NormalKeyboardSwitcher2.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet run --project NormalKeyboardSwitcher2.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    & dotnet publish NormalKeyboardSwitcher2 -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$Version" -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    foreach ($file in @('NormalKeyboardSwitcher2.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $file))) { throw "Missing package file: $file" }
    }
    & $InnoCompiler "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$OutputDirectory" `
        (Join-Path $repoRoot 'packaging\windows\NormalKeyboardSwitcher2.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $OutputDirectory "NormalKeyboardSwitcher2-$Version-win-x64-setup.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output is missing.' }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$installer.sha256", "$hash  $([IO.Path]::GetFileName($installer))`n")
    Write-Output "Installer: $installer"
    Write-Output "SHA256: $hash"
}
finally { Pop-Location }
