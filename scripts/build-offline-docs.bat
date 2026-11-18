@echo off
REM NCMapper offline docs package builder - Windows entry
REM Usage: build-offline-docs.bat [win-x64|linux-x64|all] [zh-Hans|en|all]
REM        (default: win-x64 zh-Hans)
REM Delegates to build-offline-docs.sh running under Git Bash.
REM Requires: Git Bash (bash.exe) and NativeAOT toolchain for win-x64.
REM
REM NOTE: this .bat is intentionally ASCII-only. cmd.exe parses .bat files using
REM the system ANSI code page; UTF-8 non-ASCII comments here get misread as
REM commands and spam the console with errors. Keep all prose in the .sh file.
REM
REM Search order: Git for Windows standard locations (deterministic, no PATH
REM dependency) -> WorkBuddy bundled PortableGit -> 'where' on PATH as fallback.
REM 2026-10-01 fix: 'where bash' used to be the FIRST choice and in some cmd
REM sessions resolved to C:\Windows\System32\bash.exe (the WSL Linux bash).
REM That bash cannot handle Windows paths like D:\... and failed with
REM "No such file or directory" (exit 127). WSL bash is now always skipped;
REM see the WSL exclusion guard in :FindBashOnPath below.

setlocal EnableExtensions

set "BASH="

REM 1) Git for Windows standard install locations
if not defined BASH if exist "%ProgramFiles%\Git\bin\bash.exe" set "BASH=%ProgramFiles%\Git\bin\bash.exe"
if not defined BASH if exist "%ProgramFiles(x86)%\Git\bin\bash.exe" set "BASH=%ProgramFiles(x86)%\Git\bin\bash.exe"
if not defined BASH if exist "%LocalAppData%\Programs\Git\bin\bash.exe" set "BASH=%LocalAppData%\Programs\Git\bin\bash.exe"

REM 2) WorkBuddy bundled PortableGit (version folder name is dynamic;
REM    bash lives under usr\bin, bin\ kept as an extra compatibility probe)
for /d %%v in ("%USERPROFILE%\.workbuddy\binaries\PortableGit\versions\*") do (
  if not defined BASH if exist "%%v\usr\bin\bash.exe" set "BASH=%%v\usr\bin\bash.exe"
  if not defined BASH if exist "%%v\bin\bash.exe" set "BASH=%%v\bin\bash.exe"
)

REM 3) PATH fallback, with WSL bash excluded
if not defined BASH call :FindBashOnPath

if not defined BASH (
  echo [BAT] Git Bash ^(bash.exe^) not found.
  echo Please install Git for Windows from https://git-scm.com/download/win
  echo or run this script from inside a Git Bash terminal.
  pause
  exit /b 1
)

echo [BAT] Using bash: %BASH%
"%BASH%" "%~dp0build-offline-docs.sh" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
  echo.
  echo [BAT] Build failed with exit code %EXIT_CODE%. See messages above.
  pause
)
exit /b %EXIT_CODE%

REM ---------------------------------------------------------------------------
REM :FindBashOnPath - resolve bash from PATH, skipping the WSL Linux bash.
REM A candidate is skipped when its path contains "Windows\System32" or
REM "WindowsApps". Result is stored in BASH (left undefined when none found).
REM ---------------------------------------------------------------------------
:FindBashOnPath
for /f "delims=" %%i in ('where bash 2^>nul') do (
  if not defined BASH call :TryBash "%%i"
)
exit /b 0

:TryBash
if defined BASH exit /b 0
echo %~1 | findstr /i /c:"Windows\System32" >nul && (
  echo [BAT] Skipped WSL bash: %~1
  exit /b 0
)
echo %~1 | findstr /i /c:"WindowsApps" >nul && (
  echo [BAT] Skipped WSL bash: %~1
  exit /b 0
)
set "BASH=%~1"
exit /b 0
