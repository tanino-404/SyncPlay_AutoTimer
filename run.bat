@echo off
REM SyncPlay_AutoTimer v2.0 - Application Launcher
REM This batch file launches the SyncPlay_AutoTimer application

setlocal enabledelayedexpansion

REM Get the directory where this batch file is located
set SCRIPT_DIR=%~dp0

REM Check if the executable exists
if not exist "%SCRIPT_DIR%v2\bin\Release\net9.0\SyncPlay_AutoTimer.exe" (
    echo.
    echo ===================================================
    echo ERROR: Application not found!
    echo ===================================================
    echo.
    echo Expected location:
    echo %SCRIPT_DIR%v2\bin\Release\net9.0\SyncPlay_AutoTimer.exe
    echo.
    echo Please ensure you have built the application using:
    echo   cd v2
    echo   dotnet build -c Release
    echo.
    pause
    exit /b 1
)

REM Run the application
echo.
echo ===================================================
echo  SyncPlay_AutoTimer v2.0 - Launching...
echo ===================================================
echo.

cd /d "%SCRIPT_DIR%v2\bin\Release\net9.0\"
SyncPlay_AutoTimer.exe

pause
