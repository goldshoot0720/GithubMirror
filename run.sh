#!/bin/bash

# GitHub Mirror Tool - Linux/macOS Quick Start Script

echo ""
echo "========================================"
echo "  GitHub Mirror Tool - Quick Start"
echo "========================================"
echo ""

# Check if .NET SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK is not installed!"
    echo "Please install .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

# Check if Git is installed
if ! command -v git &> /dev/null; then
    echo "ERROR: Git is not installed!"
    echo "Please install Git using your package manager"
    if [[ "$OSTYPE" == "darwin"* ]]; then
        echo "  macOS: brew install git"
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        echo "  Ubuntu/Debian: sudo apt-get install git"
        echo "  Fedora: sudo dnf install git"
        echo "  Arch: sudo pacman -S git"
    fi
    exit 1
fi

echo "[OK] .NET SDK found:"
dotnet --version
echo ""
echo "[OK] Git found:"
git --version
echo ""

# Build the project
echo "Building GitHub Mirror Tool..."
dotnet build --configuration Release
if [ $? -ne 0 ]; then
    echo "ERROR: Build failed!"
    exit 1
fi

echo ""
echo "[OK] Build completed successfully!"
echo ""

# Run the application
echo "Starting GitHub Mirror Tool..."
dotnet run --configuration Release --project GithubMirror.csproj
