// ─────────────────────────────────────────────────────────────
//  Launch Keynote（发布会主题）· 桌面版风格层
//
//  纯黑舞台 + 唯一电蓝。这里把 Web 版的 token 映射到 WinForms：
//    bg-black      -> Stage   #171717（用户指定，非风格默认的纯黑）
//    bg-[#1D1D1F]  -> Elevated #1D1D1F（全窗口只允许一处：日志卡片）
//    text-[#F5F5F7] -> Ink
//    text-[#86868B] -> Muted
//    #2997FF / #0071E3 -> Accent / AccentPress（全窗口唯一强调色）
//
//  两处必须说明的差异：
//    1) WinForms 的 Font 无法设置 letter-spacing，所以 hero 的 -0.03em 紧字距做不出来，
//       只能用 Segoe UI Semibold + 更大的字号去逼近同样的视觉重量。
//    2) prefers-reduced-motion 在桌面上对应 Windows 的「在 Windows 中显示动画」开关，
//       用 SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) 读取；关掉动画时所有过渡直接到位。
// ─────────────────────────────────────────────────────────────

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Stage = Color.FromArgb(0x17, 0x17, 0x17);   // 按用户要求改为 #171717（与画布卡片同色），有意偏离"纯黑舞台"
    public static readonly Color Elevated = Color.FromArgb(29, 29, 31);
    public static readonly Color ElevatedHover = Color.FromArgb(42, 42, 44);
    public static readonly Color Ink = Color.FromArgb(245, 245, 247);
    public static readonly Color Muted = Color.FromArgb(134, 134, 139);
    public static readonly Color Accent = Color.FromArgb(41, 151, 255);
    public static readonly Color AccentPress = Color.FromArgb(0, 113, 227);
    public static readonly Color Hairline = Color.FromArgb(52, 52, 53);   // white/10 压在 #1D1D1F 上的近似值

    public const int MotionMs = 300;      // duration-300
    public const float PressScale = 0.98f; // active:scale-[0.98]

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    /// <summary>
    /// 把控件自带的滚动条切成深色。
    /// 不做这一步，滚动条仍是系统浅色轨道 —— 一条 17px 宽的亮带压在深色卡片上，
    /// 看起来就是日志旁边"竖着一个白条"。
    /// </summary>
    public static void UseDarkScrollbars(Control control)
    {
        if (control == null) return;
        try
        {
            if (!control.IsHandleCreated) return;
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            // 滚动条在编辑控件里属于非客户区，不是 .NET 意义上的子控件，
            // 所以这里对每个子控件也刷一遍，尽量覆盖到。
            foreach (Control child in control.Controls) UseDarkScrollbars(child);
        }
        catch { }
    }

    /// <summary>
    /// 把窗口标题栏与边框切成深色。
    /// 不做这一步，深色界面顶上会顶着一条系统浅色标题栏、四周还有一圈浅色边框，
    /// 看上去就像"外围有个大白框"。
    /// </summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int on = 1;
            // Win10 20H1 及以后用属性 20；更早的 1809~1909 用 19。两个都试，谁成功算谁。
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// Windows 的「在 Windows 中显示动画」开关。关掉它就等同于 prefers-reduced-motion：
    /// 过渡全部直接到位，不做补间。
    /// </summary>
    public static bool AnimationsEnabled()
    {
        try
        {
            int enabled;
            if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out enabled, 0)) return enabled != 0;
        }
        catch { }
        return true;
    }

    public static Font Ui(float size, FontStyle style)
    {
        // 主题指定系统 SF 级无衬线；Windows 上对应 Segoe UI
        try { return new Font("Segoe UI", size, style); }
        catch { return new Font(FontFamily.GenericSansSerif, size, style); }
    }

    public static Font Mono(float size, FontStyle style)
    {
        try { return new Font("Consolas", size, style); }
        catch { return new Font(FontFamily.GenericMonospace, size, style); }
    }

    /// <summary>圆角矩形路径（用于 rounded-2xl / rounded-full 的绘制）。</summary>
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>三次缓出（ease-out），禁止 bounce / elastic。</summary>
    public static float EaseOut(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Max(0f, Math.Min(1f, t));
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }
}

/// <summary>
/// 主题按钮。主色 = 电蓝 + 白字（唯一 CTA，字号大且粗，
/// 这样 3.04:1 的白字压在蓝上按 WCAG 的"大字"阈值仍然达标）；
/// 辅色 = #1D1D1F + hairline 描边 + 近白字（15:1，稳过 AA）。
/// 形状 rounded-full，过渡 duration-300 ease-out，按下 scale-0.98。
/// </summary>
class PillButton : Control
{
    public bool Primary;
    public bool Compact;
    private bool _checked;
    private bool _hover, _pressed;
    private float _anim;                  // 0..1 悬停过渡
    private float _pressAnim;             // 0..1 按下过渡
    private Timer _timer;

    public PillButton(string text, bool primary, bool compact)
    {
        Primary = primary;
        Compact = compact;
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.Stage;
        if (Primary) { Font = Theme.Ui(14f, FontStyle.Bold); Height = 50; }        // 大号粗体 CTA
        else if (Compact) { Font = Theme.Ui(9.5f, FontStyle.Regular); Height = 38; }
        else { Font = Theme.Ui(10f, FontStyle.Regular); Height = 42; }
        _timer = new Timer();
        _timer.Interval = 16;
        _timer.Tick += delegate { Advance(); };
    }

    /// <summary>当开关用时的高亮态（开机自启）。只在真实点击时由外部改，不做事件回调。</summary>
    public bool Checked
    {
        get { return _checked; }
        set { _checked = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Start(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Start(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Start(); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Start(); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    /// <summary>让空格/回车也能触发（键盘可达）。</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            _pressed = false;
            Invalidate();
            OnClick(EventArgs.Empty);
        }
    }

    private void Start()
    {
        if (!Theme.AnimationsEnabled())
        {
            // 关掉动画时直接到位（等价 prefers-reduced-motion 的降级）
            _anim = _hover ? 1f : 0f;
            _pressAnim = _pressed ? 1f : 0f;
            Invalidate();
            return;
        }
        if (_timer != null && !_timer.Enabled) _timer.Start();
    }

    private void Advance()
    {
        float target = _hover ? 1f : 0f;
        float pTarget = _pressed ? 1f : 0f;
        float step = 16f / Theme.MotionMs;
        float d1 = target - _anim;
        float d2 = pTarget - _pressAnim;
        _anim += Math.Abs(d1) <= step ? d1 : Math.Sign(d1) * step;
        _pressAnim += Math.Abs(d2) <= step ? d2 : Math.Sign(d2) * step;
        Invalidate();
        if (Math.Abs(target - _anim) < 0.001f && Math.Abs(pTarget - _pressAnim) < 0.001f)
        {
            _anim = target; _pressAnim = pTarget;
            if (_timer != null) _timer.Stop();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float eased = Theme.EaseOut(_anim);
        float press = Theme.EaseOut(_pressAnim);

        // active:scale-[0.98] —— 按下时整体缩 2%
        float scale = 1f - (Theme.PressScale == 1f ? 0f : (1f - Theme.PressScale) * press);
        int pad = (int)Math.Round(Width * (1f - scale) / 2f);
        int padY = (int)Math.Round(Height * (1f - scale) / 2f);
        var rect = new Rectangle(pad, padY, Width - pad * 2, Height - padY * 2);

        Color fill;
        Color text;
        if (Primary)
        {
            // hover:bg-[#0071E3]；按下同样是按下态电蓝
            fill = Theme.Mix(Theme.Accent, Theme.AccentPress, Math.Max(eased, press));
            text = Color.White;
        }
        else
        {
            fill = Theme.Mix(Theme.Elevated, Theme.ElevatedHover, Math.Max(eased, press * 0.6f));
            text = Theme.Ink;
        }
        if (Checked && !Primary) { fill = Theme.Mix(fill, Theme.Accent, 0.18f); }

        using (var path = Theme.Rounded(rect, rect.Height / 2))   // rounded-full
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (!Primary)
            {
                using (var pen = new Pen(Theme.Hairline, 1f)) g.DrawPath(pen, path);
            }
            // focus-visible:ring-2 ring-[#2997FF]/60
            if (Focused)
            {
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 2f))
                using (var ring = Theme.Rounded(Rectangle.Inflate(rect, -2, -2), (rect.Height - 4) / 2))
                    g.DrawPath(pen, ring);
            }
        }

        TextRenderer.DrawText(g, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// 纯自绘文本块。hero 的标题/副标题用它，而不用 Label ——
/// Label 在纯黑自绘窗口里出现过"整块涂白、字看不见"的渲染问题，自绘最可控。
/// </summary>
class TextBlock : Control
{
    private string _content = "";
    private Font _font;

    public TextBlock(Font font, Color color)
    {
        _font = font;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Stage;
        ForeColor = color;
    }

    public void Set(string content)
    {
        _content = content == null ? "" : content;
        Size s = TextRenderer.MeasureText(_content, _font);
        Width = s.Width + 4;
        Height = s.Height + 6;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var b = new SolidBrush(Theme.Stage)) g.FillRectangle(b, ClientRectangle);
        TextRenderer.DrawText(g, _content, _font, ClientRectangle, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// 规格项：**同一行**里左边是中灰标签、右边是强调色的值（关键数字用电蓝）。
/// 只占一行，横向排成一条规格带，把纵向空间留给日志。
/// </summary>
class SpecTile : Control
{
    public string Value = "";
    public string Label = "";
    public bool AccentValue = true;

    public SpecTile()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Stage;
        Height = 30;
        Width = 80;
    }

    public void Set(string label, string value, bool accent)
    {
        Label = label;
        Value = value;
        AccentValue = accent;
        // 按内容自适应宽度，让整条规格正好排成一行
        using (Font lf = Theme.Ui(9.5f, FontStyle.Regular))
        using (Font vf = Theme.Ui(13.5f, FontStyle.Bold))
        {
            int lw = TextRenderer.MeasureText(label, lf).Width;
            int vw = TextRenderer.MeasureText(value, vf).Width;
            Width = lw + 10 + vw + 6;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var b = new SolidBrush(Theme.Stage)) g.FillRectangle(b, ClientRectangle);

        Font lf = Theme.Ui(9.5f, FontStyle.Regular);
        Font vf = Theme.Ui(13.5f, FontStyle.Bold);
        Color vc = AccentValue ? Theme.Accent : Theme.Ink;

        Size ls = TextRenderer.MeasureText(Label, lf);
        TextRenderer.DrawText(g, Label, lf,
            new Rectangle(0, (Height - ls.Height) / 2, ls.Width, ls.Height),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(g, Value, vf,
            new Rectangle(ls.Width + 10, 0, Math.Max(10, Width - ls.Width - 10), Height),
            vc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        lf.Dispose(); vf.Dispose();
    }
}
