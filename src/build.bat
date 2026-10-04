@echo off
REM ============================================================
REM  build.bat  -  rebuilds ResolutionSwitcher.exe
REM  Created by Claude, 2026-09-17.
REM
REM  Uses the C# compiler that ships with Windows (.NET Framework
REM  4.x), so nothing needs to be installed. Edit the mode list in
REM  ResolutionSwitcher.cs (see BuildModes) and run this to get a
REM  fresh exe on the Desktop.
REM ============================================================

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find the C# compiler at
    echo   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    pause
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /out:"%~dp0..\ResolutionSwitcher.exe" ^
  /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll ^
  "%~dp0ResolutionSwitcher.cs"

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    pause
    exit /b 1
)

echo Built "%~dp0..\ResolutionSwitcher.exe"
pause
