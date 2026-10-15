# Builds the download for a GitHub release: a self-contained win-x64 zip of the committed code (HEAD), so
# uncommitted changes never end up in a release. Prints the zip's path and SHA-256.
#   powershell -ExecutionPolicy Bypass -File release.ps1
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
Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Write-Host $zip
Write-Host "SHA-256 $hash"
