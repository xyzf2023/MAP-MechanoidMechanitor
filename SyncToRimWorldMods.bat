@echo off
chcp 65001 >nul
setlocal EnableExtensions

REM ============================================================
REM  Mechanoid Mechanitor Test - Sync non-source MOD files
REM ============================================================
REM  This script should be placed in the MOD root folder:
REM  D:\Rimworld_MOD_dev\机械族机械师-技术验证
REM
REM  It mirrors loadable MOD files to RimWorld local Mods folder.
REM  Source code, project files, Git files, IDE files and build cache
REM  are excluded.
REM ============================================================

set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"

set "DST=D:\steam\steamapps\common\RimWorld\Mods\机械族机械师-技术验证"
set "TEMP_ID=%TEMP%\MMT_PublishedFileId_backup.txt"

echo.
echo [MMT] 同步 MOD 文件到 RimWorld 本地 Mods 加载目录
echo ------------------------------------------------------------
echo 源目录: %SRC%
echo 目标目录: %DST%
echo ------------------------------------------------------------
echo.
echo 注意：
echo   这个脚本会让目标目录变成源目录的“干净加载副本”。
echo   源目录里删除的非源码 MOD 文件，目标目录里也会删除。
echo   .git、Source、IDE配置、项目文件、编译缓存不会同步。
echo   1.6\Assemblies 中的已编译 DLL 会被同步。
echo   PublishedFileId.txt 会尽量保留，避免 Steam 创意工坊 ID 丢失。
echo.

if /I "%SRC%"=="%DST%" (
    echo [错误] 源目录和目标目录相同。已停止，避免误删文件。
    echo.
    pause
    exit /b 1
)

if not exist "%SRC%" (
    echo [错误] 源目录不存在：
    echo %SRC%
    echo.
    pause
    exit /b 1
)

if not exist "%DST%" (
    echo [提示] 目标目录不存在，将自动创建：
    echo %DST%
    mkdir "%DST%"
)

echo [1/5] 备份 PublishedFileId.txt...
if exist "%TEMP_ID%" del "%TEMP_ID%" >nul 2>nul

if exist "%DST%\PublishedFileId.txt" (
    copy "%DST%\PublishedFileId.txt" "%TEMP_ID%" >nul
    echo      已备份现有 Workshop ID。
) else (
    echo      目标目录暂无 PublishedFileId.txt，跳过备份。
)

echo.
echo [2/5] 清理目标目录中可能残留的开发文件夹...

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
echo [3/5] 开始镜像同步非源码 MOD 文件...
echo.

robocopy "%SRC%" "%DST%" /MIR ^
/XD .git .github .vs .idea .vscode Source Doc Docs bin obj Debug Release ^
/XF SyncToRimWorldMods.bat build.bat *.sln *.csproj *.csproj.user *.user *.suo *.cs *.pdb *.mdb *.cache *.tmp *.bak *.log .gitignore .gitattributes README.md PublishedFileId.txt

set "ROBOCOPY_EXIT=%ERRORLEVEL%"

echo.
echo [4/5] 恢复 PublishedFileId.txt...
if exist "%TEMP_ID%" (
    copy "%TEMP_ID%" "%DST%\PublishedFileId.txt" >nul
    echo      已恢复 Workshop ID。
) else (
    echo      没有可恢复的 Workshop ID。
)

echo.
echo [5/5] 检查同步结果...
if %ROBOCOPY_EXIT% GEQ 8 (
    echo [失败] Robocopy 返回错误码：%ROBOCOPY_EXIT%
    echo       请检查上方输出，可能存在文件占用、权限不足或路径错误。
    echo.
    pause
    exit /b %ROBOCOPY_EXIT%
)

echo [完成] 同步成功。
echo RimWorld 本地加载目录：
echo %DST%
echo.
echo 已排除：
echo .git, .github, .vs, .idea, .vscode, Source, Doc, Docs, bin, obj, Debug, Release
echo.
echo 已排除文件类型：
echo *.sln, *.csproj, *.csproj.user, *.user, *.suo, *.cs, *.pdb, *.mdb, *.cache, *.tmp, *.bak, *.log
echo.
pause
exit /b 0
