@echo off
setlocal
cd /d "%~dp0"

if exist dist rmdir /s /q dist

dotnet publish ValidateJson.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none -o dist
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo.
echo Built: %~dp0dist\ValidateJson.exe
