@echo off
rem ---------------------------------------------------------------
rem  GameDock build script
rem  Uses the csc.exe that ships with .NET Framework.
rem  No Visual Studio, no NuGet, no third-party dependencies.
rem
rem  NOTE: this file is intentionally ASCII-only. cmd.exe reads .bat
rem  files with the OEM codepage (936 on zh-CN Windows), so non-ASCII
rem  characters in here would corrupt the script.
rem ---------------------------------------------------------------
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)

echo [*] Compiling GameDock.exe ...

"%CSC%" /nologo /codepage:65001 /target:winexe /win32icon:gamedock.ico /out:GameDock.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll GameDock.cs

if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)

echo [*] Done: GameDock.exe
echo     Run it by double-clicking. Data is stored in %%APPDATA%%\GameDock
endlocal
