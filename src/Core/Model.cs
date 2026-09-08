using System.Text.Json;
using System.Text.Json.Serialization;

namespace HousePlanning.Core;

public readonly record struct Point(double X, double Y)
{
    public double DistanceTo(Point b) => Math.Sqrt((X - b.X) * (X - b.X) + (Y - b.Y) * (Y - b.Y));
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator *(Point a, double s) => new(a.X * s, a.Y * s);
}
public enum Kind
{
    Wall, Floor, Room, Void, Window, Door, Stairs, ReturnStairs, Railing, Balcony, FlatRoof, ShedRoof, GableRoof, Desk, Chair, Sofa, Bed, Storage, Kitchen, Bath, Basin, Toilet, Car, Fence, Parking, Site, Light
}
public sealed class Element
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Kind Kind
    {
        get; set;
    }
    public int Floor
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public Point A
    {
        get; set;
    }
    public Point B { get; set; } = new(3000, 0);
    public List<Point> Points { get; set; } = [];
    public string? HostId
    {
        get; set;
    }
    public double Offset
    {
        get; set;
    }
    public double Width { get; set; } = 1000;
    public double Depth { get; set; } = 1000;
    public double Height { get; set; } = 2400;
    public double Thickness { get; set; } = 200;
    public double Sill
    {
        get; set;
    }
    public double Angle
    {
        get; set;
    }
    public string Color { get; set; } = "#ddd7cd";
    public string Material { get; set; } = "plain";
    [JsonIgnore] public bool IsOpening => Kind is Kind.Window or Kind.Door;
    [JsonIgnore] public bool IsPolygon => Kind is Kind.Floor or Kind.Room or Kind.Void or Kind.Site;
    [JsonIgnore] public bool IsRoof => Kind is Kind.FlatRoof or Kind.ShedRoof or Kind.GableRoof;
}
public sealed class FloorLevel
{
    public string Name { get; set; } = "1階";
    public double Elevation
    {
        get; set;
    }
    public double Ceiling { get; set; } = 2400;
    public double Storey { get; set; } = 2800;
    public string CeilingColor { get; set; } = "#eeeae2";
    public string CeilingMaterial { get; set; } = "plain";
    public Blueprint? Blueprint
    {
        get; set;
    }
}
public sealed class Blueprint
{
    [JsonIgnore] public byte[] Image { get; set; } = [];
    public string Asset { get; set; } = "";
    public double MmPerPixel { get; set; } = 10;
    public Point OriginPixel
    {
        get; set;
    }
    public Point Position
    {
        get; set;
    }
    public double Opacity { get; set; } = .45;
    public void Calibrate(Point a, Point b, double length)
    {
        if (a.DistanceTo(b) < 1 || !double.IsFinite(length) || length <= 0)
            throw new ArgumentException("校正する2点と実寸を確認してください。");
        MmPerPixel = length / a.DistanceTo(b);
        OriginPixel = a;
    }
    public Point ImageToWorld(Point p) => (p - OriginPixel) * MmPerPixel + Position;
}
public sealed class SavedView
{
    public string Name { get; set; } = "視点";
    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
    public double Z
    {
        get; set;
    }
    public double Pitch
    {
        get; set;
    }
    public double Yaw
    {
        get; set;
    }
}
public sealed class House
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "新しい住宅";
    public double Grid { get; set; } = 910;
    public List<FloorLevel> Floors { get; set; } = [new(), new() { Name = "2階", Elevation = 2800 }];
    public List<Element> Elements { get; set; } = [];
    public List<SavedView> Views { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static House CreateEmpty() => new();
    public House Clone()
    {
        var h = JsonSerializer.Deserialize<House>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
        for (int i = 0; i < Floors.Count; i++)
            if (Floors[i].Blueprint is { } b)
                h.Floors[i].Blueprint!.Image = b.Image;
        return h;
    }
    public Point OpeningCenter(Element e)
    {
        var w = Elements.Single(x => x.Id == e.HostId);
        return w.A + (w.B - w.A) * ((e.Offset + e.Width / 2) / w.A.DistanceTo(w.B));
    }
    public void Remove(string id) => Elements.RemoveAll(e => e.Id == id || e.HostId == id);
    public void Validate()
    {
        if (Version != 1)
            throw new ArgumentException("この保存形式のバージョンには対応していません。");
        if (Floors.Count != 2 || !double.IsFinite(Grid) || Grid <= 0)
            throw new ArgumentException("階またはグリッドが不正です。");
        foreach (var f in Floors)
        {
            if (!double.IsFinite(f.Elevation) || !double.IsFinite(f.Ceiling) || !double.IsFinite(f.Storey) || f.Ceiling <= 0 || f.Storey < f.Ceiling)
                throw new ArgumentException("階高は天井高以上にしてください。");
            if (f.Blueprint is { } b && (!double.IsFinite(b.MmPerPixel) || b.MmPerPixel <= 0 || !double.IsFinite(b.Opacity) || b.Opacity < 0 || b.Opacity > 1))
                throw new ArgumentException("下敷きの設定が不正です。");
        }
        if (Elements.Select(e => e.Id).Distinct().Count() != Elements.Count)
            throw new ArgumentException("要素IDが重複しています。");
        foreach (var e in Elements)
        {
            if (e.Floor < 0 || e.Floor >= Floors.Count || !Enum.IsDefined(e.Kind))
                throw new ArgumentException("要素の階または種類が不正です。");
            if (new[] { e.Width, e.Depth, e.Height, e.Thickness }.Any(v => !double.IsFinite(v) || v <= 0) || new[] { e.A.X, e.A.Y, e.B.X, e.B.Y, e.Angle, e.Offset, e.Sill }.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("寸法には正の有限数を入力してください。");
            if (e.Kind == Kind.Wall && e.A.DistanceTo(e.B) < 10)
                throw new ArgumentException("壁の長さは10mm以上必要です。");
            if (e.IsPolygon)
                Geometry.ValidatePolygon(e.Points);
            if (e.IsOpening)
            {
                var w = Elements.Find(x => x.Id == e.HostId && x.Kind == Kind.Wall);
                if (w == null || w.Floor != e.Floor)
                    throw new ArgumentException("窓・扉の接続先の壁がありません。");
                if (e.Offset < 0 || e.Offset + e.Width > w.A.DistanceTo(w.B) + .01 || e.Sill < 0 || e.Sill + e.Height > w.Height + .01)
                    throw new ArgumentException("窓・扉が壁の外にはみ出しています。");
                if (Elements.Any(o => o.Id != e.Id && o.IsOpening && o.HostId == e.HostId && o.Offset < e.Offset + e.Width - .01 && o.Offset + o.Width > e.Offset + .01 && o.Sill < e.Sill + e.Height - .01 && o.Sill + o.Height > e.Sill + .01))
                    throw new ArgumentException("窓・扉が重なっています。");
            }
        }
    }
}
public sealed class EditorSession(House initial)
{
    public House House { get; private set; } = initial;
    readonly List<House> undo = []; readonly List<House> redo = [];
    public bool Dirty
    {
        get; private set;
    }
    public void Change(Action<House> action)
    {
        var next = House.Clone();
        action(next);
        next.Validate();
        undo.Add(House);
        if (undo.Count > 100)
            undo.RemoveAt(0);
        redo.Clear();
        House = next;
        Dirty = true;
    }
    public void Undo()
    {
        if (undo.Count == 0)
            return;
        redo.Add(House);
        House = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        Dirty = true;
    }
    public void Redo()
    {
        if (redo.Count == 0)
            return;
        undo.Add(House);
        House = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        Dirty = true;
    }
    public void MarkSaved() => Dirty = false;
}
