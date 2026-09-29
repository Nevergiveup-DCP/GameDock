using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--scan-folder")
        {
            Headless.ScanFolder(args[1]);
            return;
        }
        if (args.Length >= 1 && args[0] == "--list")
        {
            Headless.List();
            return;
        }
        if (args.Length >= 2 && args[0] == "--launch")
        {
            Headless.LaunchByName(args[1]);
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new DockForm());
    }
}

static class Headless
{
    static string Dir()
    {
        string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameDock");
        if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        return d;
    }

    public static void ScanFolder(string root)
    {
        DockForm f = new DockForm();
        int n = f.ScanFolderCore(root);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SCAN  " + root);
        sb.AppendLine("added: " + n);
        for (int i = 0; i < f.Lib.Count; i++)
            sb.AppendLine("  " + f.Lib[i].Name + "  |  " + f.Lib[i].Platform + "  |  " + f.Lib[i].Exe);
        File.WriteAllText(Path.Combine(Dir(), "scan_report.txt"), sb.ToString(), Encoding.UTF8);
        f.Dispose();
    }

    public static void List()
    {
        DockForm f = new DockForm();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("LIBRARY  total=" + f.Lib.Count);
        for (int i = 0; i < f.Lib.Count; i++)
            sb.AppendLine("  " + f.Lib[i].Name + "  |  " + f.Lib[i].Platform + "  |  " + f.Lib[i].Status +
                "  |  " + (long)f.Lib[i].PlaySeconds + "s  |  " + f.Lib[i].Exe);
        File.WriteAllText(Path.Combine(Dir(), "list_report.txt"), sb.ToString(), Encoding.UTF8);
        f.Dispose();
    }

    public static void LaunchByName(string name)
    {
        DockForm f = new DockForm();
        int idx = -1;
        for (int i = 0; i < f.Lib.Count; i++) if (f.Lib[i].Name == name) { idx = i; break; }
        StringBuilder sb = new StringBuilder();
        if (idx < 0) { sb.AppendLine("NOT FOUND: " + name); }
        else
        {
            double before = f.Lib[idx].PlaySeconds;
            sb.AppendLine("launching: " + f.Lib[idx].Name);
            sb.AppendLine("exe: " + f.Lib[idx].Exe);
            f.LaunchDirect(idx);
            if (f.IsRunning)
            {
                try { f.WaitRunning(120000); } catch (Exception) { }
                f.StopTrackingPublic();
            }
            sb.AppendLine("play before: " + (long)before + "s");
            sb.AppendLine("play after : " + (long)f.Lib[idx].PlaySeconds + "s");
            sb.AppendLine("delta      : " + (long)(f.Lib[idx].PlaySeconds - before) + "s");
            sb.AppendLine("status     : " + f.Lib[idx].Status);
            sb.AppendLine("launchcnt  : " + f.Lib[idx].LaunchCount);
        }
        File.WriteAllText(Path.Combine(Dir(), "launch_report.txt"), sb.ToString(), Encoding.UTF8);
        f.Dispose();
    }
}

class GameEntry
{
    public string Name = "";
    public string Exe = "";
    public string Args = "";
    public string Platform = "";
    public string Dir = "";
    public string Status = "未分类";
    public string Added = "";
    public string Last = "";
    public double PlaySeconds = 0;
    public int LaunchCount = 0;
    public string StoreName = "";     // 对应星港里的游戏名
    public string InstalledVer = "";  // 本机已安装的版本
    public string AvailVer = "";      // 星港上的最新版本
    public string AppId = "";         // Steam 编号，用来拉官方封面
    public string Cover = "";         // 封面图片在本机的路径
    public bool HideConsole = false;  // 控制台程序：启动时是否隐藏那个黑窗口
    public string Note = "";          // 我停在哪：进度备忘
    public string NoteTime = "";      // 备忘记录时间
}

// 星港发布的构建包
class ShipBuild
{
    public string Ver = "";
    public string File = "";
    public long Size = 0;
    public string Time = "";
    public string Note = "";
    public string Sha = "";
}

// 星港上的一个作品
class ShipGame
{
    public string Name = "", Genre = "", Desc = "", Dev = "", Rating = "";
    public string Stage = "", PrevEnd = "", CurVer = "";
    public List<ShipBuild> Vers = new List<ShipBuild>();
}

class TLEntry
{
    public DateTime T;
    public string Text = "";
}

class Click
{
    public Rectangle R;
    public int Id;
}

class DockForm : Form
{
    Color BG = Color.FromArgb(14, 19, 28);
    Color PANEL = Color.FromArgb(210, 18, 28, 46);
    Color NEON = Color.FromArgb(34, 211, 238);
    Color NEON2 = Color.FromArgb(167, 139, 250);
    Color TXT = Color.FromArgb(232, 240, 254);
    Color DIM = Color.FromArgb(178, 193, 212);
    Color OK = Color.FromArgb(74, 222, 128);
    Color WARN = Color.FromArgb(251, 191, 36);
    Color DANGER = Color.FromArgb(248, 113, 113);

    List<GameEntry> lib = new List<GameEntry>();
    public List<GameEntry> Lib { get { return lib; } }
    public bool IsRunning { get { return runningProc != null && !runningProc.HasExited; } }

    public void WaitRunning(int ms)
    {
        if (runningProc == null) return;
        try { runningProc.WaitForExit(ms); } catch (Exception) { }
    }

    public void StopTrackingPublic() { StopTracking(); }
    List<TLEntry> timeline = new List<TLEntry>();
    string dataDir, libPath, tlPath;

    int sel = 0;
    int scroll = 0;
    int hot = -1;
    string search = "";
    List<Click> clicks = new List<Click>();
    List<int> shown = new List<int>();

    TextBox tbSearch;
    Label lbTip;
    Button btnScan, btnAdd, btnRefresh, btnFolder, btnShip, btnCover, btnAccel;

    Process runningProc = null;
    DateTime runStart = DateTime.Now;
    string runningName = "";

    bool showReco = false;
    List<int> reco = new List<int>();

    // 星港联动
    List<ShipGame> ship = new List<ShipGame>();
    string shipDb, shipBuilds, instDir;
    bool showVer = false;
    int selVer = 0;
    string verMsg = "";
    double verMsgT = 0;

    // 封面与首次运行
    string coverDir;
    Dictionary<string, Bitmap> coverCache = new Dictionary<string, Bitmap>();
    Dictionary<string, bool> consoleCache = new Dictionary<string, bool>();
    bool showFirst = false;
    int firstFound = 0;
    bool freshInstall = false;
    bool pendingFirstScan = false;
    volatile bool scanning = false;
    string coverErr = "";

    // 加速器联动
    string setPath, accelPath = "", accelName = "";
    bool accelAuto = false;
    bool showAccel = false;
    List<string[]> accelFound = new List<string[]>();

    // 进度备忘
    bool showNote = false;
    bool noteBrief = false;
    int pendLaunch = -1;
    int noteIdx = -1;
    TextBox tbNote;

    Timer tick = new Timer();
    double animT = 0;
    DateTime lastTick = DateTime.Now;
    Random rnd = new Random();
    class Star { public double X, Y, R, Ph, Sp; }
    List<Star> stars = new List<Star>();

    string appDir;
    Bitmap logoImg = null;

    public DockForm()
    {
        Text = "游戏坞  GameDock";
        // 按屏幕可用区域自适应：小屏或者系统缩放（125%/150%）下，
        // 写死的 1280x780 会被放大到超出屏幕，底部那排按钮直接被切掉。
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int cw = Math.Min(1280, wa.Width - 30);
        int ch = Math.Min(780, wa.Height - 50);
        if (cw < 860) cw = Math.Max(600, wa.Width - 10);
        if (ch < 560) ch = Math.Max(420, wa.Height - 10);
        ClientSize = new Size(cw, ch);
        MinimumSize = new Size(Math.Min(1020, cw), Math.Min(700, ch));
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = BG;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        appDir = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            string ico = Path.Combine(appDir, "gamedock.ico");
            if (File.Exists(ico)) Icon = new Icon(ico, 32, 32);
        }
        catch (Exception) { }

        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameDock");
        try { if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir); } catch (Exception) { }
        libPath = Path.Combine(dataDir, "library.db");
        tlPath = Path.Combine(dataDir, "timeline.log");

        // 星港的发布数据就在同级的 Starport 目录里，直接读，不做复制
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        shipDb = Path.Combine(roaming, "Starport", "games.db");
        shipBuilds = Path.Combine(roaming, "Starport", "builds");
        instDir = Path.Combine(dataDir, "installed");
        coverDir = Path.Combine(dataDir, "covers");
        setPath = Path.Combine(dataDir, "settings.txt");
        LoadShip();
        LoadSettings();

        BuildStars();
        MakeControls();
        freshInstall = !File.Exists(libPath);
        LoadLib();
        LoadTL();
        Rebuild();
        RefreshAccelBtn();

        // 首次运行：窗口显示出来之后再在后台扫，不然慢盘会把启动卡住
        if (freshInstall && lib.Count == 0)
        {
            pendingFirstScan = true;
            Log("首次运行，准备后台自动扫描");
        }

        tick.Interval = 16;
        tick.Tick += new EventHandler(OnTick);
        tick.Start();
        lastTick = DateTime.Now;
    }

    void BuildStars()
    {
        for (int i = 0; i < 140; i++)
        {
            Star s = new Star();
            s.X = rnd.NextDouble(); s.Y = rnd.NextDouble() * 0.75;
            s.R = 0.5 + rnd.NextDouble() * 1.5;
            s.Ph = rnd.NextDouble() * 6.28;
            s.Sp = 0.6 + rnd.NextDouble() * 2.0;
            stars.Add(s);
        }
    }

    void MakeControls()
    {
        tbSearch = new TextBox();
        tbSearch.Font = new Font("Microsoft YaHei", 11);
        tbSearch.BorderStyle = BorderStyle.FixedSingle;
        tbSearch.BackColor = Color.FromArgb(16, 28, 48);
        tbSearch.ForeColor = TXT;
        tbSearch.Location = new Point(26, 74);
        tbSearch.Size = new Size(340, 30);
        tbSearch.TextChanged += new EventHandler(OnSearch);
        Controls.Add(tbSearch);

        lbTip = new Label();
        lbTip.Font = new Font("Microsoft YaHei", 9.5f);
        lbTip.ForeColor = OK;
        lbTip.BackColor = Color.Transparent;
        lbTip.TextAlign = ContentAlignment.MiddleCenter;
        lbTip.Size = new Size(560, 26);
        lbTip.Location = new Point(360, 690);
        Controls.Add(lbTip);

        // 进度备忘的输入框（多行），平时隐藏，编辑面板打开时才显示
        tbNote = new TextBox();
        tbNote.Multiline = true;
        tbNote.ScrollBars = ScrollBars.Vertical;
        tbNote.Font = new Font("Microsoft YaHei", 10.5f);
        tbNote.BorderStyle = BorderStyle.FixedSingle;
        tbNote.BackColor = Color.FromArgb(16, 28, 48);
        tbNote.ForeColor = TXT;
        tbNote.Visible = false;
        Controls.Add(tbNote);

        btnScan = MkBtn("自动扫描", 26, 640, 80);
        btnFolder = MkBtn("扫文件夹", 112, 640, 96);
        btnAdd = MkBtn("手动添加", 214, 640, 88);
        btnRefresh = MkBtn("刷新", 308, 640, 64);
        btnShip = MkBtn("同步星港", 378, 640, 92);
        btnCover = MkBtn("批量封面", 478, 640, 92);
        btnAccel = MkBtn("加速器", 578, 640, 76);
        btnScan.Click += new EventHandler(delegate(object s, EventArgs e) { DoScan(); });
        btnFolder.Click += new EventHandler(delegate(object s, EventArgs e) { DoScanFolder(); });
        btnAdd.Click += new EventHandler(delegate(object s, EventArgs e) { DoAdd(); });
        btnRefresh.Click += new EventHandler(delegate(object s, EventArgs e) { ScanRunning(); Say("已刷新", false); });
        btnShip.Click += new EventHandler(delegate(object s, EventArgs e) { DoShipSync(); });
        btnCover.Click += new EventHandler(delegate(object s, EventArgs e) { FetchAllCovers(); });
        btnAccel.Click += new EventHandler(delegate(object s, EventArgs e)
        {
            DetectAccel();
            showAccel = true;
            Invalidate();
        });
        LayoutCtl();
    }

    Button MkBtn(string t, int x, int y, int w)
    {
        Button b = new Button();
        b.Text = t;
        b.Font = new Font("Microsoft YaHei", 10);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(60, 34, 211, 238);
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Color.FromArgb(30, 54, 86);
        b.ForeColor = TXT;
        b.Location = new Point(x, y); b.Size = new Size(w, 34);
        b.Cursor = Cursors.Hand;
        Controls.Add(b);
        return b;
    }

    void LayoutCtl()
    {
        if (tbSearch == null) return;
        int h = ClientSize.Height, w = ClientSize.Width;

        // 工具栏放到顶部，标题栏下面单独一排
        int ty = 62;
        btnScan.Location = new Point(26, ty);
        btnFolder.Location = new Point(112, ty);
        btnAdd.Location = new Point(214, ty);
        btnRefresh.Location = new Point(308, ty);
        btnShip.Location = new Point(378, ty);
        btnCover.Location = new Point(478, ty);
        btnAccel.Location = new Point(578, ty);
        Button[] bs = new Button[] { btnScan, btnFolder, btnAdd, btnRefresh, btnShip, btnCover, btnAccel };
        for (int i = 0; i < bs.Length; i++) if (bs[i] != null) bs[i].Size = new Size(bs[i].Width, 32);

        // 搜索框挪到工具栏这一排的右端，窄窗口下自动变短
        int sw = 340;
        int sx = w - sw - 40;
        if (sx < 670) { sw = Math.Max(160, w - 710); sx = w - sw - 40; }
        tbSearch.Location = new Point(sx, 63);
        tbSearch.Size = new Size(sw, 30);

        lbTip.Location = new Point(26, h - 36);
        lbTip.Size = new Size(Math.Max(240, w - 60), 26);
        lbTip.TextAlign = ContentAlignment.MiddleLeft;

        if (tbNote != null && showNote && !noteBrief)
        {
            int nw = Math.Min(700, w - 100);
            int nx = (w - nw) / 2;
            int ny = (h - 420) / 2;
            tbNote.SetBounds(nx + 28, ny + 122, nw - 56, 176);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        try { LayoutCtl(); Invalidate(); } catch (Exception) { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (pendingFirstScan) { pendingFirstScan = false; DoScanAsync(true); }
    }

    void Say(string m, bool bad)
    {
        lbTip.Text = m;
        lbTip.ForeColor = bad ? DANGER : OK;
        Invalidate();
    }

    void Log(string m)
    {
        TLEntry t = new TLEntry();
        t.T = DateTime.Now; t.Text = m;
        timeline.Add(t);
        if (timeline.Count > 400) timeline.RemoveAt(0);
        try { File.AppendAllText(tlPath, t.T.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine, Encoding.UTF8); } catch (Exception) { }
    }

    void LoadTL()
    {
        try
        {
            if (!File.Exists(tlPath)) return;
            string[] ls = File.ReadAllLines(tlPath, Encoding.UTF8);
            int st = ls.Length - 200; if (st < 0) st = 0;
            for (int i = st; i < ls.Length; i++)
            {
                if (ls[i].Trim().Length == 0) continue;
                TLEntry t = new TLEntry();
                string s = ls[i];
                DateTime d = DateTime.Now;
                if (s.Length > 19) { DateTime.TryParse(s.Substring(0, 19), out d); s = s.Substring(19).Trim(); }
                t.T = d; t.Text = s;
                timeline.Add(t);
            }
        }
        catch (Exception) { }
    }

    void LoadLib()
    {
        try
        {
            if (!File.Exists(libPath)) return;
            foreach (string ln in File.ReadAllLines(libPath, Encoding.UTF8))
            {
                if (ln.Trim().Length == 0) continue;
                string[] p = ln.Split('|');
                if (p.Length < 10) continue;
                GameEntry g = new GameEntry();
                g.Name = p[0]; g.Exe = p[1]; g.Args = p[2]; g.Platform = p[3]; g.Dir = p[4];
                g.Status = p[5]; g.Added = p[6]; g.Last = p[7];
                double ps = 0; double.TryParse(p[8], out ps); g.PlaySeconds = ps;
                int lc = 0; int.TryParse(p[9], out lc); g.LaunchCount = lc;
                if (p.Length > 10) g.StoreName = p[10];
                if (p.Length > 11) g.InstalledVer = p[11];
                if (p.Length > 12) g.AvailVer = p[12];
                if (p.Length > 13) g.AppId = p[13];
                if (p.Length > 14) g.Cover = p[14];
                if (p.Length > 15) g.HideConsole = (p[15] == "1");
                if (p.Length > 16) g.Note = UnField(p[16]);
                if (p.Length > 17) g.NoteTime = p[17];
                lib.Add(g);
            }
        }
        catch (Exception) { }
    }

    void SaveLib()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < lib.Count; i++)
        {
            GameEntry g = lib[i];
            sb.AppendLine(g.Name + "|" + g.Exe + "|" + g.Args + "|" + g.Platform + "|" + g.Dir + "|" +
                g.Status + "|" + g.Added + "|" + g.Last + "|" + ((long)g.PlaySeconds) + "|" + g.LaunchCount + "|" +
                g.StoreName + "|" + g.InstalledVer + "|" + g.AvailVer + "|" + g.AppId + "|" + g.Cover + "|" +
                (g.HideConsole ? "1" : "0") + "|" + SafeField(g.Note) + "|" + g.NoteTime);
        }
        try { File.WriteAllText(libPath, sb.ToString(), Encoding.UTF8); } catch (Exception) { }
    }

    void Rebuild()
    {
        shown.Clear();
        string q = search.Trim().ToLower();
        for (int i = 0; i < lib.Count; i++)
        {
            if (q.Length > 0 && lib[i].Name.ToLower().IndexOf(q) < 0 && lib[i].Platform.ToLower().IndexOf(q) < 0) continue;
            shown.Add(i);
        }
        if (sel >= shown.Count) sel = shown.Count - 1;
        if (sel < 0) sel = 0;
        scroll = 0;
        Invalidate();
    }

    void OnSearch(object s, EventArgs e)
    {
        search = tbSearch.Text;
        Rebuild();
    }

    // ================= 扫描 =================
    void DoScan()
    {
        DoScanAsync(false);
    }

    // 扫描一律走后台线程：Steam 库有可能在响应很慢的可移动盘上，
    // 直接在界面线程上扫会让整个窗口卡住十几秒。
    void DoScanAsync(bool withGuide)
    {
        if (scanning) { Say("正在扫描中，稍等一下…", false); return; }
        scanning = true;
        Say("正在后台扫描 Steam / Epic，界面不会卡…", false);
        // 先把窗口句柄逼出来，否则后台线程回来时 BeginInvoke 会直接抛异常，扫描结果就白扫了
        try { IntPtr force = this.Handle; } catch (Exception) { }
        List<GameEntry> found = new List<GameEntry>();
        System.Threading.Thread th = new System.Threading.Thread(delegate()
        {
            try { CollectSteam(found); } catch (Exception) { }
            try { CollectEpic(found); } catch (Exception) { }
            try
            {
                if (IsDisposed) return;
                this.BeginInvoke((MethodInvoker)delegate()
                {
                    int added = MergeFound(found);
                    SaveLib();
                    Rebuild();
                    scanning = false;
                    Log("后台扫描完成：新增 " + added + " 个");
                    Say("扫描完成，新增 " + added + " 个游戏", false);
                    if (withGuide) { firstFound = lib.Count; showFirst = true; }
                    Invalidate();
                });
            }
            catch (Exception) { scanning = false; }
        });
        th.IsBackground = true;
        th.Start();
    }

    // 下面两个只负责收集，绝不碰 lib 和界面控件，可以安全地在后台线程跑
    void CollectSteam(List<GameEntry> outp)
    {
        string sp = "";
        RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam");
        if (k != null) { object v = k.GetValue("SteamPath"); if (v != null) sp = v.ToString(); k.Close(); }
        if (sp.Length == 0) return;

        List<string> roots = new List<string>();
        roots.Add(sp);
        string lf = Path.Combine(sp, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(lf))
            {
                string txt = File.ReadAllText(lf, Encoding.UTF8);
                MatchCollection ms = Regex.Matches(txt, "\"path\"\\s*\"([^\"]+)\"");
                for (int i = 0; i < ms.Count; i++)
                {
                    string p = ms[i].Groups[1].Value.Replace("\\\\", "\\");
                    if (!roots.Contains(p)) roots.Add(p);
                }
            }
        }
        catch (Exception) { }

        for (int r = 0; r < roots.Count; r++)
        {
            string sa = Path.Combine(roots[r], "steamapps");
            try { if (!Directory.Exists(sa)) continue; } catch (Exception) { continue; }
            string[] acfs;
            try { acfs = Directory.GetFiles(sa, "appmanifest_*.acf"); } catch (Exception) { continue; }
            for (int i = 0; i < acfs.Length; i++)
            {
                try
                {
                    string txt = File.ReadAllText(acfs[i], Encoding.UTF8);
                    string nm = Grab(txt, "name");
                    string id = Grab(txt, "installdir");
                    if (nm.Length == 0 || id.Length == 0) continue;
                    string appid = Path.GetFileNameWithoutExtension(acfs[i]).Replace("appmanifest_", "");
                    string dir = Path.Combine(sa, "common", id);
                    if (!Directory.Exists(dir)) continue;
                    string exe = PickExe(dir, id);
                    if (exe.Length == 0) continue;
                    GameEntry g = new GameEntry();
                    g.Name = nm; g.Exe = exe; g.Platform = "Steam"; g.Dir = dir;
                    g.AppId = appid;
                    g.Status = "未分类"; g.Added = DateTime.Now.ToString("yyyy-MM-dd");
                    outp.Add(g);
                }
                catch (Exception) { }
            }
        }
    }

    void CollectEpic(List<GameEntry> outp)
    {
        try
        {
            string md = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
            if (!Directory.Exists(md)) return;
            string[] fs = Directory.GetFiles(md, "*.item");
            for (int i = 0; i < fs.Length; i++)
            {
                try
                {
                    string txt = File.ReadAllText(fs[i], Encoding.UTF8);
                    string nm = GrabJson(txt, "DisplayName");
                    string loc = GrabJson(txt, "InstallLocation");
                    string lex = GrabJson(txt, "LaunchExecutable");
                    if (nm.Length == 0 || loc.Length == 0) continue;
                    loc = loc.Replace("\\\\", "\\").Replace("/", "\\");
                    if (!Directory.Exists(loc)) continue;
                    string exe = "";
                    if (lex.Length > 0)
                    {
                        string cand = Path.Combine(loc, lex.Replace("/", "\\"));
                        if (File.Exists(cand)) exe = cand;
                    }
                    if (exe.Length == 0) exe = PickExe(loc, Path.GetFileName(loc));
                    if (exe.Length == 0) continue;
                    GameEntry g = new GameEntry();
                    g.Name = nm; g.Exe = exe; g.Platform = "Epic"; g.Dir = loc;
                    g.Status = "未分类"; g.Added = DateTime.Now.ToString("yyyy-MM-dd");
                    outp.Add(g);
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    // 合并进库：只能在界面线程调用
    int MergeFound(List<GameEntry> found)
    {
        int added = 0;
        for (int i = 0; i < found.Count; i++)
        {
            GameEntry g = found[i];
            GameEntry ex = null;
            for (int j = 0; j < lib.Count; j++) if (lib[j].Name == g.Name) { ex = lib[j]; break; }
            if (ex != null)
            {
                if (ex.AppId.Length == 0 && g.AppId.Length > 0) ex.AppId = g.AppId;
                continue;
            }
            lib.Add(g);
            added++;
        }
        return added;
    }

    void ScanRunning()
    {
        // 检查所有库里的程序是否正在运行，用于重置状态
        if (runningProc != null && runningProc.HasExited) StopTracking();
    }

    int ScanSteam()
    {
        List<GameEntry> found = new List<GameEntry>();
        CollectSteam(found);
        return MergeFound(found);
    }

    int ScanEpic()
    {
        List<GameEntry> found = new List<GameEntry>();
        CollectEpic(found);
        return MergeFound(found);
    }

    void DoScanFolder()
    {
        FolderBrowserDialog d = new FolderBrowserDialog();
        d.Description = "选一个文件夹，游戏坞会把里面每个子文件夹当成一个游戏（子文件夹里找体积最大的 exe）";
        if (d.ShowDialog() != DialogResult.OK) return;
        int n = ScanFolderCore(d.SelectedPath);
        Say("扫描完成，新增 " + n + " 个游戏", false);
    }

    public int ScanFolderCore(string root)
    {
        int n = 0;
        try
        {
            string[] subs = Directory.GetDirectories(root);
            for (int i = 0; i < subs.Length; i++)
            {
                string nm = Path.GetFileName(subs[i]);
                if (nm.Length == 0) continue;
                if (Exists(nm)) continue;
                string exe = PickExe(subs[i], nm);
                if (exe.Length == 0) continue;
                GameEntry g = new GameEntry();
                g.Name = nm; g.Exe = exe; g.Platform = "本地"; g.Dir = subs[i];
                g.Status = "未分类"; g.Added = DateTime.Now.ToString("yyyy-MM-dd");
                lib.Add(g);
                n++;
            }
            // 根目录本身也当成一个游戏
            string rn = Path.GetFileName(root.TrimEnd('\\'));
            if (rn.Length > 0 && !Exists(rn))
            {
                string rex = "";
                try
                {
                    string[] f = Directory.GetFiles(root, "*.exe");
                    long bs = 0;
                    for (int i = 0; i < f.Length; i++)
                    {
                        string fl = Path.GetFileName(f[i]).ToLower();
                        if (Regex.IsMatch(fl, "unins|vcredist|setup|crash|report")) continue;
                        long sz = 0; try { sz = new FileInfo(f[i]).Length; } catch (Exception) { }
                        if (sz > bs) { bs = sz; rex = f[i]; }
                    }
                }
                catch (Exception) { }
                if (rex.Length > 0)
                {
                    GameEntry g2 = new GameEntry();
                    g2.Name = rn; g2.Exe = rex; g2.Platform = "本地"; g2.Dir = root;
                    g2.Status = "未分类"; g2.Added = DateTime.Now.ToString("yyyy-MM-dd");
                    lib.Add(g2);
                    n++;
                }
            }
            SaveLib();
            Rebuild();
            Log("扫描文件夹 " + root + "：新增 " + n + " 个");
            return n;
        }
        catch (Exception ex) { Log("扫描失败：" + ex.Message); return -1; }
    }

    bool Exists(string name)
    {
        for (int i = 0; i < lib.Count; i++) if (lib[i].Name == name) return true;
        return false;
    }

    string Grab(string txt, string key)
    {
        Match m = Regex.Match(txt, "\"" + key + "\"\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    string GrabJson(string txt, string key)
    {
        Match m = Regex.Match(txt, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    string PickExe(string dir, string hint)
    {
        try
        {
            string[] ex = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
            string best = ""; long bestSize = 0;
            string bad = "unins|vcredist|dxsetup|setup|install|crash|report|helper|launcher_|redist|dotnet|directx|ue4prereq|unrealcef|eac|battleye|python|node";
            for (int i = 0; i < ex.Length; i++)
            {
                string f = Path.GetFileName(ex[i]);
                string fl = f.ToLower();
                if (Regex.IsMatch(fl, bad)) continue;
                long sz = 0;
                try { sz = new FileInfo(ex[i]).Length; } catch (Exception) { }
                if (fl.Replace(" ", "").Contains(hint.ToLower().Replace(" ", ""))) return ex[i];
                if (sz > bestSize) { bestSize = sz; best = ex[i]; }
                if (ex.Length > 400) break;
            }
            return best;
        }
        catch (Exception) { return ""; }
    }

    void DoAdd()
    {
        OpenFileDialog d = new OpenFileDialog();
        d.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
        d.Title = "选择要加入游戏坞的程序";
        if (d.ShowDialog() != DialogResult.OK) return;
        string exe = d.FileName;
        string nm = Path.GetFileNameWithoutExtension(exe);
        if (Exists(nm)) { Say("库里已经有同名游戏了", true); return; }
        GameEntry g = new GameEntry();
        g.Name = nm; g.Exe = exe; g.Platform = "本地"; g.Dir = Path.GetDirectoryName(exe);
        g.Status = "未分类"; g.Added = DateTime.Now.ToString("yyyy-MM-dd");
        lib.Add(g);
        SaveLib();
        Rebuild();
        Log("手动添加：" + nm);
        Say("已添加：" + nm, false);
    }

    // ================= 启动与计时 =================
    public void Launch(int idx)
    {
        if (idx < 0 || idx >= lib.Count) return;
        GameEntry g = lib[idx];
        // 有进度备忘、而且挺久没玩了：先把你自己留的话给你看一眼，再决定开不开
        if (g.Note.Length > 0 && DaysSince(g.Last) >= 3)
        {
            noteIdx = idx;
            pendLaunch = idx;
            noteBrief = true;
            showNote = true;
            ApplyNote();
            Invalidate();
            return;
        }
        LaunchNow(idx);
    }

    // 供命令行模式使用：绕过回坑简报，直接启动
    public void LaunchDirect(int idx) { LaunchNow(idx); }

    void LaunchNow(int idx)
    {
        if (idx < 0 || idx >= lib.Count) return;
        GameEntry g = lib[idx];
        if (!File.Exists(g.Exe)) { Say("文件不存在了：" + g.Exe, true); return; }
        if (runningProc != null && !runningProc.HasExited)
        {
            Say("已经有一个游戏在运行（" + runningName + "），先关掉它", true);
            return;
        }
        // 加速器联动：先把它拉起来，等进程真的出现了再启动游戏
        if (accelAuto && accelPath.Length > 0 && File.Exists(accelPath) && !AccelRunning())
        {
            Say("先启动加速器，等它就绪…", false);
            Application.DoEvents();
            StartAccel();
            for (int w = 0; w < 25; w++)
            {
                // 这里故意用 Sleep 而不是 DoEvents：避免等待期间又被点一次启动
                System.Threading.Thread.Sleep(120);
                if (AccelRunning()) break;
            }
            Log("加速器就绪，继续启动游戏：" + g.Name);
        }
        try
        {
            ProcessStartInfo si = new ProcessStartInfo();
            si.FileName = g.Exe;
            si.WorkingDirectory = g.Dir;
            if (g.Args.Length > 0) si.Arguments = g.Args;
            if (IsConsoleGame(g) && g.HideConsole)
            {
                // CreateNoWindow 只在 UseShellExecute=false 时生效，用它把黑窗口收掉
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
            }
            else si.UseShellExecute = true;
            runningProc = Process.Start(si);
            runStart = DateTime.Now;
            runningName = g.Name;
            g.LaunchCount++;
            g.Last = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (g.Status == "未分类" || g.Status == "搁置") g.Status = "在玩";
            SaveLib();
            Log("启动：" + g.Name);
            Say("正在运行：" + g.Name, false);
            if (runningProc != null)
            {
                try { runningProc.EnableRaisingEvents = true; } catch (Exception) { }
            }
        }
        catch (Exception ex)
        {
            Log("启动失败：" + g.Name + "  " + ex.Message);
            Say("启动失败：" + ex.Message, true);
        }
    }

    public void StopTracking()
    {
        if (runningProc == null) return;
        double sec = (DateTime.Now - runStart).TotalSeconds;
        for (int i = 0; i < lib.Count; i++)
        {
            if (lib[i].Name == runningName) { lib[i].PlaySeconds += sec; }
        }
        SaveLib();
        Log("结束：" + runningName + "  本次 " + Fmt(sec));
        Say("已记录：" + runningName + "  本次游玩 " + Fmt(sec), false);
        runningProc = null; runningName = "";
    }

    void OnTick(object s, EventArgs e)
    {
        DateTime now = DateTime.Now;
        double dt = (now - lastTick).TotalSeconds;
        lastTick = now;
        if (dt > 0.05) dt = 0.05;
        animT += dt;
        if (verMsgT > 0) verMsgT -= dt;

        if (runningProc != null)
        {
            bool exited = false;
            try { exited = runningProc.HasExited; } catch (Exception) { exited = true; }
            if (exited) StopTracking();
        }
        Invalidate();
    }

    // ================= 推荐 =================
    void MakeReco()
    {
        reco.Clear();
        List<int> cand = new List<int>();
        for (int i = 0; i < lib.Count; i++)
        {
            if (lib[i].Status == "通关" || lib[i].Status == "弃坑") continue;
            cand.Add(i);
        }
        if (cand.Count == 0) { for (int i = 0; i < lib.Count; i++) cand.Add(i); }

        List<double> sc = new List<double>();
        for (int i = 0; i < cand.Count; i++)
        {
            GameEntry g = lib[cand[i]];
            double s = 0;
            if (g.Status == "在玩") s += 60;
            if (g.Status == "搁置") s += 25;
            if (g.Status == "未分类") s += 15;
            if (g.PlaySeconds > 0 && g.PlaySeconds < 3600 * 3) s += 20;      // 刚开个头
            if (g.PlaySeconds > 3600 * 60) s += 10;                          // 玩得久，容易回味
            double days = 999;
            if (g.Last.Length >= 10)
            {
                DateTime d;
                if (DateTime.TryParse(g.Last, out d)) days = (DateTime.Now - d).TotalDays;
            }
            if (days > 30) s += 18;                                          // 很久没碰
            if (days > 7 && days <= 30) s += 10;
            if (days <= 1) s -= 25;                                          // 刚玩过，先别重复
            s += rnd.NextDouble() * 12;
            sc.Add(s);
        }
        for (int k = 0; k < 3 && cand.Count > 0; k++)
        {
            int bi = 0;
            for (int i = 1; i < sc.Count; i++) if (sc[i] > sc[bi]) bi = i;
            reco.Add(cand[bi]);
            cand.RemoveAt(bi); sc.RemoveAt(bi);
        }
        showReco = true;
        Log("生成今晚推荐：" + reco.Count + " 个候选");
    }

    // ================= 星港联动 =================
    static string SafeName(string s)
    {
        // 必须和星港里的目录命名规则完全一致，否则找不到构建包
        string r = "";
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') r += c;
            else if (c == ' ') r += '_';
        }
        if (r.Length == 0) r = "game";
        if (r.Length > 32) r = r.Substring(0, 32);
        return r;
    }

    static string FmtSize(long b)
    {
        if (b < 1024) return b + " B";
        if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0") + " KB";
        if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0") + " MB";
        return (b / 1073741824.0).ToString("0.00") + " GB";
    }

    static List<ShipBuild> DecVers(string s)
    {
        List<ShipBuild> vs = new List<ShipBuild>();
        if (s == null || s.Length == 0) return vs;
        string[] parts = s.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string[] f = parts[i].Split('~');
            if (f.Length < 6) continue;
            ShipBuild v = new ShipBuild();
            v.Ver = f[0]; v.File = f[1];
            long sz = 0; long.TryParse(f[2], out sz); v.Size = sz;
            v.Time = f[3]; v.Note = f[4]; v.Sha = f[5];
            vs.Add(v);
        }
        return vs;
    }

    void LoadShip()
    {
        ship.Clear();
        try
        {
            if (!File.Exists(shipDb)) return;
            string[] ls = File.ReadAllLines(shipDb, Encoding.UTF8);
            for (int i = 0; i < ls.Length; i++)
            {
                if (ls[i].Trim().Length == 0) continue;
                string[] p = ls[i].Split('|');
                if (p.Length < 10) continue;
                ShipGame s = new ShipGame();
                s.Name = p[0]; s.Genre = p[1]; s.Desc = p[2]; s.Dev = p[3]; s.Rating = p[4];
                if (p.Length > 12) s.Stage = p[12];
                if (p.Length > 13) s.PrevEnd = p[13];
                if (p.Length > 14) s.CurVer = p[14];
                if (p.Length > 15) s.Vers = DecVers(p[15]);
                if (s.Stage.Length == 0) s.Stage = (p[6] == "1") ? "已上架" : "已下架";
                ship.Add(s);
            }
        }
        catch (Exception) { }
    }

    ShipGame ShipFor(string name)
    {
        for (int i = 0; i < ship.Count; i++) if (ship[i].Name == name) return ship[i];
        return null;
    }

    GameEntry FindEntry(string storeName)
    {
        for (int i = 0; i < lib.Count; i++) if (lib[i].StoreName == storeName) return lib[i];
        for (int i = 0; i < lib.Count; i++)
            if (lib[i].Name == storeName && (lib[i].Platform.Length == 0 || lib[i].Platform == "星港")) return lib[i];
        return null;
    }

    string BuildPath(ShipGame s, ShipBuild b)
    {
        return Path.Combine(shipBuilds, SafeName(s.Name), b.Ver, b.File);
    }

    string InstalledPath(ShipGame s, string ver)
    {
        return Path.Combine(instDir, SafeName(s.Name), ver);
    }

    ShipBuild FindBuild(ShipGame s, string ver)
    {
        for (int i = 0; i < s.Vers.Count; i++) if (s.Vers[i].Ver == ver) return s.Vers[i];
        return null;
    }

    void DoShipSync()
    {
        LoadShip();
        if (ship.Count == 0)
        {
            Say("没读到星港的发布数据，先在星港里发布一个游戏并上传构建包", true);
            return;
        }
        int add = 0, upd = 0, preview = 0;
        for (int i = 0; i < ship.Count; i++)
        {
            ShipGame s = ship[i];
            if (s.Stage == "预告中") { preview++; continue; }
            if (s.Stage != "已上架") continue;
            if (s.Vers.Count == 0) continue;
            GameEntry g = FindEntry(s.Name);
            if (g == null)
            {
                g = new GameEntry();
                g.Name = s.Name; g.Platform = "星港"; g.Status = "未分类";
                g.Added = DateTime.Now.ToString("yyyy-MM-dd");
                lib.Add(g); add++;
            }
            else upd++;
            g.StoreName = s.Name;
            g.AvailVer = s.CurVer;
        }
        SaveLib(); Rebuild();
        Log("同步星港：新增 " + add + " 个，更新 " + upd + " 个，预告中跳过 " + preview + " 个");
        string tip = "同步完成：新增 " + add + " 个，更新 " + upd + " 个";
        if (preview > 0) tip += "（" + preview + " 个还在预告期，不能安装）";
        Say(tip, false);
    }

    bool InstallVer(ShipGame s, string ver)
    {
        ShipBuild b = FindBuild(s, ver);
        if (b == null) { verMsg = "星港里没有版本 " + ver; verMsgT = 4; Invalidate(); return false; }
        string src = BuildPath(s, b);
        if (!File.Exists(src))
        {
            verMsg = "构建包不在本机：" + b.File + "（可能是在别的机器上传的，或星港数据被清过）";
            verMsgT = 6; Invalidate(); return false;
        }
        string dst = InstalledPath(s, ver);
        try
        {
            if (Directory.Exists(dst)) { try { Directory.Delete(dst, true); } catch (Exception) { } }
            Directory.CreateDirectory(dst);

            string ext = Path.GetExtension(src).ToLower();
            if (ext == ".zip")
            {
                try { System.IO.Compression.ZipFile.ExtractToDirectory(src, dst); }
                catch (Exception) { File.Copy(src, Path.Combine(dst, b.File), true); }
            }
            else File.Copy(src, Path.Combine(dst, b.File), true);

            string exe = PickExe(dst, s.Name);
            if (exe.Length == 0)
            {
                string direct = Path.Combine(dst, b.File);
                if (File.Exists(direct) && direct.ToLower().EndsWith(".exe")) exe = direct;
            }

            GameEntry g = FindEntry(s.Name);
            if (g == null)
            {
                g = new GameEntry();
                g.Name = s.Name; g.Status = "未分类"; g.Added = DateTime.Now.ToString("yyyy-MM-dd");
                lib.Add(g);
            }
            g.Platform = "星港"; g.StoreName = s.Name;
            g.Dir = dst; g.InstalledVer = ver; g.AvailVer = s.CurVer;
            if (exe.Length > 0) g.Exe = exe;
            else if (g.Exe.Length == 0) g.Exe = dst;

            SaveLib(); Rebuild();
            Log("安装星港构建 " + s.Name + " " + ver + " → " + dst);
            verMsg = (exe.Length > 0) ? ("已安装 " + s.Name + " " + ver) : "文件已释放，但没找到 exe，请用「手动添加」指一下";
            verMsgT = 5;
            Invalidate();
            return true;
        }
        catch (Exception ex)
        {
            verMsg = "安装失败：" + ex.Message; verMsgT = 6; Invalidate();
            Log("安装星港构建失败 " + s.Name + " " + ver + "：" + ex.Message);
            return false;
        }
    }

    void UninstallShip(string name)
    {
        try
        {
            string full = Path.GetFullPath(Path.Combine(instDir, SafeName(name)));
            string baseDir = Path.GetFullPath(instDir);
            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) { verMsg = "路径校验失败，已拒绝删除"; verMsgT = 4; return; }
            if (Directory.Exists(full)) Directory.Delete(full, true);

            GameEntry g = FindEntry(name);
            if (g != null)
            {
                g.Exe = ""; g.Dir = ""; g.InstalledVer = "";
                g.Status = "未分类";
            }
            SaveLib(); Rebuild();
            Log("卸载星港构建：" + name);
            verMsg = "已卸载本机文件：" + name + "（星港上的构建包没有动）"; verMsgT = 5;
            Invalidate();
        }
        catch (Exception ex) { verMsg = "卸载失败：" + ex.Message; verMsgT = 6; Invalidate(); }
    }

    void HandleVerClick(int id)
    {
        if (id == 753) { showVer = false; Invalidate(); return; }
        if (shown.Count == 0) { showVer = false; Invalidate(); return; }
        GameEntry ge = lib[shown[sel]];
        ShipGame s = ShipFor(ge.StoreName.Length > 0 ? ge.StoreName : ge.Name);

        // 按钮先判，否则会被下面的版本行区间吞掉
        if (id == 750 || id == 751 || id == 752)
        {
            if (s == null) { verMsg = "没读到这个作品的星港数据"; verMsgT = 4; Invalidate(); return; }
            if (id == 750)
            {
                string latest = s.CurVer;
                if (latest.Length == 0 && s.Vers.Count > 0) latest = s.Vers[s.Vers.Count - 1].Ver;
                InstallVer(s, latest);
                return;
            }
            if (id == 751)
            {
                if (selVer < 0 || selVer >= s.Vers.Count) { verMsg = "先在列表里点一个版本"; verMsgT = 4; Invalidate(); return; }
                InstallVer(s, s.Vers[selVer].Ver);
                return;
            }
            UninstallShip(s.Name);
            return;
        }
        if (id >= 700 && id < 750) { selVer = id - 700; Invalidate(); return; }
    }

    // ================= 加速器联动 =================
    // 备忘是多行文本，而库文件是按竖线分隔的，所以要转义
    static string SafeField(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("|", "\\p")
                .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
    }

    static string UnField(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char c = s[++i];
                if (c == 'n') sb.Append('\n');
                else if (c == 'p') sb.Append('|');
                else if (c == '\\') sb.Append('\\');
                else { sb.Append('\\'); sb.Append(c); }
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    double DaysSince(string stamp)
    {
        if (stamp == null || stamp.Length < 10) return 999;
        DateTime d;
        if (!DateTime.TryParse(stamp, out d)) return 999;
        return (DateTime.Now - d).TotalDays;
    }

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(setPath)) return;
            string[] ls = File.ReadAllLines(setPath, Encoding.UTF8);
            for (int i = 0; i < ls.Length; i++)
            {
                int p = ls[i].IndexOf('|');
                if (p <= 0) continue;
                string k = ls[i].Substring(0, p), v = ls[i].Substring(p + 1);
                if (k == "accelPath") accelPath = v;
                else if (k == "accelName") accelName = v;
                else if (k == "accelAuto") accelAuto = (v == "1");
            }
        }
        catch (Exception) { }
    }

    void SaveSettings()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("accelPath|" + accelPath);
        sb.AppendLine("accelName|" + accelName);
        sb.AppendLine("accelAuto|" + (accelAuto ? "1" : "0"));
        try { File.WriteAllText(setPath, sb.ToString(), Encoding.UTF8); } catch (Exception) { }
    }

    static bool LooksLikeAccel(string name)
    {
        if (name == null || name.Length == 0) return false;
        string l = name.ToLower();
        string[] keys = new string[] { "加速器", "加速", "uu", "迅游", "雷神", "奇游", "biubiu", "netch", "游戏加速" };
        for (int i = 0; i < keys.Length; i++) if (l.IndexOf(keys[i]) >= 0) return true;
        return false;
    }

    void AddAccel(string name, string path)
    {
        for (int i = 0; i < accelFound.Count; i++)
            if (string.Equals(accelFound[i][1], path, StringComparison.OrdinalIgnoreCase)) return;
        accelFound.Add(new string[] { name, path });
    }

    string BestExeIn(string folder, string hint)
    {
        try
        {
            if (folder == null || folder.Length == 0 || !Directory.Exists(folder)) return "";
            string[] fs = Directory.GetFiles(folder, "*.exe");
            string best = ""; long bs = -1;
            string bad = "unins|update|setup|install|helper|crash|report|repair|service|daemon|driver|vcredist";
            string[] brands = new string[] { "uu", "launcher", "start", "accel", "netch", "speed" };
            for (int i = 0; i < fs.Length; i++)
            {
                string fl = Path.GetFileNameWithoutExtension(fs[i]).ToLower();
                if (Regex.IsMatch(fl, bad)) continue;
                long sz = 0; try { sz = new FileInfo(fs[i]).Length; } catch (Exception) { }
                for (int b = 0; b < brands.Length; b++)
                    if (fl.IndexOf(brands[b]) >= 0) return fs[i];
                if (sz > bs) { bs = sz; best = fs[i]; }
            }
            return best;
        }
        catch (Exception) { return ""; }
    }

    void ScanUninstall(RegistryKey root, string sub)
    {
        try
        {
            RegistryKey k = root.OpenSubKey(sub);
            if (k == null) return;
            string[] names = k.GetSubKeyNames();
            for (int i = 0; i < names.Length; i++)
            {
                RegistryKey e = null;
                try
                {
                    e = k.OpenSubKey(names[i]);
                    if (e == null) continue;
                    string dn = Convert.ToString(e.GetValue("DisplayName"));
                    if (!LooksLikeAccel(dn)) continue;
                    string exe = "";
                    string icon = Convert.ToString(e.GetValue("DisplayIcon"));
                    if (icon != null && icon.Length > 0)
                    {
                        string p = icon.Trim('"');
                        int c = p.LastIndexOf(',');
                        if (c > 3) p = p.Substring(0, c);
                        if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p)) exe = p;
                    }
                    if (exe.Length == 0) exe = BestExeIn(Convert.ToString(e.GetValue("InstallLocation")), dn);
                    if (exe.Length > 0) AddAccel(dn, exe);
                }
                catch (Exception) { }
                finally { if (e != null) e.Close(); }
            }
            k.Close();
        }
        catch (Exception) { }
    }

    void ScanAccelFolders()
    {
        List<string> roots = new List<string>();
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
        for (int r = 0; r < roots.Count; r++)
        {
            try
            {
                string root = roots[r];
                if (root.Length == 0 || !Directory.Exists(root)) continue;
                string[] subs = Directory.GetDirectories(root);
                for (int i = 0; i < subs.Length && i < 400; i++)
                {
                    string nm = Path.GetFileName(subs[i]);
                    if (!LooksLikeAccel(nm)) continue;
                    string exe = BestExeIn(subs[i], nm);
                    if (exe.Length > 0) { AddAccel(nm, exe); continue; }
                    // 有些是装在 品牌\产品\ 下面，再往里看一层
                    string[] sub2 = Directory.GetDirectories(subs[i]);
                    for (int j = 0; j < sub2.Length && j < 40; j++)
                    {
                        string e2 = BestExeIn(sub2[j], Path.GetFileName(sub2[j]));
                        if (e2.Length > 0) AddAccel(Path.GetFileName(sub2[j]), e2);
                    }
                }
            }
            catch (Exception) { }
        }
    }

    void DetectAccel()
    {
        accelFound.Clear();
        ScanUninstall(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        ScanUninstall(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        ScanUninstall(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        ScanAccelFolders();
        Log("加速器检测：找到 " + accelFound.Count + " 个");
    }

    bool AccelRunning()
    {
        if (accelPath.Length == 0) return false;
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(accelPath)).Length > 0; }
        catch (Exception) { return false; }
    }

    bool StartAccel()
    {
        if (accelPath.Length == 0 || !File.Exists(accelPath)) return false;
        try
        {
            ProcessStartInfo si = new ProcessStartInfo();
            si.FileName = accelPath;
            si.WorkingDirectory = Path.GetDirectoryName(accelPath);
            si.UseShellExecute = true;
            Process.Start(si);
            Log("启动加速器：" + accelPath);
            return true;
        }
        catch (Exception ex) { Log("启动加速器失败：" + ex.Message); return false; }
    }

    void RefreshAccelBtn()
    {
        if (btnAccel == null) return;
        btnAccel.Text = accelAuto && accelPath.Length > 0 ? "加速器 ●" : "加速器";
    }

    void HandleAccelClick(int id)
    {
        if (id == 514) { showAccel = false; Invalidate(); return; }
        if (id == 510)
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Title = "指定加速器的主程序";
            d.Filter = "可执行文件 (*.exe)|*.exe";
            if (d.ShowDialog() != DialogResult.OK) return;
            accelPath = d.FileName;
            accelName = Path.GetFileNameWithoutExtension(d.FileName);
            SaveSettings(); RefreshAccelBtn();
            Say("已指定加速器：" + accelName, false);
            Invalidate();
            return;
        }
        if (id == 511)
        {
            accelPath = ""; accelName = ""; accelAuto = false;
            SaveSettings(); RefreshAccelBtn();
            Say("已清除加速器设置", false);
            Invalidate();
            return;
        }
        if (id == 512)
        {
            if (accelPath.Length == 0) { Say("先选一个加速器，再开自动启动", true); return; }
            accelAuto = !accelAuto;
            SaveSettings(); RefreshAccelBtn();
            Say(accelAuto ? "已开启：启动游戏前先启动加速器" : "已关闭：不再自动启动加速器", false);
            Invalidate();
            return;
        }
        if (id == 513)
        {
            if (accelPath.Length == 0) { Say("还没指定加速器", true); return; }
            if (AccelRunning()) { Say("加速器已经在运行了", false); return; }
            bool ok = StartAccel();
            Say(ok ? "已启动加速器，等它连上再点启动游戏" : "启动失败，路径可能已经失效", !ok);
            Invalidate();
            return;
        }
        if (id >= 500 && id < 510)
        {
            int k = id - 500;
            if (k < accelFound.Count)
            {
                accelPath = accelFound[k][1];
                accelName = accelFound[k][0];
                SaveSettings(); RefreshAccelBtn();
                Say("已选用：" + accelName, false);
                Invalidate();
            }
            return;
        }
    }

    // ================= 控制台程序识别 =================
    static bool IsConsoleExe(string path)
    {
        // 直接读 PE 头判断子系统：3 = 控制台，2 = 图形界面
        try
        {
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] h = new byte[0x40];
                if (fs.Read(h, 0, 0x40) < 0x40) return false;
                if (h[0] != 'M' || h[1] != 'Z') return false;
                int peOff = BitConverter.ToInt32(h, 0x3C);
                if (peOff <= 0 || peOff > 0x1000000) return false;
                fs.Seek(peOff, SeekOrigin.Begin);
                byte[] ph = new byte[0x60];
                if (fs.Read(ph, 0, 0x60) < 0x60) return false;
                if (ph[0] != 'P' || ph[1] != 'E') return false;
                return BitConverter.ToInt16(ph, 0x5C) == 3;
            }
        }
        catch (Exception) { return false; }
    }

    bool IsConsoleGame(GameEntry g)
    {
        if (g.Exe.Length == 0 || !File.Exists(g.Exe)) return false;
        if (consoleCache.ContainsKey(g.Exe)) return consoleCache[g.Exe];
        bool r = IsConsoleExe(g.Exe);
        consoleCache[g.Exe] = r;
        return r;
    }

    // ================= 封面 =================
    void SetAppId(string name, string appid)
    {
        if (appid.Length == 0) return;
        for (int i = 0; i < lib.Count; i++)
            if (lib[i].Name == name && lib[i].AppId.Length == 0) { lib[i].AppId = appid; return; }
    }

    Color PlatformColor(GameEntry g)
    {
        if (g.Platform == "Steam") return Color.FromArgb(96, 165, 250);
        if (g.Platform == "Epic") return Color.FromArgb(167, 139, 250);
        if (g.Platform == "本地") return Color.FromArgb(74, 222, 128);
        if (g.Platform == "星港") return Color.FromArgb(251, 191, 36);
        return Color.FromArgb(148, 163, 184);
    }

    Bitmap CoverImage(GameEntry g)
    {
        if (g.Cover.Length == 0) return null;
        if (coverCache.ContainsKey(g.Cover)) return coverCache[g.Cover];
        Bitmap b = null;
        try
        {
            if (File.Exists(g.Cover))
            {
                // 先读进内存再解码，避免 Image.FromFile 锁住文件
                byte[] bytes = File.ReadAllBytes(g.Cover);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image tmp = Image.FromStream(ms))
                    b = new Bitmap(tmp);
            }
        }
        catch (Exception) { b = null; }
        coverCache[g.Cover] = b;
        return b;
    }

    void DrawCover(Graphics g, Rectangle r, GameEntry ge)
    {
        Bitmap b = CoverImage(ge);
        using (GraphicsPath path = Chamfer(r, 8))
        {
            if (b != null)
            {
                // 按比例裁切填满，不拉伸变形
                float sr = (float)b.Width / b.Height;
                float dr = (float)r.Width / r.Height;
                RectangleF src;
                if (sr > dr)
                {
                    float nw = b.Height * dr;
                    src = new RectangleF((b.Width - nw) / 2f, 0, nw, b.Height);
                }
                else
                {
                    float nh = b.Width / dr;
                    src = new RectangleF(0, (b.Height - nh) / 2f, b.Width, nh);
                }
                GraphicsState st = g.Save();
                g.SetClip(path);
                g.DrawImage(b, r, src, GraphicsUnit.Pixel);
                g.Restore(st);
                // 底部压一层暗色，保证文字压上去也看得清
                using (LinearGradientBrush sh = new LinearGradientBrush(
                    new Rectangle(r.X, r.Bottom - r.Height / 3, r.Width, r.Height / 3),
                    Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90f))
                {
                    GraphicsState st2 = g.Save();
                    g.SetClip(path);
                    g.FillRectangle(sh, r.X, r.Bottom - r.Height / 3, r.Width, r.Height / 3);
                    g.Restore(st2);
                }
            }
            else
            {
                // 没有封面就画一块平台配色的渐变牌，带游戏名首字
                Color c1 = PlatformColor(ge);
                Color c2 = Color.FromArgb(c1.R / 4, c1.G / 4, c1.B / 4);
                using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 45f))
                using (GraphicsPath p2 = Chamfer(r, 8)) g.FillPath(br, p2);
                if (r.Width >= 40 && r.Height >= 30)
                {
                    string ch = ge.Name.Length > 0 ? ge.Name.Substring(0, 1) : "?";
                    float fs = Math.Min(r.Height * 0.52f, r.Width * 0.5f);
                    if (fs > 44f) fs = 44f;
                    using (Font f = new Font("Microsoft YaHei", fs, FontStyle.Bold))
                    {
                        SizeF z = g.MeasureString(ch, f);
                        g.DrawString(ch, f, new SolidBrush(Color.FromArgb(235, 255, 255, 255)),
                            r.X + (r.Width - z.Width) / 2, r.Y + (r.Height - z.Height) / 2);
                    }
                }
            }
        }
        ChamEdge(g, r, 8, Color.FromArgb(150, PlatformColor(ge)), 1, 0);
    }

    string TruncToFit(Graphics g, string s, Font f, int maxW)
    {
        if (maxW <= 24 || s.Length == 0) return "";
        if (g.MeasureString(s, f).Width <= maxW) return s;
        for (int len = s.Length - 1; len > 0; len--)
        {
            string t = s.Substring(0, len) + "…";
            if (g.MeasureString(t, f).Width <= maxW) return t;
        }
        return "…";
    }

    string TruncPathToFit(Graphics g, string s, Font f, int maxW)
    {
        // 路径保留尾部，因为文件名才是有效信息
        if (maxW <= 30 || s.Length == 0) return "";
        if (g.MeasureString(s, f).Width <= maxW) return s;
        for (int len = s.Length - 1; len > 0; len--)
        {
            string t = "…" + s.Substring(s.Length - len);
            if (g.MeasureString(t, f).Width <= maxW) return t;
        }
        return "…";
    }

    void SetCoverFromFile(GameEntry ge)
    {
        OpenFileDialog d = new OpenFileDialog();
        d.Title = "给「" + ge.Name + "」选一张封面（横图更好看）";
        d.Filter = "图片 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp";
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(coverDir);
            string ext = Path.GetExtension(d.FileName).ToLower();
            if (ext == ".jpeg") ext = ".jpg";
            string dst = Path.Combine(coverDir, SafeName(ge.Name) + ext);
            ClearCoverFiles(ge.Name, dst);
            File.Copy(d.FileName, dst, true);
            ge.Cover = dst;
            coverCache.Remove(dst);
            SaveLib();
            Log("设置封面：" + ge.Name + " → " + dst);
            Say("封面已设置：" + ge.Name, false);
            Invalidate();
        }
        catch (Exception ex) { Say("设置封面失败：" + ex.Message, true); }
    }

    void ClearCoverFiles(string name, string keep)
    {
        foreach (string e in new string[] { ".jpg", ".png", ".bmp" })
        {
            string p = Path.Combine(coverDir, SafeName(name) + e);
            if (File.Exists(p) && p != keep) { try { File.Delete(p); coverCache.Remove(p); } catch (Exception) { } }
        }
    }

    void ClearCover(GameEntry ge)
    {
        try
        {
            ClearCoverFiles(ge.Name, "");
            if (ge.Cover.Length > 0) coverCache.Remove(ge.Cover);
            ge.Cover = "";
            SaveLib();
            Log("清除封面：" + ge.Name);
            Say("已清除封面：" + ge.Name, false);
            Invalidate();
        }
        catch (Exception ex) { Say("清除失败：" + ex.Message, true); }
    }

    bool FetchSteamCover(GameEntry ge)
    {
        coverErr = "";
        if (ge.AppId.Length == 0) return false;
        string tmp = Path.Combine(Path.GetTempPath(), "gd_cover_" + ge.AppId + ".jpg");
        try
        {
            // csc 编出来的 exe 默认还是老的 SSL/TLS，Steam 的 CDN 只收 TLS 1.2，不设这行会握手失败
            try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; }
            catch (Exception) { }
            string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + ge.AppId + "/header.jpg";
            using (System.Net.WebClient wc = new System.Net.WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                wc.DownloadFile(url, tmp);
            }
            // 确认下回来的确实是图片而不是错误页
            using (FileStream fs = File.OpenRead(tmp))
            using (Image probe = Image.FromStream(fs)) { }

            Directory.CreateDirectory(coverDir);
            string dst = Path.Combine(coverDir, SafeName(ge.Name) + ".jpg");
            ClearCoverFiles(ge.Name, dst);
            File.Copy(tmp, dst, true);
            ge.Cover = dst;
            coverCache.Remove(dst);
            return true;
        }
        catch (Exception ex) { coverErr = ex.Message; return false; }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
    }

    void FetchAllCovers()
    {
        int ok = 0, skip = 0, fail = 0;
        for (int i = 0; i < lib.Count; i++)
        {
            GameEntry g = lib[i];
            if (g.Cover.Length > 0) { skip++; continue; }
            if (g.AppId.Length == 0) { skip++; continue; }
            if (FetchSteamCover(g)) ok++; else fail++;
        }
        SaveLib(); Rebuild();
        Log("批量获取封面：成功 " + ok + "，跳过 " + skip + "，失败 " + fail);
        Say("封面获取完成：成功 " + ok + " 个" + (skip > 0 ? "，跳过 " + skip + " 个" : "") +
            (fail > 0 ? "，失败 " + fail + " 个（检查网络）" : ""), fail > 0 && ok == 0);
    }

    void HandleCoverClick(int id)
    {
        if (shown.Count == 0) return;
        GameEntry ge = lib[shown[sel]];
        if (id == 410) { SetCoverFromFile(ge); return; }
        if (id == 411)
        {
            if (ge.AppId.Length == 0) { Say("这个游戏没有 Steam 编号，拉不到官方封面，请手动选一张", true); return; }
            Say("正在从 Steam 获取封面…", false);
            Application.DoEvents();
            if (FetchSteamCover(ge)) { SaveLib(); Say("已获取封面：" + ge.Name, false); }
            else Say("获取失败：" + (coverErr.Length > 0 ? coverErr : "可能没联网，或这款游戏没有公开封面"), true);
            Invalidate();
            return;
        }
        if (id == 412) { ClearCover(ge); return; }
    }

    void HandleFirstClick(int id)
    {
        if (id == 460) { showFirst = false; Invalidate(); return; }   // 开始用 / 先跳过
        if (id == 461) { showFirst = false; Invalidate(); DoScanFolder(); return; }
        if (id == 462) { showFirst = false; Invalidate(); DoAdd(); return; }
        if (id == 463) { showFirst = false; Invalidate(); DoScan(); return; }
    }

    // ================= 进度备忘 / 回坑简报 =================
    void ApplyNote()
    {
        if (tbNote == null) return;
        tbNote.Visible = showNote && !noteBrief;
        LayoutCtl();
    }

    int NoteIndex()
    {
        if (shown.Count == 0 || sel < 0 || sel >= shown.Count) return -1;
        return shown[sel];
    }

    void OpenNoteEditor()
    {
        int gi = NoteIndex();
        if (gi < 0) return;
        noteIdx = gi;
        tbNote.Text = lib[gi].Note;
        noteBrief = false;
        showNote = true;
        ApplyNote();
        tbNote.Focus();
        Invalidate();
    }

    void SaveNote(bool clear)
    {
        if (noteIdx < 0 || noteIdx >= lib.Count) { showNote = false; ApplyNote(); Invalidate(); return; }
        GameEntry g = lib[noteIdx];
        if (clear) { g.Note = ""; g.NoteTime = ""; }
        else
        {
            string t = tbNote.Text.Trim();
            if (t.Length > 600) t = t.Substring(0, 600);
            g.Note = t;
            g.NoteTime = t.Length > 0 ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : "";
        }
        SaveLib();
        Log("更新进度备忘：" + g.Name + (clear ? "（清除）" : "（" + g.Note.Length + " 字）"));
        Say(clear ? "已清除备忘：" + g.Name : "记下了：" + g.Name, false);
        showNote = false;
        ApplyNote();
        Invalidate();
    }

    void HandleNoteClick(int id)
    {
        if (id == 440) { SaveNote(false); return; }
        if (id == 441) { SaveNote(true); return; }
        if (id == 442) { showNote = false; ApplyNote(); Invalidate(); return; }
        if (id == 451)
        {
            int gi = pendLaunch;
            showNote = false; noteBrief = false; pendLaunch = -1;
            ApplyNote();
            if (gi >= 0 && gi < lib.Count) LaunchNow(gi);
            Invalidate();
            return;
        }
        if (id == 452) { showNote = false; pendLaunch = -1; ApplyNote(); Invalidate(); return; }
        if (id == 453)
        {
            if (noteIdx >= 0 && noteIdx < lib.Count) tbNote.Text = lib[noteIdx].Note;
            noteBrief = false;
            ApplyNote();
            tbNote.Focus();
            Invalidate();
            return;
        }
    }

    void DrawNote(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        using (SolidBrush ov = new SolidBrush(Color.FromArgb(205, 4, 8, 16))) g.FillRectangle(ov, 0, 0, w, h);
        if (noteIdx < 0 || noteIdx >= lib.Count) return;
        GameEntry ge = lib[noteIdx];

        Font fS = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font fT = new Font("Microsoft YaHei", 17, FontStyle.Bold);
        Font fB = new Font("Microsoft YaHei", 10.5f, FontStyle.Regular);
        Font fBold = new Font("Microsoft YaHei", 11, FontStyle.Bold);

        if (noteBrief)
        {
            int bw = Math.Min(680, w - 100), bh = 392;
            int bx = (w - bw) / 2, by = (h - bh) / 2;
            Rectangle p = new Rectangle(bx, by, bw, bh);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 12, 22, 38))) Cham(g, p, 18, b);
            ChamEdge(g, p, 18, Color.FromArgb(215, OK), 2, 8);

            g.DrawString("WELCOME BACK", fS, new SolidBrush(Color.FromArgb(205, OK)), bx + 28, by + 20);
            GlowText(g, "欢迎回来", fT, Color.White, bx + 28, by + 38);
            int days = (int)DaysSince(ge.Last);
            g.DrawString(days >= 999 ? "这款游戏还没启动过，先把你自己留的话看一眼："
                                     : "距离上次玩已经 " + days + " 天了，先把你自己留的话看一眼：",
                fB, new SolidBrush(Color.FromArgb(200, 190, 205, 225)), bx + 28, by + 80);

            Rectangle nb = new Rectangle(bx + 28, by + 108, bw - 56, 156);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(180, 18, 32, 54))) Cham(g, nb, 10, b);
            ChamEdge(g, nb, 10, Color.FromArgb(170, OK), 1, 0);
            g.DrawString(ge.Note, fB, new SolidBrush(TXT),
                new RectangleF(nb.Left + 14, nb.Top + 12, nb.Width - 28, nb.Height - 24));

            string meta = "累计 " + Fmt(ge.PlaySeconds) + "　·　启动 " + ge.LaunchCount + " 次" +
                          (ge.NoteTime.Length > 0 ? "　·　备忘写在 " + ge.NoteTime : "");
            g.DrawString(meta, fB, new SolidBrush(Color.FromArgb(180, 148, 163, 184)), bx + 28, by + 276);

            string[] bt = new string[] { "开始游戏", "改一下备忘", "先不玩" };
            int[] bid = new int[] { 451, 453, 452 };
            int[] bwid = new int[] { 150, 140, 110 };
            int bxx = bx + 28;
            for (int i = 0; i < bt.Length; i++)
            {
                Rectangle r = new Rectangle(bxx, p.Bottom - 62, bwid[i], 42);
                bool hv = (hot == bid[i]);
                Color cc = (i == 2) ? Color.FromArgb(148, 163, 184) : (i == 0 ? OK : NEON);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(hv ? 225 : 180, 16, 30, 52))) Cham(g, r, 8, b);
                ChamEdge(g, r, 8, Color.FromArgb(190, cc), 2, hv ? 5 : 0);
                SizeF z = g.MeasureString(bt[i], fBold);
                g.DrawString(bt[i], fBold, new SolidBrush(cc), r.Left + (r.Width - z.Width) / 2, r.Top + 10);
                Click c = new Click(); c.R = r; c.Id = bid[i]; clicks.Add(c);
                bxx += r.Width + 10;
            }
        }
        else
        {
            int bw = Math.Min(700, w - 100), bh = 420;
            int bx = (w - bw) / 2, by = (h - bh) / 2;
            Rectangle p = new Rectangle(bx, by, bw, bh);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 12, 22, 38))) Cham(g, p, 18, b);
            ChamEdge(g, p, 18, Color.FromArgb(205, NEON), 2, 8);

            g.DrawString("PROGRESS NOTE", fS, new SolidBrush(Color.FromArgb(205, NEON)), bx + 28, by + 20);
            GlowText(g, "我停在哪", fT, Color.White, bx + 28, by + 38);
            g.DrawString("写一句你停下的地方——第几章、刚打完什么、按键是什么。\n" +
                         "下次这款游戏搁久了再点启动，我会先把这句话给你看一眼。",
                fB, new SolidBrush(Color.FromArgb(200, 190, 205, 225)), new RectangleF(bx + 28, by + 80, bw - 56, 46));
            if (ge.NoteTime.Length > 0)
                g.DrawString("上次记录：" + ge.NoteTime, fS,
                    new SolidBrush(Color.FromArgb(170, 148, 163, 184)), bx + 28, by + 302);

            string[] bt = new string[] { "保存", "清除", "关闭" };
            int[] bid = new int[] { 440, 441, 442 };
            int[] bwid = new int[] { 120, 100, 100 };
            int bxx = bx + 28;
            for (int i = 0; i < bt.Length; i++)
            {
                Rectangle r = new Rectangle(bxx, p.Bottom - 62, bwid[i], 42);
                bool hv = (hot == bid[i]);
                Color cc = (i == 1) ? DANGER : (i == 0 ? OK : Color.FromArgb(148, 163, 184));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(hv ? 225 : 180, 16, 30, 52))) Cham(g, r, 8, b);
                ChamEdge(g, r, 8, Color.FromArgb(190, cc), 2, hv ? 5 : 0);
                SizeF z = g.MeasureString(bt[i], fBold);
                g.DrawString(bt[i], fBold, new SolidBrush(cc), r.Left + (r.Width - z.Width) / 2, r.Top + 10);
                Click c = new Click(); c.R = r; c.Id = bid[i]; clicks.Add(c);
                bxx += r.Width + 10;
            }
        }

        fS.Dispose(); fT.Dispose(); fB.Dispose(); fBold.Dispose();
    }

    // ================= 绘制 =================
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        clicks.Clear();
        DrawBg(g);
        DrawTop(g);
        DrawList(g);
        DrawDetail(g);
        if (showReco) DrawReco(g);
        if (showVer) DrawVer(g);
        if (showAccel) DrawAccel(g);
        if (showFirst) DrawFirst(g);
        if (showNote) DrawNote(g);
    }

    void DrawBg(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
            Color.FromArgb(11, 15, 23), Color.FromArgb(21, 33, 54), 90f);
        g.FillRectangle(bg, 0, 0, w, h); bg.Dispose();
        for (int i = 0; i < stars.Count; i++)
        {
            Star s = stars[i];
            double tw = 0.45 + 0.55 * Math.Abs(Math.Sin(animT * s.Sp + s.Ph));
            int al = (int)(190 * tw); if (al < 12) al = 12;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(al, 175, 228, 255)))
                g.FillEllipse(b, (float)(s.X * w), (float)(s.Y * h), (float)(s.R * 2), (float)(s.R * 2));
        }
        using (Pen p = new Pen(Color.FromArgb(9, 255, 255, 255), 1))
            for (int y = 0; y < h; y += 3) g.DrawLine(p, 0, y, w, y);
    }

    void DrawTop(Graphics g)
    {
        int w = ClientSize.Width;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 6, 12, 24)))
            g.FillRectangle(b, 0, 0, w, 58);
        using (Pen p = new Pen(Color.FromArgb(130, NEON), 1)) g.DrawLine(p, 0, 58, w, 58);

        try
        {
            if (logoImg == null)
            {
                string ip = Path.Combine(appDir, "gamedock_256.png");
                if (File.Exists(ip))
                {
                    byte[] bytes = File.ReadAllBytes(ip);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (System.Drawing.Image tmp = System.Drawing.Image.FromStream(ms))
                    {
                        logoImg = new Bitmap(tmp);
                    }
                }
            }
            if (logoImg != null) g.DrawImage(logoImg, 16, 11, 36, 36);
        }
        catch (Exception) { }

        Font fLogo = new Font("Microsoft YaHei", 17, FontStyle.Bold);
        Font fEn = new Font("Consolas", 10.5f, FontStyle.Regular);
        Font fZhS = new Font("Microsoft YaHei", 10.5f, FontStyle.Regular);
        Font fZhB = new Font("Microsoft YaHei", 12, FontStyle.Bold);

        GlowText(g, "游戏坞", fLogo, NEON, 62, 15);
        g.DrawString("GAMEDOCK", fEn, new SolidBrush(Color.FromArgb(180, 125, 211, 252)), 132, 24);

        int playing = 0;
        for (int i = 0; i < lib.Count; i++) if (lib[i].Status == "在玩") playing++;
        g.DrawString("库内 " + lib.Count + " 个　在玩 " + playing + " 个　已通关 " + CountStatus("通关"), fZhS,
            new SolidBrush(Color.FromArgb(190, 148, 163, 184)), 260, 22);

        Rectangle rb = new Rectangle(w - 168, 13, 150, 32);
        bool hov = (hot == 900);
        using (SolidBrush b = new SolidBrush(hov ? Color.FromArgb(230, 60, 40, 8) : Color.FromArgb(190, 30, 44, 70)))
            Cham(g, rb, 8, b);
        ChamEdge(g, rb, 8, WARN, 2, hov ? 6 : 0);
        g.DrawString("今晚玩什么？", fZhB, new SolidBrush(WARN), rb.Left + 22, rb.Top + 7);
        Click cl = new Click(); cl.R = rb; cl.Id = 900; clicks.Add(cl);

        if (runningProc != null)
        {
            double sec = (DateTime.Now - runStart).TotalSeconds;
            string t = "● 运行中 " + runningName + "  " + Fmt(sec);
            SizeF sz = g.MeasureString(t, fZhB);
            Rectangle ab = new Rectangle(w / 2 - (int)sz.Width / 2 - 16, 66, (int)sz.Width + 32, 32);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 8, 40, 26))) Cham(g, ab, 8, b);
            ChamEdge(g, ab, 8, OK, 2, 6);
            g.DrawString(t, fZhB, new SolidBrush(OK), ab.Left + 16, ab.Top + 6);
        }

        fLogo.Dispose(); fEn.Dispose(); fZhS.Dispose(); fZhB.Dispose();
    }

    int CountStatus(string st)
    {
        int n = 0;
        for (int i = 0; i < lib.Count; i++) if (lib[i].Status == st) n++;
        return n;
    }

    int RowH = 62;

    void DrawList(Graphics g)
    {
        int h = ClientSize.Height;
        Rectangle side = new Rectangle(14, 116, 384, h - 160);
        using (SolidBrush b = new SolidBrush(PANEL)) Cham(g, side, 16, b);
        ChamEdge(g, side, 16, Color.FromArgb(140, NEON), 2, 4);

        Font fS = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font fN = new Font("Microsoft YaHei", 11.5f, FontStyle.Bold);
        Font fI = new Font("Microsoft YaHei", 9.5f, FontStyle.Regular);

        g.DrawString("LIBRARY", fS, new SolidBrush(Color.FromArgb(180, 125, 211, 252)), 30, 98);
        g.DrawString(shown.Count + " / " + lib.Count, fS, new SolidBrush(Color.FromArgb(150, 125, 211, 252)), 112, 98);

        if (shown.Count == 0)
        {
            Rectangle eb = new Rectangle(24, 130, 364, h - 180);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 16, 30, 52))) Cham(g, eb, 12, b);
            ChamEdge(g, eb, 12, Color.FromArgb(130, NEON), 1, 0);
            g.DrawString("库里还没有游戏", fN, new SolidBrush(TXT), 46, 152);
            g.DrawString("点任意一条开始：", fI, new SolidBrush(DIM), 46, 180);
            using (Font fb = new Font("Microsoft YaHei", 10.5f, FontStyle.Bold))
            {
                string[] bs = new string[] { "自动扫描 Steam / Epic", "选一个文件夹扫描", "手动挑一个 exe" };
                int[] bid = new int[] { 463, 461, 462 };
                int byy = 210;
                for (int i = 0; i < bs.Length; i++)
                {
                    Rectangle r = new Rectangle(40, byy, 332, 40);
                    bool hv = (hot == bid[i]);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(hv ? 215 : 165, 16, 34, 56))) Cham(g, r, 8, b);
                    ChamEdge(g, r, 8, Color.FromArgb(175, NEON), 2, hv ? 5 : 0);
                    g.DrawString(bs[i], fb, new SolidBrush(TXT), r.Left + 16, r.Top + 9);
                    Click c = new Click(); c.R = r; c.Id = bid[i]; clicks.Add(c);
                    byy += 50;
                }
            }
            g.DrawString("平时不联网；只有你点「从 Steam 获取」时才会去下载封面。", fI,
                new SolidBrush(Color.FromArgb(160, 148, 163, 184)), 46, 366);
            fS.Dispose(); fN.Dispose(); fI.Dispose();
            return;
        }

        int y = 130 - scroll;
        for (int k = 0; k < shown.Count; k++)
        {
            int gi = shown[k];
            Rectangle r = new Rectangle(24, y, 364, RowH - 6);
            if (y + RowH > 116 && y < h - 52)
            {
                bool on = (k == sel);
                if (on)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 16, 38, 66))) Cham(g, r, 10, b);
                    ChamEdge(g, r, 10, NEON, 2, 4);
                }
                else if (hot == 100 + k)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 16, 30, 52))) Cham(g, r, 10, b);
                }
                GameEntry ge = lib[gi];
                Color pc = PlatformColor(ge);

                using (SolidBrush b = new SolidBrush(pc)) g.FillRectangle(b, 24, y + 8, 3, RowH - 22);
                DrawCover(g, new Rectangle(31, y + 7, 66, RowH - 20), ge);
                string nm = ge.Name;
                if (nm.Length > 20) nm = nm.Substring(0, 20) + "…";
                g.DrawString(nm, fN, new SolidBrush(TXT), 108, y + 10);
                string sub;
                if (ge.StoreName.Length > 0)
                {
                    string vs;
                    if (ge.InstalledVer.Length == 0) vs = "未安装";
                    else if (ge.AvailVer.Length > 0 && ge.AvailVer != ge.InstalledVer) vs = "已装 " + ge.InstalledVer + " → 可更新 " + ge.AvailVer;
                    else vs = "已装 " + ge.InstalledVer;
                    sub = "星港   ·   " + vs + "   ·   " + StatusMark(ge.Status);
                }
                else sub = ge.Platform + "   ·   " + StatusMark(ge.Status) + "   ·   " + FmtShort(ge.PlaySeconds);
                if (sub.Length > 30) sub = sub.Substring(0, 30) + "…";
                g.DrawString(sub, fI, new SolidBrush(Color.FromArgb(200, 148, 163, 184)), 108, y + 33);
                Click cl = new Click(); cl.R = r; cl.Id = 100 + k; clicks.Add(cl);
            }
            y += RowH;
        }

        fS.Dispose(); fN.Dispose(); fI.Dispose();
    }

    string StatusMark(string st)
    {
        if (st == "在玩") return "在玩";
        if (st == "搁置") return "搁置";
        if (st == "通关") return "已通关";
        if (st == "弃坑") return "弃坑";
        return "未分类";
    }

    Color StatusColor(string st)
    {
        if (st == "在玩") return OK;
        if (st == "搁置") return WARN;
        if (st == "通关") return NEON2;
        if (st == "弃坑") return DANGER;
        return DIM;
    }

    string Fmt(double sec)
    {
        int s = (int)sec;
        if (s < 60) return s + " 秒";
        int m = s / 60; s = s % 60;
        if (m < 60) return m + " 分 " + s + " 秒";
        int hh = m / 60; m = m % 60;
        return hh + " 小时 " + m + " 分";
    }

    string FmtShort(double sec)
    {
        int s = (int)sec;
        if (s < 60) return s + "s";
        int m = s / 60;
        if (m < 60) return m + "min";
        return (m / 60) + "h" + (m % 60) + "m";
    }

    void DrawDetail(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        int cx = 412;
        Rectangle d = new Rectangle(cx, 116, w - cx - 20, h - 160);
        using (SolidBrush b = new SolidBrush(PANEL)) Cham(g, d, 16, b);
        ChamEdge(g, d, 16, Color.FromArgb(140, NEON), 2, 4);

        Font fS = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font fT = new Font("Microsoft YaHei", 23, FontStyle.Bold);
        Font fB = new Font("Microsoft YaHei", 11, FontStyle.Regular);
        Font fBold = new Font("Microsoft YaHei", 12, FontStyle.Bold);

        if (shown.Count == 0) { fS.Dispose(); fT.Dispose(); fB.Dispose(); fBold.Dispose(); return; }
        GameEntry ge = lib[shown[sel]];

        // 封面放右上角，标题和启动路径都要给它让位
        int colRight = d.Right - 18;
        int covW = Math.Min(340, Math.Max(200, d.Width - 460));
        int covH = covW * 215 / 460;
        Rectangle cov = new Rectangle(colRight - covW, 240, covW, covH);

        g.DrawString("DETAILS", fS, new SolidBrush(Color.FromArgb(180, 125, 211, 252)), cx + 22, 132);
        GlowText(g, TruncToFit(g, ge.Name, fT, d.Width - 40), fT, Color.White, cx + 22, 154);

        Color pc = PlatformColor(ge);
        Rectangle badge = new Rectangle(cx + 24, 202, 90, 26);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(70, pc))) Cham(g, badge, 6, b);
        ChamEdge(g, badge, 6, pc, 1, 0);
        g.DrawString(ge.Platform, fB, new SolidBrush(pc), badge.Left + 16, badge.Top + 4);

        int bx0 = badge.Right + 10;
        Rectangle sb = new Rectangle(bx0, 202, 96, 26);
        Color sc = StatusColor(ge.Status);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(70, sc))) Cham(g, sb, 6, b);
        ChamEdge(g, sb, 6, sc, 1, 0);
        g.DrawString(StatusMark(ge.Status), fB, new SolidBrush(sc), sb.Left + 16, sb.Top + 4);
        bx0 = sb.Right + 10;

        // 星港来的游戏：详情页顶部直接给一个版本与更新的入口
        if (ge.StoreName.Length > 0 || ge.Platform == "星港")
        {
            string vt;
            if (ge.InstalledVer.Length == 0) vt = "星港 · 未安装，点这里";
            else if (ge.AvailVer.Length > 0 && ge.AvailVer != ge.InstalledVer) vt = "星港 · " + ge.InstalledVer + " → " + ge.AvailVer + " 可更新";
            else vt = "星港 · 已装 " + ge.InstalledVer;
            SizeF vz = g.MeasureString(vt, fBold);
            Rectangle vb = new Rectangle(bx0, 202, (int)vz.Width + 34, 26);
            bool vh = (hot == 400);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(vh ? 210 : 160, 46, 34, 6))) Cham(g, vb, 6, b);
            ChamEdge(g, vb, 6, Color.FromArgb(vh ? 255 : 200, WARN), 1, vh ? 5 : 0);
            g.DrawString(vt, fBold, new SolidBrush(WARN), vb.Left + 16, vb.Top + 3);
            Click vc = new Click(); vc.R = vb; vc.Id = 400; clicks.Add(vc);
            bx0 = vb.Right + 10;
        }

        // 控制台程序：先告诉用户为什么点启动会弹黑窗口，再给一个开关
        if (IsConsoleGame(ge))
        {
            string ct = ge.HideConsole ? "控制台程序 · 已隐藏黑窗口" : "控制台程序 · 点这里隐藏黑窗口";
            Color ctag = ge.HideConsole ? OK : WARN;
            SizeF cz = g.MeasureString(ct, fBold);
            Rectangle cbb = new Rectangle(bx0, 202, (int)cz.Width + 34, 26);
            bool chv = (hot == 420);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(chv ? 215 : 165, 46, 34, 6))) Cham(g, cbb, 6, b);
            ChamEdge(g, cbb, 6, Color.FromArgb(chv ? 255 : 200, ctag), 1, chv ? 5 : 0);
            g.DrawString(ct, fBold, new SolidBrush(ctag), cbb.Left + 16, cbb.Top + 3);
            Click cc2 = new Click(); cc2.R = cbb; cc2.Id = 420; clicks.Add(cc2);
        }

        // 封面本体 + 三个操作按钮
        DrawCover(g, cov, ge);
        int cby = cov.Bottom + 10;
        string[] cbt = new string[] { "选封面", "从 Steam 获取", "清除" };
        int[] cbid = new int[] { 410, 411, 412 };
        int[] cbw = new int[] { 100, 148, 76 };
        int cbx = colRight - (cbw[0] + cbw[1] + cbw[2] + 16);
        if (cbx < cov.Left) cbx = cov.Left;
        for (int i = 0; i < cbt.Length; i++)
        {
            Rectangle r = new Rectangle(cbx, cby, cbw[i], 30);
            bool hov = (hot == cbid[i]);
            Color cc = (i == 2) ? Color.FromArgb(148, 163, 184) : NEON;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(hov ? 220 : 170, 16, 30, 52))) Cham(g, r, 6, b);
            ChamEdge(g, r, 6, Color.FromArgb(175, cc), 1, hov ? 4 : 0);
            g.DrawString(cbt[i], fB, new SolidBrush(cc), r.Left + 12, r.Top + 5);
            Click c = new Click(); c.R = r; c.Id = cbid[i]; clicks.Add(c);
            cbx += r.Width + 8;
        }
        if (ge.Cover.Length == 0)
            g.DrawString("还没有封面。Steam 游戏可以一键拉官方封面，其他手动选一张。",
                fS, new SolidBrush(Color.FromArgb(150, 148, 163, 184)), cov.Left, cby + 38);

        int y = 250;
        string[] lab = new string[] { "累计游玩", "启动次数", "最后游玩", "加入日期" };
        string[] val = new string[] {
            Fmt(ge.PlaySeconds),
            ge.LaunchCount + " 次",
            (ge.Last.Length == 0 ? "从未启动" : ge.Last),
            ge.Added
        };
        for (int i = 0; i < 4; i++)
        {
            g.DrawString(lab[i], fS, new SolidBrush(Color.FromArgb(170, 125, 211, 252)), cx + 24, y);
            g.DrawString(val[i], fBold, new SolidBrush(TXT), cx + 130, y - 3);
            y += 34;
        }

        g.DrawString("启动路径", fS, new SolidBrush(Color.FromArgb(170, 125, 211, 252)), cx + 24, y);
        string px = ge.Exe;
        px = TruncPathToFit(g, px, fB, Math.Max(140, cov.Left - 24 - (cx + 130)));
        g.DrawString(px, fB, new SolidBrush(Color.FromArgb(185, 148, 163, 184)), cx + 130, y - 1);
        y += 44;

        // 进度备忘：一行，点一下就能改
        if (y + 46 < h - 216)
        {
            g.DrawString("我停在哪", fS, new SolidBrush(Color.FromArgb(170, 125, 211, 252)), cx + 24, y);
            Rectangle nb = new Rectangle(cx + 130, y - 6, Math.Max(180, cov.Left - 24 - (cx + 130)), 34);
            bool has = ge.Note.Length > 0;
            bool nh = (hot == 430);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(nh ? 200 : 150, 16, 30, 52))) Cham(g, nb, 6, b);
            ChamEdge(g, nb, 6, Color.FromArgb(180, has ? OK : Color.FromArgb(100, 116, 139)), 1, nh ? 5 : 0);
            string ntxt = has ? ge.Note.Replace("\r\n", " / ").Replace("\n", " / ") : "（还没写）点这里填一句";
            g.DrawString(TruncToFit(g, ntxt, fB, nb.Width - 24), fB,
                new SolidBrush(has ? TXT : Color.FromArgb(145, 148, 163, 184)), nb.Left + 12, nb.Top + 7);
            Click nc = new Click(); nc.R = nb; nc.Id = 430; clicks.Add(nc);
            y += 46;
        }

        // 启动按钮
        Rectangle lb = new Rectangle(cx + 24, h - 210, 260, 58);
        using (SolidBrush gl = new SolidBrush(Color.FromArgb(70, NEON))) Cham(g, new Rectangle(lb.X - 4, lb.Y - 4, lb.Width + 8, lb.Height + 8), 18, gl);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 12, 74, 110))) Cham(g, lb, 16, b);
        ChamEdge(g, lb, 16, NEON, 2, 8);
        SizeF ts = g.MeasureString("▶  启 动 游 戏", fBold);
        g.DrawString("▶  启 动 游 戏", fBold, new SolidBrush(Color.White), lb.Left + (lb.Width - ts.Width) / 2, lb.Top + 20);
        Click cl = new Click(); cl.R = lb; cl.Id = 200; clicks.Add(cl);

        // 状态按钮
        string[] sts = new string[] { "在玩", "搁置", "通关", "弃坑", "移除" };
        int bx = cx + 300;
        for (int i = 0; i < sts.Length; i++)
        {
            SizeF z = g.MeasureString(sts[i], fB);
            Rectangle r = new Rectangle(bx, h - 200, (int)z.Width + 34, 38);
            bool hov = (hot == 300 + i);
            Color cc = (i == 4) ? DANGER : StatusColor(sts[i]);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(hov ? 215 : 170, 16, 30, 52))) Cham(g, r, 8, b);
            ChamEdge(g, r, 8, Color.FromArgb(170, cc), 2, hov ? 5 : 0);
            g.DrawString(sts[i], fB, new SolidBrush(cc), r.Left + 17, r.Top + 9);
            Click c2 = new Click(); c2.R = r; c2.Id = 300 + i; clicks.Add(c2);
            bx += r.Width + 10;
        }

        // 时间线
        int ty = h - 132;
        g.DrawString("最近记录", fS, new SolidBrush(Color.FromArgb(170, 125, 211, 252)), cx + 24, ty);
        int st = timeline.Count - 3; if (st < 0) st = 0;
        int ly = ty + 22;
        for (int i = st; i < timeline.Count; i++)
        {
            string t = timeline[i].T.ToString("MM-dd HH:mm") + "   " + timeline[i].Text;
            if (t.Length > 78) t = t.Substring(0, 78) + "…";
            g.DrawString(t, fB, new SolidBrush(Color.FromArgb(175, 148, 163, 184)), cx + 24, ly);
            ly += 22;
        }

        fS.Dispose(); fT.Dispose(); fB.Dispose(); fBold.Dispose();
    }

    void DrawReco(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        using (SolidBrush ov = new SolidBrush(Color.FromArgb(170, 4, 8, 16))) g.FillRectangle(ov, 0, 0, w, h);

        int pw = Math.Min(760, w - 120);
        int ph = 500;
        int px = (w - pw) / 2, py = (h - ph) / 2;
        Rectangle p = new Rectangle(px, py, pw, ph);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 10, 20, 38))) Cham(g, p, 20, b);
        ChamEdge(g, p, 20, WARN, 2, 10);

        Font fS = new Font("Consolas", 10, FontStyle.Regular);
        Font fT = new Font("Microsoft YaHei", 18, FontStyle.Bold);
        Font fN = new Font("Microsoft YaHei", 14, FontStyle.Bold);
        Font fB = new Font("Microsoft YaHei", 10.5f, FontStyle.Regular);

        g.DrawString("WHAT TO PLAY TONIGHT", fS, new SolidBrush(Color.FromArgb(200, WARN)), px + 28, py + 22);
        g.DrawString("今晚玩什么？", fT, new SolidBrush(TXT), px + 28, py + 44);
        g.DrawString("根据你的游玩时长、闲置天数和当前状态算出来的三个候选。", fB,
            new SolidBrush(Color.FromArgb(190, 148, 163, 184)), px + 28, py + 82);

        if (reco.Count == 0)
        {
            g.DrawString("库里还没有游戏，先去扫描或手动添加。", fB, new SolidBrush(DIM), px + 28, py + 130);
        }
        int y = py + 122;
        for (int i = 0; i < reco.Count; i++)
        {
            GameEntry ge = lib[reco[i]];
            Rectangle r = new Rectangle(px + 24, y, pw - 48, 90);
            bool hov = (hot == 600 + i);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(hov ? 235 : 200, 18, 34, 58))) Cham(g, r, 12, b);
            ChamEdge(g, r, 12, Color.FromArgb(hov ? 255 : 150, WARN), 2, hov ? 6 : 0);
            string nm = ge.Name;
            if (nm.Length > 26) nm = nm.Substring(0, 26) + "…";
            g.DrawString((i + 1) + ".  " + nm, fN, new SolidBrush(TXT), r.Left + 20, r.Top + 12);
            string sub = ge.Platform + "   ·   " + StatusMark(ge.Status) + "   ·   累计 " + Fmt(ge.PlaySeconds) +
                (ge.Last.Length == 0 ? "   ·   从未启动" : "   ·   上次 " + ge.Last);
            g.DrawString(sub, fB, new SolidBrush(Color.FromArgb(200, 148, 163, 184)), r.Left + 20, r.Top + 42);
            if (ge.Note.Length > 0)
            {
                string nt = "我停在哪：" + ge.Note.Replace("\r\n", " / ").Replace("\n", " / ");
                g.DrawString(TruncToFit(g, nt, fB, r.Width - 44), fB,
                    new SolidBrush(Color.FromArgb(225, 74, 222, 128)), r.Left + 20, r.Top + 64);
            }
            Click cl = new Click(); cl.R = r; cl.Id = 600 + i; clicks.Add(cl);
            y += 96;
        }

        Rectangle cb = new Rectangle(px + pw - 150, py + ph - 60, 126, 38);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 30, 44, 70))) Cham(g, cb, 8, b);
        ChamEdge(g, cb, 8, Color.FromArgb(170, 148, 163, 184), 2, 0);
        g.DrawString("换一批", fB, new SolidBrush(TXT), cb.Left + 34, cb.Top + 9);
        Click c1 = new Click(); c1.R = cb; c1.Id = 601; clicks.Add(c1);

        Rectangle xb = new Rectangle(px + 24, py + ph - 60, 120, 38);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 30, 44, 70))) Cham(g, xb, 8, b);
        ChamEdge(g, xb, 8, Color.FromArgb(170, 148, 163, 184), 2, 0);
        g.DrawString("关闭", fB, new SolidBrush(TXT), xb.Left + 42, xb.Top + 9);
        Click c3 = new Click(); c3.R = xb; c3.Id = 602; clicks.Add(c3);

        fS.Dispose(); fT.Dispose(); fN.Dispose(); fB.Dispose();
    }

    void DrawVer(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        using (SolidBrush ov = new SolidBrush(Color.FromArgb(185, 4, 8, 16))) g.FillRectangle(ov, 0, 0, w, h);
        if (shown.Count == 0) return;
        GameEntry ge = lib[shown[sel]];
        ShipGame s = ShipFor(ge.StoreName.Length > 0 ? ge.StoreName : ge.Name);

        int pw = Math.Min(840, w - 100), ph = 486;
        int px = (w - pw) / 2, py = (h - ph) / 2;
        Rectangle p = new Rectangle(px, py, pw, ph);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 12, 22, 38))) Cham(g, p, 18, b);
        ChamEdge(g, p, 18, Color.FromArgb(205, WARN), 2, 8);

        Font fS = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font fT = new Font("Microsoft YaHei", 17, FontStyle.Bold);
        Font fB = new Font("Microsoft YaHei", 11, FontStyle.Regular);
        Font fBold = new Font("Microsoft YaHei", 11.5f, FontStyle.Bold);

        g.DrawString("STARPORT BUILDS", fS, new SolidBrush(Color.FromArgb(205, WARN)), px + 26, py + 18);
        string ttl = ge.Name;
        if (ttl.Length > 24) ttl = ttl.Substring(0, 24) + "…";
        GlowText(g, ttl, fT, Color.White, px + 26, py + 38);

        if (s == null)
        {
            g.DrawString("本机没读到这个作品的星港数据。\n\n先在星港里发布它并上传构建包，\n再回主界面点「同步星港」。", fB,
                new SolidBrush(DIM), new RectangleF(px + 26, py + 100, pw - 52, 140));
        }
        else
        {
            string head = "开发者 " + s.Dev + "　·　" + s.Genre + "　·　" + s.Rating +
                "　·　星港最新 " + (s.CurVer.Length > 0 ? s.CurVer : "未上传") +
                "　·　本机 " + (ge.InstalledVer.Length > 0 ? ("已装 " + ge.InstalledVer) : "未安装");
            g.DrawString(head, fB, new SolidBrush(DIM), px + 26, py + 76);

            g.DrawString("版本号", fS, new SolidBrush(Color.FromArgb(190, 251, 191, 36)), px + 30, py + 108);
            g.DrawString("大小", fS, new SolidBrush(Color.FromArgb(190, 251, 191, 36)), px + 150, py + 108);
            g.DrawString("上传时间", fS, new SolidBrush(Color.FromArgb(190, 251, 191, 36)), px + 240, py + 108);
            g.DrawString("指纹", fS, new SolidBrush(Color.FromArgb(190, 251, 191, 36)), px + 370, py + 108);
            g.DrawString("状态", fS, new SolidBrush(Color.FromArgb(190, 251, 191, 36)), px + 540, py + 108);
            using (Pen pen = new Pen(Color.FromArgb(90, WARN), 1)) g.DrawLine(pen, px + 26, py + 128, p.Right - 26, py + 128);

            int vy = py + 142;
            int limit = p.Bottom - 128;
            for (int i = s.Vers.Count - 1; i >= 0; i--)
            {
                if (vy > limit) break;
                ShipBuild b = s.Vers[i];
                bool inst = (ge.InstalledVer == b.Ver);
                bool pick = (i == selVer);
                Rectangle r = new Rectangle(px + 22, vy - 5, pw - 44, 34);
                if (pick)
                {
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(190, 44, 34, 6))) Cham(g, r, 8, hb);
                    ChamEdge(g, r, 8, WARN, 2, 3);
                }
                g.DrawString(b.Ver, fBold, new SolidBrush(inst ? OK : TXT), px + 30, vy);
                g.DrawString(FmtSize(b.Size), fB, new SolidBrush(DIM), px + 150, vy);
                g.DrawString(b.Time, fB, new SolidBrush(DIM), px + 240, vy);
                g.DrawString(b.Sha, fS, new SolidBrush(Color.FromArgb(160, 125, 211, 252)), px + 370, vy + 1);
                g.DrawString(inst ? "已安装" : "可安装", fB,
                    new SolidBrush(inst ? OK : Color.FromArgb(170, 148, 163, 184)), px + 540, vy);
                if (b.Note.Length > 0)
                {
                    string nt = "说明：" + b.Note;
                    if (nt.Length > 46) nt = nt.Substring(0, 46) + "…";
                    g.DrawString(nt, fS, new SolidBrush(Color.FromArgb(150, 148, 163, 184)), px + 150, vy + 18);
                }
                Click c = new Click(); c.R = r; c.Id = 700 + i; clicks.Add(c);
                vy += 42;
            }
            if (s.Vers.Count == 0)
                g.DrawString("这个作品在星港上还没有上传过任何构建包。", fB, new SolidBrush(DIM), px + 30, vy);
        }

        if (verMsgT > 0 && verMsg.Length > 0)
        {
            Rectangle mb = new Rectangle(px + 26, p.Bottom - 112, pw - 52, 34);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 10, 30, 46))) Cham(g, mb, 8, b);
            ChamEdge(g, mb, 8, Color.FromArgb(190, NEON), 1, 0);
            g.DrawString(verMsg, fB, new SolidBrush(TXT), mb.Left + 14, mb.Top + 7);
        }

        int by = p.Bottom - 62;
        string[] bt = new string[] { "安装 / 更新到最新", "安装选中版本", "卸载本机文件", "关闭" };
        int[] bid = new int[] { 750, 751, 752, 753 };
        int[] bw = new int[] { 190, 170, 160, 100 };
        int bx = px + 26;
        for (int i = 0; i < bt.Length; i++)
        {
            if (i == 3) bx = p.Right - 26 - bw[i];
            Rectangle r = new Rectangle(bx, by, bw[i], 42);
            bool hov = (hot == bid[i]);
            Color cc = (i == 2) ? DANGER : (i == 3 ? Color.FromArgb(148, 163, 184) : WARN);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(hov ? 225 : 180, 16, 30, 52))) Cham(g, r, 8, b);
            ChamEdge(g, r, 8, Color.FromArgb(190, cc), 2, hov ? 5 : 0);
            SizeF z = g.MeasureString(bt[i], fBold);
            g.DrawString(bt[i], fBold, new SolidBrush(cc), r.Left + (r.Width - z.Width) / 2, r.Top + 10);
            Click cl = new Click(); cl.R = r; cl.Id = bid[i]; clicks.Add(cl);
            bx += r.Width + 10;
        }

        fS.Dispose(); fT.Dispose(); fB.Dispose(); fBold.Dispose();
    }

    void DrawAccel(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        using (SolidBrush ov = new SolidBrush(Color.FromArgb(205, 4, 8, 16))) g.FillRectangle(ov, 0, 0, w, h);

        int pw = Math.Min(760, w - 100), ph = 508;
        int px = (w - pw) / 2, py = (h - ph) / 2;
        Rectangle p = new Rectangle(px, py, pw, ph);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 12, 22, 38))) Cham(g, p, 18, b);
        ChamEdge(g, p, 18, Color.FromArgb(205, NEON2), 2, 8);

        Font fS = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font fT = new Font("Microsoft YaHei", 18, FontStyle.Bold);
        Font fB = new Font("Microsoft YaHei", 10.5f, FontStyle.Regular);
        Font fBold = new Font("Microsoft YaHei", 11, FontStyle.Bold);

        g.DrawString("GAME ACCELERATOR", fS, new SolidBrush(Color.FromArgb(205, NEON2)), px + 28, py + 20);
        GlowText(g, "加速器联动", fT, Color.White, px + 28, py + 40);
        g.DrawString("游戏坞不修改加速器，也不替它在里面选游戏。\n" +
                     "这里只做一件事：启动游戏前先把它拉起来，省得你每次手动开一遍。",
            fB, new SolidBrush(Color.FromArgb(200, 190, 205, 225)), new RectangleF(px + 28, py + 82, pw - 56, 48));

        int y = py + 138;
        g.DrawString("当前选用 CURRENT", fS, new SolidBrush(Color.FromArgb(190, 167, 139, 250)), px + 28, y);
        y += 22;
        Rectangle cur = new Rectangle(px + 24, y, pw - 48, 54);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(170, 16, 28, 48))) Cham(g, cur, 8, b);
        ChamEdge(g, cur, 8, Color.FromArgb(140, NEON2), 1, 0);
        if (accelPath.Length == 0)
        {
            g.DrawString("还没有指定加速器。自动检测到的可以直接点「用这个」，",
                fB, new SolidBrush(DIM), cur.Left + 14, cur.Top + 8);
            g.DrawString("检测不到就用「手动指定」直接挑它的 exe。",
                fB, new SolidBrush(DIM), cur.Left + 14, cur.Top + 28);
        }
        else
        {
            bool run = AccelRunning();
            g.DrawString((accelName.Length > 0 ? accelName : Path.GetFileNameWithoutExtension(accelPath)) +
                         "　·　" + (run ? "运行中" : "未运行"), fBold,
                new SolidBrush(run ? OK : TXT), cur.Left + 14, cur.Top + 8);
            g.DrawString(TruncToFit(g, accelPath, fS, pw - 90), fS,
                new SolidBrush(Color.FromArgb(170, 148, 163, 184)), cur.Left + 14, cur.Top + 32);
        }
        y += 64;

        Rectangle tg = new Rectangle(px + 24, y, pw - 48, 40);
        bool on = accelAuto && accelPath.Length > 0;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(on ? 190 : 130, on ? 18 : 16, on ? 52 : 30, on ? 40 : 52))) Cham(g, tg, 8, b);
        ChamEdge(g, tg, 8, Color.FromArgb(hot == 512 ? 235 : 165, on ? OK : Color.FromArgb(148, 163, 184)), 2, hot == 512 ? 5 : 0);
        g.DrawString((on ? "[√]" : "[  ]") + "   启动游戏前先启动加速器", fBold,
            new SolidBrush(on ? OK : TXT), tg.Left + 16, tg.Top + 9);
        Click ct = new Click(); ct.R = tg; ct.Id = 512; clicks.Add(ct);
        y += 54;

        g.DrawString("自动检测到的加速器（" + accelFound.Count + "）", fS,
            new SolidBrush(Color.FromArgb(190, 167, 139, 250)), px + 28, y);
        y += 24;
        if (accelFound.Count == 0)
        {
            g.DrawString("没检测到。可能没装，也可能装在非常规目录——用「手动指定」直接挑 exe 就行。",
                fB, new SolidBrush(DIM), px + 30, y + 2);
        }
        for (int i = 0; i < accelFound.Count && i < 4; i++)
        {
            Rectangle r = new Rectangle(px + 24, y, pw - 48, 42);
            g.DrawString(TruncToFit(g, accelFound[i][0], fBold, pw - 220), fBold, new SolidBrush(TXT), r.Left + 14, r.Top + 4);
            g.DrawString(TruncToFit(g, accelFound[i][1], fS, pw - 220), fS,
                new SolidBrush(Color.FromArgb(165, 148, 163, 184)), r.Left + 14, r.Top + 23);
            Rectangle ub = new Rectangle(r.Right - 104, r.Top + 7, 92, 28);
            bool hv = (hot == 500 + i);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(hv ? 220 : 175, 30, 24, 56))) Cham(g, ub, 6, b);
            ChamEdge(g, ub, 6, Color.FromArgb(180, NEON2), 1, hv ? 4 : 0);
            g.DrawString("用这个", fB, new SolidBrush(NEON2), ub.Left + 20, ub.Top + 4);
            Click c = new Click(); c.R = ub; c.Id = 500 + i; clicks.Add(c);
            y += 48;
        }

        int by = p.Bottom - 64;
        string[] bt = new string[] { "手动指定 exe", "立即启动", "清除" };
        int[] bid = new int[] { 510, 513, 511 };
        int[] bw = new int[] { 152, 120, 90 };
        int bx = px + 28;
        for (int i = 0; i < bt.Length; i++)
        {
            Rectangle r = new Rectangle(bx, by, bw[i], 42);
            bool hv = (hot == bid[i]);
            Color cc = (i == 2) ? Color.FromArgb(148, 163, 184) : NEON2;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(hv ? 220 : 175, 16, 30, 52))) Cham(g, r, 8, b);
            ChamEdge(g, r, 8, Color.FromArgb(180, cc), 2, hv ? 5 : 0);
            SizeF z = g.MeasureString(bt[i], fBold);
            g.DrawString(bt[i], fBold, new SolidBrush(cc), r.Left + (r.Width - z.Width) / 2, r.Top + 10);
            Click c = new Click(); c.R = r; c.Id = bid[i]; clicks.Add(c);
            bx += r.Width + 10;
        }
        Rectangle cb2 = new Rectangle(p.Right - 28 - 96, by, 96, 42);
        bool ch2 = (hot == 514);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(ch2 ? 220 : 175, 16, 30, 52))) Cham(g, cb2, 8, b);
        ChamEdge(g, cb2, 8, Color.FromArgb(180, Color.FromArgb(148, 163, 184)), 2, ch2 ? 5 : 0);
        g.DrawString("关闭", fBold, new SolidBrush(TXT), cb2.Left + 30, cb2.Top + 10);
        Click cc2 = new Click(); cc2.R = cb2; cc2.Id = 514; clicks.Add(cc2);

        fS.Dispose(); fT.Dispose(); fB.Dispose(); fBold.Dispose();
    }

    void DrawFirst(Graphics g)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        using (SolidBrush ov = new SolidBrush(Color.FromArgb(208, 4, 8, 16))) g.FillRectangle(ov, 0, 0, w, h);

        int pw = Math.Min(700, w - 100), ph = 406;
        int px = (w - pw) / 2, py = (h - ph) / 2;
        Rectangle p = new Rectangle(px, py, pw, ph);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 12, 22, 38))) Cham(g, p, 18, b);
        ChamEdge(g, p, 18, Color.FromArgb(205, NEON), 2, 8);

        Font fS = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font fT = new Font("Microsoft YaHei", 20, FontStyle.Bold);
        Font fB = new Font("Microsoft YaHei", 11, FontStyle.Regular);
        Font fBold = new Font("Microsoft YaHei", 11.5f, FontStyle.Bold);

        g.DrawString("FIRST RUN", fS, new SolidBrush(Color.FromArgb(200, NEON)), px + 30, py + 22);
        GlowText(g, "欢迎使用游戏坞", fT, Color.White, px + 30, py + 42);

        int ty = py + 108;
        if (firstFound > 0)
        {
            g.DrawString("已经自动扫到 " + firstFound + " 个游戏，可以直接用了。", fBold, new SolidBrush(OK), px + 30, ty);
            ty += 36;
            g.DrawString("这个工具的核心只有一个功能：「今晚玩什么」。\n" +
                         "先给游戏标上状态（在玩 / 搁置 / 通关 / 弃坑），\n" +
                         "之后它按游玩时长、状态、闲置天数算分，直接给你一个结果。", fB,
                new SolidBrush(TXT), new RectangleF(px + 30, ty, pw - 60, 110));
            ty += 116;
            g.DrawString("小提示：右上角「批量封面」能给 Steam 游戏一键拉官方封面。", fS,
                new SolidBrush(Color.FromArgb(190, 148, 163, 184)), px + 30, ty);

            Rectangle b1 = new Rectangle(px + 30, p.Bottom - 68, pw - 60, 46);
            bool hov = (hot == 460);
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(hov ? 90 : 60, NEON))) Cham(g, new Rectangle(b1.X - 4, b1.Y - 4, b1.Width + 8, b1.Height + 8), 14, gb);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 12, 74, 110))) Cham(g, b1, 12, b);
            ChamEdge(g, b1, 12, NEON, 2, hov ? 9 : 7);
            SizeF ts = g.MeasureString("开始使用", fBold);
            g.DrawString("开始使用", fBold, new SolidBrush(Color.White), b1.Left + (b1.Width - ts.Width) / 2, b1.Top + 12);
            Click c1 = new Click(); c1.R = b1; c1.Id = 460; clicks.Add(c1);
        }
        else
        {
            g.DrawString("没有找到已安装的游戏。", fBold, new SolidBrush(WARN), px + 30, ty);
            ty += 32;
            g.DrawString("常见原因：\n" +
                         "·  这台电脑没装 Steam 或 Epic\n" +
                         "·  游戏装在另一个系统账户下\n" +
                         "·  玩的是绿色版，平台不认识它", fB,
                new SolidBrush(TXT), new RectangleF(px + 30, ty, pw - 60, 100));
            ty += 104;
            g.DrawString("选一条继续：", fB, new SolidBrush(DIM), px + 30, ty);

            string[] bs = new string[] { "选文件夹扫描", "手动加一个 exe", "先跳过" };
            int[] bid = new int[] { 461, 462, 460 };
            int[] bw = new int[] { 176, 176, 118 };
            int bx = px + 30;
            for (int i = 0; i < bs.Length; i++)
            {
                Rectangle r = new Rectangle(bx, p.Bottom - 68, bw[i], 46);
                bool hv = (hot == bid[i]);
                Color cc = (i == 2) ? Color.FromArgb(148, 163, 184) : NEON;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(hv ? 220 : 175, 16, 30, 52))) Cham(g, r, 10, b);
                ChamEdge(g, r, 10, Color.FromArgb(185, cc), 2, hv ? 6 : 0);
                SizeF z = g.MeasureString(bs[i], fBold);
                g.DrawString(bs[i], fBold, new SolidBrush(cc), r.Left + (r.Width - z.Width) / 2, r.Top + 12);
                Click c = new Click(); c.R = r; c.Id = bid[i]; clicks.Add(c);
                bx += r.Width + 10;
            }
        }

        fS.Dispose(); fT.Dispose(); fB.Dispose(); fBold.Dispose();
    }

    void GlowText(Graphics g, string s, Font f, Color col, float x, float y)
    {
        // 辉光只用于标题级文字：小字加辉光会把笔画糊开
        if (f.Size >= 16f)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, col.R, col.G, col.B)))
            {
                g.DrawString(s, f, b, x - 1.4f, y);
                g.DrawString(s, f, b, x + 1.4f, y);
                g.DrawString(s, f, b, x, y - 1.4f);
                g.DrawString(s, f, b, x, y + 1.4f);
            }
        }
        using (SolidBrush b2 = new SolidBrush(col)) g.DrawString(s, f, b2, x, y);
    }

    void Cham(Graphics g, Rectangle r, int c, Brush b)
    {
        using (GraphicsPath p = Chamfer(r, c)) g.FillPath(b, p);
    }

    void ChamEdge(Graphics g, Rectangle r, int c, Color col, int wdt, int glow)
    {
        if (glow > 0)
        {
            for (int i = glow; i >= 1; i--)
            {
                int al = (int)(30.0 * i / glow);
                using (Pen p = new Pen(Color.FromArgb(al, col.R, col.G, col.B), wdt + i * 2))
                using (GraphicsPath path = Chamfer(r, c)) g.DrawPath(p, path);
            }
        }
        using (Pen p2 = new Pen(col, wdt))
        using (GraphicsPath path2 = Chamfer(r, c)) g.DrawPath(p2, path2);
    }

    GraphicsPath Chamfer(Rectangle r, int c)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddPolygon(new Point[] {
            new Point(r.X + c, r.Y), new Point(r.Right - c, r.Y),
            new Point(r.Right, r.Y + c), new Point(r.Right, r.Bottom - c),
            new Point(r.Right - c, r.Bottom), new Point(r.X + c, r.Bottom),
            new Point(r.X, r.Bottom - c), new Point(r.X, r.Y + c)
        });
        p.CloseFigure();
        return p;
    }

    // ================= 交互 =================
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        hot = -1;
        for (int i = 0; i < clicks.Count; i++) if (clicks[i].R.Contains(e.Location)) hot = clicks[i].Id;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int maxSc = shown.Count * RowH - (ClientSize.Height - 250);
        if (maxSc < 0) maxSc = 0;
        scroll -= e.Delta / 3;
        if (scroll < 0) scroll = 0;
        if (scroll > maxSc) scroll = maxSc;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (showNote)
        {
            for (int i = 0; i < clicks.Count; i++)
            {
                if (!clicks[i].R.Contains(e.Location)) continue;
                int nid = clicks[i].Id;
                if ((nid >= 440 && nid <= 442) || (nid >= 450 && nid <= 459)) { HandleNoteClick(nid); return; }
            }
            // 简报模式点外面等于「先不玩」；编辑模式点外面不关，避免把没保存的内容弄丢
            if (noteBrief) { showNote = false; pendLaunch = -1; ApplyNote(); Invalidate(); }
            return;
        }
        if (showFirst)
        {
            // 首次引导层压在最上面，必须做一次选择才消失
            for (int i = 0; i < clicks.Count; i++)
            {
                if (!clicks[i].R.Contains(e.Location)) continue;
                int fid = clicks[i].Id;
                if (fid >= 460 && fid < 470) { HandleFirstClick(fid); return; }
            }
            return;
        }
        if (showVer)
        {
            for (int i = 0; i < clicks.Count; i++)
            {
                if (!clicks[i].R.Contains(e.Location)) continue;
                int vid = clicks[i].Id;
                if (vid >= 700 && vid < 800) { HandleVerClick(vid); return; }
            }
            showVer = false; Invalidate();   // 点面板外面就关掉
            return;
        }
        if (showAccel)
        {
            for (int i = 0; i < clicks.Count; i++)
            {
                if (!clicks[i].R.Contains(e.Location)) continue;
                int aid = clicks[i].Id;
                if (aid >= 500 && aid < 520) { HandleAccelClick(aid); return; }
            }
            showAccel = false; Invalidate();
            return;
        }
        for (int i = 0; i < clicks.Count; i++)
        {
            if (!clicks[i].R.Contains(e.Location)) continue;
            int id = clicks[i].Id;
            // 200 必须在 100~299 之前判，否则启动按钮永远点不动
            if (id == 200) { if (shown.Count > 0) Launch(shown[sel]); return; }
            if (id == 400)
            {
                if (shown.Count == 0) return;
                LoadShip();
                GameEntry g0 = lib[shown[sel]];
                ShipGame s0 = ShipFor(g0.StoreName.Length > 0 ? g0.StoreName : g0.Name);
                selVer = (s0 != null && s0.Vers.Count > 0) ? s0.Vers.Count - 1 : 0;
                verMsg = ""; verMsgT = 0;
                showVer = true; Invalidate();
                return;
            }
            if (id >= 100 && id < 300) { sel = id - 100; showReco = false; Invalidate(); return; }
            if (id >= 300 && id < 400) { SetStatus(id - 300); return; }
            if (id >= 410 && id < 420) { HandleCoverClick(id); return; }
            if (id == 430) { OpenNoteEditor(); return; }
            if (id == 420)
            {
                if (shown.Count == 0) return;
                GameEntry gc = lib[shown[sel]];
                gc.HideConsole = !gc.HideConsole;
                SaveLib();
                Log("控制台窗口设置 " + gc.Name + " → " + (gc.HideConsole ? "隐藏" : "显示"));
                Say(gc.HideConsole ? "已设置：启动时隐藏控制台窗口" : "已设置：启动时显示控制台窗口", false);
                Invalidate();
                return;
            }
            if (id >= 460 && id < 470) { HandleFirstClick(id); return; }
            if (id == 900) { MakeReco(); Invalidate(); return; }
            if (id >= 600 && id < 601) { if (id - 600 < reco.Count) { sel = IndexOfShown(reco[id - 600]); showReco = false; Invalidate(); } return; }
            if (id == 601) { MakeReco(); Invalidate(); return; }
            if (id == 602) { showReco = false; Invalidate(); return; }
        }
    }

    int IndexOfShown(int gi)
    {
        for (int i = 0; i < shown.Count; i++) if (shown[i] == gi) return i;
        return 0;
    }

    void SetStatus(int k)
    {
        if (shown.Count == 0) return;
        int gi = shown[sel];
        if (k == 4)
        {
            string nm = lib[gi].Name;
            lib.RemoveAt(gi);
            SaveLib(); Rebuild();
            Log("移出库：" + nm);
            Say("已移出：" + nm, false);
            return;
        }
        string[] sts = new string[] { "在玩", "搁置", "通关", "弃坑" };
        lib[gi].Status = sts[k];
        SaveLib();
        Log("标记 " + lib[gi].Name + " 为 " + sts[k]);
        Say(lib[gi].Name + " → " + sts[k], false);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down && shown.Count > 0) { sel = Math.Min(shown.Count - 1, sel + 1); Invalidate(); }
        if (e.KeyCode == Keys.Up && shown.Count > 0) { sel = Math.Max(0, sel - 1); Invalidate(); }
        if (e.KeyCode == Keys.Enter && shown.Count > 0) Launch(shown[sel]);
        if (e.KeyCode == Keys.Escape) showReco = false;
        if (e.KeyCode == Keys.F5) DoScan();
    }
}
