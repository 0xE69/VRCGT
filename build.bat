@echo off
echo ============================================
echo   VRC Group Tools - Build Script
echo ============================================
echo.

cd /d "%~dp0src"

echo [1/3] Restoring NuGet packages...
dotnet restore
if errorlevel 1 (
    echo ERROR: Failed to restore packages
    pause
    exit /b 1
)

echo.
echo [2/3] Building Release version...
dotnet build -c Release
if errorlevel 1 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

echo.
echo [3/3] Publishing self-contained executable...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "bin\Publish"
if errorlevel 1 (
    echo ERROR: Publish failed
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Build Complete!
echo ============================================
echo.
echo Output location: %~dp0VRCGroupTools\bin\Publish\
echo.
echo You can now:
echo   1. Run VRCGroupTools.exe directly from the Publish folder
echo   2. Use Inno Setup to create an installer (see installer\setup.iss)
echo.
pause
