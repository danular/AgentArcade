# Builds Agent Arcade, installs it in %LOCALAPPDATA%\Programs\AgentArcade and starts it: the one script to run after
# a change. It builds first, so a build error leaves the open app alone. Then it closes the open app the way its
# close button does: with agents running, the app asks first, and Cancel there stops this script.
# Run it from this folder:  powershell -ExecutionPolicy Bypass -File install-and-run.ps1
# Not from a pane inside Agent Arcade: the app stops its panes' programs as it closes, this script too.
$ErrorActionPreference = 'Stop'
$exe = Join-Path $env:LOCALAPPDATA 'Programs\AgentArcade\AgentArcade.exe'
$session = (Get-Process -Id $PID).SessionId

# Inside Agent Arcade, this script descends from the app. A parent that started after its child is a stranger that
# got the ID of a parent long gone, so the walk stops there.
$child = Get-CimInstance Win32_Process -Filter "ProcessId = $PID"
for ($hop = 0; $child -and $hop -lt 40; $hop++) {
    $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($child.ParentProcessId)" -ErrorAction SilentlyContinue
    if (-not $parent -or $parent.CreationDate -gt $child.CreationDate) { break }
    if ($parent.ExecutablePath -eq $exe) {
        Write-Host 'This runs inside Agent Arcade, which closes during the install and would stop this script halfway.'
        Write-Host 'Run it from a terminal outside Agent Arcade, or with the Run button in the Claude app.'
        exit 1
    }
    $child = $parent
}

dotnet publish (Join-Path $PSScriptRoot 'AgentArcade.csproj') -c Release -r win-x64 --no-self-contained -o (Join-Path $PSScriptRoot 'publish')
if ($LASTEXITCODE -ne 0) {
    Write-Host 'The build failed, so Agent Arcade was left as it is.'
    exit $LASTEXITCODE
}

if (-not ('AgentArcadeInstall.Window' -as [type])) {
    Add-Type -Namespace AgentArcadeInstall -Name Window -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
'@
}

# Only this Windows session counts. A test copy (test-run.ps1) runs from another folder and can stay open; a copy
# whose path can't be read may be the installed one.
$open = @(Get-Process AgentArcade -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session -and (-not $_.Path -or $_.Path -eq $exe) })
if ($open.Count -gt 0) {
    if (@($open | Where-Object { -not $_.Path -or $_.MainWindowHandle -eq [IntPtr]::Zero }).Count -gt 0) {
        Write-Host "Agent Arcade is open, but this script can't reach its window. Close it yourself, then run this again."
        exit 1
    }
    Write-Host 'Closing Agent Arcade. If agents are running, it asks you first.'
    # WM_SYSCOMMAND with SC_CLOSE, which the title bar's close button sends: the app sees you closing it and asks
    # about running agents. Process.CloseMainWindow's bare WM_CLOSE looks like Task Manager to the app, which then
    # stops every agent without asking.
    foreach ($process in $open) {
        [void][AgentArcadeInstall.Window]::PostMessage($process.MainWindowHandle, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero)
    }
    $asked = Get-Date
    $questionSeen = $false
    $usable = 0
    $told = $false
    while (@($open | Where-Object { -not $_.HasExited }).Count -gt 0) {
        Start-Sleep -Milliseconds 500
        $waiting = @($open | Where-Object { -not $_.HasExited })
        if ($waiting.Count -eq 0) { break }
        $window = $waiting[0].MainWindowHandle
        if (-not [AgentArcadeInstall.Window]::IsWindowEnabled($window)) {
            # The app's question is up and disables the window. No time limit while you decide.
            $questionSeen = $true
            $usable = 0
            if (-not $told -and ((Get-Date) - $asked).TotalSeconds -gt 5) {
                Write-Host "Answer Agent Arcade's question (OK closes it, Cancel keeps it). It may be behind other windows."
                $told = $true
            }
            continue
        }
        # After Cancel the window is usable again and stays so; after OK the app hides it at once.
        if ($questionSeen -and [AgentArcadeInstall.Window]::IsWindowVisible($window)) { $usable++ } else { $usable = 0 }
        if ($usable -ge 6) {
            Write-Host 'Agent Arcade stays open, so nothing was installed.'
            exit 1
        }
        if (-not $questionSeen -and ((Get-Date) - $asked).TotalMinutes -gt 2) {
            Write-Host 'Agent Arcade is still open, so nothing was installed. Close it yourself, then run this again.'
            exit 1
        }
    }
    Start-Sleep -Seconds 1
}

try {
    & (Join-Path $PSScriptRoot 'install.ps1') -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw "install.ps1 stopped with exit code $LASTEXITCODE." }
}
catch {
    Write-Host "The install failed: $($_.Exception.Message)"
    if ($open.Count -gt 0 -and (Test-Path $exe)) {
        Write-Host 'Starting Agent Arcade again as it was.'
        & (Join-Path $env:windir 'explorer.exe') $exe
    }
    exit 1
}

# Through Explorer, so the app gets your normal environment and not this shell's (no test copy settings either).
& (Join-Path $env:windir 'explorer.exe') $exe
Write-Host 'Agent Arcade started.'
