[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must be a numeric version such as 1.0.0.' }
if (-not $IsccPath) {
    $compilerCandidates = @(
        (Join-Path $projectRoot 'artifacts/tools/InnoSetup/ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6.7 or later from https://jrsoftware.org/isdl.php, or pass -IsccPath.'
}
$IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
$buildRoot = Join-Path $projectRoot "artifacts/installer-build/$Version"
$output = Join-Path $projectRoot 'publish/installers'
$downloadRoot = Join-Path $buildRoot 'downloads'
New-Item -ItemType Directory -Force -Path $output, $downloadRoot | Out-Null

# Resolve a fixed Microsoft URL for this build, then pin the downloaded bytes in setup.
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json'
$release = $metadata.releases | Where-Object { $_.'release-version' -eq $metadata.'latest-release' } | Select-Object -First 1
$runtime = $release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } | Select-Object -First 1
if (-not $runtime -or ([uri]$runtime.url).Scheme -ne 'https' -or
    ([uri]$runtime.url).Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com')) {
    throw 'No trusted Microsoft x64 Desktop Runtime download found in the release metadata.'
}
$runtimePath = Join-Path $downloadRoot 'windowsdesktop-runtime.exe'
if (-not (Test-Path -LiteralPath $runtimePath) -or
    (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA512).Hash -ne $runtime.hash) {
    Invoke-WebRequest -Uri $runtime.url -OutFile $runtimePath
}
if ((Get-FileHash -LiteralPath $runtimePath -Algorithm SHA512).Hash -ne $runtime.hash) {
    throw 'The Microsoft runtime SHA-512 hash did not match the release metadata.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $runtimePath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'Microsoft runtime Authenticode signature verification failed.'
}
$runtimeSha256 = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
$pdfLicense = Join-Path $downloadRoot 'PdfPig-LICENSE.txt'
Invoke-WebRequest 'https://raw.githubusercontent.com/UglyToad/PdfPig/master/LICENSE' -OutFile $pdfLicense
$include = Join-Path $buildRoot 'Runtime.iss'
@"
#define RuntimeUrl "$($runtime.url)"
#define RuntimeSha256 "$runtimeSha256"
"@ | Set-Content -LiteralPath $include -Encoding utf8

Push-Location $projectRoot
try {
    foreach ($mode in @('Online', 'Offline')) {
        # A new staging directory prevents stale files from previous publishes entering setup.
        $payload = Join-Path $buildRoot ("payload-$mode-" + [Guid]::NewGuid().ToString('N'))
        $selfContained = if ($mode -eq 'Offline') { 'true' } else { 'false' }
        & dotnet publish 'src/TypingMimic.App/TypingMimic.App.csproj' -c Release -r win-x64 `
            --self-contained $selfContained --artifacts-path (Join-Path $buildRoot "dotnet-$mode") `
            '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:DebugType=None' '-p:DebugSymbols=false' `
            "-p:Version=$Version" -o $payload
        if ($LASTEXITCODE -ne 0) { throw "$mode publish failed." }
        $notices = Join-Path $payload 'licenses'
        New-Item -ItemType Directory -Force -Path $notices | Out-Null
        Copy-Item -LiteralPath $pdfLicense -Destination $notices
        if ($mode -eq 'Offline') {
            $cacheLine = & dotnet nuget locals global-packages --list
            if ($LASTEXITCODE -ne 0) { throw 'Could not locate runtime package licenses.' }
            $packageCache = ($cacheLine -replace '^.*?:\s*', '').Trim()
            $runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'TypingMimic.runtimeconfig.json') -Raw | ConvertFrom-Json
            foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
                $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
                $packageDirectory = Join-Path $packageCache "$packageName/$($framework.version)"
                $licenseFiles = Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }
                if (-not $licenseFiles) { throw "Missing license notices for $packageName." }
                foreach ($notice in $licenseFiles) {
                    Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices "$packageName-$($notice.Name)")
                }
            }
        }
        Write-Output "Compressing $mode setup..."
        & $IsccPath '/Q' "/DMode=$mode" "/DAppVersion=$Version" "/DPayloadDir=$payload" `
            "/DInstallerOutput=$output" "/DRuntimeInclude=$include" (Join-Path $PSScriptRoot 'TypingMimic.iss')
        if ($LASTEXITCODE -ne 0) { throw "$mode installer compilation failed." }
    }
} finally { Pop-Location }

$packages = foreach ($mode in @('Online', 'Offline')) {
    $file = Get-Item -LiteralPath (Join-Path $output "TypingMimic-$Version-$mode-Setup.exe")
    [pscustomobject]@{
        File = $file.Name; Bytes = $file.Length; MiB = [Math]::Round($file.Length / 1MB, 2)
        SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}
$packages | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'checksums.json') -Encoding utf8
@"
TypingMimic $Version - choose ONE installer

Online: smallest setup. Internet and administrator approval are needed only if
the Microsoft .NET 8 Desktop Runtime (x64) is not already installed.

Offline: larger setup, includes a private .NET runtime. No internet or separate
runtime installation needed. Installs for the current user without administrator
approval at the default location.

Both contain the same app, Start menu shortcut, optional desktop shortcut, and
uninstaller. Close TypingMimic before upgrading. The setup files are unsigned.

Only use automated typing where permitted.
"@ | Set-Content -LiteralPath (Join-Path $output 'CHOOSE-YOUR-INSTALLER.txt') -Encoding utf8
$packages | Format-Table File, MiB, SHA256 -AutoSize
