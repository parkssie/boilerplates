@echo off
setlocal EnableExtensions

set "PROJECT_DIR=%~dp0"
set "PROJECT_FILE=%PROJECT_DIR%DotnetRestApiInfWorker.csproj"
set "PUBLISH_DIR=%PROJECT_DIR%_publish"

if not exist "%PROJECT_FILE%" (
    echo [ERROR] Project file not found: "%PROJECT_FILE%"
    exit /b 1
)

if exist "%PUBLISH_DIR%" (
    echo [INFO] Removing previous publish output...
    rmdir /s /q "%PUBLISH_DIR%"
    if exist "%PUBLISH_DIR%" (
        echo [ERROR] Failed to remove: "%PUBLISH_DIR%"
        exit /b 1
    )
)

echo [INFO] Publishing win-x64 Release build...
dotnet publish "%PROJECT_FILE%" --configuration Release --output "%PUBLISH_DIR%"
if errorlevel 1 (
    echo [ERROR] Publish failed.
    exit /b 1
)

echo [OK] Publish completed: "%PUBLISH_DIR%"
exit /b 0
