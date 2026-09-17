@echo off
chcp 65001 >nul
title 无限画布（重新构建）
cd /d "D:\sdh\xiangmu1\_eval\infinite-canvas"

echo 正在重新构建前端，约 10-20 秒...
pushd web
call bun run build
set BUILD_EXIT=%errorlevel%
popd

if not "%BUILD_EXIT%"=="0" (
    echo.
    echo [错误] 构建失败（退出码 %BUILD_EXIT%），已中止，不会启动服务。
    pause
    exit /b 1
)

rem 有旧实例就先停掉，避免端口冲突
netstat -ano | findstr /C:"127.0.0.1:3000" | findstr /C:"LISTENING" >nul 2>&1
if not errorlevel 1 (
    echo.
    echo [提示] 3000 端口已被占用，请先关掉正在运行的「无限画布」窗口，再重新运行本脚本。
    pause
    exit /b 1
)

echo.
echo 构建完成，正在启动无限画布...

rem 等几秒让服务起来后自动打开页面（用 ping 代替 timeout：无控制台场景下 timeout 会报错）
start "" /b cmd /c "ping -n 4 127.0.0.1 >nul & start http://localhost:3000"

node serve.mjs

echo.
echo 服务已退出。
pause
