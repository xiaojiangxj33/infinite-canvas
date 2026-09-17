import { Button } from "antd";
import { ExternalLink, RefreshCw, ServerCrash } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

/**
 * h3ui（游戏特效序列帧生成器）由本项目的单服务入口（serve.mjs）在**同一端口**上伺服，
 * 并由它把 h3ui 需要的 ComfyUI 接口（/prompt、/view、/upload/image、/history、/system_stats、/ws）
 * 一并转发给真正的 ComfyUI(8188)。
 *
 * 这里一律用**相对路径**，三个好处：
 *   1) h3ui 的 origin 就是本服务的 origin —— 它内部拿 window.location.origin 当 ComfyUI 地址，
 *      同源就不会被浏览器跨域拦；
 *   2) 换端口、换主机都不用改代码；
 *   3) 不再需要单独的 h3ui 转接服务。
 *
 * 历史教训（实测过，别再改回去）：把 iframe 直接指向 ComfyUI 的 8188 时，
 * 真实浏览器里 iframe 那个文档加载不出来（标题为空、body 为空）→ 白屏；
 * 换成"本服务同源伺服 + 转发"后完全正常。
 */
const H3UI_URL = "/extensions/comfyui-minimax-h3-audio-T8/h3ui.html";
/** 单服务会把 /system_stats 转发给 ComfyUI；ComfyUI 没起时它返回 502。 */
const HEALTH_URL = "/system_stats";

/** 整合包自定义节点多，ComfyUI 冷启动约 200 秒 —— 检测得等得住，不能几秒就判死。 */
const PROBE_TIMEOUT_MS = 4000;
/** 未就绪时每隔这么久自动重探一次，起来后自动进入。 */
const AUTO_RECHECK_MS = 5000;

type Status = "checking" | "ready" | "offline";

/**
 * 探测 ComfyUI 是否就绪。
 *
 * 同源请求，所以不需要 no-cors 那套兜底：能解析出 JSON 才算真就绪，
 * 转发层的 502 不会被误判成"已启动"。
 */
async function probeComfy(): Promise<boolean> {
    try {
        const response = await fetch(HEALTH_URL, { cache: "no-store", signal: AbortSignal.timeout(PROBE_TIMEOUT_MS) });
        if (!response.ok) return false;
        await response.json();
        return true;
    } catch {
        return false;
    }
}

export default function H3uiPage() {
    const { t } = useTranslation();

    // ?src= 可覆盖 iframe 地址，便于本地用 tools/h3ui/proxy.js 从磁盘调试时指定
    const src = useMemo(() => new URLSearchParams(window.location.search).get("src") || H3UI_URL, []);

    const [status, setStatus] = useState<Status>("checking");
    // 手动点「仍然进入」后就不再拦；探测成功也会置上
    const [entered, setEntered] = useState(false);
    const probingRef = useRef(false);
    const enteredRef = useRef(false);
    enteredRef.current = entered;

    const check = useCallback(async () => {
        if (probingRef.current) return;
        probingRef.current = true;
        setStatus("checking");
        const ready = await probeComfy();
        probingRef.current = false;
        if (ready) {
            setStatus("ready");
            setEntered(true);
            return;
        }
        setStatus(enteredRef.current ? "ready" : "offline");
    }, []);

    useEffect(() => {
        void check();
    }, [check]);

    // 未就绪时自动重探：ComfyUI 一启动就会自己进去
    useEffect(() => {
        if (status !== "offline" || entered) return;
        const timer = window.setInterval(() => void check(), AUTO_RECHECK_MS);
        return () => window.clearInterval(timer);
    }, [status, entered, check]);

    if (entered) {
        return (
            <div className="relative h-full w-full">
                <iframe src={src} title={t("navigation.h3ui")} className="h-full w-full border-0 bg-white" allow="clipboard-read; clipboard-write" />
                <div className="absolute right-3 top-3 z-10 flex items-center gap-1 rounded-md border border-stone-200/80 bg-background/85 px-1.5 py-1 opacity-60 backdrop-blur transition hover:opacity-100 dark:border-stone-700/80">
                    <span
                        className={`size-1.5 rounded-full ${status === "ready" ? "bg-emerald-500" : "bg-amber-500"}`}
                        title={status === "ready" ? t("h3ui.ready") : t("h3ui.checking")}
                    />
                    <Button type="text" size="small" icon={<RefreshCw className="size-3.5" />} onClick={() => void check()} title={t("h3ui.recheck")} />
                    <Button type="text" size="small" icon={<ExternalLink className="size-3.5" />} href={src} target="_blank" rel="noreferrer" title={t("h3ui.openExternal")} />
                </div>
            </div>
        );
    }

    return (
        <div className="flex h-full w-full items-center justify-center p-6">
            <div className="max-w-md text-center">
                <ServerCrash className={`mx-auto size-10 ${status === "checking" ? "animate-pulse text-stone-400" : "text-amber-500"}`} />
                <div className="mt-4 text-base font-medium">{status === "checking" ? t("h3ui.checking") : t("h3ui.offlineTitle")}</div>
                <p className="mt-2 text-sm leading-6 text-stone-500">{t("h3ui.offlineDescription")}</p>
                <div className="mt-5 flex flex-wrap items-center justify-center gap-2">
                    <Button type="primary" icon={<RefreshCw className="size-4" />} loading={status === "checking"} onClick={() => void check()}>
                        {t("h3ui.recheck")}
                    </Button>
                    <Button onClick={() => setEntered(true)}>{t("h3ui.enterAnyway")}</Button>
                    <Button type="text" icon={<ExternalLink className="size-4" />} href={src} target="_blank" rel="noreferrer">
                        {t("h3ui.openExternal")}
                    </Button>
                </div>
                <div className="mt-4 truncate text-xs text-stone-400" title={src}>
                    {src}
                </div>
            </div>
        </div>
    );
}
