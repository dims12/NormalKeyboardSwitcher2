[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [string]$PreviousInstaller,
    [string]$ExpectedVersion = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$Installer = (Resolve-Path -LiteralPath $Installer).Path
if ($PreviousInstaller) { $PreviousInstaller = (Resolve-Path -LiteralPath $PreviousInstaller).Path }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D17CBEB9-1812-4B01-9DD3-E4B4965CC225}_is1'
if (Test-Path $uninstallKey) { throw 'Run installer tests in a user profile where the product is not installed.' }
$running = $null
if ([Threading.Mutex]::TryOpenExisting('Local\NormalKeyboardSwitcher2', [ref]$running)) {
    $running.Dispose()
    throw 'Exit NormalKeyboardSwitcher2 before running installer tests.'
}
$testRoot = Join-Path $repoRoot ('artifacts\installer-smoke\' + [Guid]::NewGuid().ToString('N'))
$installDir = Join-Path $testRoot 'installed'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$group = 'NormalKeyboardSwitcher2'
$groupPath = Join-Path ([Environment]::GetFolderPath('Programs')) $group
if (Test-Path -LiteralPath $groupPath) { throw 'Remove existing product Start menu shortcuts before running installer tests in this profile.' }
$app = Join-Path $installDir 'NormalKeyboardSwitcher2.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
$startupName = 'NormalKeyboardSwitcher2'
$oldStartup = $runKey.GetValue($startupName)
$oldStartupKind = if ($null -ne $oldStartup) { $runKey.GetValueKind($startupName) } else { $null }
$settingsPath = Join-Path $env:LOCALAPPDATA 'NormalKeyboardSwitcher2\settings.json'
$oldSettingsHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
$common = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')

function Run-Process([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "Process did not finish: $File" }
    return $process.ExitCode
}
function Install-Package([string]$File, [string]$LogName) {
    $code = Run-Process $File ($common + @("/DIR=`"$installDir`"", "/LOG=`"$testRoot\$LogName.log`""))
    if ($code -ne 0) { throw "Installation failed: $code" }
    if (-not (Test-Path -LiteralPath $app)) { throw 'Installed executable is missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installDir 'coreclr.dll'))) { throw 'Bundled runtime is missing.' }
}
function Uninstall-Package([string]$LogName) {
    $code = Run-Process $uninstaller ($common + @("/LOG=`"$testRoot\$LogName.log`""))
    if ($code -ne 0) { throw "Uninstallation failed: $code" }
    # Inno's helper can delete the uninstaller after its main process exits.
    # Wait for that cleanup before reinstalling into the same directory.
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Test-Path -LiteralPath $installDir) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (Test-Path -LiteralPath $installDir) { throw 'Uninstaller cleanup did not finish.' }
    if ((Test-Path -LiteralPath $app) -or (Test-Path $uninstallKey)) { throw 'Uninstallation left application registration or executable behind.' }
}

try {
    # The real setup must refuse to replace a running application's files.
    $guard = [Threading.Mutex]::new($true, 'Local\NormalKeyboardSwitcher2')
    try {
        $code = Run-Process $Installer ($common + @("/DIR=`"$installDir`"", "/LOG=`"$testRoot\blocked.log`""))
        if ($code -eq 0 -or (Test-Path -LiteralPath $app)) { throw 'Running-app protection failed.' }
    }
    finally { $guard.ReleaseMutex(); $guard.Dispose() }

    Install-Package $(if ($PreviousInstaller) { $PreviousInstaller } else { $Installer }) 'fresh-install'
    Install-Package $Installer 'upgrade'
    if ((Get-ItemProperty $uninstallKey).DisplayVersion -ne $ExpectedVersion) { throw 'Upgrade did not register the expected version.' }
    if ($runKey.GetValue($startupName) -ne $oldStartup) { throw 'Installation changed the startup preference.' }
    $shortcut = Join-Path $groupPath 'NormalKeyboardSwitcher2.lnk'
    if (-not (Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut is missing.' }

    # Render-only mode exercises the installed WPF application without enabling hooks.
    $preview = Join-Path $testRoot 'installed-preview.png'
    if ((Run-Process $app @('--render-contrast-preview', "`"$preview`"")) -ne 0 -or -not (Test-Path -LiteralPath $preview)) {
        throw 'Installed application render check failed.'
    }
    $guard = [Threading.Mutex]::new($true, 'Local\NormalKeyboardSwitcher2')
    try {
        if ((Run-Process $app @('--uninstall-cleanup')) -ne 2) { throw 'Cleanup did not reject a running app.' }
        $code = Run-Process $uninstaller ($common + @("/LOG=`"$testRoot\blocked-uninstall.log`""))
        if ($code -eq 0 -or -not (Test-Path -LiteralPath $app)) { throw 'Uninstaller ignored a running app.' }
    }
    finally { $guard.ReleaseMutex(); $guard.Dispose() }

    # A startup entry pointing at a different copy must survive removal.
    $foreignStartup = '"' + (Join-Path $testRoot 'other-copy\NormalKeyboardSwitcher2.exe') + '" --startup'
    $runKey.SetValue($startupName, $foreignStartup)
    Uninstall-Package 'uninstall-foreign-startup'
    if ($runKey.GetValue($startupName) -ne $foreignStartup) { throw 'Uninstaller removed another copy''s startup entry.' }
    if (Test-Path -LiteralPath $shortcut) { throw 'Uninstaller left the Start menu shortcut behind.' }

    Install-Package $Installer 'reinstall'
    $runKey.SetValue($startupName, '"' + $app + '" --startup')
    Uninstall-Package 'uninstall-owned-startup'
    if ($null -ne $runKey.GetValue($startupName)) { throw 'Uninstaller left its own startup entry behind.' }
    $newSettingsHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
    if ($newSettingsHash -ne $oldSettingsHash) { throw 'User settings changed during installer testing.' }
    Write-Output "Installer smoke checks passed. Logs: $testRoot"
}
finally {
    try {
        if (Test-Path -LiteralPath $uninstaller) {
            $null = Run-Process $uninstaller ($common + @("/LOG=`"$testRoot\cleanup.log`""))
        }
    }
    finally {
        if ($null -eq $oldStartup) { $runKey.DeleteValue($startupName, $false) }
        else { $runKey.SetValue($startupName, $oldStartup, $oldStartupKind) }
        $runKey.Dispose()
    }
}
