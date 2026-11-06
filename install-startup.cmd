@echo off
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup')+'\MxLite.lnk');$s.TargetPath='%~dp0MxLite.exe';$s.WorkingDirectory='%~dp0';$s.Save()"
start "" "%~dp0MxLite.exe"
echo MxLite installed to Startup and started.
