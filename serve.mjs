#!/usr/bin/env node
/**
 * 无限画布 —— 单服务入口（零依赖，只用 Node 内置模块）
 *
 * 一个进程、一个端口，同时承担四件事：
 *   1) 画布前端静态站        （web/dist，SPA 路由回退到 index.html）
 *   2) h3ui 页面伺服         （从 ComfyUI 的 web 目录读磁盘文件）
 *   3) ComfyUI 接口转发      （/prompt /view /upload/image /history /system_stats ... + WebSocket /ws）
 *   4) AI 接口 CORS 代理     （把 <本服务>/https://目标地址 原样转发，替代独立的 canvas-proxy）
 *
 * 为什么把 h3ui 和 ComfyUI 接口放在同一个端口：
 *   h3ui 内部把 window.location.origin 当作 ComfyUI 地址（COMFY_URL），
 *   只有让它和接口同源，它对 ComfyUI 的请求才不会被浏览器跨域拦掉 —— 也就是不再需要 8799 那个转接服务。
 *
 * 用法：
 *   node serve.mjs                      # 默认 3000 端口，ComfyUI 在 127.0.0.1:8188
 *   node serve.mjs --port 3000 --comfy-port 8188
 *   环境变量：PORT / COMFY_PORT / H3UI_DIR
 */
import { createServer } from "node:http";
import { request as httpRequest } from "node:http";
import { connect as netConnect } from "node:net";
import { createReadStream, existsSync, readFileSync, statSync } from "node:fs";
import { Readable } from "node:stream";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));

function readArg(name, fallback) {
    const index = process.argv.indexOf(`--${name}`);
    return index >= 0 && process.argv[index + 1] ? process.argv[index + 1] : fallback;
}

const PORT = Number(readArg("port", process.env.PORT || 3000));
const HOST = readArg("host", process.env.HOST || "127.0.0.1");
const COMFY_HOST = readArg("comfy-host", process.env.COMFY_HOST || "127.0.0.1");
const COMFY_PORT = Number(readArg("comfy-port", process.env.COMFY_PORT || 8188));
const WEB_DIST = resolve(HERE, readArg("dist", process.env.WEB_DIST || "web/dist"));

/**
 * h3ui.html 所在目录（ComfyUI 自定义节点的 web 目录）。
 * 每台机器的 ComfyUI 安装位置都不一样，所以**不写死**，按这个顺序找：
 *   1) 命令行 --h3ui-dir <路径>
 *   2) 环境变量 H3UI_DIR
 *   3) 本文件同目录下的 .h3ui-dir（一行路径；已在 .gitignore 里，不会被提交）
 * 三个都没有时返回 null —— 画布本身照常可用，只有 h3ui 页面会给出提示。
 */
function resolveH3uiDir() {
    const fromArgOrEnv = readArg("h3ui-dir", process.env.H3UI_DIR);
    if (fromArgOrEnv) return resolve(fromArgOrEnv);
    try {
        const pointed = readFileSync(resolve(HERE, ".h3ui-dir"), "utf8").trim();
        if (pointed) return resolve(pointed);
    } catch {
        /* 没有这个文件是正常情况 */
    }
    return null;
}

const H3UI_DIR = resolveH3uiDir();
const H3UI_PREFIX = "/extensions/comfyui-minimax-h3-audio-T8/";

/** 画布的前端路由：命中就走 index.html，其余未知路径转给 ComfyUI。 */
const SPA_ROUTES = ["/", "/image", "/h3ui", "/assets", "/prompts", "/canvas", "/config"];

const MIME = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".mjs": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".svg": "image/svg+xml",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".webp": "image/webp",
    ".gif": "image/gif",
    ".ico": "image/x-icon",
    ".woff": "font/woff",
    ".woff2": "font/woff2",
    ".map": "application/json; charset=utf-8",
};

/** 跨源代理要补的宽松头（与 @basketikun/canvas-proxy 行为一致）。 */
const CORS_HEADERS = {
    "access-control-allow-origin": "*",
    "access-control-allow-methods": "*",
    "access-control-allow-headers": "*",
    "access-control-expose-headers": "*",
    "access-control-max-age": "86400",
};

function sendJson(response, status, payload) {
    response.writeHead(status, { ...CORS_HEADERS, "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify(payload));
}

function sendText(response, status, text) {
    response.writeHead(status, { ...CORS_HEADERS, "content-type": "text/plain; charset=utf-8" });
    response.end(text);
}

/** 目标地址形如 /https://host/path —— 与 canvas-proxy 的用法完全一致。 */
function readProxyTarget(rawUrl) {
    let target = rawUrl.slice(1);
    try {
        target = decodeURI(target);
    } catch {
        /* 转义损坏时按原样处理 */
    }
    // 有些客户端会把嵌入地址里的 "//" 折叠掉，这里补回来
    target = target.replace(/^(https?:)\/*/i, "$1//");
    return /^https?:\/\/[^/]/i.test(target) ? target : "";
}

/** h3ui 页面：从磁盘读，带 charset 且剥掉会挡跨源读取的头（与 h3ui 自带 proxy.js 同口径）。 */
function serveH3ui(response, pathname) {
    if (!H3UI_DIR) {
        return sendText(
            response,
            404,
            "还没有配置 h3ui 目录（序列帧生成器页面）。\n" +
                "请任选一种方式指定 ComfyUI 自定义节点的 web 目录：\n" +
                "  · node serve.mjs --h3ui-dir \"D:\\ComfyUI\\custom_nodes\\comfyui-minimax-h3-audio-T8\\web\"\n" +
                "  · 设置环境变量 H3UI_DIR\n" +
                "  · 在本项目根目录建一个 .h3ui-dir 文件，里面只写一行该路径\n" +
                "画布功能不受影响，可以直接使用。"
        );
    }
    const relative = decodeURIComponent(pathname.slice(H3UI_PREFIX.length).split("?")[0]);
    if (relative.includes("..")) return sendText(response, 400, "bad path");
    const file = join(H3UI_DIR, relative || "h3ui.html");
    if (!existsSync(file) || !statSync(file).isFile()) {
        return sendText(response, 404, `h3ui 文件不存在：${file}\n请确认 H3UI_DIR 指向 ComfyUI 自定义节点的 web 目录。`);
    }
    const type = MIME[extname(file).toLowerCase()] || "application/octet-stream";
    response.writeHead(200, { "content-type": type, "cache-control": "no-store" });
    createReadStream(file).pipe(response);
}

/** 画布静态文件：命中真实文件就发文件，否则对 SPA 路由发 index.html。 */
function serveStatic(response, pathname) {
    const relative = decodeURIComponent(pathname).replace(/^\/+/, "");
    const file = join(WEB_DIST, relative);
    if (relative && !relative.includes("..") && existsSync(file) && statSync(file).isFile()) {
        const type = MIME[extname(file).toLowerCase()] || "application/octet-stream";
        response.writeHead(200, { "content-type": type });
        createReadStream(file).pipe(response);
        return true;
    }
    const fallback = join(WEB_DIST, "index.html");
    if (!existsSync(fallback)) {
        sendText(response, 500, `没找到画布构建产物：${fallback}\n请先执行  cd web && bun run build`);
        return true;
    }
    response.writeHead(200, { "content-type": MIME[".html"] });
    createReadStream(fallback).pipe(response);
    return true;
}

/**
 * 带一次重试的 fetch。
 * 本机 Clash 代理环境下，node 对某个外部域名的**第一次**解析/连接偶发直接失败
 * （实测：同一个地址第一次 fetch failed，紧接着第二次就成功），所以失败后退一步再试一次。
 */
async function fetchWithRetry(target, options) {
    try {
        return await fetch(target, options);
    } catch (error) {
        const reason = error instanceof Error ? error.message : String(error);
        console.log(`  · 首次请求失败（${reason}），重试一次：${target}`);
        await new Promise((done) => setTimeout(done, 200));
        return await fetch(target, options);
    }
}

/** 代理模式：原样转发到任意目标地址，并补宽松 CORS 头（替代独立的 canvas-proxy）。 */
async function proxyForward(request, response, target) {
    const headers = {};
    for (const [key, value] of Object.entries(request.headers)) {
        if (["host", "connection", "content-length", "accept-encoding", "origin", "referer"].includes(key)) continue;
        headers[key] = Array.isArray(value) ? value.join(", ") : value;
    }
    const hasBody = !["GET", "HEAD"].includes(request.method || "GET");
    const body = hasBody ? await new Promise((done, fail) => {
        const chunks = [];
        request.on("data", (chunk) => chunks.push(chunk));
        request.on("end", () => done(Buffer.concat(chunks)));
        request.on("error", fail);
    }) : undefined;

    try {
        const upstream = await fetchWithRetry(target, { method: request.method, headers, body, redirect: "follow" });
        const out = { ...CORS_HEADERS };
        upstream.headers.forEach((value, key) => {
            if (["content-encoding", "content-length", "transfer-encoding", "connection", "keep-alive"].includes(key)) return;
            if (key.startsWith("access-control-")) return;
            out[key] = value;
        });
        response.writeHead(upstream.status, out);
        if (!upstream.body) return response.end();
        Readable.fromWeb(upstream.body).pipe(response);
        return undefined;
    } catch (error) {
        const reason = error instanceof Error ? error.message : String(error);
        console.log(`  ✗ 代理转发失败 ${target} -> ${reason}`);
        return sendJson(response, 502, { error: reason });
    }
}

/** ComfyUI 转发：原样透传（含二进制），并剥掉会挡跨源读取的头。 */
function comfyForward(request, response) {
    const upstream = httpRequest(
        { host: COMFY_HOST, port: COMFY_PORT, path: request.url, method: request.method, headers: request.headers },
        (upstreamResponse) => {
            const headers = { ...upstreamResponse.headers };
            delete headers["x-frame-options"];
            delete headers["content-security-policy"];
            response.writeHead(upstreamResponse.statusCode || 502, headers);
            upstreamResponse.pipe(response);
        },
    );
    upstream.on("error", (error) => {
        // ComfyUI 没启动时给出明确的 502，前端据此显示"未就绪"而不是白屏
        sendText(response, 502, `ComfyUI 未就绪（${COMFY_HOST}:${COMFY_PORT}）：${error.message}`);
    });
    request.pipe(upstream);
}

function handleRequest(request, response) {
    const rawUrl = request.url || "/";
    const pathname = rawUrl.split("?")[0];

    if (request.method === "OPTIONS") {
        response.writeHead(204, CORS_HEADERS);
        return response.end();
    }

    // 1) AI 接口跨源代理
    const target = readProxyTarget(rawUrl);
    if (target) {
        console.log(`${new Date().toLocaleTimeString()} [代理] ${request.method} ${target}`);
        void proxyForward(request, response, target);
        return;
    }

    // 2) h3ui 页面（必须与下面的 ComfyUI 转发同源）
    if (pathname.startsWith(H3UI_PREFIX)) return serveH3ui(response, rawUrl);

    // 3) 画布静态文件
    const relative = decodeURIComponent(pathname).replace(/^\/+/, "");
    if (relative && existsSync(join(WEB_DIST, relative))) return serveStatic(response, pathname);

    // 4) 画布前端路由 → index.html
    if (SPA_ROUTES.includes(pathname) || pathname.startsWith("/canvas/")) return serveStatic(response, pathname);

    // 5) 其余交给 ComfyUI（/prompt /view /upload/image /history/... /system_stats /<workflow-hash> 等）
    if (pathname === "/status") {
        return sendJson(response, 200, { app: "infinite-canvas", service: "single", port: PORT, comfy: `${COMFY_HOST}:${COMFY_PORT}`, dist: WEB_DIST, h3ui: H3UI_DIR });
    }
    comfyForward(request, response);
}

// WebSocket（h3ui 靠 /ws 收生成进度）：原样隧道给 ComfyUI
function handleUpgrade(request, socket, head) {
    const upstream = netConnect(COMFY_PORT, COMFY_HOST, () => {
        const lines = [`${request.method} ${request.url} HTTP/1.1`];
        for (let i = 0; i < request.rawHeaders.length; i += 2) lines.push(`${request.rawHeaders[i]}: ${request.rawHeaders[i + 1]}`);
        upstream.write(`${lines.join("\r\n")}\r\n\r\n`);
        if (head && head.length) upstream.write(head);
        socket.pipe(upstream);
        upstream.pipe(socket);
    });
    upstream.on("error", () => socket.destroy());
    socket.on("error", () => upstream.destroy());
}

/**
 * 同一个进程监听多个端口。
 * 主端口承担全部职责；
 * 额外监听 23210 纯粹是为了兼容画布里已有的「本地代理」配置（默认值就是它），
 * 这样不用去改浏览器里那项设置也能直接生图。
 */
const COMPAT_PORTS = [Number(process.env.COMPAT_PROXY_PORT || 23210)].filter((port) => port && port !== PORT);

function listen(port, label) {
    const instance = createServer(handleRequest);
    instance.on("upgrade", handleUpgrade);
    instance.on("error", (error) => console.log(`  ✗ ${label} 端口 ${port} 启动失败：${error.message}`));
    instance.listen(port, HOST, () => console.log(`  ${label.padEnd(12)} http://${HOST}:${port}`));
    return instance;
}

console.log("无限画布 单服务已启动（一个进程，承担画布 + h3ui + 接口转发 + 跨源代理）");
listen(PORT, "主服务");
for (const port of COMPAT_PORTS) listen(port, "代理兼容");
console.log(`  静态产物      ${WEB_DIST}`);
console.log(`  h3ui 目录     ${H3UI_DIR || "未配置（画布可用；要用序列帧生成器请设 H3UI_DIR）"}`);
console.log(`  ComfyUI       ${COMFY_HOST}:${COMFY_PORT}（需另起）`);
console.log(`  自检          http://${HOST}:${PORT}/status`);
