@echo off
taskkill /IM MxLite.exe /F >nul 2>&1
powershell -NoProfile -Command "Remove-Item ([Environment]::GetFolderPath('Startup')+'\MxLite.lnk') -ErrorAction SilentlyContinue"
echo MxLite stopped and removed from Startup. (Turn the mouse off/on to fully reset the button.)
