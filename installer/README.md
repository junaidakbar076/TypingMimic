# Windows setup packages

`Build-Installers.ps1` produces two compressed Inno Setup executables in `publish/installers`, plus a choice guide and SHA-256 manifest. Share either setup file on its own, or both so recipients can choose.

| Package | Prerequisite behavior |
| --- | --- |
| Online | Includes the framework-dependent app. Detects stable .NET 8 Desktop and Core runtimes for x64 in both registry views. If missing, downloads the Microsoft Desktop Runtime installer, verifies its pinned SHA-256, and requests elevation for runtime installation. |
| Offline | Includes a self-contained app and its private runtime. No downloads or system runtime installation. |

Both target Windows 10/11 x64-compatible systems and install per user under `%LOCALAPPDATA%\Programs\TypingMimic`. They use the same app identity and Start menu shortcut. Each variant has a separate payload subfolder so changing variants cannot mix framework-dependent and self-contained runtime files. If a user installs both variants successively, the previous payload remains until uninstall; the shortcut points to the most recently installed variant. Uninstall removes files installed by either variant but leaves the shared Microsoft runtime installed by Online setup for other applications.

The installer offers an optional desktop shortcut and optional app launch. The app should be closed before upgrading. No typing starts during installation. Settings remain session-only as in the portable app.

## Rebuild

Use PowerShell on Windows with the .NET SDK and [Inno Setup](https://jrsoftware.org/isdl.php) 6.7 or later:

```powershell
./installer/Build-Installers.ps1
# Or specify the compiler and version:
./installer/Build-Installers.ps1 -Version 1.0.1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The script downloads current .NET 8 release metadata and the x64 Desktop Runtime from Microsoft, checks the release SHA-512 and Microsoft Authenticode signature, and embeds a fixed URL and SHA-256 into the Online setup. It publishes fresh payload folders without single-file compression; Inno Setup applies solid LZMA2 compression to the payload. The Offline publish includes its own runtime. Build staging is kept under `artifacts/installer-build` for inspection. Rebuild periodically to incorporate supported runtime security updates.

The setup files are not code signed. Publishers can add their own signing certificate and Inno Setup signing configuration before wider distribution. Do not tell recipients to disable Windows security protections.

## Validation

Compile both installers; check their generated checksums and sizes. In a disposable Windows VM, test Online setup with and without .NET 8 Desktop Runtime, an interrupted download, declined elevation, and a required restart. Test Offline setup with networking disabled and no installed .NET. Test install, app launch, reinstall, switching variants, shortcuts, and uninstall. `PrepareToInstall` handles download/runtime errors before application files are copied, so users can retry or choose Offline setup.

The build machine has .NET installed. Any smoke checks on that machine cannot establish the prerequisite-installation path on a clean machine.

Verified on September 17, 2026: both final setup packages compiled successfully (Online 3.03 MiB; Offline 48.76 MiB). Both installed, launched a WPF window, and shut down normally. Online correctly skipped the existing runtime. Switching from Offline to Online succeeded, and uninstall removed both payloads and the application registration. No typing was performed. Missing-runtime, download-failure, elevation-denial, and restart paths remain untested on a clean VM.

To smoke-test the generated packages on a development machine with .NET already installed:

```powershell
./installer/Test-Installers.ps1
```

This installs Offline then Online into a fresh workspace test directory, launches and closes each installed app without typing, verifies that Online skips the existing runtime, and uninstalls both. It temporarily writes the normal per-user uninstall registration, suppresses shortcuts, and refuses to run if this application identity is already installed. Logs remain under `artifacts/installer-smoke`. It does not replace or close an existing portable app instance.
