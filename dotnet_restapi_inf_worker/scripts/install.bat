@echo off
setlocal EnableExtensions

set "SERVICE_NAME=DotnetRestApiInfWorker"
if not "%~1"=="" set "SERVICE_NAME=%~1"

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Run this script as Administrator.
    exit /b 1
)

for %%I in ("%~dp0..") do set "APP_DIR=%%~fI"
set "EXE_PATH=%APP_DIR%\DotnetRestApiInfWorker.exe"

if not exist "%EXE_PATH%" (
    echo [ERROR] Executable not found: "%EXE_PATH%"
    exit /b 1
)

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if not errorlevel 1 (
    echo [ERROR] Service "%SERVICE_NAME%" already exists.
    exit /b 1
)

sc.exe create "%SERVICE_NAME%" binPath= "\"%EXE_PATH%\"" start= auto DisplayName= "Dotnet REST API INF Worker"
if errorlevel 1 exit /b 1

sc.exe description "%SERVICE_NAME%" "Collects REST API input data and publishes PostgreSQL simulation results."
sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/5000/restart/15000/restart/60000
sc.exe start "%SERVICE_NAME%"
if errorlevel 1 (
    echo [WARN] Service was installed but could not be started. Check logs\application.log.
    exit /b 1
)

echo [OK] Service "%SERVICE_NAME%" installed and started.
exit /b 0
