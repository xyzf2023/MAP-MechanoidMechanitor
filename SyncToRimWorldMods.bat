@echo off
setlocal EnableExtensions DisableDelayedExpansion

REM ============================================================
REM  Sync loadable RimWorld mod files to local RimWorld Mods
REM ============================================================
REM  This file must be placed in the mod root folder.
REM  It mirrors non-source mod files to:
REM  D:\steam\steamapps\common\RimWorld\Mods\<current folder name>
REM ============================================================

set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"

for %%I in ("%SRC%") do set "MOD_NAME=%%~nxI"

set "DST_ROOT=D:\steam\steamapps\common\RimWorld\Mods"
set "DST=%DST_ROOT%\%MOD_NAME%"
set "TEMP_ID=%TEMP%\MMT_PublishedFileId_backup.txt"

echo.
echo [MMT] Sync loadable mod files to RimWorld local Mods folder
echo ------------------------------------------------------------
echo Source: %SRC%
echo Target: %DST%
echo ------------------------------------------------------------
echo.

if "%MOD_NAME%"=="" (
    echo [ERROR] Failed to detect current mod folder name.
    echo.
    pause
    exit /b 1
)

if /I "%SRC%"=="%DST%" (
    echo [ERROR] Source and target are the same folder. Stop.
    echo.
    pause
    exit /b 1
)

if not exist "%SRC%" (
    echo [ERROR] Source folder does not exist:
    echo %SRC%
    echo.
    pause
    exit /b 1
)

if not exist "%DST%" (
    echo [INFO] Target folder does not exist. Creating:
    echo %DST%
    mkdir "%DST%"
)

echo [1/5] Backing up PublishedFileId.txt...
if exist "%TEMP_ID%" del "%TEMP_ID%" >nul 2>nul

if exist "%DST%\PublishedFileId.txt" (
    copy "%DST%\PublishedFileId.txt" "%TEMP_ID%" >nul
    echo       Existing PublishedFileId.txt backed up.
) else (
    echo       No PublishedFileId.txt found in target.
)

echo.
echo [2/5] Removing old development folders from target...

if exist "%DST%\.git" rmdir /S /Q "%DST%\.git"
if exist "%DST%\.github" rmdir /S /Q "%DST%\.github"
if exist "%DST%\.vs" rmdir /S /Q "%DST%\.vs"
if exist "%DST%\.idea" rmdir /S /Q "%DST%\.idea"
if exist "%DST%\.vscode" rmdir /S /Q "%DST%\.vscode"
if exist "%DST%\Source" rmdir /S /Q "%DST%\Source"
if exist "%DST%\Doc" rmdir /S /Q "%DST%\Doc"
if exist "%DST%\Docs" rmdir /S /Q "%DST%\Docs"
if exist "%DST%\bin" rmdir /S /Q "%DST%\bin"
if exist "%DST%\obj" rmdir /S /Q "%DST%\obj"
if exist "%DST%\Debug" rmdir /S /Q "%DST%\Debug"
if exist "%DST%\Release" rmdir /S /Q "%DST%\Release"

echo.
echo [3/5] Running robocopy mirror sync...
echo.

robocopy "%SRC%" "%DST%" /MIR /XD .git .github .vs .idea .vscode Source Doc Docs bin obj Debug Release /XF SyncToRimWorldMods.bat build.bat *.sln *.csproj *.csproj.user *.user *.suo *.cs *.pdb *.mdb *.cache *.tmp *.bak *.log .gitignore .gitattributes README.md PublishedFileId.txt

set "ROBOCOPY_EXIT=%ERRORLEVEL%"

echo.
echo [4/5] Restoring PublishedFileId.txt...
if exist "%TEMP_ID%" (
    copy "%TEMP_ID%" "%DST%\PublishedFileId.txt" >nul
    del "%TEMP_ID%" >nul 2>nul
    echo       PublishedFileId.txt restored.
) else (
    echo       No PublishedFileId.txt backup to restore.
)

echo.
echo [5/5] Checking result...
if %ROBOCOPY_EXIT% GEQ 8 (
    echo [FAIL] Robocopy failed. Exit code: %ROBOCOPY_EXIT%
    echo.
    pause
    exit /b %ROBOCOPY_EXIT%
)

echo [OK] Sync completed.
echo Target folder:
echo %DST%
echo.
echo Excluded folders:
echo .git, .github, .vs, .idea, .vscode, Source, Doc, Docs, bin, obj, Debug, Release
echo.
echo Excluded files:
echo SyncToRimWorldMods.bat, build.bat, *.sln, *.csproj, *.csproj.user, *.user, *.suo, *.cs, *.pdb, *.mdb, *.cache, *.tmp, *.bak, *.log
echo.
pause
exit /b 0
