@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "SERVICE_NAME=DotnetRestApiInfWorker"
if not "%~1"=="" set "SERVICE_NAME=%~1"

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Run this script as Administrator.
    exit /b 1
)

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo [INFO] Service "%SERVICE_NAME%" does not exist.
    exit /b 0
)

sc.exe stop "%SERVICE_NAME%" >nul 2>&1

for /L %%I in (1,1,30) do (
    sc.exe query "%SERVICE_NAME%" | findstr /C:"STOPPED" >nul 2>&1
    if not errorlevel 1 goto :deleteService
    timeout /t 1 /nobreak >nul
)

:deleteService
sc.exe delete "%SERVICE_NAME%"
if errorlevel 1 exit /b 1

echo [OK] Service "%SERVICE_NAME%" deleted.
exit /b 0
