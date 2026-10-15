# Builds the download for a GitHub release: a self-contained win-x64 zip of the committed code (HEAD), so
# uncommitted changes never end up in a release. Prints the zip's path and SHA-256. It also copies the Android app's
# release APK next to it, so every release carries the phone app too.
#   powershell -ExecutionPolicy Bypass -File release.ps1 [-Apk <path to a signed APK>]
param(
    # gradlew assembleRelease in the Android project. Gradle names an unsigned build app-release-unsigned.apk, so this
    # file is only there when keystore.properties signed it.
    [string]$Apk = (Join-Path $env:USERPROFILE 'AndroidStudioProjects\AgentArcade\app\build\outputs\apk\release\app-release.apk')
)
$ErrorActionPreference = 'Stop'
[xml]$proj = Get-Content (Join-Path $PSScriptRoot 'AgentArcade.csproj')
$version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$work = Join-Path $env:TEMP "AgentArcade-release-$version"
$src = Join-Path $work 'src'
$name = "AgentArcade-$version-win-x64"
$out = Join-Path $work $name
$zip = Join-Path $PSScriptRoot "publish-release\$name.zip"

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $src | Out-Null
$tar = Join-Path $work 'src.tar'
git -C $PSScriptRoot archive --format=tar -o $tar HEAD
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $env:SystemRoot 'System32\tar.exe') -xf $tar -C $src
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $src 'AgentArcade.csproj') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($f in 'LICENSE.md', 'LICENSING.md', 'THIRD-PARTY-NOTICES.md') { Copy-Item (Join-Path $src $f) $out }

New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip }
# Windows tar (bsdtar) writes forward-slash paths; Compress-Archive in Windows PowerShell 5.1 writes backslashes,
# which other unzip tools turn into flat file names.
& (Join-Path $env:SystemRoot 'System32\tar.exe') -a -cf $zip -C $work $name
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# Upload both: the website and README link to releases/latest/download/AgentArcade-win-x64.zip, which always
# serves the newest release.
Copy-Item $zip (Join-Path (Split-Path $zip) 'AgentArcade-win-x64.zip') -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Write-Host $zip
Write-Host "SHA-256 $hash"

# The website links to releases/latest/download/AgentArcadeRemote.apk, so every release needs the APK under that name.
if (Test-Path $Apk) {
    $meta = Join-Path (Split-Path $Apk) 'output-metadata.json'
    $apkVersion = if (Test-Path $meta) { (Get-Content $meta -Raw | ConvertFrom-Json).elements[0].versionName } else { $null }
    $apkName = if ($apkVersion) { "AgentArcadeRemote-$apkVersion.apk" } else { 'AgentArcadeRemote.apk' }
    $apkOut = Join-Path (Split-Path $zip) $apkName
    Copy-Item $Apk $apkOut -Force
    Copy-Item $Apk (Join-Path (Split-Path $zip) 'AgentArcadeRemote.apk') -Force
    $apkFile = Get-Item $apkOut
    Write-Host $apkOut
    Write-Host ("APK v{0}, built {1:d MMM yyyy HH:mm}, {2:0.0} MB" -f $apkVersion, $apkFile.LastWriteTime, ($apkFile.Length / 1MB))
    Write-Host "APK SHA-256 $((Get-FileHash $apkOut -Algorithm SHA256).Hash.ToLower())"
} else {
    Write-Warning "No signed APK at $Apk. Without it this release has no AgentArcadeRemote.apk and the website's Android download fails."
}
