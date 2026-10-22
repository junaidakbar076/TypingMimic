[CmdletBinding()]
param([string]$Version = '1.0.0')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{E6FE3AD9-7637-4A41-98C2-99E66DAC41D8}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'TypingMimic is already installed for this user. Use a disposable account or VM for installer tests.' }
# This smoke test must not install a system runtime on the developer machine.
$desktopRuntime = Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App' -ErrorAction SilentlyContinue
$desktopRuntimeNative = Get-ItemProperty 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App' -ErrorAction SilentlyContinue
if (-not (@($desktopRuntime.PSObject.Properties.Name) + @($desktopRuntimeNative.PSObject.Properties.Name) | Where-Object { $_ -match '^8\.0\.\d+$' })) {
    throw 'The smoke test requires an already-installed .NET 8 x64 Desktop Runtime; test the prerequisite path separately in a VM.'
}
$testRoot = Join-Path $projectRoot ('artifacts/installer-smoke/' + [Guid]::NewGuid().ToString('N'))
$appRoot = Join-Path $testRoot 'installed'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$results = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($mode in @('Offline', 'Online')) {
        $setup = Join-Path $projectRoot "publish/installers/TypingMimic-$Version-$mode-Setup.exe"
        $log = Join-Path $testRoot "$mode-install.log"
        $arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /NOCLOSEAPPLICATIONS /DIR=`"$appRoot`" /LOG=`"$log`""
        $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "$mode installer failed: $($process.ExitCode). See $log" }
        if ($mode -eq 'Online' -and -not (Select-String -LiteralPath $log -SimpleMatch 'no download needed' -Quiet)) {
            throw 'Online setup did not confirm that it skipped the runtime download.'
        }
        $payload = Join-Path $appRoot $mode
        if ($mode -eq 'Offline' -and -not (Test-Path -LiteralPath (Join-Path $payload 'coreclr.dll'))) {
            throw 'Offline installation is missing its private runtime.'
        }
        $app = Start-Process -FilePath (Join-Path $payload 'TypingMimic.exe') -WindowStyle Hidden -PassThru
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            do {
                Start-Sleep -Milliseconds 200
                $app.Refresh()
            } while (-not $app.HasExited -and $app.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
            if ($app.HasExited -or $app.MainWindowHandle -eq 0) { throw "$mode installed app did not create a window." }
            if (-not $app.WaitForInputIdle(10000)) { throw "$mode installed app did not become idle." }
            if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(10000) -or $app.ExitCode -ne 0) {
                throw "$mode installed app did not close cleanly."
            }
            $results.Add("$mode setup: installed, opened WPF window, and exited cleanly.")
        } finally {
            if (-not $app.HasExited) { $app.Kill() }
            $app.Dispose()
        }
    }
} finally {
    $uninstaller = Join-Path $appRoot 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $uninstallLog = Join-Path $testRoot 'uninstall.log'
        $cleanup = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$uninstallLog`"" -WindowStyle Hidden -Wait -PassThru
        if ($cleanup.ExitCode -ne 0) { throw "Smoke-test uninstall failed: $($cleanup.ExitCode). See $uninstallLog" }
    }
}
if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall left its application registration behind.' }
foreach ($mode in @('Online', 'Offline')) {
    if (Test-Path -LiteralPath (Join-Path $appRoot "$mode/TypingMimic.exe")) { throw 'Uninstall left an application executable behind.' }
}
$results.Add('Uninstall removed both variants and the application registration.')
$results.Add('Missing-runtime/download-failure paths require a clean Windows VM; they were not exercised here.')
$results | Set-Content -LiteralPath (Join-Path $testRoot 'results.txt') -Encoding utf8
$results
Write-Output "Logs: $testRoot"
