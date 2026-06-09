@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ========================================
echo MAP Mechanoid Mechanitor MOD build
echo ========================================
echo.

REM Check dotnet is available
echo Checking .NET SDK...
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: dotnet not found. Install .NET SDK.
    echo Download: https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

REM Pre-clean build outputs
echo Cleaning previous build artifacts...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
echo [OK] Pre-build clean done.
echo.

REM Ensure output folders exist
if not exist "..\1.6\Assemblies" mkdir "..\1.6\Assemblies"

set "CONFIG=Release"
set "MAIN_OK=0"

REM ============================================================
REM  Phase 1: main mod (MAP-MechanoidMechanitor.dll)
REM ============================================================
echo ----------------------------------------
echo Building MAP-MechanoidMechanitor.csproj ...
echo ----------------------------------------
dotnet build "MAP-MechanoidMechanitor.csproj" --configuration %CONFIG% --verbosity normal

if %ERRORLEVEL% EQU 0 (
    set "MAIN_OK=1"
    echo [OK] Main mod build succeeded.
) else (
    echo [FAIL] Main mod build failed. See errors above.
)
echo.

REM ============================================================
REM  Clean intermediate outputs and summary
REM ============================================================
:summary

if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

if exist "..\1.6\Assemblies\MAP-MechanoidMechanitor.pdb" del /q "..\1.6\Assemblies\MAP-MechanoidMechanitor.pdb"

echo ========================================
echo Build summary
echo ========================================
echo.

if !MAIN_OK! EQU 1 (
    echo  [Main mod]     [OK]
    if exist "..\1.6\Assemblies\MAP-MechanoidMechanitor.dll" (
        echo                ..\1.6\Assemblies\MAP-MechanoidMechanitor.dll
    ) else (
        echo                WARNING: MAP-MechanoidMechanitor.dll not found at expected path
    )
) else (
    echo  [Main mod]     [FAIL]
)

echo.
echo ========================================

if !MAIN_OK! EQU 1 (
    echo  Main mod build succeeded.
) else (
    echo  Build finished with errors. See output above.
)

echo ========================================
echo.
pause
endlocal
