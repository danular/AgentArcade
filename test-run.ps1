# Builds Agent Arcade into a temp folder and starts it as a test copy. Nothing is installed,
# and the installed app is left alone: the test copy keeps its own settings, agents and paired phones
# in %TEMP%\AgentArcade-test-home, and its phone remote listens on this PC only (no firewall prompt).
# Run it from this folder:  powershell -ExecutionPolicy Bypass -File test-run.ps1
$ErrorActionPreference = 'Stop'
$build = Join-Path $env:TEMP 'AgentArcade-test-build'
# Not $home: PowerShell's $HOME is read-only, and variable names ignore case.
$dataFolder = Join-Path $env:TEMP 'AgentArcade-test-home'

# A test copy that is still open locks its files, and a new start would only bring that old copy to the front.
if (Get-Process AgentArcade -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $build 'AgentArcade.exe') }) {
    Write-Host 'The test copy is still open. Close it first, then run this again.'
    exit 1
}

dotnet publish (Join-Path $PSScriptRoot 'AgentArcade.csproj') -c Release -r win-x64 --no-self-contained -o $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force $dataFolder | Out-Null
$env:AGENT_ARCADE_HOME = $dataFolder
$env:AGENT_ARCADE_REMOTE_LOOPBACK = '1'
Start-Process -FilePath (Join-Path $build 'AgentArcade.exe') -WorkingDirectory $build
Write-Host "Started the test copy from $build (data in $dataFolder)."
