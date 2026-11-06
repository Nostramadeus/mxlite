# MxLite - "start with Windows as administrator" installer (Task Scheduler).
#
# WHY: Windows has a security rule (UIPI): keystrokes simulated by a normal
# program are silently dropped while a window of an "as administrator" app
# has focus. If you run any elevated apps (VPN/proxy clients, some game
# tools...), MxLite's desktop switching goes dead over them unless MxLite
# itself runs as administrator too.
#
# This creates a Task Scheduler task that starts MxLite elevated at logon:
# ONE UAC prompt during this install, NO prompt at boot, ever.
# Undo: uninstall-elevated.cmd
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath)
    )
    exit
}

try {
    $exe = Join-Path $PSScriptRoot 'MxLite.exe'
    if (-not (Test-Path $exe)) { throw 'MxLite.exe not found next to this script.' }
    $me = "$env:USERDOMAIN\$env:USERNAME"

    # stop any running copy, so we don't end up with two
    Get-Process MxLite -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    # remove the normal Startup-folder shortcut (would double-start MxLite)
    $lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'MxLite.lnk'
    if (Test-Path $lnk) { Remove-Item $lnk -Force }

    $action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $PSScriptRoot
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $me
    $principal = New-ScheduledTaskPrincipal -UserId $me -LogonType Interactive -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
    $settings.ExecutionTimeLimit = 'PT0S'   # never auto-kill (default would end it after 3 days)

    Register-ScheduledTask -TaskName 'MxLite' -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName 'MxLite'

    Write-Host ''
    Write-Host 'Done. MxLite now starts as administrator with Windows (no UAC prompt at boot).'
    Write-Host 'Note: leave "Start MxLite with Windows" UNticked in MxLite Settings -'
    Write-Host 'this replaces it. (Starting both = "already running" popup at logon.)'
} catch {
    Write-Host "FAILED: $($_.Exception.Message)"
}
Write-Host ''
Write-Host 'Press Enter to close...'
Read-Host | Out-Null
