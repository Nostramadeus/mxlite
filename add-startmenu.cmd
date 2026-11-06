@echo off
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs')+'\MxLite.lnk');$s.TargetPath='%~dp0MxLite.exe';$s.WorkingDirectory='%~dp0';$s.Description='MxLite - lightweight Logitech mouse driver';$s.Save()"
echo MxLite added to the Start Menu (search "MxLite" or find it under All apps).
pause
