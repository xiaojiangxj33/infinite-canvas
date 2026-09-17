@echo off
chcp 65001 >nul
title 编译无限画布托盘程序
rem ============================================================
rem  编译托盘守护程序。
rem  csc.exe 是 Windows 自带的（.NET Framework），不需要装任何东西。
rem ============================================================

cd /d "%~dp0.."

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [错误] 找不到 csc.exe，本机可能没有 .NET Framework 4.x
    pause
    exit /b 1
)

rem 源文件含中文，必须带 UTF-8 BOM，否则 csc 会按系统 ANSI 码页解析成乱码
powershell -NoProfile -Command "foreach($p in @('tray\Program.cs','tray\Theme.cs')){ $b=[IO.File]::ReadAllBytes($p); $hasBom=($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF); if(-not $hasBom){ $t=[IO.File]::ReadAllText($p,(New-Object Text.UTF8Encoding($false))); [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true))); Write-Host ('[修正] 已补 UTF-8 BOM: ' + $p) } }"

echo 正在编译...
"%CSC%" /nologo /target:winexe /optimize+ /utf8output /out:"无限画布托盘.exe" /win32icon:logo.ico /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "tray\Program.cs" "tray\Theme.cs"
if errorlevel 1 (
    echo.
    echo [错误] 编译失败，请看上面的报错。
    pause
    exit /b 1
)

echo.
echo   编译完成：无限画布托盘.exe
echo   双击它即可用托盘方式运行（不会有关不掉的黑色窗口）。
pause
