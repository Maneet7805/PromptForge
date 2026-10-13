@echo off
rem PromptForge installs itself: this just runs PromptForge.exe, which offers to install or update.
set "SRC=%~dp0PromptForge.exe"
if not exist "%SRC%" set "SRC=%~dp0..\dist\PromptForge.exe"
if not exist "%SRC%" (
  echo PromptForge.exe not found next to this script or in ..\dist.
  pause
  exit /b 1
)
start "" "%SRC%"
