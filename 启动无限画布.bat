@echo off
chcp 65001 >nul
title 无限画布
cd /d "D:\sdh\xiangmu1\_eval\infinite-canvas"

rem 首次启动（或删过 dist）时先构建前端
if not exist "web\dist\index.html" (
    echo 首次启动，正在构建前端，约 10-20 秒...
    pushd web
    call bun run build
    popd
    if not exist "web\dist\index.html" (
        echo [错误] 前端构建失败，请手动执行：cd web ^&^& bun run build
        pause
        exit /b 1
    )
)

rem 已经在跑就不再起，直接打开页面
netstat -ano | findstr /C:"127.0.0.1:3000" | findstr /C:"LISTENING" >nul 2>&1
if not errorlevel 1 (
    echo 无限画布已在运行，正在打开页面：http://localhost:3000
    start "" http://localhost:3000
    ping -n 2 127.0.0.1 >nul
    exit /b 0
)

echo 正在启动无限画布（单服务：画布 + 序列帧生成器 + 接口转发 + 跨源代理）
echo.
echo   地址            http://localhost:3000
echo   默认进图工作台  http://localhost:3000/image
echo   序列帧生成器    http://localhost:3000/h3ui   （需要 ComfyUI 在 127.0.0.1:8188 运行）
echo   自检            http://localhost:3000/status
echo.
echo   关闭本窗口即停止服务。改了前端代码请用「重新构建并启动.bat」。
echo.

rem 等几秒让服务起来后自动打开页面（用 ping 代替 timeout：无控制台场景下 timeout 会报错）
start "" /b cmd /c "ping -n 4 127.0.0.1 >nul & start http://localhost:3000"

node serve.mjs

echo.
echo 服务已退出。
pause
