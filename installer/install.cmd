@echo off
setlocal
title PromptForge setup
set "SRC=%~dp0PromptForge.exe"
set "DEST=%LOCALAPPDATA%\Programs\PromptForge"

if not exist "%SRC%" (
  echo PromptForge.exe was not found next to install.cmd.
  pause
  exit /b 1
)

echo Installing PromptForge for %USERNAME% (no admin rights needed)...
taskkill /IM PromptForge.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%SRC%" "%DEST%\PromptForge.exe" >nul
if errorlevel 1 (
  echo Could not copy PromptForge.exe to %DEST%.
  pause
  exit /b 1
)
copy /Y "%~dp0uninstall.cmd" "%DEST%\uninstall.cmd" >nul 2>&1

rem Remove the "downloaded from the internet" mark so Windows doesn't block it, and add a Start menu shortcut.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Unblock-File -Path '%DEST%\PromptForge.exe' -ErrorAction SilentlyContinue;" ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs') + '\PromptForge.lnk');" ^
  "$s.TargetPath = '%DEST%\PromptForge.exe'; $s.WorkingDirectory = '%DEST%'; $s.Description = 'Rewrite rough prompts into clear ones'; $s.Save()"

choice /C YN /M "Start PromptForge automatically when Windows starts"
if errorlevel 2 (
  reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PromptForge /f >nul 2>&1
) else (
  reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PromptForge /t REG_SZ /d "\"%DEST%\PromptForge.exe\" --background" /f >nul
)

start "" "%DEST%\PromptForge.exe"
echo.
echo Done. PromptForge is running in the system tray (bottom-right, you may need the ^ arrow).
echo It is also in the Start menu. To update later, run install.cmd again with the new PromptForge.exe.
echo To uninstall, run %DEST%\uninstall.cmd
echo.
pause
