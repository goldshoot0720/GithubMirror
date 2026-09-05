@echo off
REM GitHub Mirror Tool - Windows Quick Start Script

echo.
echo ========================================
echo   GitHub Mirror Tool - Quick Start
echo ========================================
echo.

REM Check if .NET SDK is installed
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK is not installed!
    echo Please install .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

REM Check if Git is installed
git --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Git is not installed!
    echo Please install Git from: https://git-scm.com/download/win
    pause
    exit /b 1
)

echo [OK] .NET SDK found: 
dotnet --version
echo.
echo [OK] Git found:
git --version
echo.

REM Build the project
echo Building GitHub Mirror Tool...
dotnet build --configuration Release
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo.
echo [OK] Build completed successfully!
echo.

REM Run the application
echo Starting GitHub Mirror Tool...
dotnet run --configuration Release --project GithubMirror.csproj

pause
