<p align="center">
  <img src="icon/icon-256.png" width="96" alt="无限画布图标">
</p>

<h1 align="center">无限画布 (infinite-canvas)</h1>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-f97316?style=flat-square" alt="License"></a>
  <a href="https://vite.dev/"><img src="https://img.shields.io/badge/Vite-7-646cff?style=flat-square&logo=vite&logoColor=white" alt="Vite"></a>
  <a href="https://reactrouter.com/"><img src="https://img.shields.io/badge/React_Router-7-ca4245?style=flat-square&logo=reactrouter&logoColor=white" alt="React Router"></a>
  <a href="https://github.com/basketikun/infinite-canvas"><img src="https://img.shields.io/badge/%E4%B8%8A%E6%B8%B8-basketikun%2Finfinite--canvas-2b6de8?style=flat-square&logo=github" alt="上游仓库"></a>
</p>

<p align="center">
  <a href="#本地部署">本地部署</a> · <a href="#序列帧生成器可选">序列帧生成器</a> · <a href="SECURITY.md">漏洞提交</a>
</p>

无限画布是一款面向图片创作的开源工作台。它把画布编排、AI 图片生成、参考图编辑、提示词库和素材沉淀放在同一个界面里，适合用来探索视觉方案并连续迭代图片结果。

> [!NOTE]
> **这是个人精简版**，基于上游 [basketikun/infinite-canvas](https://github.com/basketikun/infinite-canvas) v0.19.0 裁剪而来：去掉了本机用不到的功能（数据分析、版本检测、画布助手 / 本地 Agent、插件系统，以及运行时无关的目录），并做了几处部署与界面调整。部署方式见下方[《本地部署》](#本地部署)。

> [!CAUTION]
> 项目目前处于开发阶段，不保证历史数据兼容。各种本地存储格式都可能直接调整，欢迎关注后续更新。
>
> 如果你需要稳定维护自己的分支，建议自行 fork 后独立开发。二次开发与 PR 请保留原作者信息和前端页面标识。

## 核心功能

- 无限画布：多画布项目、节点拖拽缩放、连线、小地图、撤销重做、导入导出。
- AI 创作：浏览器前台直连你配置的 OpenAI 兼容接口，支持文生图、图生图、参考图编辑、文本问答、音频和视频生成。
- 自定义接口调用：可自定义生图 / 视频接口的调用方式，灵活适配各类中转站与自建服务。
- 提示词库：内置 7 个开源提示词来源并支持自定义标准 JSON 来源，由浏览器前端直连并缓存到 IndexedDB。
- 序列帧生成器：整合 ComfyUI 的序列帧流水线，见下方[《序列帧生成器》](#序列帧生成器可选)。

## 本地部署

### 1. 准备

| 需要 | 用途 |
|---|---|
| **Node.js 18+** | 跑 `serve.mjs`，必需；**仓库已带构建产物，装完 Node 即可直接运行** |
| **.NET Framework 4.x** | 只有用托盘程序才需要。Win10 / Win11 系统自带，不用装 |
| **ComfyUI** | 可选，只在要用序列帧生成器时需要 |
| **bun** | 可选，只在改了前端源码、需要重新构建时用（仓库**已包含构建产物** `web/dist`，直接用 Node 就能跑） |

### 2. 跑起来

```bash
git clone https://github.com/xiaojiangxj33/infinite-canvas.git
cd infinite-canvas
```

**推荐：双击 `无限画布托盘.exe`。** 它把服务收进右下角通知区域：

- **没有控制台窗口，不会被误关**（原来那个黑窗口一点叉服务就没了）
- **每 4 秒自检一次** `http://127.0.0.1:3000/status`，连续失败或进程退出都会**自动重启**
- 托盘图标右键：打开无限画布 / 打开序列帧生成器 / 重启服务 / 查看日志 / 打开项目目录 / **开机自动启动** / 退出
- 双击托盘图标即可打开页面；首次运行会自动打开浏览器

也可以双击 **`启动无限画布.bat`**：缺少构建产物时会先自动构建，但会占用一个控制台窗口（关掉窗口即停止服务，且不会自动重启）。

想自己重新编译托盘程序（改过源码后）：双击 `tray\build.bat`，用的是 Windows 自带的 csc，不需要装编译器。

手动启动服务：

```bash
node serve.mjs
```

打开 `http://localhost:3000`。首次使用进入右上角「配置」，填入自己的 OpenAI 兼容 `Base URL` 和 `API Key`；如果接口调用方式与默认不同，可自定义生图 / 视频脚本。

> `serve.mjs` 是**单服务**：一个进程同时承担画布静态资源、序列帧生成器页面、ComfyUI 接口转发（含 WebSocket 进度）与跨源代理，不需要再单独启动转接服务。
>
> **API Key、画布、素材和生成记录都存在浏览器本地**，换机器要重新填。

## 序列帧生成器（可选）

画布里的「序列帧生成器」页面依赖一套 ComfyUI 环境，**相关代码不在本仓库**，来自这两个仓库：

- **[h3-sprite-generator](https://github.com/xiaojiangxj33/h3-sprite-generator)** —— 序列帧生成器本体。单文件前端，约 270 KB、零依赖、零构建步骤。
- **[ComfyUI-H3-ImageKey](https://github.com/xiaojiangxj33/ComfyUI-H3-ImageKey)** —— 配套的 ComfyUI 抠图节点，放进 `custom_nodes/` 即可用。

装好之后，把这个网页目录告诉本服务（指向该自定义节点的 `web` 目录），三种方式任选一种：

```bash
node serve.mjs --h3ui-dir "D:\ComfyUI\custom_nodes\comfyui-minimax-h3-audio-T8\web"
```

```powershell
$env:H3UI_DIR = "D:\ComfyUI\custom_nodes\comfyui-minimax-h3-audio-T8\web"
```

```
# 或者在项目根目录建一个 .h3ui-dir 文件，里面只写一行该路径（已在 .gitignore 中，不会被提交）
```

**不配置也能正常使用画布**，只是序列帧生成器页面会显示一段提示。启动顺序：先起 ComfyUI，再起本服务。

> 本精简版已移除 Docker 相关文件，如需容器部署请使用[上游仓库](https://github.com/basketikun/infinite-canvas)。

## 上游与致谢

本项目基于 [basketikun/infinite-canvas](https://github.com/basketikun/infinite-canvas)（MIT License，Copyright © 2026 basketikun）裁剪而来，只是个人自用的精简副本，去掉了用不到的功能。上游的设计与实现归原作者所有；功能建议与问题反馈请到[上游仓库](https://github.com/basketikun/infinite-canvas)。

## 开源协议

本项目使用 [MIT License](LICENSE)。

