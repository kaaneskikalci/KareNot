namespace KareNot;

// Shows a frozen video frame over the player while an area is selected; the selection is drawn as a red frame.
sealed class FrameOverlay : Control
{
    public Bitmap Frame;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Rectangle Selection { get; set; } // image coordinates

    public FrameOverlay()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
    }

    // Where the frame is drawn (aspect-fit, like the player).
    public RectangleF ImageRect
    {
        get
        {
            if (Frame == null || Width == 0 || Height == 0) return RectangleF.Empty;
            float scale = Math.Min((float)Width / Frame.Width, (float)Height / Frame.Height);
            float w = Frame.Width * scale, h = Frame.Height * scale;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }
    }

    public Point ClientToImage(Point p)
    {
        var r = ImageRect;
        if (r.Width <= 0) return Point.Empty;
        int x = (int)((p.X - r.X) / r.Width * Frame.Width);
        int y = (int)((p.Y - r.Y) / r.Height * Frame.Height);
        return new Point(Math.Clamp(x, 0, Frame.Width - 1), Math.Clamp(y, 0, Frame.Height - 1));
    }

    RectangleF ImageToClient(Rectangle s)
    {
        var r = ImageRect;
        float k = r.Width / Frame.Width;
        return new RectangleF(r.X + s.X * k, r.Y + s.Y * k, s.Width * k, s.Height * k);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
        if (Frame == null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        e.Graphics.DrawImage(Frame, ImageRect);
        if (Selection.Width > 0 && Selection.Height > 0)
        {
            using var pen = new Pen(Color.Red, 3);
            var s = ImageToClient(Selection);
            e.Graphics.DrawRectangle(pen, s.X, s.Y, s.Width, s.Height);
        }
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        const string hint = "Alan seçili — notu yaz, türünü seç (Esc: iptal, Boşluk: devam)";
        var size = e.Graphics.MeasureString(hint, font);
        using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
        e.Graphics.FillRectangle(back, 8, 8, size.Width + 12, size.Height + 6);
        e.Graphics.DrawString(hint, font, Brushes.White, 14, 11);
    }

    // The frame with the selection drawn on it, as saved for the note.
    public Bitmap RenderMarked()
    {
        var bmp = new Bitmap(Frame);
        if (Selection.Width > 0 && Selection.Height > 0)
        {
            using var g = Graphics.FromImage(bmp);
            float width = Math.Max(4, Frame.Width / 300f);
            using var pen = new Pen(Color.Red, width);
            g.DrawRectangle(pen, Selection);
        }
        return bmp;
    }
}
