@echo off
rem 本脚本以 UTF-8（无 BOM）保存：开头这一句把控制台切到 UTF-8，
rem 否则在默认代码页（中文系统 936）下脚本里的中文会显示成乱码，也会让 zip 文件名变形。
chcp 65001 >nul
setlocal enabledelayedexpansion
rem ============================================================
rem  ScheduleReminder 发布脚本（解压即用测试包）
rem  用法：双击运行，或在命令行执行 build\publish.bat
rem  做三件事：1) Release publish  2) 校验发布包  3) 生成 zip
rem  依赖：本机需安装 .NET SDK（能执行 dotnet publish），无需 Visual Studio
rem ============================================================

set ROOT=%~dp0..
set PROJ=%ROOT%\src\ScheduleReminder\ScheduleReminder.csproj
set BIN=%ROOT%\src\ScheduleReminder\bin\Release\net48
set OUT=%ROOT%\publish\ScheduleReminder
set ZIPNAME=ScheduleReminder-测试版

rem ---- 程序运行时 exe 被占用，先提示退出（否则 publish 会失败） ----
tasklist /FI "IMAGENAME eq ScheduleReminder.exe" 2>nul | find /I "ScheduleReminder.exe" >nul
if not errorlevel 1 (
    echo [中止] ScheduleReminder 正在运行，exe 被占用。
    echo         请先退出程序（托盘图标右键 - 退出），再运行本脚本。
    goto fail
)

echo [1/3] 以 Release 发布...
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish "%PROJ%" -c Release -o "%OUT%" -v m
if errorlevel 1 (
    echo 发布失败，请查看上方错误信息。
    goto fail
)

echo [2/3] 校验发布包...
set MISSING=0
for %%F in (
    "%OUT%\ScheduleReminder.exe"
    "%OUT%\ScheduleReminder.exe.config"
    "%OUT%\Resources\icon.ico"
    "%OUT%\Resources\icon_taskbar.png"
    "%OUT%\data\holidays.json"
) do (
    if not exist %%F (
        echo       缺少：%%~F
        set MISSING=1
    )
)

rem 音效必须随包带出：publish 已通过 csproj 从工程 sound\ 复制到 data\sound
set SOUNDCOUNT=0
for /f %%C in ('dir /b "%OUT%\data\sound\*.mp3" 2^>nul ^| find /c /v ""') do set SOUNDCOUNT=%%C
if !SOUNDCOUNT! EQU 0 (
    echo       缺少：data\sound\*.mp3（提示音库为空）
    set MISSING=1
)

if "!MISSING!"=="1" (
    echo [中止] 发布包缺少必需文件。
    echo         图标缺失会导致程序无法启动；holidays.json 缺失会导致公历节日不显示；
    echo         data\sound 为空会导致提醒没有内置提示音。
    goto fail
)
echo       必需文件齐全，音效 !SOUNDCOUNT! 个。

echo [3/3] 生成压缩包...
set PUBOUT=%OUT%
powershell -NoProfile -Command "$o=[IO.Path]::GetFullPath($env:PUBOUT); $z=[IO.Path]::Combine($o,'..',$env:ZIPNAME+'-'+(Get-Date -Format yyyyMMdd)+'.zip'); Compress-Archive -Path $o -DestinationPath $z -Force; Write-Host ('        压缩包：'+$z)"
if errorlevel 1 (
    echo 压缩失败；发布目录已生成，可手动右键压缩。
    goto fail
)

echo.
echo   完成。发布目录：%OUT%
echo.
echo   发给测试员时请一并说明：
echo     1) 需 .NET Framework 4.8（Win10 1903+ / Win11 自带；Win7、8.1 需先装一次）
echo     2) 不要解压到 C:\Program Files：日程数据写在 exe 旁的 data\ 里，需要写权限，建议解压到 D:\日程提醒 之类
echo     3) 若弹「Windows 已保护你的电脑」，点「更多信息 - 仍要运行」（exe 未做数字签名）
echo     4) 点窗口右上角 X 是最小化到托盘；要真正退出请用托盘图标右键 - 退出
echo     5) 首次启动会自动创建 data\ 目录并使用默认设置，不会弹任何错误提示
echo.
goto end

:fail
echo.
echo 发布中止。

:end
endlocal
pause
