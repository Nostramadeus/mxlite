@echo off
rem MxLite - start with Windows AS ADMINISTRATOR (Task Scheduler, no UAC at boot).
rem Do this if you run some apps "as administrator" - otherwise Windows blocks
rem MxLite's simulated keys while those apps have focus (desktop switching
rem silently does nothing over them). One UAC click now, none at boot.
rem Undo: uninstall-elevated.cmd
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-elevated.ps1"
