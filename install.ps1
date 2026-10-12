# Builds Agent Arcade and installs it in %LOCALAPPDATA%\Programs\AgentArcade.
# Run it from this folder:  powershell -ExecutionPolicy Bypass -File install.ps1
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\AgentArcade'
$publish = Join-Path $PSScriptRoot 'publish'

if (Get-Process AgentArcade -ErrorAction SilentlyContinue) {
    Write-Host 'Close Agent Arcade first (it locks its own files), then run this again.'
    exit 1
}

dotnet publish (Join-Path $PSScriptRoot 'AgentArcade.csproj') -c Release -r win-x64 --no-self-contained -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $publish '*') $target -Recurse -Force

$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Agent Arcade.lnk'
if (-not (Test-Path $shortcut)) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath = Join-Path $target 'AgentArcade.exe'
    $link.WorkingDirectory = $target
    $link.Save()
}

Write-Host "Installed in $target"
