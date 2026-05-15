@echo off
setlocal

REM Builds the self-contained app, then compiles the Inno Setup installer.
REM Requires Inno Setup 6 (iscc.exe) on PATH or at the default install location.

pushd "%~dp0\.."

echo === Step 1: Publish ScrollerCapture (Release, win-x64, self-contained) ===
call build.bat publish
if errorlevel 1 goto :fail

set ISCC=
where iscc >nul 2>nul && set ISCC=iscc

if not defined ISCC (
  if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  ) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
    set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
  )
)

if not defined ISCC (
  echo [ERROR] Inno Setup compiler ^(iscc^) not found.
  echo Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
  echo or add ISCC.exe to PATH, then run this script again.
  goto :fail
)

echo.
echo === Step 2: Compile installer ===
"%ISCC%" "installer\ScrollerCapture.iss"
if errorlevel 1 goto :fail

echo.
echo Installer created:
echo   %CD%\installer\output\ScrollerCapture-1.0.0-setup.exe
popd
endlocal
exit /b 0

:fail
echo.
echo [ERROR] Installer build failed.
popd
endlocal
exit /b 1
