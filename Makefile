.PHONY: help build run clean restore test debug release publish install

# Default target
help:
	@echo "GitHub Mirror Tool - Makefile Commands"
	@echo "========================================"
	@echo ""
	@echo "Available targets:"
	@echo "  make build       - Build the project (Debug configuration)"
	@echo "  make build-release - Build the project (Release configuration)"
	@echo "  make run         - Build and run the application"
	@echo "  make clean       - Clean build artifacts"
	@echo "  make restore     - Restore NuGet packages"
	@echo "  make test        - Run tests (if available)"
	@echo "  make debug       - Build and run in debug mode"
	@echo "  make release     - Build and run in release mode"
	@echo "  make publish     - Publish as standalone application"
	@echo "  make help        - Show this help message"
	@echo ""

restore:
	@echo "Restoring NuGet packages..."
	dotnet restore

build: restore
	@echo "Building GitHub Mirror Tool (Debug)..."
	dotnet build --configuration Debug

build-release: restore
	@echo "Building GitHub Mirror Tool (Release)..."
	dotnet build --configuration Release

clean:
	@echo "Cleaning build artifacts..."
	dotnet clean
	rm -rf bin obj

run: build
	@echo "Running GitHub Mirror Tool..."
	dotnet run --configuration Debug --no-build

debug: build
	@echo "Running GitHub Mirror Tool in Debug mode..."
	dotnet run --configuration Debug --no-build

release: build-release
	@echo "Running GitHub Mirror Tool in Release mode..."
	dotnet run --configuration Release --no-build

test:
	@echo "Running tests..."
	dotnet test

publish:
	@echo "Publishing standalone application..."
	@echo "Windows (x64):"
	dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
	@echo ""
	@echo "macOS (x64):"
	dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
	@echo ""
	@echo "macOS (arm64):"
	dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
	@echo ""
	@echo "Linux (x64):"
	dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
	@echo ""

install: build-release
	@echo "Installing dependencies..."
	@echo "Make sure .NET 8.0 SDK and Git are installed on your system"
	@echo ""
	@echo "To use the application:"
	@echo "  Windows: run-windows.bat"
	@echo "  Linux/macOS: ./run.sh or make run"
	@echo ""

info:
	@echo "Project Information"
	@echo "==================="
	@echo "Name: GitHub Mirror Tool"
	@echo "Framework: .NET 8.0"
	@echo "UI Framework: Avalonia"
	@echo ""
	@echo ".NET SDK Version:"
	@dotnet --version
	@echo ""
	@echo "Git Version:"
	@git --version
