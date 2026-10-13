@echo off
setlocal
title PromptForge uninstall
set "DEST=%LOCALAPPDATA%\Programs\PromptForge"
cd /d "%TEMP%"

echo Removing PromptForge...
taskkill /IM PromptForge.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PromptForge /f >nul 2>&1
del /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\PromptForge.lnk" >nul 2>&1

choice /C YN /M "Also delete your PromptForge settings, saved API keys and logs"
if errorlevel 2 goto keepdata
rmdir /S /Q "%APPDATA%\PromptForge" >nul 2>&1
echo Settings, keys and logs deleted.
:keepdata

echo PromptForge removed.
pause
rem This script may live inside the folder it deletes, so remove the folder from a separate process.
start "" /min cmd /c "timeout /t 2 /nobreak >nul & rmdir /S /Q "%DEST%""
exit /b 0
