@echo off
setlocal
set "compiler=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if /i "%~1"=="/build" goto build
if exist "%~dp0build\ChargeStrip.exe" goto launch
:build
if not exist "%~dp0build" mkdir "%~dp0build"
"%compiler%" /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0build\ChargeStrip.exe" "%~dp0ChargeStrip.cs" "%~dp0Batteries.cs" "%~dp0Settings.cs"
if errorlevel 1 (
    pause
    exit /b 1
)
:launch
if /i "%~1"=="/settings" (
    start "" "%~dp0build\ChargeStrip.exe" --settings
) else (
    start "" "%~dp0build\ChargeStrip.exe"
)
