# MxLite - removes the "start with Windows as administrator" Task Scheduler
# task (the undo of install-elevated.cmd). The currently running MxLite is
# left alone; it just won't auto-start elevated after your next logon.
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath)
    )
    exit
}

try {
    Unregister-ScheduledTask -TaskName 'MxLite' -Confirm:$false
    Write-Host ''
    Write-Host 'Done. The MxLite administrator-start task is removed.'
    Write-Host 'Want the NORMAL start-with-Windows instead? Open MxLite Settings and'
    Write-Host 'tick "Start MxLite with Windows".'
} catch {
    Write-Host "FAILED: $($_.Exception.Message)"
}
Write-Host ''
Write-Host 'Press Enter to close...'
Read-Host | Out-Null
