@echo off
setlocal
pushd "%~dp0"
dotnet run --project tools\GenerateAppIcon\GenerateAppIcon.csproj -c Release --nologo
if errorlevel 1 (
    echo [ERROR] Icon generation failed.
    popd
    exit /b 1
)
popd
endlocal
exit /b 0
