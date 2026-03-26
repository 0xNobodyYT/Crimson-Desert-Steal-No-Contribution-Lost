@echo off
setlocal

fltmc >nul 2>nul
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
  exit /b
)

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "EXE_PATH=%SCRIPT_DIR%\CrimsonDesertContributionInjector.exe"

echo Starting Crimson Desert contribution guard...
echo.

if not exist "%EXE_PATH%" (
  echo Missing file:
  echo   %EXE_PATH%
  echo.
  echo Keep the BAT and EXE in the same folder.
  echo.
  set /p "=Press Enter to close this window..."
  exit /b 1
)

start "Crimson Desert Contribution Injector" "%EXE_PATH%"

echo Guard started.
echo.
echo What it does:
echo   - waits for CrimsonDesert.exe
echo   - protects contribution when you steal with R or G
echo   - activates only briefly during the steal window
echo.
echo Important:
echo   - run it through this BAT so it gets admin rights
echo   - stop it with F8 in the helper window
echo   - restart the game to fully clear it
echo.
set /p "=Press Enter to close this window..."
