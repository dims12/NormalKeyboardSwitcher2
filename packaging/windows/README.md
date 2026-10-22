# Windows installer

The installer packages a self-contained Windows x64 build, including the .NET 10 Windows Desktop runtime. It supports Windows 10 22H2 and later on x64. ARM64 is not currently packaged. Installation is per user, defaults to `%LOCALAPPDATA%\Programs\NormalKeyboardSwitcher2`, and requires no administrator privileges.

## Build

Requirements: Windows x64, the .NET 10 SDK selected by `global.json`, and [Inno Setup](https://jrsoftware.org/isinfo.php) 6.7.3 or newer.

From the repository root:

```powershell
# Optional: download, verify, and install the pinned compiler for the current user.
.\scripts\Install-InnoSetup.ps1
.\scripts\Build-WindowsInstaller.ps1 -InnoCompiler .\artifacts\tools\InnoSetup\ISCC.exe
```

If Inno Setup is already available on PATH or in a standard installation directory, omit `-InnoCompiler`. The prerequisite script verifies the pinned download's SHA-256 hash and publisher signature before running it. It installs the compiler under `artifacts/tools/InnoSetup` and registers it with Windows; remove it through Installed apps when no longer needed.

The build script runs the regression checks, publishes into a fresh directory, and produces:

- `artifacts/installer/NormalKeyboardSwitcher2-0.1.0-win-x64-setup.exe`
- The adjacent `.exe.sha256` checksum file.

Pass `-Version 0.2.0` to change the application and installer version, or `-OutputDirectory` to change the output location. Update the workflow's expected filename/version when changing the default version. Generated binaries are excluded from Git; the [Windows installer workflow](../../.github/workflows/windows-installer.yml) uploads the installer and checksum after validation.

The package is unsigned. Public signed releases require an Authenticode signing certificate and a signing step for both the application and installer. This package does not enable UIAccess; elevated applications still require **Restart as administrator**.

## Install, upgrade, and remove

Run setup as the intended user. It adds Start menu shortcuts and an Installed apps entry. Startup remains controlled by the application's **Start with Windows** checkbox. Exit the app from its tray menu before installing an update or removing it; setup refuses to replace files while the app's instance mutex exists.

Updates use the same installation identifier and directory. Preferences in `%LOCALAPPDATA%\NormalKeyboardSwitcher2` are preserved during upgrades and removal. Before deleting the program, the uninstaller restores any outstanding Windows shortcut recovery journal and removes the startup entry only if it points to this installed copy. A failed cleanup aborts removal before files are deleted. Cancelling the uninstall confirmation does not run cleanup.

Unattended installation and removal:

```powershell
.\NormalKeyboardSwitcher2-0.1.0-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
& "$env:LOCALAPPDATA\Programs\NormalKeyboardSwitcher2\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Silent setup does not launch the app. See Inno Setup's [setup](https://jrsoftware.org/ishelp/topic_setupcmdline.htm) and [uninstaller](https://jrsoftware.org/ishelp/topic_uninstcmdline.htm) command-line documentation for logging and destination options.

## Validate

Use a test user profile with no installed copy, and exit any running copy first:

```powershell
.\scripts\Test-WindowsInstaller.ps1 -Installer .\artifacts\installer\NormalKeyboardSwitcher2-0.1.0-win-x64-setup.exe
```

The smoke test checks fresh installation, reinstall/upgrade, installed WPF rendering, runtime files, Start menu shortcuts, running-app protection, owned versus unrelated startup entries, preference retention, and removal. It temporarily changes the app's startup registration and restores its previous value in a `finally` block. Logs and the rendered preview are kept under `artifacts/installer-smoke`.

To test an earlier release upgrading to the current package, supply `-PreviousInstaller path\to\earlier-setup.exe`. The default exercises an in-place reinstall of the same version. These packaging checks complement the application acceptance checklist in the [main README](../../README.md); they do not establish compatibility with every editor or monitor arrangement.
