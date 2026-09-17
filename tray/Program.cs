// 无限画布 · 托盘守护程序
//
// 为什么要有它：原来靠 启动无限画布.bat 起服务，那个黑色控制台窗口一点叉就整个服务没了，
// 而且崩了不会自己起来。这个托盘程序把服务收进托盘：
//   · 没有控制台窗口，不会误关
//   · 每 4 秒自检一次 http://127.0.0.1:3000/status，连续失败就自动重启服务
//   · 服务进程自己退出（崩溃/被结束）也会自动拉起
//   · 托盘图标右键：打开页面 / 重启 / 看日志 / 开机自启 / 退出
//
// 编译（无需装任何东西，csc 是 Windows 自带的）：
//   csc /target:winexe /out:无限画布托盘.exe /win32icon:logo.ico
//       /r:System.Windows.Forms.dll /r:System.Drawing.dll tray\Program.cs
//
// 注意：本文件含中文，必须保存成 **UTF-8 带 BOM**，否则 csc 会按系统 ANSI 码页解析成乱码。
// （和 启动游戏特效序列帧生成器.bat 里给 .ps1 补 BOM 是同一个道理。）

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class TrayProgram
{
    const string AppName = "无限画布";
    const string Url = "http://localhost:3000";
    const string StatusUrl = "http://127.0.0.1:3000/status";
    const int Port = 3000;
    const int CheckIntervalMs = 4000;
    const int FailThreshold = 3;          // 连续几次自检失败才判定"挂了"，避免瞬时抖动就重启
    const string AutoStartName = "无限画布本地服务";

    static NotifyIcon _tray;
    static Process _child;
    static System.Windows.Forms.Timer _timer;
    static string _dir;                   // 项目根目录（本 exe 所在目录）
    static string _logPath;
    static int _failStreak;
    static int _restarts;
    static bool _opened;                  // 是否已经自动打开过页面
    static DateTime _lastStart = DateTime.MinValue;
    static string _state = "启动中";
    static Mutex _single;

    // 控制台窗口相关
    static Form _win;
    static TextBox _logBox;
    static SpecTile _tileStatus;
    static SpecTile _tileInterval;
    static SpecTile _tileRestarts;
    static SpecTile _tileH3ui;
    static PillButton _autoBox;
    static bool _reallyExit;              // true 时才真的关闭窗口；平时点叉只是缩回托盘
    static bool _hideHintShown;

    [STAThread]
    static void Main()
    {
        // 单实例：重复双击时只提示，不再起第二个
        bool createdNew;
        _single = new Mutex(true, "InfiniteCanvasTraySingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("无限画布托盘已经在运行了，看右下角通知区域。", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _logPath = Path.Combine(_dir, "tray.log");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Log("---- 托盘启动，项目目录 " + _dir);

        // 先把可能残留的旧实例接管掉，保证 3000 端口是自己的
        ReclaimPort();

        BuildWindow();          // 先建好窗口，后面日志才能实时进去
        BuildTray();
        StartService();
        StartTimer();

        // 像原来的 CMD 窗口一样，启动时把控制台显示出来；
        // 但它点叉只是缩回托盘，服务照跑。
        ShowWindow();

        Application.Run();
    }

    // ------------------------------------------------------------ 托盘

    static void BuildTray()
    {
        _tray = new NotifyIcon();
        _tray.Icon = LoadIcon();
        _tray.Text = AppName + " · 启动中";
        _tray.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add(MenuItem("打开控制台", delegate { ShowWindow(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("打开无限画布", delegate { Open(Url); }));
        menu.Items.Add(MenuItem("打开序列帧生成器", delegate { Open(Url + "/h3ui"); }));
        menu.Items.Add(new ToolStripSeparator());
        var statusItem = new ToolStripMenuItem("状态：启动中");
        statusItem.Enabled = false;
        statusItem.Name = "status";
        menu.Items.Add(statusItem);
        menu.Items.Add(MenuItem("立即重启服务", delegate { Restart("手动重启"); }));
        menu.Items.Add(MenuItem("打开日志文件", delegate { OpenLog(); }));
        menu.Items.Add(MenuItem("打开项目目录", delegate { Open(_dir); }));
        menu.Items.Add(new ToolStripSeparator());
        var auto = new ToolStripMenuItem("开机自动启动");
        auto.Checked = IsAutoStart();
        auto.Click += delegate
        {
            SetAutoStart(!IsAutoStart());
            auto.Checked = IsAutoStart();
        };
        menu.Items.Add(auto);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("退出（停止服务）", delegate { Shutdown(); }));

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += delegate { ShowWindow(); };
    }

    // ------------------------------------------------------------ 控制台窗口
    //
    //  按 Launch Keynote（发布会主题）排版：
    //    · 纯黑 #000000 舞台，全窗口只有一处抬升面（日志卡片 #1D1D1F rounded-2xl）
    //    · 一屏一句话：hero 只写产品名 + 一句说明
    //    · 规格用大数字网格呈现，关键数字用唯一强调色 #2997FF
    //    · 全窗口唯一 CTA 是「打开无限画布」（电蓝胶囊），其余按钮是 #1D1D1F + hairline 描边
    //    · 不用渐变、不用第二支强调色、不做嵌套卡片、不用玻璃态

    static SpecTile NewTile(string label, string value, bool accent, int width)
    {
        var t = new SpecTile();
        t.Width = width;
        t.Margin = new Padding(0, 0, 40, 0);
        t.Set(label, value, accent);
        return t;
    }

    static void ApplyRounded(Control c, int radius)
    {
        try
        {
            using (var path = Theme.Rounded(new Rectangle(0, 0, c.Width, c.Height), radius))
                c.Region = new Region(path);
        }
        catch { }
    }

    static void BuildWindow()
    {
        if (_win != null) return;

        var f = new Form();
        f.Text = AppName + " · 控制台";
        f.Size = new Size(1120, 720);
        f.MinimumSize = new Size(660, 420);
        f.StartPosition = FormStartPosition.CenterScreen;
        f.BackColor = Theme.Stage;                 // 纯黑舞台
        f.ForeColor = Theme.Ink;
        try { f.Icon = LoadIcon(); } catch { }

        // 点叉不退出，只缩回托盘；要真退出得走托盘菜单里的"退出"
        f.FormClosing += delegate(object s, FormClosingEventArgs e)
        {
            if (_reallyExit) return;
            e.Cancel = true;
            f.Hide();
            if (!_hideHintShown)
            {
                _hideHintShown = true;
                _tray.ShowBalloonTip(3500, AppName,
                    "窗口已缩到右下角托盘，服务照常在跑。\n要彻底退出请右键托盘图标 →「退出（停止服务）」。",
                    ToolTipIcon.Info);
            }
        };

        var layout = new TableLayoutPanel();
        layout.Dock = DockStyle.Fill;
        layout.BackColor = Theme.Stage;
        layout.ColumnCount = 1;
        layout.RowCount = 4;
        layout.Padding = new Padding(44, 34, 44, 26);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));   // hero
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));    // 规格格
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));    // 日志卡片
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // 按钮行（可换行，高度自适应）

        // --- Hero：一屏一句话 ---
        var hero = new Panel();
        hero.Dock = DockStyle.Fill;
        hero.BackColor = Theme.Stage;

        // hero 用自绘文本块：Label 在纯黑窗口里出现过整块涂白的渲染问题
        var title = new TextBlock(Theme.Ui(26f, FontStyle.Bold), Theme.Ink);   // 巨字 hero
        title.Location = new Point(0, 0);
        title.Set("无限画布");

        var sub = new TextBlock(Theme.Ui(10.5f, FontStyle.Regular), Theme.Muted);
        sub.Location = new Point(3, 56);
        sub.Set("本地单服务 —— 画布 · 序列帧生成器 · 接口转发 · 跨源代理");

        hero.Controls.Add(title);
        hero.Controls.Add(sub);

        // --- 规格格：大数字，冷静克制可信 ---
        var specs = new FlowLayoutPanel();
        specs.Dock = DockStyle.Fill;
        specs.BackColor = Theme.Stage;
        specs.FlowDirection = FlowDirection.LeftToRight;
        specs.WrapContents = false;
        specs.Padding = new Padding(0, 10, 0, 0);

        _tileStatus = NewTile("服务状态", "启动中", true, 168);
        _tileInterval = NewTile("自检间隔", "4 秒", true, 118);
        _tileRestarts = NewTile("已自动重启", "0 次", true, 142);
        _tileH3ui = NewTile("序列帧生成器", "检测中", false, 168);
        specs.Controls.Add(_tileStatus);
        specs.Controls.Add(_tileInterval);
        specs.Controls.Add(_tileRestarts);
        specs.Controls.Add(_tileH3ui);

        // --- 日志：全窗口唯一的一张抬升面（#1D1D1F rounded-2xl）---
        var card = new Panel();
        card.Dock = DockStyle.Fill;
        card.BackColor = Theme.Elevated;
        card.Padding = new Padding(20, 16, 10, 12);
        card.Resize += delegate { ApplyRounded(card, 16); };

        _logBox = new TextBox();
        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.WordWrap = true;
        _logBox.BorderStyle = BorderStyle.None;          // 不要输入框描边，融入卡片
        _logBox.BackColor = Theme.Elevated;
        _logBox.ForeColor = Theme.Ink;
        _logBox.Font = Theme.Mono(9.5f, FontStyle.Regular);
        card.Controls.Add(_logBox);
        // 滚动条切深色：系统默认给的是浅色轨道，压在深色卡片上会成一条白条
        _logBox.HandleCreated += delegate { Theme.UseDarkScrollbars(_logBox); };
        Theme.UseDarkScrollbars(_logBox);
        f.Shown += delegate { Theme.UseDarkScrollbars(_logBox); };

        // --- 按钮行 ---
        // 允许换行 + 行高自适应：窗口变窄时按钮折到第二行，不会横向溢出。
        var bottom = new FlowLayoutPanel();
        bottom.Dock = DockStyle.Top;
        bottom.AutoSize = true;
        bottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        bottom.BackColor = Theme.Stage;
        bottom.FlowDirection = FlowDirection.LeftToRight;
        bottom.WrapContents = true;
        bottom.Padding = new Padding(0, 20, 0, 0);

        bottom.Controls.Add(NewPill("打开无限画布", true, false, 158, delegate { Open(Url); }));
        bottom.Controls.Add(NewPill("序列帧生成器", false, false, 116, delegate { Open(Url + "/h3ui"); }));
        bottom.Controls.Add(NewPill("重启服务", false, false, 92, delegate { Restart("手动重启"); }));
        bottom.Controls.Add(NewPill("清空日志", false, false, 84, delegate { if (_logBox != null) _logBox.Text = ""; }));
        bottom.Controls.Add(NewPill("打开日志文件", false, false, 106, delegate { OpenLog(); }));

        // 开机自启做成同款胶囊开关：只在真实点击时才写注册表
        _autoBox = new PillButton("开机自动启动", false, true);
        _autoBox.Width = 118;
        _autoBox.Margin = new Padding(0, 4, 10, 0);
        _autoBox.Checked = IsAutoStart();
        _autoBox.Click += delegate
        {
            bool on = !IsAutoStart();
            SetAutoStart(on);
            _autoBox.Checked = on;
        };
        bottom.Controls.Add(_autoBox);

        bottom.Controls.Add(NewPill("缩到托盘", false, true, 92, delegate { f.Hide(); }));

        layout.Controls.Add(hero, 0, 0);
        layout.Controls.Add(specs, 0, 1);
        layout.Controls.Add(card, 0, 2);
        layout.Controls.Add(bottom, 0, 3);
        f.Controls.Add(layout);

        _win = f;
        // 提前创建窗口句柄：否则在窗口第一次显示之前调用 BeginInvoke 更新状态会抛异常
        // （启动那一次状态更新就丢了）。
        try { IntPtr h = f.Handle; } catch { }
        // 标题栏与窗口边框切成深色：不然深色界面外围会有一圈浅色的"大白框"
        f.HandleCreated += delegate { Theme.UseDarkTitleBar(f.Handle); };
        Theme.UseDarkTitleBar(f.Handle);

        // 把已经写进文件的日志补进窗口，避免打开是空的
        try
        {
            if (File.Exists(_logPath))
            {
                string[] lines = File.ReadAllLines(_logPath);
                int from = Math.Max(0, lines.Length - 300);
                for (int i = from; i < lines.Length; i++) AppendToWindow(lines[i]);
            }
        }
        catch { }
    }

    static PillButton NewPill(string text, bool primary, bool compact, int width, EventHandler onClick)
    {
        var b = new PillButton(text, primary, compact);
        b.Width = width;
        b.Margin = new Padding(0, primary ? 0 : 4, 8, 0);
        b.Click += onClick;
        return b;
    }

    static void ShowWindow()
    {
        try
        {
            BuildWindow();
            if (!_win.Visible) _win.Show();
            if (_win.WindowState == FormWindowState.Minimized) _win.WindowState = FormWindowState.Normal;
            _win.ShowInTaskbar = true;
            _win.Activate();
            _win.BringToFront();
        }
        catch (Exception ex) { Log("打开控制台失败：" + ex.Message); }
    }

    static ToolStripMenuItem MenuItem(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    static Icon LoadIcon()
    {
        // 优先用项目里的新图标，缺失就退回系统图标
        try
        {
            string ico = Path.Combine(_dir, "logo.ico");
            if (File.Exists(ico)) return new Icon(ico);
        }
        catch { }
        return SystemIcons.Application;
    }

    static void SetStatus(string text, bool healthy)
    {
        _state = text;
        if (_tray != null)
        {
            string tip = AppName + " · " + text;
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;   // NotifyIcon.Text 上限 63
            if (_tray.ContextMenuStrip != null)
            {
                var item = _tray.ContextMenuStrip.Items["status"];
                if (item != null) item.Text = "状态：" + text;
            }
        }
        // 同步到控制台窗口的规格格。关键数字用唯一强调色 #2997FF；
        // 状态同时用文字表达，不靠颜色单独传递（无障碍要求）。
        if (_tileStatus != null)
        {
            try
            {
                _tileStatus.BeginInvoke((MethodInvoker)delegate
                {
                    _tileStatus.Set("服务状态", text, healthy);
                    if (_tileInterval != null) _tileInterval.Set("自检间隔", (CheckIntervalMs / 1000) + " 秒", true);
                    if (_tileRestarts != null) _tileRestarts.Set("已自动重启", _restarts + " 次", true);
                    if (_tileH3ui != null) _tileH3ui.Set("序列帧生成器", H3uiDirConfigured() ? "已配置" : "未配置", false);
                });
            }
            catch { }
        }
        Log("状态 -> " + text);
    }

    /// <summary>只用于界面上显示 h3ui 是否配置好（读 .h3ui-dir 是否存在）。</summary>
    static bool H3uiDirConfigured()
    {
        try { return File.Exists(Path.Combine(_dir, ".h3ui-dir")); }
        catch { return false; }
    }

    // ------------------------------------------------------------ 服务进程

    static string FindNode()
    {
        string[] candidates = new string[]
        {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\nodejs\node.exe"),
        };
        foreach (string c in candidates) if (File.Exists(c)) return c;
        return "node.exe";   // 交给 PATH
    }

    static void StartService()
    {
        if (_child != null && !_child.HasExited) return;

        string script = Path.Combine(_dir, "serve.mjs");
        if (!File.Exists(script))
        {
            SetStatus("找不到 serve.mjs", false);
            _tray.ShowBalloonTip(5000, AppName, "在本程序所在目录找不到 serve.mjs，请把它放到项目根目录。", ToolTipIcon.Error);
            return;
        }
        if (!File.Exists(Path.Combine(_dir, "web", "dist", "index.html")))
        {
            SetStatus("缺少构建产物", false);
            _tray.ShowBalloonTip(6000, AppName,
                "缺少 web\\dist，画布页面无法显示。请先运行「重新构建并启动.bat」构建一次。", ToolTipIcon.Warning);
        }

        try
        {
            var psi = new ProcessStartInfo();
            psi.FileName = FindNode();
            psi.Arguments = "serve.mjs";
            psi.WorkingDirectory = _dir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            // node 往管道里写的是 UTF-8；不显式指定的话 .NET 会按系统 ANSI（本机是 GBK）解，
            // 日志里的中文就全变成乱码了。
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            _child = new Process();
            _child.StartInfo = psi;
            _child.EnableRaisingEvents = true;
            _child.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Log("  " + e.Data); };
            _child.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Log("  [err] " + e.Data); };
            _child.Exited += delegate { Log("服务进程已退出（退出码 " + SafeExitCode() + "）"); };
            _child.Start();
            _child.BeginOutputReadLine();
            _child.BeginErrorReadLine();
            _lastStart = DateTime.Now;
            Log("已启动服务进程 PID " + _child.Id);
            SetStatus("启动中", false);
        }
        catch (Exception ex)
        {
            Log("启动服务失败：" + ex.Message);
            SetStatus("启动失败", false);
            _tray.ShowBalloonTip(5000, AppName, "启动服务失败：" + ex.Message, ToolTipIcon.Error);
        }
    }

    static int SafeExitCode()
    {
        try { return _child.ExitCode; } catch { return -1; }
    }

    static void StopService()
    {
        try
        {
            if (_child != null && !_child.HasExited)
            {
                Log("正在停止服务进程 PID " + _child.Id);
                // 连同子进程一起结束（serve.mjs 本身不派生子进程，稳妥起见还是带上）
                try
                {
                    var psi = new ProcessStartInfo("taskkill", "/PID " + _child.Id + " /T /F");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    Process.Start(psi).WaitForExit(5000);
                }
                catch { try { _child.Kill(); } catch { } }
                _child.WaitForExit(5000);
            }
        }
        catch { }
    }

    static void Restart(string why)
    {
        Log("重启服务：" + why);
        StopService();
        _failStreak = 0;
        Thread.Sleep(600);
        ReclaimPort();
        StartService();
    }

    /// <summary>
    /// 如果 3000 端口被别的进程听着（通常是上一次没退干净的 serve.mjs），先接管掉。
    /// 只杀进程名是 node 的，避免误伤其它程序。
    /// </summary>
    static void ReclaimPort()
    {
        int pid = FindListenerPid();
        if (pid <= 0) return;
        try
        {
            var p = Process.GetProcessById(pid);
            if (p.ProcessName.ToLower() == "node")
            {
                Log("3000 端口被残留的 node 进程占用（PID " + pid + "），接管");
                try
                {
                    var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    Process.Start(psi).WaitForExit(5000);
                }
                catch { try { p.Kill(); } catch { } }
                Thread.Sleep(700);
            }
            else
            {
                Log("3000 端口被非 node 进程占用（PID " + pid + " " + p.ProcessName + "），不动它");
            }
        }
        catch { }
    }

    static int FindListenerPid()
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            var p = Process.Start(psi);
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                if (line.IndexOf("127.0.0.1:" + Port) >= 0 && line.IndexOf("LISTENING") >= 0)
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int pid;
                    if (parts.Length >= 1 && int.TryParse(parts[parts.Length - 1], out pid)) return pid;
                }
            }
            p.WaitForExit(4000);
        }
        catch { }
        return 0;
    }

    // ------------------------------------------------------------ 自检

    static void StartTimer()
    {
        _timer = new System.Windows.Forms.Timer();
        _timer.Interval = CheckIntervalMs;
        _timer.Tick += delegate { Tick(); };
        _timer.Start();
    }

    static void Tick()
    {
        bool alive = _child != null && !_child.HasExited;
        bool healthy = IsHealthy();

        if (!alive)
        {
            // 进程没了：等 1.5 秒再拉，避免启动失败时疯狂重试
            if ((DateTime.Now - _lastStart).TotalMilliseconds < 1500) return;
            _restarts++;
            SetStatus("进程已退出，正在重启", false);
            Restart("服务进程不存在");
            return;
        }

        if (healthy)
        {
            if (_failStreak > 0) Log("自检恢复正常");
            _failStreak = 0;
            SetStatus("运行中" + (_restarts > 0 ? "（已自动重启 " + _restarts + " 次）" : ""), true);
            if (!_opened)
            {
                _opened = true;
                Log("服务就绪，打开页面");
                Open(Url);
            }
            return;
        }

        _failStreak++;
        Log("自检失败 " + _failStreak + "/" + FailThreshold);
        if (_failStreak >= FailThreshold)
        {
            _restarts++;
            SetStatus("自检失败，正在重启", false);
            _tray.ShowBalloonTip(4000, AppName, "服务自检失败，正在自动重启。", ToolTipIcon.Warning);
            Restart("自检连续失败");
        }
        else
        {
            SetStatus("自检异常 " + _failStreak + "/" + FailThreshold, false);
        }
    }

    static bool IsHealthy()
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(StatusUrl);
            req.Timeout = 2500;
            req.ReadWriteTimeout = 2500;
            req.Method = "GET";
            // 关键：必须绕过系统代理。本机开着 Clash 之类的代理时，
            // 走代理去访问 127.0.0.1 会被拦掉，自检会误判成"服务挂了"。
            req.Proxy = null;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
            {
                return sr.ReadToEnd().IndexOf("infinite-canvas") >= 0;
            }
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 杂项

    static void Open(string target)
    {
        try
        {
            var psi = new ProcessStartInfo(target);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex) { Log("打开失败：" + ex.Message); }
    }

    static void OpenLog()
    {
        try
        {
            if (!File.Exists(_logPath)) File.WriteAllText(_logPath, "", Encoding.UTF8);
            Open(_logPath);
        }
        catch (Exception ex) { Log("打开日志失败：" + ex.Message); }
    }

    static void Log(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
        try { File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8); }
        catch { }
        AppendToWindow(line);
    }

    /// <summary>
    /// 把一行日志追加到控制台窗口。
    /// 日志可能来自后台线程（子进程输出事件），所以要回到 UI 线程再改控件。
    /// </summary>
    static void AppendToWindow(string line)
    {
        if (_logBox == null) return;
        try
        {
            if (_logBox.InvokeRequired)
            {
                _logBox.BeginInvoke((MethodInvoker)delegate { AppendToWindow(line); });
                return;
            }
            // 简单限长：太长就丢掉前面的，避免常驻几天后窗口越来越大
            if (_logBox.TextLength > 150000) _logBox.Text = "(…较早的日志已省略，完整内容看 tray.log)\r\n";
            _logBox.AppendText(line + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
        catch { }
    }

    static string AutoStartKeyPath()
    {
        return @"Software\Microsoft\Windows\CurrentVersion\Run";
    }

    static bool IsAutoStart()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath(), false))
            {
                return key != null && key.GetValue(AutoStartName) != null;
            }
        }
        catch { return false; }
    }

    static void SetAutoStart(bool on)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath(), true))
            {
                if (key == null) return;
                if (on) key.SetValue(AutoStartName, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else key.DeleteValue(AutoStartName, false);
            }
            Log("开机自启 -> " + (on ? "开" : "关"));
        }
        catch (Exception ex) { Log("设置开机自启失败：" + ex.Message); }
    }

    static void Shutdown()
    {
        Log("退出托盘，停止服务");
        _reallyExit = true;                  // 放行窗口关闭，否则 FormClosing 会拦下它
        try { if (_timer != null) _timer.Stop(); } catch { }
        StopService();
        try { if (_win != null) { _win.Close(); _win.Dispose(); _win = null; } } catch { }
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Application.Exit();
    }
}
