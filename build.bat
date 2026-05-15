@echo off
setlocal

REM Build script for ScrollerCapture.
REM Usage:
REM   build.bat            -> Debug build
REM   build.bat release    -> Release build
REM   build.bat publish    -> Self-contained single-file Release publish (win-x64)

pushd "%~dp0"

set CONFIG=Debug
set MODE=build

if /I "%~1"=="release" (
    set CONFIG=Release
    set MODE=build
) else if /I "%~1"=="publish" (
    set CONFIG=Release
    set MODE=publish
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet SDK not found in PATH.
    popd
    exit /b 1
)

if /I "%MODE%"=="publish" (
    echo === Publishing ScrollerCapture ^(%CONFIG%, win-x64, single file^) ===
    dotnet publish ScrollerCapture.csproj -c %CONFIG% -r win-x64 --self-contained true ^
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:DebugType=embedded --nologo
    if errorlevel 1 goto :fail
    echo.
    echo Published output:
    echo   %CD%\bin\%CONFIG%\net8.0-windows10.0.19041.0\win-x64\publish\ScrollerCapture.exe
) else (
    echo === Building ScrollerCapture ^(%CONFIG%^) ===
    dotnet build ScrollerCapture.csproj -c %CONFIG% --nologo -v minimal
    if errorlevel 1 goto :fail
    echo.
    echo Build output:
    echo   %CD%\bin\%CONFIG%\net8.0-windows\ScrollerCapture.exe
)

popd
endlocal
exit /b 0

:fail
echo.
echo [ERROR] Build failed.
popd
endlocal
exit /b 1
