using Godot;
using HousePlanning.Core;
using P = HousePlanning.Core.Point;
namespace HousePlanning;

public partial class Main : Control
{
    public EditorSession Session = new(SampleHouse.Create());
    public int FloorIndex;
    public string? Selected;
    public bool SnapEnabled = true;
    public PlanCanvas Canvas = null!;
    public WorldView World = null!;
    VBoxContainer inspector = null!;
    Label status = null!, title = null!;
    Control workspace = null!;
    SubViewportContainer viewContainer = null!;
    SubViewport viewport = null!;
    string? currentPath;
    bool is3D, refreshing;
    public bool IsTyping => GetViewport().GuiGetFocusOwner() is LineEdit or SpinBox;
    readonly Dictionary<string, Button> toolButtons = [];
    static readonly Dictionary<Kind, string> labels = new() { [Kind.Wall] = "壁", [Kind.Floor] = "床", [Kind.Room] = "部屋", [Kind.Void] = "吹き抜け", [Kind.Window] = "窓", [Kind.Door] = "扉", [Kind.Stairs] = "直階段", [Kind.ReturnStairs] = "折返し階段", [Kind.Railing] = "手すり", [Kind.Balcony] = "バルコニー", [Kind.FlatRoof] = "陸屋根", [Kind.ShedRoof] = "片流れ屋根", [Kind.GableRoof] = "切妻屋根", [Kind.Desk] = "机", [Kind.Chair] = "椅子", [Kind.Sofa] = "ソファ", [Kind.Bed] = "ベッド", [Kind.Storage] = "収納", [Kind.Kitchen] = "キッチン", [Kind.Bath] = "浴槽", [Kind.Basin] = "洗面台", [Kind.Toilet] = "トイレ", [Kind.Car] = "車", [Kind.Fence] = "塀", [Kind.Parking] = "駐車場", [Kind.Site] = "敷地", [Kind.Light] = "照明" };
    public static string LabelFor(Kind k) => labels.GetValueOrDefault(k, k.ToString());
    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;
        Theme = BuildTheme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new("f5f7f8"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var layout = new VBoxContainer();
        AddChild(layout);
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.OffsetLeft = 16;
        layout.OffsetTop = 12;
        layout.OffsetRight = -16;
        layout.OffsetBottom = -10;
        var top = new HBoxContainer();
        layout.AddChild(top);
        title = new Label { Text = "HOUSEPLANNING", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 24);
        top.AddChild(title);
        Button(top, "新規", () => ConfirmDiscard(() => Reset(House.CreateEmpty())));
        Button(top, "サンプル", () => ConfirmDiscard(() => Reset(SampleHouse.Create())));
        Button(top, "開く", () => ConfirmDiscard(Open));
        Button(top, "保存", () => Save(false));
        Button(top, "別名で保存", () => Save(true));
        var subtitle = new Label { Text = "間取りを描く。空間を歩く。", Modulate = new("6d838e") };
        layout.AddChild(subtitle);
        var bar = new HBoxContainer();
        layout.AddChild(bar);
        Button(bar, "2D 間取り", () => Set3D(false));
        Button(bar, "3D 空間", () => Set3D(true));
        var floors = new OptionButton();
        floors.AddItem("1階");
        floors.AddItem("2階");
        floors.ItemSelected += i => { FloorIndex = (int)i; Selected = null; Canvas.Draft.Clear(); Refresh(); };
        bar.AddChild(floors);
        Button(bar, "元に戻す", () => { Session.Undo(); Refresh(); });
        Button(bar, "やり直し", () => { Session.Redo(); Refresh(); });
        Button(bar, "全体表示", () => { Canvas.Fit(); World.Orbit(); });
        var snap = new CheckButton { Text = "吸着", ButtonPressed = true };
        snap.Toggled += v => SnapEnabled = v;
        bar.AddChild(snap);
        Button(bar, "図面取込", Import);
        Button(bar, "PNG撮影", Capture);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.AddChild(body);
        var leftScroll = new ScrollContainer { CustomMinimumSize = new(190, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(leftScroll);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftScroll.AddChild(left);
        Section(left, "編集ツール");
        Tool(left, "select", "選択 / 移動");
        foreach (var k in new[] { Kind.Wall, Kind.Floor, Kind.Room, Kind.Window, Kind.Door, Kind.Void })
            Tool(left, k.ToString(), LabelFor(k));
        Section(left, "上下階と屋根");
        foreach (var k in new[] { Kind.Stairs, Kind.ReturnStairs, Kind.Railing, Kind.Balcony, Kind.FlatRoof, Kind.ShedRoof, Kind.GableRoof })
            Tool(left, k.ToString(), LabelFor(k));
        Section(left, "家具・設備・外構");
        var parts = new OptionButton();
        foreach (var k in labels.Keys.Where(k => (int)k >= (int)Kind.Desk || k == Kind.Site))
        {
            parts.AddItem(LabelFor(k), (int)k);
        }
        left.AddChild(parts);
        Button(left, "選んだパーツを配置", () => SetTool(((Kind)parts.GetSelectedId()).ToString()));
        Section(left, "図面調整");
        Tool(left, "calibrate", "2点で縮尺を合わせる");
        Tool(left, "origin", "クリック位置を原点に");
        Tool(left, "crop", "2点で切り抜く");
        Button(left, "図面を90度回転", RotateBlueprint);
        Section(left, "3D操作");
        Button(left, "外から見る", () => { Set3D(true); World.Orbit(); });
        Button(left, "自由に飛行", () => { Set3D(true); World.Fly(); });
        Button(left, "この階を歩く", () => { Set3D(true); World.Walk(FloorIndex); });
        var roofs = new CheckButton { Text = "屋根を表示", ButtonPressed = true };
        roofs.Toggled += v => World.ShowRoof(v);
        left.AddChild(roofs);
        var only = new CheckButton { Text = "選択階だけ表示" };
        only.Toggled += v => World.OnlyFloor(v);
        left.AddChild(only);
        var quality = new OptionButton();
        quality.AddItem("標準品質");
        quality.AddItem("低品質");
        quality.ItemSelected += i => World.SetQuality(i == 1);
        left.AddChild(quality);
        Button(left, "視点を保存", () => Change(h => h.Views.Add(World.GetView())));
        Button(left, "最後の視点に戻る", () => { if (Session.House.Views.Count > 0) { Set3D(true); World.Restore(Session.House.Views[^1]); } });
        workspace = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(workspace);
        Canvas = new PlanCanvas { App = this };
        workspace.AddChild(Canvas);
        Canvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        viewContainer = new SubViewportContainer { Stretch = true, Visible = false };
        workspace.AddChild(viewContainer);
        viewContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        viewport = new SubViewport { Size = new(900, 700), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, OwnWorld3D = true };
        viewContainer.AddChild(viewport);
        World = new WorldView { App = this };
        viewport.AddChild(World);
        var rightScroll = new ScrollContainer { CustomMinimumSize = new(272, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(rightScroll);
        inspector = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rightScroll.AddChild(inspector);
        status = new Label { Text = "準備完了", CustomMinimumSize = new(0, 30), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        layout.AddChild(status);
        Refresh();
        Callable.From(() => Canvas.Fit()).CallDeferred();
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--self-test"))
            Callable.From(RunSelfTest).CallDeferred();
        if (args.Contains("--capture"))
            Callable.From(CaptureChecks).CallDeferred();
        if (args.Contains("--benchmark"))
        {
            Reset(SampleHouse.Create(true));
            Set3D(true);
            viewContainer.Stretch = false;
            viewport.Size = new(1920, 1080);
            World.BeginBenchmark();
        }
        int pdfArg = Array.IndexOf(args, "--pdf-check");
        if (pdfArg >= 0 && pdfArg + 1 < args.Length)
            Callable.From(() => RunPdfCheck(args[pdfArg + 1])).CallDeferred();
    }
    Theme BuildTheme()
    {
        var t = new Theme { DefaultFont = new SystemFont { FontNames = ["Yu Gothic UI", "Meiryo", "sans-serif"] }, DefaultFontSize = 14 };
        t.SetColor("font_color", "Label", new("243e4b"));
        t.SetColor("font_color", "Button", new("294853"));
        foreach (string type in new[] { "Button", "OptionButton" })
        {
            var normal = new StyleBoxFlat { BgColor = new("e5ecee"), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 };
            t.SetStylebox("normal", type, normal);
            var hover = (StyleBoxFlat)normal.Duplicate();
            hover.BgColor = new("d1e5e6");
            t.SetStylebox("hover", type, hover);
            var pressed = (StyleBoxFlat)normal.Duplicate();
            pressed.BgColor = new("abd6d4");
            t.SetStylebox("pressed", type, pressed);
        }
        t.SetConstant("separation", "VBoxContainer", 7);
        t.SetConstant("separation", "HBoxContainer", 9);
        return t;
    }
    public static Button Button(Container parent, string text, Action callback)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.Pressed += callback;
        parent.AddChild(b);
        return b;
    }
    void Section(Container p, string text)
    {
        var l = new Label { Text = text, Modulate = new("6d838e") };
        l.AddThemeFontSizeOverride("font_size", 12);
        p.AddChild(l);
    }
    void Tool(Container p, string key, string text)
    {
        toolButtons[key] = Button(p, text, () => SetTool(key));
    }
    void SetTool(string key)
    {
        Set3D(false);
        Canvas.Tool = key;
        Canvas.Draft.Clear();
        foreach (var (k, b) in toolButtons)
            b.Modulate = k == key ? new("77c6c4") : Colors.White;
        Tell(key == "select" ? "要素を選択し、ドラッグまたは右の寸法で編集します。" : "図面をクリックして配置。多角形はEnterで完了、右クリックで取消。");
        Canvas.GrabFocus();
    }
    public void Tell(string text)
    {
        status.Text = text;
    }
    public bool Change(Action<House> edit)
    {
        try
        {
            Session.Change(edit);
            Refresh();
            Tell("変更しました。");
            return true;
        }
        catch (Exception e) { Tell(e.Message); return false; }
    }
    public void Refresh()
    {
        title.Text = "HOUSEPLANNING   /   " + Session.House.Name + (Session.Dirty ? " *" : "");
        if (Selected != null && !Session.House.Elements.Any(e => e.Id == Selected))
            Selected = null;
        Canvas.QueueRedraw();
        World.ActiveFloor = FloorIndex;
        World.Sync(Session.House);
        BuildInspector();
    }
    public void Select(string? id)
    {
        Selected = id;
        BuildInspector();
        Canvas.QueueRedraw();
    }
    void Set3D(bool value)
    {
        is3D = value;
        Canvas.Visible = !value;
        viewContainer.Visible = value;
        World.Active = value;
        if (!value)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }
    void Reset(House h)
    {
        Session = new(h);
        currentPath = null;
        Selected = null;
        Refresh();
        Canvas.Fit();
    }
    public void AddWall(P a, P b)
    {
        var e = new Element { Kind = Kind.Wall, Floor = FloorIndex, A = a, B = b, Height = Session.House.Floors[FloorIndex].Ceiling };
        if (Change(h => h.Elements.Add(e)))
            Select(e.Id);
    }
    public void AddOpening(Kind kind, P p)
    {
        var wall = Session.House.Elements.Where(e => e.Kind == Kind.Wall && e.Floor == FloorIndex).MinBy(e => Geometry.SegmentDistance(p, e.A, e.B));
        if (wall == null || Geometry.SegmentDistance(p, wall.A, wall.B) > 300)
        {
            Tell("配置先の壁をクリックしてください。");
            return;
        }
        var e = new Element { Kind = kind, Floor = FloorIndex, HostId = wall.Id, Width = 900, Height = kind == Kind.Door ? 2100 : 1200, Sill = kind == Kind.Door ? 0 : 900 };
        e.Offset = Math.Clamp(Geometry.Along(p, wall.A, wall.B) - e.Width / 2, 0, Math.Max(0, wall.A.DistanceTo(wall.B) - e.Width));
        if (Change(h => h.Elements.Add(e)))
            Select(e.Id);
    }
    public void AddPart(Kind kind, P p)
    {
        var e = new Element { Kind = kind, Floor = FloorIndex, A = p, Height = 800, Color = "#a5b8b9" };
        if (kind is Kind.Stairs or Kind.ReturnStairs)
        {
            e.Height = Session.House.Floors[FloorIndex].Storey;
            e.Width = kind == Kind.Stairs ? 1000 : 2000;
            e.Depth = 3600;
        }
        if (e.IsRoof)
        {
            e.Width = 6000;
            e.Depth = 6000;
            e.Height = kind == Kind.FlatRoof ? 180 : 1000;
            e.Sill = Session.House.Floors[FloorIndex].Ceiling;
            e.Color = "#405b67";
        }
        if (kind == Kind.Balcony)
        {
            e.Width = 3000;
            e.Depth = 1500;
            e.Height = 180;
        }
        if (kind is Kind.Railing or Kind.Fence)
        {
            e.Width = 3000;
            e.Depth = 100;
            e.Height = 1100;
        }
        if (kind == Kind.Parking)
        {
            e.Width = 2500;
            e.Depth = 5000;
            e.Height = 50;
        }
        if (kind == Kind.Car)
        {
            e.Width = 1800;
            e.Depth = 4200;
            e.Height = 1500;
        }
        if (kind == Kind.Light)
        {
            e.Width = 200;
            e.Depth = 200;
            e.Height = 100;
            e.Sill = 2200;
        }
        if (Change(h => h.Elements.Add(e)))
            Select(e.Id);
    }
    public void MoveSelected(P delta)
    {
        if (Selected == null)
            return;
        Change(h => { var e = h.Elements.Single(e => e.Id == Selected); if (e.IsOpening) { var w = h.Elements.Single(w => w.Id == e.HostId); e.Offset += Geometry.Along(w.A + delta, w.A, w.B); } else { e.A += delta; e.B += delta; e.Points = e.Points.Select(p => p + delta).ToList(); } });
    }
    void Number(string label, double value, Action<double> apply, double min = -1000000, double max = 1000000, double step = 1)
    {
        var row = new HBoxContainer();
        inspector.AddChild(row);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new(110, 0) });
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, UpdateOnTextChanged = false };
        row.AddChild(spin);
        spin.ValueChanged += v => { if (!refreshing) apply(v); };
    }
    void BuildInspector()
    {
        refreshing = true;
        foreach (var n in inspector.GetChildren())
        {
            inspector.RemoveChild(n);
            n.QueueFree();
        }
        var e = Session.House.Elements.Find(e => e.Id == Selected);
        if (e == null)
        {
            Section(inspector, "プロジェクト / 単位 mm");
            var projectName = new LineEdit { Text = Session.House.Name, PlaceholderText = "住宅の名前" };
            inspector.AddChild(projectName);
            projectName.TextSubmitted += v => Change(h => h.Name = v);
            Number("グリッド", Session.House.Grid, v => Change(h => h.Grid = v), 10, 10000);
            var f = Session.House.Floors[FloorIndex];
            Number("床の高さ", f.Elevation, v => Change(h => h.Floors[FloorIndex].Elevation = v));
            Number("天井高", f.Ceiling, v => Change(h => { var floor = h.Floors[FloorIndex]; double previous = floor.Ceiling; floor.Ceiling = v; foreach (var wall in h.Elements.Where(w => w.Floor == FloorIndex && w.Kind == Kind.Wall && w.Height == previous)) wall.Height = v; }), 100, 10000);
            Number("階高", f.Storey, v => Change(h => h.Floors[FloorIndex].Storey = v), 100, 15000);
            Section(inspector, "天井の色・素材");
            var ceilingColor = new ColorPickerButton { Color = Color.FromString(f.CeilingColor, Colors.White), CustomMinimumSize = new(0, 30), EditAlpha = false };
            inspector.AddChild(ceilingColor);
            ceilingColor.PopupClosed += () => Change(h => h.Floors[FloorIndex].CeilingColor = "#" + ceilingColor.Color.ToHtml(false));
            var ceilingMaterial = new OptionButton();
            ceilingMaterial.AddItem("無地");
            ceilingMaterial.AddItem("木目");
            ceilingMaterial.AddItem("タイル");
            ceilingMaterial.Select(f.CeilingMaterial == "wood" ? 1 : f.CeilingMaterial == "tile" ? 2 : 0);
            inspector.AddChild(ceilingMaterial);
            ceilingMaterial.ItemSelected += i => Change(h => h.Floors[FloorIndex].CeilingMaterial = i == 1 ? "wood" : i == 2 ? "tile" : "plain");
            if (Session.House.Views.Count > 0)
            {
                Section(inspector, "保存した視点");
                var views = new OptionButton();
                foreach (var v in Session.House.Views)
                    views.AddItem(v.Name);
                inspector.AddChild(views);
                Button(inspector, "選んだ視点へ移動", () => { Set3D(true); World.Restore(Session.House.Views[views.Selected]); });
            }
            var note = new Label { Text = "高さ・壁厚の初期値は仮値です。\n図面の寸法に合わせて変更してください。\n\n壁: 2点をクリック\n床・部屋・敷地: 頂点をクリック→Enter\n窓・扉: 壁をクリック\n家具: 中心をクリック\n\n右クリック: 作成中の取消\nDelete: 選択を削除\nCtrl+Z / Y: 元に戻す / やり直し\n\n3D: 右ドラッグで視線\nWASD: 移動 / Q・E: 上下\nEsc: マウスを解放", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(250, 0) };
            inspector.AddChild(note);
        }
        else
        {
            Section(inspector, LabelFor(e.Kind) + " / 単位 mm");
            void Edit(Action<Element> a) => Change(h => a(h.Elements.Single(v => v.Id == e.Id)));
            var name = new LineEdit { Text = e.Name, PlaceholderText = "名前" };
            inspector.AddChild(name);
            name.TextSubmitted += v => Edit(x => x.Name = v);
            if (e.IsOpening)
            {
                Number("壁端から", e.Offset, v => Edit(x => x.Offset = v), 0);
                Number("幅", e.Width, v => Edit(x => x.Width = v), 1);
                Number("高さ", e.Height, v => Edit(x => x.Height = v), 1);
                Number("下端の高さ", e.Sill, v => Edit(x => x.Sill = v), 0);
            }
            else if (e.IsPolygon)
            {
                for (int i = 0; i < e.Points.Count; i++)
                {
                    int index = i;
                    Number($"頂点{i + 1} X", e.Points[i].X, v => Edit(x => x.Points[index] = new(v, x.Points[index].Y)));
                    Number($"頂点{i + 1} Y", e.Points[i].Y, v => Edit(x => x.Points[index] = new(x.Points[index].X, v)));
                }
                Number("厚さ", e.Height, v => Edit(x => x.Height = v), 1);
            }
            else
            {
                Number("X", e.A.X, v => Edit(x => { var d = new P(v - x.A.X, 0); x.A += d; if (x.Kind == Kind.Wall) x.B += d; }));
                Number("Y", e.A.Y, v => Edit(x => { var d = new P(0, v - x.A.Y); x.A += d; if (x.Kind == Kind.Wall) x.B += d; }));
                if (e.Kind == Kind.Wall)
                {
                    Number("終点 X", e.B.X, v => Edit(x => x.B = new(v, x.B.Y)));
                    Number("終点 Y", e.B.Y, v => Edit(x => x.B = new(x.B.X, v)));
                    Number("壁厚", e.Thickness, v => Edit(x => x.Thickness = v), 10);
                }
                else
                {
                    Number("幅", e.Width, v => Edit(x => x.Width = v), 1);
                    Number("奥行", e.Depth, v => Edit(x => x.Depth = v), 1);
                    Number("角度", e.Angle, v => Edit(x => x.Angle = v), -360, 360);
                    Number("床からの高さ", e.Sill, v => Edit(x => x.Sill = v));
                }
                Number("高さ", e.Height, v => Edit(x => x.Height = v), 1);
            }
            var color = new ColorPickerButton { Color = Color.FromString(e.Color, Colors.White), CustomMinimumSize = new(0, 34), EditAlpha = false };
            inspector.AddChild(color);
            color.PopupClosed += () => Edit(x => x.Color = "#" + color.Color.ToHtml(false));
            var mat = new OptionButton();
            mat.AddItem("無地");
            mat.AddItem("木目");
            mat.AddItem("タイル");
            mat.Select(e.Material == "wood" ? 1 : e.Material == "tile" ? 2 : 0);
            inspector.AddChild(mat);
            mat.ItemSelected += i => Edit(x => x.Material = i == 1 ? "wood" : i == 2 ? "tile" : "plain");
            Button(inspector, "複製", Duplicate);
            Button(inspector, "削除", Delete);
            Button(inspector, "選択を解除", () => Select(null));
        }
        if (Session.House.Floors[FloorIndex].Blueprint is { } bp)
        {
            Section(inspector, "下敷き");
            Number("透過度", bp.Opacity, v => Change(h => h.Floors[FloorIndex].Blueprint!.Opacity = v), 0, 1, .05);
            Number("mm / pixel", bp.MmPerPixel, v => Change(h => h.Floors[FloorIndex].Blueprint!.MmPerPixel = v), .001, 10000, .01);
            Number("図面 X", bp.Position.X, v => Change(h => h.Floors[FloorIndex].Blueprint!.Position = new(v, bp.Position.Y)));
            Number("図面 Y", bp.Position.Y, v => Change(h => h.Floors[FloorIndex].Blueprint!.Position = new(bp.Position.X, v)));
            Button(inspector, "下敷きを削除", () => Change(h => h.Floors[FloorIndex].Blueprint = null));
        }
        refreshing = false;
    }
    void Delete()
    {
        if (Selected != null)
            Change(h => h.Remove(Selected));
    }
    void Duplicate()
    {
        if (Selected == null)
            return;
        string id = Selected;
        Change(h => { var copy = h.Clone().Elements.Single(e => e.Id == id); copy.Id = Guid.NewGuid().ToString("N"); if (copy.IsOpening) copy.Offset += copy.Width + 100; else { copy.A += new P(300, 300); copy.B += new P(300, 300); copy.Points = copy.Points.Select(p => p + new P(300, 300)).ToList(); } h.Elements.Add(copy); Selected = copy.Id; });
    }
    void FileDialog(string title, Godot.FileDialog.FileModeEnum mode, string[] filters, Action<string> callback)
    {
        var d = new Godot.FileDialog { Title = title, FileMode = mode, Access = Godot.FileDialog.AccessEnum.Filesystem, Filters = filters, Size = new(950, 650) };
        AddChild(d);
        d.FileSelected += p => { try { callback(p); } catch (Exception e) { Tell(e.Message); } d.QueueFree(); };
        d.Canceled += () => d.QueueFree();
        d.PopupCentered();
    }
    void Open() => FileDialog("プロジェクトを開く", Godot.FileDialog.FileModeEnum.OpenFile, ["*.houseplan ; HousePlanning"], p => { var h = HouseArchive.Load(p); ValidateImages(h); Reset(h); currentPath = p; Tell("プロジェクトを開きました。"); });
    void ValidateImages(House h)
    {
        foreach (var floor in h.Floors)
        {
            if (floor.Blueprint == null)
                continue;
            using var image = new Image();
            if (image.LoadPngFromBuffer(floor.Blueprint.Image) != Error.Ok || image.IsEmpty() || image.GetWidth() > 4096 || image.GetHeight() > 4096)
                throw new InvalidDataException("保存された下敷き画像が不正です。現在の編集内容は保持しました。");
        }
    }
    void Save(bool other)
    {
        void Write(string p)
        {
            if (!p.EndsWith(".houseplan", StringComparison.OrdinalIgnoreCase))
                p += ".houseplan";
            HouseArchive.Save(Session.House, p);
            currentPath = p;
            Session.MarkSaved();
            Refresh();
            Tell("保存しました: " + p);
        }
        if (!other && currentPath != null)
        {
            try
            {
                Write(currentPath);
            }
            catch (Exception e) { Tell(e.Message); }
        }
        else
            FileDialog("名前を付けて保存", Godot.FileDialog.FileModeEnum.SaveFile, ["*.houseplan ; HousePlanning"], Write);
    }
    void ConfirmDiscard(Action action)
    {
        if (!Session.Dirty)
        {
            action();
            return;
        }
        var d = new ConfirmationDialog { Title = "未保存の変更", DialogText = "保存していない変更を破棄しますか？" };
        AddChild(d);
        d.Confirmed += () => { action(); d.QueueFree(); };
        d.Canceled += () => d.QueueFree();
        d.PopupCentered();
    }
    void Import() => FileDialog("図面を読み込む", Godot.FileDialog.FileModeEnum.OpenFile, ["*.pdf,*.png,*.jpg,*.jpeg ; 図面 (PDF / PNG / JPEG)"], p =>
    {
        if (System.IO.Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdf = new PdfRasterizer(p);
            var dialog = new ConfirmationDialog { Title = "PDFのページを選択", Size = new(400, 150) };
            var pages = new OptionButton();
            for (int i = 0; i < pdf.Count; i++)
                pages.AddItem($"{i + 1} ページ");
            dialog.AddChild(pages);
            pages.Position = new(25, 35);
            AddChild(dialog);
            dialog.Confirmed += () => { try { InstallImage(pdf.Render(pages.Selected)); } catch (Exception e) { Tell(e.Message); } finally { pdf.Dispose(); dialog.QueueFree(); } };
            dialog.Canceled += () => { pdf.Dispose(); dialog.QueueFree(); };
            dialog.PopupCentered();
        }
        else
        {
            var img = Image.LoadFromFile(p);
            if (img == null || img.IsEmpty())
                throw new InvalidDataException("画像を読み込めません。");
            InstallImage(img);
        }
    });
    void InstallImage(Image img)
    {
        if (Math.Max(img.GetWidth(), img.GetHeight()) > 4096)
        {
            double s = 4096.0 / Math.Max(img.GetWidth(), img.GetHeight());
            img.Resize((int)(img.GetWidth() * s), (int)(img.GetHeight() * s));
        }
        var bytes = img.SavePngToBuffer();
        Change(h => h.Floors[FloorIndex].Blueprint = new() { Image = bytes });
        Canvas.Fit();
        SetTool("calibrate");
        Tell("既知の寸法の両端をクリックしてください。");
    }
    public void Calibrate(P a, P b)
    {
        var bp = Session.House.Floors[FloorIndex].Blueprint;
        if (bp == null)
        {
            Tell("先に図面を読み込んでください。");
            return;
        }
        var d = new ConfirmationDialog { Title = "2点間の実寸 (mm)", Size = new(400, 150) };
        var s = new SpinBox { MinValue = 1, MaxValue = 1000000, Value = 910, Position = new(25, 40), Size = new(300, 40) };
        d.AddChild(s);
        AddChild(d);
        d.Confirmed += () => { Change(h => h.Floors[FloorIndex].Blueprint!.Calibrate((a - bp.Position) * (1 / bp.MmPerPixel) + bp.OriginPixel, (b - bp.Position) * (1 / bp.MmPerPixel) + bp.OriginPixel, s.Value)); Canvas.Fit(); d.QueueFree(); };
        d.Canceled += () => d.QueueFree();
        d.PopupCentered();
    }
    public void Crop(P a, P b)
    {
        var bp = Session.House.Floors[FloorIndex].Blueprint;
        if (bp == null)
        {
            Tell("先に図面を読み込んでください。");
            return;
        }
        using var img = new Image();
        img.LoadPngFromBuffer(bp.Image);
        var pa = (a - bp.Position) * (1 / bp.MmPerPixel) + bp.OriginPixel;
        var pb = (b - bp.Position) * (1 / bp.MmPerPixel) + bp.OriginPixel;
        int x = Math.Clamp((int)Math.Min(pa.X, pb.X), 0, img.GetWidth());
        int y = Math.Clamp((int)Math.Min(pa.Y, pb.Y), 0, img.GetHeight());
        int right = Math.Clamp((int)Math.Max(pa.X, pb.X), 0, img.GetWidth());
        int bottom = Math.Clamp((int)Math.Max(pa.Y, pb.Y), 0, img.GetHeight());
        if (right - x < 2 || bottom - y < 2)
        {
            Tell("切り抜き範囲が小さすぎます。");
            return;
        }
        var bytes = img.GetRegion(new(x, y, right - x, bottom - y)).SavePngToBuffer();
        Change(house => { var next = house.Floors[FloorIndex].Blueprint!; next.Image = bytes; next.OriginPixel -= new P(x, y); });
    }
    void RotateBlueprint()
    {
        var bp = Session.House.Floors[FloorIndex].Blueprint;
        if (bp == null)
            return;
        var img = new Image();
        img.LoadPngFromBuffer(bp.Image);
        int oldHeight = img.GetHeight();
        img.Rotate90(ClockDirection.Clockwise);
        var bytes = img.SavePngToBuffer();
        Change(h => { var b = h.Floors[FloorIndex].Blueprint!; b.Image = bytes; b.OriginPixel = new(oldHeight - b.OriginPixel.Y, b.OriginPixel.X); });
        Canvas.Fit();
    }
    void Capture() => FileDialog("画像を保存", Godot.FileDialog.FileModeEnum.SaveFile, ["*.png ; PNG"], p => { if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) p += ".png"; var img = is3D ? viewport.GetTexture().GetImage() : GetViewport().GetTexture().GetImage(); var err = img.SavePng(p); if (err != Error.Ok) throw new IOException("画像を保存できません: " + err); Tell("画像を保存しました。"); });
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey k || !k.Pressed || k.Echo)
            return;
        if (k.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Canvas.Draft.Clear();
            Canvas.QueueRedraw();
        }
        if (GetViewport().GuiGetFocusOwner() is LineEdit or SpinBox)
            return;
        if (k.CtrlPressed)
        {
            if (k.Keycode == Key.Z)
            {
                Session.Undo();
                Refresh();
            }
            if (k.Keycode == Key.Y)
            {
                Session.Redo();
                Refresh();
            }
            if (k.Keycode == Key.S)
                Save(false);
        }
        else if (!is3D)
        {
            if (k.Keycode == Key.Delete)
                Delete();
            if (k.Keycode == Key.Enter)
                Canvas.Complete();
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            ConfirmDiscard(() => GetTree().Quit());
    }
    async void CaptureChecks()
    {
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://editor.png"));
        Set3D(true);
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://scene.png"));
        World.Restore(new()
        {
            X = 2.5,
            Y = 1.6,
            Z = 6.5,
            Pitch = -.06,
            Yaw = 0
        });
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://interior.png"));
        GD.Print("CAPTURE " + ProjectSettings.GlobalizePath("user://"));
        GetTree().Quit();
    }
    void RunPdfCheck(string path)
    {
        try
        {
            using var pdf = new PdfRasterizer(path);
            if (pdf.Count != 3)
                throw new Exception("Expected 3 pages");
            for (int i = 0; i < pdf.Count; i++)
            {
                using var img = pdf.Render(i);
                if (img.GetWidth() < 100 || img.GetHeight() < 100)
                    throw new Exception("Empty page");
                GD.Print($"PDF_PAGE_PASS {i + 1} {img.GetWidth()}x{img.GetHeight()}");
            }
            var image = pdf.Render(1);
            var h = House.CreateEmpty();
            h.Floors[0].Blueprint = new()
            {
                Image = image.SavePngToBuffer()
            };
            string saved = ProjectSettings.GlobalizePath("user://pdf-check.houseplan");
            HouseArchive.Save(h, saved);
            if (HouseArchive.Load(saved).Floors[0].Blueprint!.Image.Length == 0)
                throw new Exception("Image archive lost");
            File.Delete(saved);
            GD.Print("PDF_CHECK_PASS embedded-image roundtrip");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    void AssertEditorFlow()
    {
        var previous = Session.House;
        Reset(House.CreateEmpty());
        SnapEnabled = false;
        Canvas.Tool = Kind.Wall.ToString();
        foreach (var p in new[] { new P(0, 0), new P(5000, 0) })
            Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Canvas.Screen(p) });
        var wall = Session.House.Elements.Single();
        if (wall.Kind != Kind.Wall || wall.B.X != 5000)
            throw new Exception("Wall click tool failed");
        AddOpening(Kind.Window, new(2000, 0));
        var opening = Session.House.Elements.Single(e => e.IsOpening);
        Select(wall.Id);
        MoveSelected(new(1000, 2000));
        if (Session.House.OpeningCenter(Session.House.Elements.Single(e => e.Id == opening.Id)).DistanceTo(new(3000, 2000)) > .1)
            throw new Exception("Editor opening move failed");
        Delete();
        if (Session.House.Elements.Count != 0)
            throw new Exception("Delete did not cascade");
        Session.Undo();
        Refresh();
        if (Session.House.Elements.Count != 2)
            throw new Exception("Undo did not restore scene");
        using var image = Image.CreateEmpty(100, 80, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        InstallImage(image);
        Crop(new(100, 100), new(600, 600));
        var data = Session.House.Floors[0].Blueprint!.Image;
        using var cropped = new Image();
        cropped.LoadPngFromBuffer(data);
        if (cropped.GetWidth() != 50 || cropped.GetHeight() != 50)
            throw new Exception("Crop tool failed");
        GD.Print("EDITOR_FLOW_PASS wall/opening/move/delete/undo/import/crop");
        Reset(previous);
        SnapEnabled = true;
    }
    async void RunSelfTest()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Canvas.Size.X < 300 || Canvas.Size.Y < 300)
                throw new Exception("Editor layout too small: " + Canvas.Size);
            Session.House.Validate();
            World.AssertScene();
            await World.AssertWalk();
            AssertEditorFlow();
            var path = ProjectSettings.GlobalizePath("user://smoke.houseplan");
            HouseArchive.Save(Session.House, path);
            var h = HouseArchive.Load(path);
            if (h.Elements.Count != Session.House.Elements.Count)
                throw new Exception("Archive mismatch");
            GD.Print("APP_SELF_TEST_PASS nodes=" + World.GetChildCount() + " canvas=" + Canvas.Size);
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("APP_SELF_TEST_FAIL " + e); GetTree().Quit(1); }
    }
}
