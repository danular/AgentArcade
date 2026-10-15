# Builds the download for a GitHub release: a self-contained win-x64 zip of the committed code (HEAD), so
# uncommitted changes never end up in a release, plus the Android app's signed APK. Prints both SHA-256 values.
#   powershell -ExecutionPolicy Bypass -File release.ps1 [-Apk <signed APK>] [-ExpectApkSha256 <hex>] [-Dotnet <dotnet.exe>]
# The release refuses to build when the bundled .NET runtime is older than Microsoft's latest .NET 10 release: the
# zip carries its own runtime, and Phone remote puts its Kestrel server on the network. Install the newest SDK
# (winget install Microsoft.DotNet.SDK.10, or dotnet-install.ps1) and pass its dotnet.exe with -Dotnet if needed.
param(
    # gradlew assembleRelease in the Android project. Gradle names an unsigned build app-release-unsigned.apk, so this
    # file is only there when keystore.properties signed it.
    [string]$Apk = (Join-Path $env:USERPROFILE 'AndroidStudioProjects\AgentArcade\app\build\outputs\apk\release\app-release.apk'),
    # When given, the APK must have exactly this SHA-256 (to ship the same APK as an earlier release).
    [string]$ExpectApkSha256 = '',
    [string]$Dotnet = $(if ($env:AGENT_ARCADE_DOTNET) { $env:AGENT_ARCADE_DOTNET } else { 'dotnet' })
)
$ErrorActionPreference = 'Stop'
$tarExe = Join-Path $env:SystemRoot 'System32\tar.exe'
[xml]$proj = Get-Content (Join-Path $PSScriptRoot 'AgentArcade.csproj')
$version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$work = Join-Path $env:TEMP "AgentArcade-release-$version"
$src = Join-Path $work 'src'
$out = Join-Path $work 'AgentArcade'
$dist = Join-Path $PSScriptRoot 'publish-release'
# One asset name per platform: the website links to releases/download/<tag>/<name> and the README to
# releases/latest/download/<name>, so both always resolve.
$zip = Join-Path $dist 'AgentArcade-win-x64.zip'
$apkOut = Join-Path $dist 'AgentArcadeRemote.apk'

# Check the APK first, so a missing phone app stops the release before the long build.
if (-not (Test-Path $Apk)) { throw "No signed APK at $Apk. Every release must carry AgentArcadeRemote.apk (the website links to it)." }
$apkHash = (Get-FileHash $Apk -Algorithm SHA256).Hash.ToLower()
if ($ExpectApkSha256 -and $apkHash -ne $ExpectApkSha256.ToLower()) { throw "APK SHA-256 is $apkHash, expected $ExpectApkSha256." }

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $src | Out-Null
$tar = Join-Path $work 'src.tar'
git -C $PSScriptRoot archive --format=tar -o $tar HEAD
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $tarExe -xf $tar -C $src
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $Dotnet publish (Join-Path $src 'AgentArcade.csproj') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($f in 'LICENSE.md', 'LICENSING.md', 'THIRD-PARTY-NOTICES.md') { Copy-Item (Join-Path $src $f) $out }

# Runtime-currency gate: every bundled framework must be at least Microsoft's latest .NET 10 runtime.
$config = Get-Content (Join-Path $out 'AgentArcade.runtimeconfig.json') -Raw | ConvertFrom-Json
$latest = [version](Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -UseBasicParsing).'latest-runtime'
foreach ($fw in $config.runtimeOptions.includedFrameworks) {
    Write-Host ("{0} {1} (latest .NET 10 runtime: {2})" -f $fw.name, $fw.version, $latest)
    if ([version]$fw.version -lt $latest) {
        throw "$($fw.name) $($fw.version) is older than $latest. Build with the newest .NET 10 SDK (see the top of this script)."
    }
}

New-Item -ItemType Directory -Force $dist | Out-Null
foreach ($f in $zip, $apkOut) { if (Test-Path $f) { Remove-Item $f } }
# Flat zip: AgentArcade.exe at the top, so extracting a new version over the old folder updates it in place.
# Windows tar (bsdtar) writes forward-slash paths; Compress-Archive in Windows PowerShell 5.1 writes backslashes,
# which other unzip tools turn into flat file names.
$items = Get-ChildItem $out | ForEach-Object { $_.Name }
& $tarExe -a -cf $zip -C $out @items
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item $Apk $apkOut -Force

$meta = Join-Path (Split-Path $Apk) 'output-metadata.json'
$apkVersion = if (Test-Path $meta) { (Get-Content $meta -Raw | ConvertFrom-Json).elements[0].versionName } else { '?' }
Write-Host ""
Write-Host "Agent Arcade $version"
Write-Host ("{0}  {1:0.0} MB  SHA-256 {2}" -f $zip, ((Get-Item $zip).Length / 1MB), (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower())
Write-Host ("{0}  v{1}  {2:0.0} MB  SHA-256 {3}" -f $apkOut, $apkVersion, ((Get-Item $apkOut).Length / 1MB), $apkHash)
