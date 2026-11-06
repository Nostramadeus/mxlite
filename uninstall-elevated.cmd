@echo off
rem MxLite - removes the administrator-start task created by install-elevated.cmd
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall-elevated.ps1"
