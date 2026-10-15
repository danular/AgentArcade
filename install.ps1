# Builds Agent Arcade and installs it in %LOCALAPPDATA%\Programs\AgentArcade.
# Run it from this folder:  powershell -ExecutionPolicy Bypass -File install.ps1
# -SkipBuild installs what the last build left in .\publish (install-and-run.ps1 builds before it closes the app).
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\AgentArcade'
$publish = Join-Path $PSScriptRoot 'publish'
$exe = Join-Path $target 'AgentArcade.exe'

# Only the installed copy locks these files; a test copy (test-run.ps1) runs from another folder. A copy whose path
# can't be read (one running as administrator, say) may be the installed one, so it counts too; another Windows
# user's copy doesn't.
$session = (Get-Process -Id $PID).SessionId
if (Get-Process AgentArcade -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session -and (-not $_.Path -or $_.Path -eq $exe) }) {
    Write-Host 'Close Agent Arcade first (it locks its own files), then run this again.'
    exit 1
}

if (-not $SkipBuild) {
    dotnet publish (Join-Path $PSScriptRoot 'AgentArcade.csproj') -c Release -r win-x64 --no-self-contained -o $publish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not (Test-Path (Join-Path $publish 'AgentArcade.exe'))) {
    Write-Host "Nothing to install: $publish has no build. Run this without -SkipBuild."
    exit 1
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $publish '*') $target -Recurse -Force

$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Agent Arcade.lnk'
if (-not (Test-Path $shortcut)) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $target
    $link.Save()
}

Write-Host "Installed in $target"
