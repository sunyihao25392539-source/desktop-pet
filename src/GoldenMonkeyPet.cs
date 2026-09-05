using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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

internal sealed class PetForm : Form
{
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
    private bool dragPending;
    private bool dragging;
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
        LoadFrame("walk_left_1", spriteRoot);
        LoadFrame("walk_left_2", spriteRoot);
        LoadFrame("walk_right_1", spriteRoot);
        LoadFrame("walk_right_2", spriteRoot);

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
        menu.Items.Add(new ToolStripSeparator());
        var top = new ToolStripMenuItem("始终置顶") { Checked = true, CheckOnClick = true };
        top.CheckedChanged += delegate { TopMost = top.Checked; };
        menu.Items.Add(top);
        menu.Items.Add("退出", null, delegate { Close(); });
        canvas.ContextMenuStrip = menu;
        pet.ContextMenuStrip = menu;

        canvas.MouseDown += MouseDownHandler;
        canvas.MouseMove += MouseMoveHandler;
        canvas.MouseUp += MouseUpHandler;
        canvas.DoubleClick += delegate { LoveNote(); };
        pet.MouseDown += MouseDownHandler;
        pet.MouseMove += MouseMoveHandler;
        pet.MouseUp += MouseUpHandler;
        pet.DoubleClick += delegate { LoveNote(); };

        effectTimer.Interval = 45;
        effectTimer.Tick += EffectTick;
        effectTimer.Start();

        nextBlink = DateTime.Now.AddMilliseconds(random.Next(2200, 4800));
        animationTimer.Interval = 70;
        animationTimer.Tick += AnimationTick;
        animationTimer.Start();

        randomHeartTimer.Interval = 6500;
        randomHeartTimer.Tick += delegate { if (!dragging && random.NextDouble() < 0.35) AddHearts(1); };
        randomHeartTimer.Start();

        nextWalk = DateTime.Now.AddMilliseconds(random.Next(1400, 2600));
        movementTimer.Interval = 80;
        movementTimer.Tick += MovementTick;
        movementTimer.Start();

        Shown += delegate { Activate(); BringToFront(); };
    }

    private void LoadFrame(string name, string root)
    {
        string path = Path.Combine(root, name + ".png");
        if (!File.Exists(path)) throw new FileNotFoundException("缺少动画素材", path);
        frames[name] = Image.FromFile(path);
    }

    private void StartAnimation(string[] names, int interval)
    {
        animation = names;
        animationIndex = 0;
        animationInterval = interval;
        nextFrame = DateTime.Now;
    }

    private void AnimationTick(object sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        if (animationIndex < animation.Length)
        {
            if (now < nextFrame) return;
            pet.Image = frames[animation[animationIndex++]];
            nextFrame = now.AddMilliseconds(animationInterval);
            if (animationIndex == animation.Length)
                nextBlink = now.AddMilliseconds(random.Next(2400, 5200));
        }
        else if (now >= nextBlink)
        {
            StartAnimation(new[] { "blink_half", "blink_closed", "blink_half", "idle" }, 90);
        }
    }

    private void Happy()
    {
        StartAnimation(new[] { "happy", "happy", "happy", "idle" }, 320);
    }

    private void Feed()
    {
        AddText("这颗樱桃送给你");
        AddHearts(5);
        Happy();
    }

    private void LoveNote()
    {
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
        if (dragPending || dragging)
        {
            walkStepsRemaining = 0;
            walkFrame = 0;
            pet.Image = frames["idle"];
            nextWalk = DateTime.Now.AddMilliseconds(random.Next(1600, 3200));
            return;
        }

        DateTime now = DateTime.Now;
        Rectangle area = Screen.FromControl(this).WorkingArea;
        if (walkStepsRemaining <= 0)
        {
            pet.Top = 28;
            pet.Image = frames["idle"];
            if (now < nextWalk) return;

            walkDx = random.Next(0, 2) == 0 ? -3 : 3;
            if (Left <= area.Left + 20) walkDx = 3;
            if (Right >= area.Right - 20) walkDx = -3;
            walkStepsRemaining = random.Next(16, 34);
            walkBob = 0;
            walkFrame = 0;
        }

        int nextX = Math.Max(area.Left, Math.Min(area.Right - Width, Left + walkDx));
        Left = nextX;
        walkBob = 1 - walkBob;
        walkFrame = 1 - walkFrame;
        pet.Image = frames[(walkDx < 0 ? "walk_left_" : "walk_right_") + (walkFrame + 1).ToString()];
        pet.Top = 28 + (walkBob == 0 ? 0 : 2);
        walkStepsRemaining--;

        if (walkStepsRemaining <= 0)
        {
            pet.Top = 28;
            walkFrame = 0;
            pet.Image = frames["idle"];
            nextWalk = DateTime.Now.AddMilliseconds(random.Next(2600, 6200));
            if (random.NextDouble() < 0.25) AddText("我走到你旁边啦");
        }
    }

    private void MouseDownHandler(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragPending = true;
        dragging = false;
        dragStartCursor = Cursor.Position;
        dragStartForm = Location;
    }

    private void MouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!dragPending) return;
        Point current = Cursor.Position;
        int dx = current.X - dragStartCursor.X;
        int dy = current.Y - dragStartCursor.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 5) return;
        dragging = true;
        Location = new Point(dragStartForm.X + dx, dragStartForm.Y + dy);
    }

    private void MouseUpHandler(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragPending = false;
        dragging = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            effectTimer.Dispose();
            animationTimer.Dispose();
            randomHeartTimer.Dispose();
            movementTimer.Dispose();
            foreach (Image image in frames.Values) image.Dispose();
        }
        base.Dispose(disposing);
    }
}








