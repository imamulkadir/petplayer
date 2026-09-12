@echo off
setlocal EnableExtensions
title Pet Player - Setup and Build

rem Always work from the folder containing this file.
cd /d "%~dp0"

set "DOTNET_VERSION=8.0.425"
set "DOTNET_URL=https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.425/dotnet-sdk-8.0.425-win-x64.exe"
set "DOTNET_INSTALLER=%TEMP%\dotnet-sdk-%DOTNET_VERSION%-win-x64.exe"
set "DOTNET_DIR=%ProgramFiles%\dotnet"

echo ========================================
echo   Pet Player - Setup and Build
echo ========================================
echo.

rem ------------------------------------------------------------
rem 1. Check whether a .NET 8 SDK is already installed.
rem ------------------------------------------------------------
set "HAS_DOTNET8="

where dotnet >nul 2>&1
if not errorlevel 1 (
    for /f "tokens=1" %%V in ('dotnet --list-sdks 2^>nul') do (
        echo %%V | findstr /b "8.0." >nul && set "HAS_DOTNET8=1"
    )
)

if defined HAS_DOTNET8 (
    echo [OK] .NET 8 SDK is already installed.
    goto :BUILD
)

echo [INFO] .NET 8 SDK was not found.
echo [INFO] Downloading .NET SDK %DOTNET_VERSION%...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri '%DOTNET_URL%' -OutFile '%DOTNET_INSTALLER%'"

if errorlevel 1 (
    echo.
    echo [ERROR] Failed to download the .NET 8 SDK.
    goto :FAIL
)

if not exist "%DOTNET_INSTALLER%" (
    echo.
    echo [ERROR] The .NET installer was not downloaded.
    goto :FAIL
)

echo [INFO] Installing .NET SDK %DOTNET_VERSION%...
echo [INFO] Windows may request administrator permission.
echo.

"%DOTNET_INSTALLER%" /install /quiet /norestart

if errorlevel 1 (
    echo.
    echo [ERROR] .NET SDK installation failed.
    echo [INFO] Try right-clicking this file and selecting "Run as administrator".
    goto :FAIL
)

del /q "%DOTNET_INSTALLER%" >nul 2>&1

rem Refresh PATH for this CMD session after installation.
if exist "%DOTNET_DIR%\dotnet.exe" (
    set "PATH=%DOTNET_DIR%;%PATH%"
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo [ERROR] .NET was installed, but dotnet.exe is still not available.
    echo [INFO] Close this window, open a new terminal, and run this file again.
    goto :FAIL
)

echo.
echo [OK] .NET SDK installed successfully.
echo.

:BUILD
echo ========================================
echo   Installed .NET SDKs
echo ========================================
dotnet --list-sdks
echo.

if not exist "%~dp0build\Build.ps1" (
    echo [ERROR] build\Build.ps1 was not found.
    echo [INFO] Put this file in the root of the PetPlayer repository.
    goto :FAIL
)

echo ========================================
echo   PowerShell Execution Policy
echo ========================================
powershell.exe -NoProfile -Command "Get-ExecutionPolicy -List"
echo.

echo ========================================
echo   Building Pet Player
echo ========================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & '%~dp0build\Build.ps1'"

if errorlevel 1 (
    echo.
    echo [ERROR] Pet Player build failed.
    goto :FAIL
)

echo.
echo ========================================
echo   Setup and build completed successfully
echo ========================================
echo.
echo Output should be under:
echo   %~dp0dist\PetPlayer
echo.
pause
exit /b 0

:FAIL
echo.
echo Setup/build did not complete.
echo.
pause
exit /b 1
