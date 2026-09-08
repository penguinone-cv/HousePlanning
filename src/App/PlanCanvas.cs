using Godot;
using HousePlanning.Core;
using P = HousePlanning.Core.Point;
namespace HousePlanning;

public partial class PlanCanvas : Control
{
    public Main App = null!;
    public string Tool = "select";
    public readonly List<P> Draft = [];
    public float Zoom = .052f;
    public Vector2 Pan = new(110, 130);
    bool panning, dragging;
    P dragStart, dragDelta;
    Vector2 mouse;
    byte[]? cachedImage;
    ImageTexture? background;
    public P World(Vector2 p) => new((p.X - Pan.X) / Zoom, (p.Y - Pan.Y) / Zoom);
    public Vector2 Screen(P p) => new((float)p.X * Zoom + Pan.X, (float)p.Y * Zoom + Pan.Y);
    public P Snap(P p)
    {
        if (!App.SnapEnabled)
            return p;
        var endpoints = App.Session.House.Elements.Where(e => e.Floor == App.FloorIndex && e.Kind == Kind.Wall).SelectMany(e => new[] { e.A, e.B }).OrderBy(v => v.DistanceTo(p)).ToArray();
        if (endpoints.Length > 0 && endpoints[0].DistanceTo(p) * Zoom < 12)
            return endpoints[0];
        var g = App.Session.House.Grid;
        return new(Math.Round(p.X / g) * g, Math.Round(p.Y / g) * g);
    }
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        FocusMode = FocusModeEnum.All;
    }
    public void Fit()
    {
        var h = App.Session.House;
        var b = h.Floors[App.FloorIndex].Blueprint;
        if (b is not null && b.Image.Length > 0)
        {
            var img = new Image();
            if (img.LoadPngFromBuffer(b.Image) == Error.Ok)
            {
                Zoom = (float)Math.Min((Size.X - 70) / (img.GetWidth() * b.MmPerPixel), (Size.Y - 70) / (img.GetHeight() * b.MmPerPixel));
                Pan = new Vector2(35, 35) - new Vector2((float)b.ImageToWorld(new(0, 0)).X, (float)b.ImageToWorld(new(0, 0)).Y) * Zoom;
                QueueRedraw();
                return;
            }
        }
        var pts = h.Elements.Where(e => e.Floor == App.FloorIndex).SelectMany(e => e.IsPolygon ? e.Points : new List<P> { e.A, e.Kind == Kind.Wall ? e.B : e.A + new P(e.Width, e.Depth) }).ToArray();
        if (pts.Length == 0)
        {
            Zoom = .052f;
            Pan = new(100, 130);
        }
        else
        {
            double minx = pts.Min(p => p.X) - 1200, miny = pts.Min(p => p.Y) - 1200, w = pts.Max(p => p.X) - minx + 1200, d = pts.Max(p => p.Y) - miny + 1200;
            Zoom = (float)Math.Min(Size.X / w, Size.Y / d);
            Pan = new(-(float)minx * Zoom, -(float)miny * Zoom);
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (App == null)
            return;
        DrawRect(new(Vector2.Zero, Size), new Color("edf1f2"));
        var house = App.Session.House;
        var bp = house.Floors[App.FloorIndex].Blueprint;
        if (bp != null && bp.Image.Length > 0)
        {
            if (cachedImage != bp.Image)
            {
                var img = new Image();
                if (img.LoadPngFromBuffer(bp.Image) == Error.Ok)
                {
                    background = ImageTexture.CreateFromImage(img);
                    cachedImage = bp.Image;
                }
            }
            if (background != null)
                DrawTextureRect(background, new Rect2(Screen(bp.ImageToWorld(new(0, 0))), background.GetSize() * (float)bp.MmPerPixel * Zoom), false, new Color(1, 1, 1, (float)bp.Opacity));
        }
        float step = (float)house.Grid * Zoom;
        if (step > 9)
        {
            for (float x = Pan.X % step; x < Size.X; x += step)
                DrawLine(new(x, 0), new(x, Size.Y), new Color(.45f, .55f, .6f, .15f));
            for (float y = Pan.Y % step; y < Size.Y; y += step)
                DrawLine(new(0, y), new(Size.X, y), new Color(.45f, .55f, .6f, .15f));
        }
        DrawLine(Screen(new(0, 0)) - new Vector2(14, 0), Screen(new(0, 0)) + new Vector2(14, 0), Colors.Coral, 2);
        DrawLine(Screen(new(0, 0)) - new Vector2(0, 14), Screen(new(0, 0)) + new Vector2(0, 14), Colors.Coral, 2);
        foreach (var e in house.Elements.Where(e => e.Floor == App.FloorIndex).OrderBy(e => e.Kind == Kind.Site ? 0 : e.Kind == Kind.Floor ? 1 : e.IsPolygon ? 2 : e.IsRoof ? 3 : e.Kind == Kind.Wall ? 5 : e.IsOpening ? 6 : 4))
            DrawElement(e);
        var accent = new Color("148b91");
        for (int i = 0; i < Draft.Count; i++)
        {
            DrawCircle(Screen(Draft[i]), 5, accent);
            if (i > 0)
                DrawLine(Screen(Draft[i - 1]), Screen(Draft[i]), accent, 2);
        }
        if (Draft.Count > 0)
            DrawLine(Screen(Draft[^1]), Screen(Snap(World(mouse))), accent, 2);
        if (dragging)
            DrawLine(Screen(dragStart), Screen(dragStart + dragDelta), accent, 3);
        var font = GetThemeDefaultFont();
        DrawString(font, new(20, Size.Y - 24), $"{house.Floors[App.FloorIndex].Name}    グリッド {house.Grid:0} mm    中ドラッグ: 移動 / ホイール: 拡大", HorizontalAlignment.Left, -1, 13, new Color("516672"));
    }
    void DrawElement(Element e)
    {
        bool selected = e.Id == App.Selected;
        Color ink = selected ? new("148b91") : new("425965");
        if (e.IsPolygon)
        {
            var pts = e.Points.Select(Screen).ToArray();
            if (pts.Length < 3)
                return;
            Color fill = Color.FromString(e.Color, new Color("cccccc"));
            fill.A = e.Kind == Kind.Void ? .85f : .2f;
            DrawColoredPolygon(pts, fill);
            DrawPolyline(pts.Append(pts[0]).ToArray(), ink, selected ? 3 : 1, true);
            if (e.Kind == Kind.Void)
            {
                DrawLine(pts[0], pts[2], ink, 1);
                DrawString(GetThemeDefaultFont(), pts[0] + new Vector2(10, 22), "吹き抜け", HorizontalAlignment.Left, -1, 13, ink);
            }
            return;
        }
        if (e.Kind == Kind.Wall)
        {
            DrawLine(Screen(e.A), Screen(e.B), ink, Math.Max(3, (float)e.Thickness * Zoom), true);
            if (selected)
            {
                DrawCircle(Screen(e.A), 5, new("e59557"));
                DrawCircle(Screen(e.B), 5, new("e59557"));
            }
            return;
        }
        if (e.IsOpening)
        {
            var w = App.Session.House.Elements.Find(w => w.Id == e.HostId);
            if (w == null)
                return;
            var a = w.A + (w.B - w.A) * (e.Offset / w.A.DistanceTo(w.B));
            var b = w.A + (w.B - w.A) * ((e.Offset + e.Width) / w.A.DistanceTo(w.B));
            DrawLine(Screen(a), Screen(b), e.Kind == Kind.Window ? new("58b6c2") : new("e3a968"), Math.Max(5, (float)w.Thickness * Zoom + 2));
            if (selected)
                DrawCircle(Screen(App.Session.House.OpeningCenter(e)), 7, ink);
            return;
        }
        float rad = (float)e.Angle * Mathf.Pi / 180;
        var center = Screen(e.A);
        var rect = new Vector2((float)e.Width, (float)e.Depth) * Zoom;
        Vector2[] corners = [new(-rect.X / 2, -rect.Y / 2), new(rect.X / 2, -rect.Y / 2), new(rect.X / 2, rect.Y / 2), new(-rect.X / 2, rect.Y / 2)];
        corners = corners.Select(p => center + p.Rotated(rad)).ToArray();
        var color = Color.FromString(e.Color, new Color("bbbbbb"));
        color.A = e.IsRoof ? .08f : .65f;
        DrawColoredPolygon(corners, color);
        DrawPolyline(corners.Append(corners[0]).ToArray(), ink, selected ? 3 : 1, true);
        if (rect.X > 28)
            DrawString(GetThemeDefaultFont(), center + new Vector2(-rect.X / 2, 4), Main.LabelFor(e.Kind), HorizontalAlignment.Left, -1, 11, ink);
        if (e.Kind is Kind.Stairs or Kind.ReturnStairs)
            for (int i = 1; i < 12; i++)
            {
                float y = -rect.Y / 2 + rect.Y * i / 12;
                DrawLine(center + new Vector2(-rect.X / 2, y).Rotated(rad), center + new Vector2(rect.X / 2, y).Rotated(rad), ink, 1);
            }
    }
    public string? Hit(P p)
    {
        var h = App.Session.House;
        var es = h.Elements.Where(e => e.Floor == App.FloorIndex).Reverse().ToArray();
        foreach (var e in es.Where(e => e.IsOpening))
            if (h.OpeningCenter(e).DistanceTo(p) < Math.Max(e.Width / 2, 120))
                return e.Id;
        foreach (var e in es.Where(e => e.Kind == Kind.Wall))
            if (Geometry.SegmentDistance(p, e.A, e.B) < Math.Max(e.Thickness / 2, 10 / Zoom))
                return e.Id;
        foreach (var e in es.Where(e => !e.IsPolygon && !e.IsOpening && e.Kind != Kind.Wall))
        {
            var v = new Vector2((float)(p.X - e.A.X), (float)(p.Y - e.A.Y)).Rotated(-(float)e.Angle * Mathf.Pi / 180);
            if (Math.Abs(v.X) < e.Width / 2 && Math.Abs(v.Y) < e.Depth / 2 && (!e.IsRoof || Math.Min(e.Width / 2 - Math.Abs(v.X), e.Depth / 2 - Math.Abs(v.Y)) * Zoom < 8))
                return e.Id;
        }
        return es.Where(e => e.IsPolygon).OrderBy(e => e.Kind == Kind.Void ? 0 : e.Kind == Kind.Room ? 1 : e.Kind == Kind.Floor ? 2 : 3).FirstOrDefault(e => Geometry.Contains(e.Points, p))?.Id;
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseMotion m)
        {
            mouse = m.Position;
            if (panning)
                Pan += m.Relative;
            if (dragging)
                dragDelta = Snap(World(m.Position)) - dragStart;
            QueueRedraw();
        }
        if (input is not InputEventMouseButton b)
            return;
        mouse = b.Position;
        if (b.ButtonIndex == MouseButton.Middle)
        {
            panning = b.Pressed;
            return;
        }
        if (b.Pressed && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            var before = World(b.Position);
            Zoom = Math.Clamp(Zoom * (b.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), .006f, 1.5f);
            Pan = b.Position - new Vector2((float)before.X, (float)before.Y) * Zoom;
            QueueRedraw();
            return;
        }
        if (b.ButtonIndex == MouseButton.Right && b.Pressed)
        {
            Draft.Clear();
            QueueRedraw();
            return;
        }
        if (b.ButtonIndex != MouseButton.Left)
            return;
        GrabFocus();
        if (!b.Pressed)
        {
            if (dragging && dragDelta.DistanceTo(new()) > 1)
                App.MoveSelected(dragDelta);
            dragging = false;
            QueueRedraw();
            return;
        }
        var raw = World(b.Position);
        var p = Snap(raw);
        if (Tool == "select")
        {
            App.Select(Hit(raw));
            if (App.Selected != null)
            {
                dragging = true;
                dragStart = p;
                dragDelta = new();
            }
            return;
        }
        if (Tool == "origin")
        {
            App.Change(h => { var bp = h.Floors[App.FloorIndex].Blueprint ?? throw new ArgumentException("先に図面を読み込んでください。"); bp.Position = bp.Position - raw; });
            Tool = "select";
            return;
        }
        if (Tool == "calibrate" || Tool == "crop")
        {
            Draft.Add(raw);
            if (Draft.Count == 2)
            {
                var a = Draft[0];
                var c = Draft[1];
                Draft.Clear();
                if (Tool == "calibrate")
                    App.Calibrate(a, c);
                else
                    App.Crop(a, c);
                Tool = "select";
            }
            QueueRedraw();
            return;
        }
        if (!Enum.TryParse<Kind>(Tool, out var kind))
            return;
        if (kind == Kind.Wall)
        {
            Draft.Add(p);
            if (Draft.Count == 2)
            {
                App.AddWall(Draft[0], Draft[1]);
                Draft.Clear();
            }
        }
        else if (kind is Kind.Floor or Kind.Room or Kind.Void or Kind.Site)
            Draft.Add(p);
        else if (kind is Kind.Window or Kind.Door)
            App.AddOpening(kind, raw);
        else
            App.AddPart(kind, p);
        QueueRedraw();
    }
    public void Complete()
    {
        if (Draft.Count >= 3 && Enum.TryParse<Kind>(Tool, out var kind))
        {
            var points = Draft.ToList();
            if (App.Change(h => h.Elements.Add(new() { Kind = kind, Floor = App.FloorIndex, Points = points, Height = 180, Color = kind == Kind.Site ? "#718c6a" : "#bc9d7c", Material = "wood" })))
                Draft.Clear();
        }
        QueueRedraw();
    }
}
