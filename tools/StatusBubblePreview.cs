using System;
using System.Drawing;
using System.Windows.Forms;

internal static class StatusBubblePreview
{
    [STAThread]
    private static void Main()
    {
        string[] phases = { "busy", "waiting", "ended", "disconnected" };
        string[] captions = { "忙碌中", "等你确认", "本轮结束", "" };
        using (Bitmap result = new Bitmap(760, 270))
        using (Graphics g = Graphics.FromImage(result))
        using (Image pet = Image.FromFile("assets/sprites/girlfriend_cherry_v2/idle.png"))
        {
            g.Clear(Color.FromArgb(237, 232, 222));
            for (int i = 0; i < phases.Length; i++)
            {
                using (StatusBubble bubble = new StatusBubble())
                {
                    bubble.Phase = phases[i]; bubble.Caption = captions[i];
                    bubble.Size = new Size(i == 3 ? 20 : 110, 26);
                    using (Bitmap bitmap = new Bitmap(bubble.Width, bubble.Height))
                    {
                        bubble.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bubble.Size));
                        bitmap.MakeTransparent(Color.Magenta);
                        g.DrawImageUnscaled(bitmap, i * 190 + (190 - bubble.Width) / 2, 22);
                    }
                }
                g.DrawImageUnscaled(pet, i * 190 + (190 - pet.Width) / 2, 52);
            }
            result.Save("assets/sprites/status_bubble_preview.png");
        }
    }
}
