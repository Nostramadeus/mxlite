@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0MxLite.exe" "%~dp0MxLite.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built MxLite.exe
