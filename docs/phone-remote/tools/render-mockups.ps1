# Renders mockups\mockups.html to PNG with headless Microsoft Edge: one image per screen (phone size,
# 2x) plus 00-overview.png with all screens.
#   powershell -NoProfile -ExecutionPolicy Bypass -File docs\phone-remote\tools\render-mockups.ps1

$ErrorActionPreference = 'Stop'
$plan = Split-Path -Parent $PSScriptRoot
$html = Join-Path $plan 'mockups\mockups.html'
$out = Join-Path $plan 'mockups'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found.' }
$profile = Join-Path $env:TEMP 'AgentArcade-mockups-edge'
$url = 'file:///' + ($html -replace '\\', '/')

function Shot([string]$file, [string]$query, [int]$width, [int]$height, [int]$scale) {
    $target = Join-Path $out $file
    if (Test-Path $target) { Remove-Item $target -Force }
    $args = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check',
        "--user-data-dir=`"$profile`"", "--force-device-scale-factor=$scale", "--window-size=$width,$height",
        '--virtual-time-budget=4000', "--screenshot=`"$target`"", "`"$url$query`"")
    Start-Process -FilePath $edge -ArgumentList $args -Wait -WindowStyle Hidden
    if (-not (Test-Path $target)) { throw "Edge did not write $file" }
    $file
}

$screens = [ordered]@{ '01-agents.png' = 'agents'; '02-chat-needs-ok.png' = 'chat'; '03-chat-working.png' = 'working';
    '04-start-agent.png' = 'start'; '05-history.png' = 'history'; '06-connect.png' = 'connect'; '07-settings.png' = 'settings'; '08-style-guide.png' = 'style' }
foreach ($name in $screens.Keys) { Shot $name "?s=$($screens[$name])" 412 915 2 }
Shot '00-overview.png' '' 1844 2016 1
