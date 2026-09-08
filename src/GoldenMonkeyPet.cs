using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--codex-hook")
        {
            try { CodexStatus.Receive(Console.In.ReadToEnd()); }
            catch (Exception) { /* Status reporting must never interrupt Codex work. */ }
            Console.WriteLine("{}");
            return;
        }
        bool created;
        using (var mutex = new Mutex(true, "GoldenMonkeyDesktopPet.Native.V1", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PetForm());
        }
    }
}

internal sealed class StatusBubble : Label
{
    internal string Phase = "disconnected";
    internal string Caption = "";
    internal int Pulse;

    public StatusBubble()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Font = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
        BackColor = Color.Magenta;
        AccessibleName = "Codex 工作状态";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        // Hard edges keep the transparent color-key window free of magenta halos.
        g.SmoothingMode = SmoothingMode.None;
        bool compact = Width <= 22;
        Color accent = Phase == "waiting" ? Color.FromArgb(185, 107, 42)
            : Phase == "ended" ? Color.FromArgb(76, 126, 103) : Color.FromArgb(130, 101, 70);
        Rectangle body = new Rectangle(0, 0, Width - 1, Height - 5);
        using (GraphicsPath path = new GraphicsPath())
        {
            int d = Math.Min(12, body.Height);
            path.AddArc(body.Left, body.Top, d, d, 180, 90);
            path.AddArc(body.Right - d, body.Top, d, d, 270, 90);
            path.AddArc(body.Right - d, body.Bottom - d, d, d, 0, 90);
            path.AddArc(body.Left, body.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            using (SolidBrush fill = new SolidBrush(Phase == "waiting" ? Color.FromArgb(255, 235, 199) : Color.FromArgb(255, 250, 236)))
                g.FillPath(fill, path);
            using (Pen outline = new Pen(Color.FromArgb(205, 183, 144))) g.DrawPath(outline, path);
        }
        if (!compact)
        {
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(205, 183, 144)))
                g.FillPolygon(fill, new[] { new Point(Width / 2 - 3, Height - 5), new Point(Width / 2 + 3, Height - 5), new Point(Width / 2, Height - 1) });
        }
        using (SolidBrush ink = new SolidBrush(accent))
        {
            if (Phase == "busy")
                for (int i = 0; i < 3; i++) g.FillEllipse(ink, 8 + i * 5, 10 - (Pulse % 3 == i ? 2 : 0), 3, 3);
            else if (Phase == "disconnected")
            {
                using (Pen ring = new Pen(accent)) g.DrawEllipse(ring, 7, 7, 5, 5);
            }
            else TextRenderer.DrawText(g, Phase == "waiting" ? "!" : Phase == "ended" ? "✓" : "?", Font,
                new Rectangle(3, 1, compact ? Width - 6 : 23, 20), accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        if (!compact) TextRenderer.DrawText(g, Caption, Font, new Rectangle(27, 0, Width - 33, Height - 4), accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

internal sealed class PetForm : Form
{
    private enum PetState { Idle, Walking, Turning, Expression, Dragging, Bouncing, Sleeping, Waking }
    private DateTime sleepStarted;
    private DateTime nextSleepText;
    private DateTime nextIdleAction = DateTime.Now.AddSeconds(7);
    private DateTime pendingPatAt;
    private bool pendingPat;
    private bool headPress;
    private bool doubleClickHandled;
    private readonly StatusBubble codexLabel = new StatusBubble();
    private readonly ToolTip codexTip = new ToolTip();
    private readonly DateTime statusOpened = DateTime.UtcNow;
    private DateTime statusHiddenUntil;
    private readonly System.Windows.Forms.Timer codexTimer = new System.Windows.Forms.Timer();
    private CodexSnapshot codex = new CodexSnapshot();
    private PetState state = PetState.Idle;
    private bool sleeping;
    private ToolStripMenuItem sleepToggleItem;
    private sealed class Effect
    {
        public Control Control;
        public int Age;
        public int Lifetime;
        public int Rise;
        public int Drift;
    }

    private readonly Color key = Color.Magenta;
    private readonly Random random = new Random();
    private readonly Dictionary<string, Image> frames = new Dictionary<string, Image>();
    private readonly List<Effect> effects = new List<Effect>();
    private readonly PictureBox pet = new PictureBox();
    private readonly Panel canvas = new Panel();
    private readonly System.Windows.Forms.Timer effectTimer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer randomHeartTimer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer movementTimer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer bounceTimer = new System.Windows.Forms.Timer();
    private readonly int[] bounceOffsets = { 0, -8, -12, -8, -3, 2, 0 };
    private ToolStripMenuItem walkToggleItem;
    private string[] animation = new string[0];
    private int animationIndex;
    private int animationInterval;
    private DateTime nextFrame;
    private DateTime nextBlink;
    private DateTime nextWalk;
    private int walkStepsRemaining;
    private int walkDx;
    private int walkBob;
    private int walkFrame;
    private int facingDx;
    private int pendingWalkDx;
    private int turnTicksRemaining;
    private int turnFrame;
    private int bounceIndex;
    private bool dragPending;
    private bool dragging;
    private bool walkingPaused;
    private Point dragStartCursor;
    private Point dragStartForm;

    public PetForm()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string spriteRoot = Path.Combine(root, "assets", "sprites", "girlfriend_cherry_v2");
        LoadFrame("idle", spriteRoot);
        LoadFrame("blink_half", spriteRoot);
        LoadFrame("blink_closed", spriteRoot);
        LoadFrame("happy", spriteRoot);
        LoadSleepFrames(Path.Combine(root, "assets", "sprites", "sleep_seated_sheet.png"));
        LoadActionFrames(Path.Combine(root, "assets", "sprites", "idle_actions_sheet.png"), new[] { "tilt", "cherry", "groom" }, 0.97f);
        for (int i = 1; i <= 4; i++) LoadFrame("walk_clean_" + i, spriteRoot);
        CreateDirectionalFrames();

        Text = "金丝猴桌面宠物";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = key;
        TransparencyKey = key;
        ClientSize = new Size(frames["idle"].Width, frames["idle"].Height + 42);
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);

        canvas.Dock = DockStyle.Fill;
        canvas.BackColor = key;
        Controls.Add(canvas);

        pet.Image = frames["idle"];
        pet.SizeMode = PictureBoxSizeMode.AutoSize;
        pet.BackColor = Color.Transparent;
        pet.Location = new Point(0, 28);
        canvas.Controls.Add(pet);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("喂樱桃", null, delegate { Feed(); });
        menu.Items.Add("爱心留言", null, delegate { LoveNote(); });
        menu.Items.Add("摸摸头", null, delegate { PatHead(); });
        walkToggleItem = new ToolStripMenuItem("停止走动");
        walkToggleItem.Click += delegate { ToggleWalking(); };
        menu.Items.Add(walkToggleItem);
        sleepToggleItem = new ToolStripMenuItem("睡觉");
        sleepToggleItem.Click += delegate { ToggleSleep(); };
        menu.Items.Add(sleepToggleItem);
        menu.Items.Add(new ToolStripSeparator());
        var top = new ToolStripMenuItem("始终置顶") { Checked = true, CheckOnClick = true };
        top.CheckedChanged += delegate { TopMost = top.Checked; };
        menu.Items.Add(top);
        menu.Items.Add("退出", null, delegate { Close(); });
        canvas.ContextMenuStrip = menu;
        pet.ContextMenuStrip = menu;
        codexLabel.SetBounds(20, 0, ClientSize.Width - 40, 26);
        codexLabel.ContextMenuStrip = menu;
        canvas.Controls.Add(codexLabel);
        codexTimer.Interval = 700;
        codexTimer.Tick += CodexTick;
        CodexTick(null, EventArgs.Empty);
        codexTimer.Start();

        canvas.MouseDown += MouseDownHandler;
        canvas.MouseMove += MouseMoveHandler;
        canvas.MouseUp += MouseUpHandler;
        canvas.MouseDoubleClick += DoubleClickHandler;
        pet.MouseDown += MouseDownHandler;
        pet.MouseMove += MouseMoveHandler;
        pet.MouseUp += MouseUpHandler;
        pet.MouseDoubleClick += DoubleClickHandler;

        effectTimer.Interval = 45;
        effectTimer.Tick += EffectTick;
        effectTimer.Start();

        nextBlink = DateTime.Now.AddMilliseconds(random.Next(2200, 4800));
        animationTimer.Interval = 70;
        animationTimer.Tick += AnimationTick;
        animationTimer.Start();

        randomHeartTimer.Interval = 6500;
        randomHeartTimer.Tick += delegate { if (!sleeping && !dragPending && random.NextDouble() < 0.35) AddHearts(1); };
        randomHeartTimer.Start();

        nextWalk = DateTime.Now.AddMilliseconds(random.Next(1400, 2600));
        movementTimer.Interval = 30;
        movementTimer.Tick += MovementTick;
        movementTimer.Start();

        bounceTimer.Interval = 32;
        bounceTimer.Tick += BounceTick;

        Shown += delegate { Activate(); BringToFront(); };
    }

    private void CodexTick(object sender, EventArgs e)
    {
        CodexSnapshot next;
        try { next = CodexStatus.Read(DateTime.UtcNow); }
        catch (Exception) { next = new CodexSnapshot { Phase = "unknown", Text = "Codex：读取暂不可用" }; }
        bool changed = next.Revision != codex.Revision;
        bool connected = codex.Revision != 0;
        codex = next;
        codexLabel.Text = codex.Text;
        UpdateStatusBubble();
        if (sleeping || dragPending || dragging) return;
        if (codex.Active > 0 && (state == PetState.Walking || state == PetState.Turning)) EnterState(PetState.Idle);
        if (changed && connected && codex.Active == 0 && codex.Phase == "ended"
            && DateTime.UtcNow.Ticks - codex.Revision < TimeSpan.FromSeconds(15).Ticks && state == PetState.Idle)
            Happy();
    }

    private void UpdateStatusBubble()
    {
        DateTime now = DateTime.UtcNow;
        string caption = codex.Phase == "waiting" ? "等你确认" : codex.Phase == "busy"
            ? (codex.Text.Contains("修改") ? "改代码中" : codex.Text.Contains("工具") ? "执行中" : "忙碌中")
            : codex.Phase == "ended" ? "本轮结束" : codex.Phase == "interrupted" ? "已暂停"
            : codex.Phase == "unknown" ? "状态待更新" : "待连接";
        if (codex.Active > 1) caption += " · " + codex.Active;
        bool compact = codex.Phase == "disconnected" && (now - statusOpened).TotalSeconds > 6;
        bool show = codex.Active > 0 || codex.Phase == "unknown" || codex.Phase == "disconnected"
            || ((codex.Phase == "ended" || codex.Phase == "interrupted") && now.Ticks - codex.Revision < TimeSpan.FromSeconds(6).Ticks);
        codexLabel.Caption = caption;
        codexLabel.Phase = codex.Phase;
        codexLabel.Pulse++;
        int width = compact ? 20 : Math.Min(ClientSize.Width - 8, TextRenderer.MeasureText(caption, codexLabel.Font).Width + 36);
        codexLabel.SetBounds((ClientSize.Width - width) / 2, 0, width, 26);
        codexLabel.Visible = show && now >= statusHiddenUntil;
        codexTip.SetToolTip(codexLabel, codex.Text);
        codexLabel.Invalidate();
    }

    private void LoadFrame(string name, string root)
    {
        string path = Path.Combine(root, name + ".png");
        if (!File.Exists(path)) throw new FileNotFoundException("缺少动画素材", path);
        frames[name] = Image.FromFile(path);
    }

    private void LoadSleepFrames(string path)
    {
        LoadActionFrames(path, new[] { "sleep", "wake" }, 0.80f);
        Image source = frames["sleep"];
        Bitmap breathe = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(breathe))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(0, -1, source.Width, source.Height + 1));
        }
        frames["sleep_breathe"] = breathe;
    }

    private void LoadActionFrames(string path, string[] names, float heightRatio)
    {
        using (Bitmap sheet = new Bitmap(path))
        {
            int cellWidth = sheet.Width / names.Length;
            Rectangle[] bounds = new Rectangle[names.Length];
            int maxWidth = 1, maxHeight = 1;
            for (int cell = 0; cell < names.Length; cell++)
            {
                int left = (cell + 1) * cellWidth, right = -1, top = sheet.Height, bottom = -1;
                for (int y = 0; y < sheet.Height; y++)
                    for (int x = cell * cellWidth; x < (cell + 1) * cellWidth; x++)
                        if (sheet.GetPixel(x, y).A >= 128)
                        {
                            left = Math.Min(left, x); right = Math.Max(right, x);
                            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                        }
                if (right < left) throw new InvalidDataException("睡眠素材为空");
                bounds[cell] = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                maxWidth = Math.Max(maxWidth, bounds[cell].Width);
                maxHeight = Math.Max(maxHeight, bounds[cell].Height);
            }
            Image idle = frames["idle"];
            float scale = Math.Min((idle.Width - 12f) / maxWidth, idle.Height * heightRatio / maxHeight);
            for (int i = 0; i < names.Length; i++)
            {
                Rectangle source = bounds[i];
                int width = (int)(source.Width * scale);
                int height = (int)(source.Height * scale);
                Bitmap frame = new Bitmap(idle.Width, idle.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(sheet, new Rectangle((idle.Width - width) / 2, idle.Height - 3 - height, width, height), source, GraphicsUnit.Pixel);
                }
                // The source keeps its alpha; the color-key window requires hard display edges.
                for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                    {
                        Color c = frame.GetPixel(x, y);
                        frame.SetPixel(x, y, c.A < 128 ? Color.Transparent : Color.FromArgb(255, c.R, c.G, c.B));
                    }
                frames[names[i]] = frame;
            }
        }
    }

    private void CreateDirectionalFrames()
    {
        Image idle = frames["idle"];
        Image idleLeft = CreateMirrorFrame(idle);
        frames["idle_left"] = idleLeft;
        foreach (string name in new[] { "blink_half", "blink_closed", "happy", "sleep", "sleep_breathe", "wake", "tilt", "cherry", "groom" })
            frames[name + "_left"] = CreateMirrorFrame(frames[name]);
        frames["turn_right_1"] = CreateSquashFrame(idle, 0.86f);
        frames["turn_right_2"] = CreateSquashFrame(idle, 0.68f);
        frames["turn_right_3"] = CreateSquashFrame(idle, 0.86f);
        frames["turn_left_1"] = CreateSquashFrame(idleLeft, 0.86f);
        frames["turn_left_2"] = CreateSquashFrame(idleLeft, 0.68f);
        frames["turn_left_3"] = CreateSquashFrame(idleLeft, 0.86f);
        for (int i = 1; i <= 4; i++)
        {
            string frame = i.ToString();
            frames["walk_left_" + frame] = CreateMirrorFrame(frames["walk_clean_" + frame]);
            frames["walk_right_" + frame] = frames["walk_clean_" + frame];
        }
    }

    private Image CreateSquashFrame(Image source, float scaleX)
    {
        Bitmap bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int scaledWidth = Math.Max(1, (int)(source.Width * scaleX));
            int x = (source.Width - scaledWidth) / 2;
            g.DrawImage(source, new Rectangle(x, 0, scaledWidth, source.Height));
        }
        return bitmap;
    }

    private Image CreateMirrorFrame(Image source)
    {
        Bitmap bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(source.Width, 0, -source.Width, source.Height));
        }
        return bitmap;
    }

    private string DirectionName(int dx)
    {
        return dx < 0 ? "left" : "right";
    }

    private Image IdleFrame()
    {
        return FacingFrame(sleeping ? "sleep" : "idle");
    }

    private Image FacingFrame(string name)
    {
        return frames[facingDx < 0 ? name + "_left" : name];
    }

    // Every action interrupts the previous one through this single transition.
    private void EnterState(PetState next)
    {
        pendingPat = false;
        walkStepsRemaining = 0;
        turnTicksRemaining = 0;
        walkFrame = 0;
        animation = new string[0];
        animationIndex = 0;
        bounceTimer.Stop();
        pet.Top = 28;
        state = next;
        pet.Image = IdleFrame();
        nextWalk = DateTime.Now.AddMilliseconds(random.Next(1600, 3200));
        nextBlink = DateTime.Now.AddMilliseconds(random.Next(2400, 5200));
    }

    private void ToggleSleep()
    {
        if (sleeping) { WakeUp(); return; }
        sleeping = true;
        sleepToggleItem.Text = "叫醒";
        EnterState(PetState.Sleeping);
        sleepStarted = DateTime.Now;
        nextSleepText = sleepStarted.AddSeconds(5);
        foreach (Effect effect in effects) effect.Control.Dispose();
        effects.Clear();
        AddText("Zzz...");
    }

    private void WakeUp()
    {
        if (!sleeping) return;
        sleeping = false;
        sleepToggleItem.Text = "睡觉";
        foreach (Effect effect in effects) effect.Control.Dispose();
        effects.Clear();
        StartAnimation(new[] { "wake", "wake", "idle" }, 220);
        state = PetState.Waking;
        pet.Image = FacingFrame("wake");
    }

    private void DoubleClickHandler(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        pendingPat = false;
        doubleClickHandled = true;
        if (sleeping) { WakeUp(); AddText("我醒啦"); }
        else LoveNote();
    }

    private void ToggleWalking()
    {
        walkingPaused = !walkingPaused;
        walkToggleItem.Text = walkingPaused ? "恢复走动" : "停止走动";
        if (state == PetState.Walking || state == PetState.Turning)
            EnterState(PetState.Idle);
        nextWalk = DateTime.Now.AddMilliseconds(random.Next(1400, 2600));
    }

    private void StartAnimation(string[] names, int interval)
    {
        EnterState(PetState.Expression);
        nextWalk = DateTime.Now.AddMilliseconds(names.Length * interval + 1800);
        animation = names;
        animationIndex = 0;
        animationInterval = interval;
        nextFrame = DateTime.Now;
    }

    private void AnimationTick(object sender, EventArgs e)
    {
        if (pendingPat && DateTime.Now >= pendingPatAt && !dragPending)
        {
            pendingPat = false;
            PatHead();
        }
        if (sleeping)
        {
            if (dragPending) return;
            double phase = (DateTime.Now - sleepStarted).TotalSeconds % 3.2;
            pet.Image = FacingFrame(phase >= 1.0 && phase < 2.2 ? "sleep_breathe" : "sleep");
            if (DateTime.Now >= nextSleepText)
            {
                AddText("Zzz...");
                nextSleepText = DateTime.Now.AddSeconds(random.Next(5, 9));
            }
            return;
        }
        if (dragPending || (state != PetState.Idle && state != PetState.Expression && state != PetState.Waking)) return;
        DateTime now = DateTime.Now;
        if (animationIndex < animation.Length)
        {
            if (now < nextFrame) return;
            pet.Image = FacingFrame(animation[animationIndex++]);
            nextFrame = now.AddMilliseconds(animationInterval);
        }
        else if ((state == PetState.Expression || state == PetState.Waking) && now >= nextFrame)
            EnterState(PetState.Idle);
        else if (state == PetState.Idle && codex.Active > 0)
        {
            pet.Image = FacingFrame(codex.Phase == "waiting" ? "tilt" : "cherry");
        }
        else if (state == PetState.Idle && now >= nextIdleAction)
        {
            StartIdleAction(random.Next(3));
        }
        else if (now >= nextBlink)
        {
            StartAnimation(new[] { "blink_half", "blink_closed", "blink_half", "idle" }, 90);
        }
    }

    private void Happy()
    {
        bool waking = state == PetState.Waking;
        StartAnimation(waking ? new[] { "wake", "wake", "happy", "happy", "idle" }
            : new[] { "happy", "happy", "happy", "idle" }, 320);
        if (waking) pet.Image = FacingFrame("wake");
    }

    private void PatHead()
    {
        if (sleeping || dragging || state == PetState.Waking) return;
        StartAnimation(new[] { "blink_half", "tilt", "tilt", "happy", "idle" }, 200);
        AddHearts(2);
        nextIdleAction = DateTime.Now.AddSeconds(random.Next(8, 15));
    }

    private void StartIdleAction(int action)
    {
        if (sleeping || dragPending || state != PetState.Idle) return;
        string pose = new[] { "tilt", "cherry", "groom" }[action];
        StartAnimation(new[] { "idle", pose, pose, pose, "idle" }, 260);
        nextIdleAction = DateTime.Now.AddSeconds(random.Next(8, 15));
    }

    private void Feed()
    {
        WakeUp();
        AddText("这颗樱桃送给你");
        AddHearts(5);
        Happy();
    }

    private void LoveNote()
    {
        WakeUp();
        string[] notes = {
            "好想你呀",
            "樱桃送给你",
            "今天也要开心",
            "我会一直陪着你",
            "记得喝水呀",
            "偷偷亲你一下",
            "小猴子在陪你",
            "不开心就摸摸我",
            "今天也辛苦啦",
            "把好运分你一半",
            "想和你一起吃樱桃",
            "你一来我就开心"
        };
        AddText(notes[random.Next(notes.Length)]);
        AddHearts(7);
        Happy();
    }

    private void AddText(string text)
    {
        statusHiddenUntil = DateTime.UtcNow.AddSeconds(2.7);
        codexLabel.Visible = false;
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(107, 58, 22),
            BackColor = key,
            Location = new Point(4, 2)
        };
        canvas.Controls.Add(label);
        label.BringToFront();
        effects.Add(new Effect { Control = label, Lifetime = 58, Rise = 1 });
    }

    private void AddHearts(int count)
    {
        Color[] colors = { Color.FromArgb(255, 107, 138), Color.FromArgb(255, 143, 176), Color.FromArgb(233, 75, 106) };
        for (int i = 0; i < count; i++)
        {
            int size = random.Next(16, 25);
            var heart = new Panel { Size = new Size(size, size), BackColor = colors[random.Next(colors.Length)] };
            Point[] points = {
                P(size,.50,.20), P(size,.34,.05), P(size,.12,.08), P(size,0,.28), P(size,.04,.50),
                P(size,.50,.96), P(size,.96,.50), P(size,1,.28), P(size,.88,.08), P(size,.66,.05)
            };
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                heart.Region = new Region(path);
            }
            heart.Location = new Point(random.Next(Width / 3, Math.Max(Width / 3 + 1, Width * 2 / 3)), random.Next(Height / 3, Height / 2));
            canvas.Controls.Add(heart);
            heart.BringToFront();
            effects.Add(new Effect { Control = heart, Lifetime = 42, Rise = 2, Drift = random.Next(-1, 2) });
        }
    }

    private static Point P(int size, double x, double y)
    {
        return new Point((int)(size * x), (int)(size * y));
    }

    private void EffectTick(object sender, EventArgs e)
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            Effect effect = effects[i];
            if (effect.Control.IsDisposed) { effects.RemoveAt(i); continue; }
            effect.Age++;
            effect.Control.Left += effect.Drift;
            effect.Control.Top -= effect.Rise;
            if (effect.Age > effect.Lifetime)
            {
                canvas.Controls.Remove(effect.Control);
                effect.Control.Dispose();
                effects.RemoveAt(i);
            }
        }
    }


    private void MovementTick(object sender, EventArgs e)
    {
        if (codex.Active > 0 || sleeping || dragPending || pendingPat || (state != PetState.Idle && state != PetState.Walking && state != PetState.Turning)) return;

        if (dragPending || dragging)
        {
            walkStepsRemaining = 0;
            turnTicksRemaining = 0;
            walkFrame = 0;
            pet.Image = IdleFrame();
            nextWalk = DateTime.Now.AddMilliseconds(random.Next(1600, 3200));
            return;
        }

        if (walkingPaused)
        {
            walkStepsRemaining = 0;
            turnTicksRemaining = 0;
            walkFrame = 0;
            pet.Top = 28;
            pet.Image = IdleFrame();
            nextWalk = DateTime.Now.AddMilliseconds(random.Next(1400, 2600));
            return;
        }

        DateTime now = DateTime.Now;
        Rectangle area = Screen.FromControl(this).WorkingArea;
        if (walkStepsRemaining <= 0)
        {
            pet.Top = 28;
            if (turnTicksRemaining > 0)
            {
                turnFrame = Math.Min(2, turnFrame);
                pet.Image = frames["turn_" + DirectionName(pendingWalkDx) + "_" + (turnFrame + 1).ToString()];
                turnFrame++;
                turnTicksRemaining--;
                if (turnTicksRemaining <= 0)
                {
                    walkDx = pendingWalkDx;
                    facingDx = walkDx;
                    EnterState(PetState.Walking);
                    walkStepsRemaining = random.Next(3, 7) * 16;
                    walkBob = 0;
                    walkFrame = 0;
                }
                return;
            }

            if (now < nextWalk) return;

            pendingWalkDx = random.Next(0, 2) == 0 ? -1 : 1;
            if (Left <= area.Left + 20) pendingWalkDx = 1;
            if (Right >= area.Right - 20) pendingWalkDx = -1;

            if (facingDx != 0 && pendingWalkDx != facingDx)
            {
                EnterState(PetState.Turning);
                turnTicksRemaining = 3;
                turnFrame = 0;
                return;
            }

            walkDx = pendingWalkDx;
            facingDx = walkDx;
            EnterState(PetState.Walking);
            walkStepsRemaining = random.Next(3, 7) * 16;
            walkBob = 0;
            walkFrame = 0;
        }

        int nextX = Math.Max(area.Left, Math.Min(area.Right - Width, Left + walkDx));
        Left = nextX;
        // Move smoothly, holding each gait pose for 120 ms.
        walkFrame = (walkBob / 4) % 4;
        pet.Image = frames["walk_" + DirectionName(walkDx) + "_" + (walkFrame + 1).ToString()];
        walkBob++;
        pet.Top = 28;
        walkStepsRemaining--;

        if (walkStepsRemaining <= 0)
        {
            EnterState(PetState.Idle);
            pet.Top = 28;
            walkFrame = 0;
            pet.Image = IdleFrame();
            nextWalk = DateTime.Now.AddMilliseconds(random.Next(2600, 6200));
            if (random.NextDouble() < 0.25) AddText("我走到你旁边啦");
        }
    }

    private void StartBounce()
    {
        EnterState(PetState.Bouncing);
        bounceIndex = 0;
        bounceTimer.Stop();
        bounceTimer.Start();
    }

    private void BounceTick(object sender, EventArgs e)
    {
        if (state != PetState.Bouncing) return;
        if (dragPending || dragging)
        {
            bounceTimer.Stop();
            pet.Top = 28;
            return;
        }

        if (bounceIndex >= bounceOffsets.Length)
        {
            EnterState(PetState.Idle);
            nextWalk = DateTime.Now.AddMilliseconds(random.Next(1200, 2600));
            return;
        }

        pet.Top = 28 + bounceOffsets[bounceIndex++];
    }

    private void MouseDownHandler(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragPending = true;
        dragging = false;
        dragStartCursor = Cursor.Position;
        dragStartForm = Location;
        doubleClickHandled = false;
        Point local = sender == pet ? e.Location : new Point(e.X - pet.Left, e.Y - pet.Top);
        headPress = local.X >= pet.Width / 8 && local.X < pet.Width * 7 / 8
            && local.Y >= 0 && local.Y < pet.Height * 2 / 3;
        if (!sleeping) EnterState(PetState.Idle);
    }

    private void MouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!dragPending) return;
        Point current = Cursor.Position;
        int dx = current.X - dragStartCursor.X;
        int dy = current.Y - dragStartCursor.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 5) return;
        if (!dragging) EnterState(PetState.Dragging);
        dragging = true;
        Location = new Point(dragStartForm.X + dx, dragStartForm.Y + dy);
    }

    private void MouseUpHandler(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        bool shouldBounce = dragging;
        dragPending = false;
        dragging = false;
        if (sleeping) EnterState(PetState.Sleeping);
        else if (shouldBounce) StartBounce();
        else if (headPress && !doubleClickHandled && state != PetState.Waking)
        {
            pendingPat = true;
            pendingPatAt = DateTime.Now.AddMilliseconds(SystemInformation.DoubleClickTime);
        }
        headPress = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            effectTimer.Dispose();
            codexTimer.Dispose();
            codexTip.Dispose();
            animationTimer.Dispose();
            randomHeartTimer.Dispose();
            movementTimer.Dispose();
            bounceTimer.Dispose();
            foreach (Image image in new HashSet<Image>(frames.Values)) image.Dispose();
        }
        base.Dispose(disposing);
    }
}








