using Godot;
using HousePlanning.Core;
using System.Text.Json;
using P = HousePlanning.Core.Point;
namespace HousePlanning;

public partial class WorldView : Node3D
{
    public Main App = null!;
    public bool Active;
    public int ActiveFloor;
    public Camera3D Camera = null!;
    CharacterBody3D walker = null!;
    DirectionalLight3D sun = null!;
    readonly Dictionary<string, (string Fingerprint, Node3D Node)> rendered = [];
    readonly Dictionary<string, StandardMaterial3D> materials = [];
    House house = House.CreateEmpty();
    string mode = "orbit";
    float yaw = .65f, pitch = -.5f, distance = 22;
    Vector3 target = new(5, 1.8f, 4);
    bool dragging, onlyFloor, showRoofs = true;
    readonly List<double> frames = [];
    double benchmarkTime = -1;
    Vector3? testMovement;
    public override void _Ready()
    {
        var environment = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("dce8ec"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new("e0ecf1"), AmbientLightEnergy = .65f, TonemapMode = Godot.Environment.ToneMapper.Filmic } };
        AddChild(environment);
        sun = new DirectionalLight3D { RotationDegrees = new(-48, -28, 0), LightColor = new("fff0d8"), LightEnergy = 1.1f, ShadowEnabled = true, DirectionalShadowMaxDistance = 60 };
        AddChild(sun);
        Camera = new Camera3D { Current = true, Near = .05f, Far = 150, Fov = 70 };
        AddChild(Camera);
        walker = new CharacterBody3D { FloorSnapLength = .5f, FloorMaxAngle = Mathf.DegToRad(50), FloorConstantSpeed = true };
        AddChild(walker);
        walker.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .22f, Height = 1.7f }, Position = new(0, .85f, 0) });
        Orbit();
    }
    static Vector3 V(P p, double height) => new((float)p.X / 1000, (float)height / 1000, (float)p.Y / 1000);
    public void Sync(House h)
    {
        house = h;
        var wanted = new HashSet<string>();
        foreach (var e in h.Elements.Where(e => !e.IsOpening && e.Kind != Kind.Void))
        {
            wanted.Add(e.Id);
            string hash = JsonSerializer.Serialize(e, House.JsonOptions) + JsonSerializer.Serialize(h.Floors[e.Floor], House.JsonOptions);
            if (e.Kind == Kind.Wall)
                hash += JsonSerializer.Serialize(h.Elements.Where(o => o.HostId == e.Id), House.JsonOptions);
            if (e.Kind == Kind.Wall && e.Floor == 0)
                hash += h.Floors[1].Elevation;
            if (e.Kind is Kind.Floor or Kind.Room)
                hash += JsonSerializer.Serialize(h.Elements.Where(o => o.Kind == Kind.Void), House.JsonOptions);
            if (rendered.TryGetValue(e.Id, out var old) && old.Fingerprint == hash)
                continue;
            if (old.Node != null)
            {
                RemoveChild(old.Node);
                old.Node.QueueFree();
            }
            var node = new Node3D { Name = "Element_" + e.Id };
            AddChild(node);
            Build(node, e);
            rendered[e.Id] = (hash, node);
        }
        foreach (var id in rendered.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            var node = rendered[id].Node;
            RemoveChild(node);
            node.QueueFree();
            rendered.Remove(id);
        }
        UpdateVisibility();
    }
    StandardMaterial3D Material(string color, string texture = "plain", bool transparent = false)
    {
        string key = color + texture + transparent;
        if (materials.TryGetValue(key, out var existing))
            return existing;
        var m = new StandardMaterial3D { AlbedoColor = Color.FromString(color, Colors.LightGray), Roughness = .8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        if (transparent)
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.AlbedoColor = new Color(m.AlbedoColor, .24f);
            m.Roughness = .1f;
        }
        if (texture != "plain")
        {
            var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float value = texture == "tile" ? (x % 64 < 2 || y % 64 < 2 ? .68f : 1) : .86f + .1f * Mathf.Sin(y * .3f + Mathf.Sin(x * .06f)) + (y % 32 < 1 ? -.14f : 0);
                    img.SetPixel(x, y, new Color(value, value, value));
                }
            m.AlbedoTexture = ImageTexture.CreateFromImage(img);
            m.Uv1Scale = new(2, 2, 2);
        }
        materials[key] = m;
        return m;
    }
    void Box(Node3D parent, Vector3 pos, Vector3 size, Material material, bool collision = true)
    {
        size = new(Math.Max(.001f, size.X), Math.Max(.001f, size.Y), Math.Max(.001f, size.Z));
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = pos };
        parent.AddChild(mesh);
        if (collision)
        {
            var body = new StaticBody3D { Position = pos };
            parent.AddChild(body);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        }
    }
    void Surface(Node3D parent, IEnumerable<Vector3[]> polygons, Material material, bool collision = true, bool visible = true)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        int vertices = 0;
        foreach (var p in polygons)
            for (int i = 1; i < p.Length - 1; i++)
            {
                foreach (var v in new[] { p[0], p[i + 1], p[i] })
                {
                    tool.SetUV(new(v.X, v.Z));
                    tool.AddVertex(v);
                    vertices++;
                }
            }
        if (vertices == 0)
            return;
        tool.GenerateNormals();
        var mesh = tool.Commit();
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Visible = visible };
        parent.AddChild(instance);
        if (collision)
        {
            var shape = mesh.CreateTrimeshShape();
            shape.BackfaceCollision = true;
            var body = new StaticBody3D();
            parent.AddChild(body);
            body.AddChild(new CollisionShape3D { Shape = shape });
        }
    }
    void Build(Node3D root, Element e)
    {
        var mat = Material(e.Color, e.Material);
        double elevation = house.Floors[e.Floor].Elevation;
        if (e.Kind == Kind.Wall)
        {
            BuildWall(root, e, elevation, mat);
            return;
        }
        if (e.IsPolygon)
        {
            if (e.Kind == Kind.Room)
                return;
            var holes = house.Elements.Where(v => v.Kind == Kind.Void && v.Floor == e.Floor).Select(v => (IReadOnlyList<P>)v.Points).ToList();
            double top = e.Kind == Kind.Site ? -100 : elevation;
            var quads = Geometry.Surface(e.Points, e.Kind == Kind.Site ? [] : holes);
            var polys = new List<Vector3[]>();
            foreach (var q in quads)
            {
                polys.Add(q.Select(p => V(p, top)).ToArray());
                polys.Add(q.Reverse().Select(p => V(p, top - e.Height)).ToArray());
            }
            foreach (var loop in new[] { (IReadOnlyList<P>)e.Points }.Concat(e.Kind == Kind.Site ? [] : holes))
                for (int i = 0; i < loop.Count; i++)
                {
                    var a = loop[i];
                    var b = loop[(i + 1) % loop.Count];
                    polys.Add([V(a, top), V(b, top), V(b, top - e.Height), V(a, top - e.Height)]);
                }
            Surface(root, polys, mat);
            if (e.Kind == Kind.Floor)
            {
                var ceilingHoles = house.Elements.Where(v => v.Kind == Kind.Void && (v.Floor == e.Floor || v.Floor == e.Floor + 1)).Select(v => (IReadOnlyList<P>)v.Points);
                var ceiling = new Node3D { Name = "Ceiling" };
                root.AddChild(ceiling);
                Surface(ceiling, Geometry.Surface(e.Points, ceilingHoles).Select(q => q.Select(p => V(p, elevation + house.Floors[e.Floor].Ceiling)).ToArray()), Material(house.Floors[e.Floor].CeilingColor, house.Floors[e.Floor].CeilingMaterial));
            }
            return;
        }
        root.Position = V(e.A, elevation + e.Sill);
        root.RotationDegrees = new(0, -(float)e.Angle, 0);
        float w = (float)e.Width / 1000, d = (float)e.Depth / 1000, h = (float)e.Height / 1000;
        var metal = Material("#3b4d57");
        var white = Material("#efefeb");
        switch (e.Kind)
        {
            case Kind.Stairs:
                BuildStair(root, -w / 2, w / 2, -d / 2, d / 2, 0, h, mat, false);
                break;
            case Kind.ReturnStairs:
                float landing = Math.Min(.9f, d / 3);
                BuildStair(root, -w / 2, 0, -d / 2 + landing, d / 2, 0, h / 2, mat, false);
                Box(root, new(0, h / 2 - .08f, -d / 2 + landing / 2), new(w, .16f, landing), mat);
                BuildStair(root, 0, w / 2, -d / 2 + landing, d / 2, h / 2, h, mat, true);
                break;
            case Kind.FlatRoof:
                Box(root, new(0, h / 2, 0), new(w, h, d), mat);
                break;
            case Kind.ShedRoof:
                Surface(root, [new Vector3[] { new(-w / 2, 0, -d / 2), new(w / 2, 0, -d / 2), new(w / 2, h, d / 2), new(-w / 2, h, d / 2) }, new Vector3[] { new(-w / 2, 0, -d / 2), new(-w / 2, h, d / 2), new(-w / 2, 0, d / 2) }, new Vector3[] { new(w / 2, 0, -d / 2), new(w / 2, 0, d / 2), new(w / 2, h, d / 2) }], mat);
                break;
            case Kind.GableRoof:
                Surface(root, [new Vector3[] { new(-w / 2, 0, -d / 2), new(0, h, -d / 2), new(0, h, d / 2), new(-w / 2, 0, d / 2) }, new Vector3[] { new(0, h, -d / 2), new(w / 2, 0, -d / 2), new(w / 2, 0, d / 2), new(0, h, d / 2) }, new Vector3[] { new(-w / 2, 0, -d / 2), new(w / 2, 0, -d / 2), new(0, h, -d / 2) }, new Vector3[] { new(-w / 2, 0, d / 2), new(0, h, d / 2), new(w / 2, 0, d / 2) }], mat);
                break;
            case Kind.Railing:
            case Kind.Fence:
                Railing(root, w, h, Math.Max(.05f, d), mat);
                break;
            case Kind.Balcony:
                Box(root, new(0, -h / 2, 0), new(w, h, d), mat);
                var rail = new Node3D { Position = new(0, 0, -d / 2) };
                root.AddChild(rail);
                Railing(rail, w, 1.1f, .06f, metal);
                foreach (float x in new[] { -w / 2, w / 2 })
                {
                    var side = new Node3D { Position = new(x, 0, 0), RotationDegrees = new(0, 90, 0) };
                    root.AddChild(side);
                    Railing(side, d, 1.1f, .06f, metal);
                }
                break;
            case Kind.Desk:
            case Kind.Chair:
                Box(root, new(0, h * .66f, 0), new(w, h * .09f, d), mat);
                foreach (float x in new[] { -.4f, .4f })
                    foreach (float z in new[] { -.4f, .4f })
                        Box(root, new(x * w, h * .33f, z * d), new(w * .08f, h * .66f, d * .08f), metal);
                if (e.Kind == Kind.Chair)
                    Box(root, new(0, h * .85f, d * .43f), new(w, h * .3f, d * .1f), mat);
                break;
            case Kind.Sofa:
                Box(root, new(0, h * .3f, 0), new(w, h * .6f, d), mat);
                Box(root, new(0, h * .8f, d * .4f), new(w, h * .4f, d * .2f), mat);
                foreach (float x in new[] { -.46f, .46f })
                    Box(root, new(x * w, h * .55f, 0), new(w * .08f, h * .4f, d), mat);
                break;
            case Kind.Bed:
                Box(root, new(0, h * .35f, 0), new(w, h * .7f, d), mat);
                Box(root, new(0, h * .8f, 0), new(w * .96f, h * .3f, d * .96f), white);
                Box(root, new(0, h * .99f, -d * .32f), new(w * .8f, h * .13f, d * .22f), white);
                break;
            case Kind.Kitchen:
            case Kind.Storage:
                Box(root, new(0, h / 2, 0), new(w, h, d), mat);
                Box(root, new(0, h, 0), new(w * 1.02f, .04f, d * 1.02f), white);
                for (float x = -w / 2 + .4f; x < w / 2; x += .6f)
                    Box(root, new(x, h * .7f, -d / 2 - .01f), new(.12f, .025f, .025f), metal, false);
                if (e.Kind == Kind.Kitchen)
                    Box(root, new(w * .23f, h + .025f, 0), new(w * .22f, .02f, d * .65f), metal, false);
                break;
            case Kind.Bath:
            case Kind.Basin:
                Box(root, new(0, h * .15f, 0), new(w, h * .3f, d), white);
                Box(root, new(0, h * .6f, -d * .45f), new(w, h * .8f, d * .1f), white);
                Box(root, new(0, h * .6f, d * .45f), new(w, h * .8f, d * .1f), white);
                foreach (float x in new[] { -.45f, .45f })
                    Box(root, new(x * w, h * .6f, 0), new(w * .1f, h * .8f, d), white);
                break;
            case Kind.Toilet:
                Box(root, new(0, h * .3f, 0), new(w * .65f, h * .6f, d * .7f), white);
                Box(root, new(0, h * .7f, d * .3f), new(w * .8f, h * .6f, d * .25f), white);
                break;
            case Kind.Car:
                Box(root, new(0, h * .35f, 0), new(w, h * .45f, d), mat);
                Box(root, new(0, h * .7f, 0), new(w * .82f, h * .4f, d * .55f), Material("#526b75"));
                foreach (float x in new[] { -.48f, .48f })
                    foreach (float z in new[] { -.3f, .3f })
                        Box(root, new(x * w, h * .2f, z * d), new(w * .12f, h * .3f, d * .14f), metal);
                break;
            case Kind.Light:
                Box(root, Vector3.Zero, new(w, h, d), white, false);
                root.AddChild(new OmniLight3D { LightColor = new("fff1d9"), LightEnergy = .8f, OmniRange = 6, ShadowEnabled = false });
                break;
            default:
                Box(root, new(0, h / 2, 0), new(w, h, d), mat);
                break;
        }
    }
    void BuildWall(Node3D root, Element e, double elevation, Material mat)
    {
        double length = e.A.DistanceTo(e.B);
        root.Position = V(e.A, elevation);
        root.Rotation = new(0, -(float)Math.Atan2(e.B.Y - e.A.Y, e.B.X - e.A.X), 0);
        var openings = house.Elements.Where(o => o.HostId == e.Id).ToArray();
        var xs = new[] { 0d, length }.Concat(openings.SelectMany(o => new[] { o.Offset, o.Offset + o.Width })).Distinct().Order().ToArray();
        void Piece(double x0, double x1, double y0, double y1)
        {
            if (x1 - x0 > .01 && y1 - y0 > .01)
                Box(root, new((float)(x0 + x1) / 2000, (float)(y0 + y1) / 2000, 0), new((float)(x1 - x0) / 1000, (float)(y1 - y0) / 1000, (float)e.Thickness / 1000), mat);
        }
        for (int i = 0; i < xs.Length - 1; i++)
        {
            var os = openings.Where(o => o.Offset < xs[i + 1] && o.Offset + o.Width > xs[i]).OrderBy(o => o.Sill).ToArray();
            double y = 0;
            foreach (var o in os)
            {
                Piece(xs[i], xs[i + 1], y, o.Sill);
                y = o.Sill + o.Height;
            }
            Piece(xs[i], xs[i + 1], y, e.Height);
        }
        if (e.Floor == 0 && e.Thickness >= 180)
        {
            double upper = house.Floors[1].Elevation - elevation;
            if (upper > e.Height)
                Piece(0, length, e.Height, upper);
        }
        foreach (var o in openings)
        {
            float x = (float)(o.Offset + o.Width / 2) / 1000, y = (float)(o.Sill + o.Height / 2) / 1000, w = (float)o.Width / 1000, h = (float)o.Height / 1000;
            var frame = Material("#50616a");
            foreach (float sx in new[] { -1f, 1f })
                Box(root, new(x + sx * w / 2, y, 0), new(.04f, h, .06f), frame);
            Box(root, new(x, y + h / 2, 0), new(w, .04f, .06f), frame);
            if (o.Kind == Kind.Window)
            {
                Box(root, new(x, y - h / 2, 0), new(w, .04f, .06f), frame);
                Box(root, new(x, y, 0), new(w, h, .012f), Material("#a9d7e5", "plain", true));
            }
            else
            {
                var leaf = new Node3D { Position = new(x - w / 2, y, 0), RotationDegrees = new(0, 90, 0) };
                root.AddChild(leaf);
                Box(leaf, new(w / 2, 0, 0), new(w, h, .035f), Material(o.Color), false);
            }
        }
    }
    void Railing(Node3D node, float w, float h, float depth, Material mat)
    {
        Box(node, new(0, h, 0), new(w, .06f, depth), mat);
        for (float x = -w / 2; x <= w / 2 + .001; x += Math.Max(.15f, w / 15))
            Box(node, new(x, h / 2, 0), new(.035f, h, .035f), mat, false);
        var body = new StaticBody3D();
        node.AddChild(body);
        body.AddChild(new CollisionShape3D { Position = new(0, h / 2, 0), Shape = new BoxShape3D { Size = new(w, h, depth) } });
    }
    void BuildStair(Node3D node, float x0, float x1, float z0, float z1, float y0, float y1, Material mat, bool reverse)
    {
        int count = Math.Max(2, (int)Math.Ceiling((y1 - y0) / .18));
        float run = (z1 - z0) / count;
        for (int i = 0; i < count; i++)
        {
            float height = y0 + (y1 - y0) * (i + 1) / count;
            float z = reverse ? z0 + run * (i + .5f) : z1 - run * (i + .5f);
            Box(node, new((x0 + x1) / 2, height / 2, z), new(x1 - x0, height, run + .002f), mat, false);
        }
        var ramp = new Vector3[] { new(x0, reverse ? y0 : y1, z0), new(x1, reverse ? y0 : y1, z0), new(x1, reverse ? y1 : y0, z1), new(x0, reverse ? y1 : y0, z1) };
        Surface(node, [ramp], mat, true, false);
    }
    public void SetVisibility(int floor, bool only, bool roof)
    {
        ActiveFloor = floor;
        onlyFloor = only;
        showRoofs = roof;
        UpdateVisibility();
    }
    public void ShowRoof(bool value)
    {
        showRoofs = value;
        UpdateVisibility();
    }
    public void OnlyFloor(bool value)
    {
        onlyFloor = value;
        UpdateVisibility();
    }
    void UpdateVisibility()
    {
        foreach (var e in house.Elements)
        {
            if (!rendered.TryGetValue(e.Id, out var pair))
                continue;
            pair.Node.Visible = (!onlyFloor || e.Floor == ActiveFloor) && (!e.IsRoof || showRoofs);
            var ceiling = pair.Node.GetNodeOrNull<Node3D>("Ceiling");
            if (ceiling != null)
                ceiling.Visible = mode != "orbit" && showRoofs;
        }
    }
    public void SetQuality(bool low)
    {
        sun.ShadowEnabled = !low;
        ((SubViewport)GetViewport()).Scaling3DScale = low ? .7f : 1f;
    }
    public void Orbit()
    {
        mode = "orbit";
        yaw = .65f;
        pitch = -.5f;
        distance = 22;
        UpdateVisibility();
    }
    public void Fly()
    {
        mode = "fly";
        UpdateVisibility();
    }
    public void Walk(int floor)
    {
        mode = "walk";
        var floors = house.Elements.Where(e => e.Floor == floor && e.Kind == Kind.Floor).ToArray();
        var holes = house.Elements.Where(e => e.Floor == floor && e.Kind == Kind.Void).ToArray();
        P? start = null;
        foreach (var f in floors)
        {
            for (double x = f.Points.Min(p => p.X) + 500; x < f.Points.Max(p => p.X) && start == null; x += 500)
                for (double y = f.Points.Min(p => p.Y) + 500; y < f.Points.Max(p => p.Y) && start == null; y += 500)
                {
                    var p = new P(x, y);
                    if (Geometry.Contains(f.Points, p) && !holes.Any(h => Geometry.Contains(h.Points, p)) && !house.Elements.Any(e => e.Floor == floor && ((e.Kind == Kind.Wall && Geometry.SegmentDistance(p, e.A, e.B) < e.Thickness / 2 + 300) || (!e.IsPolygon && !e.IsOpening && e.Kind != Kind.Wall && !e.IsRoof && p.DistanceTo(e.A) < Math.Max(e.Width, e.Depth) / 2 + 300))))
                        start = p;
                }
        }
        if (start == null)
        {
            mode = "fly";
            App.Tell("歩行開始できる床がありません。床を作成し、通路を確保してください。");
            return;
        }
        walker.Position = V(start.Value, house.Floors[floor].Elevation + 150);
        walker.Velocity = Vector3.Zero;
        yaw = 0;
        pitch = 0;
        UpdateVisibility();
    }
    public SavedView GetView() => new() { Name = $"視点 {house.Views.Count + 1}", X = Camera.Position.X, Y = Camera.Position.Y, Z = Camera.Position.Z, Pitch = pitch, Yaw = yaw };
    public void Restore(SavedView v)
    {
        Fly();
        Camera.Position = new((float)v.X, (float)v.Y, (float)v.Z);
        pitch = (float)v.Pitch;
        yaw = (float)v.Yaw;
    }
    public override void _Input(InputEvent ev)
    {
        if (!Active)
            return;
        if (ev is InputEventMouseButton b)
        {
            if (b.ButtonIndex == MouseButton.Right)
            {
                dragging = b.Pressed;
                Input.MouseMode = b.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
            if (mode == "orbit" && b.Pressed)
            {
                if (b.ButtonIndex == MouseButton.WheelUp)
                    distance = Math.Max(2, distance * .9f);
                if (b.ButtonIndex == MouseButton.WheelDown)
                    distance = Math.Min(80, distance * 1.1f);
            }
        }
        if (ev is InputEventMouseMotion m && dragging)
        {
            yaw -= m.Relative.X * .005f;
            pitch = Math.Clamp(pitch - m.Relative.Y * .005f, -1.5f, 1.4f);
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (!Active || mode != "walk")
            return;
        var dir = Movement();
        var v = walker.Velocity;
        v.X = dir.X * 2.5f;
        v.Z = dir.Z * 2.5f;
        if (!walker.IsOnFloor())
            v.Y -= 9.8f * (float)delta;
        else
            v.Y = -.1f;
        walker.Velocity = v;
        walker.MoveAndSlide();
        if (walker.Position.Y < -10)
            Walk(ActiveFloor);
        Camera.Position = walker.Position + new Vector3(0, 1.6f, 0);
    }
    Vector3 Movement()
    {
        if (testMovement.HasValue)
            return testMovement.Value;
        if (App.IsTyping)
            return Vector3.Zero;
        float x = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0), z = (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0);
        return new Vector3(x, 0, z).Rotated(Vector3.Up, yaw).Normalized();
    }
    public override void _Process(double delta)
    {
        if (Camera == null || !Active)
            return;
        if (mode == "orbit")
        {
            Camera.Position = target + new Vector3(0, 0, distance).Rotated(Vector3.Right, pitch).Rotated(Vector3.Up, yaw);
            Camera.LookAt(target);
        }
        else
        {
            Camera.Rotation = new(pitch, yaw, 0);
            if (mode == "fly")
            {
                var dir = Movement();
                dir.Y = (Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0);
                Camera.Position += dir * (float)delta * (Input.IsPhysicalKeyPressed(Key.Shift) ? 8 : 3);
            }
        }
        if (benchmarkTime >= 0)
        {
            benchmarkTime += delta;
            if (benchmarkTime < 20)
            {
                yaw += (float)delta * .25f;
            }
            else
            {
                mode = "fly";
                UpdateVisibility();
                Camera.Position = new(2.5f, benchmarkTime < 27 ? 1.6f : 4.4f, 6.5f - (float)((benchmarkTime - 20) % 7) * .35f);
                Camera.Rotation = new(-.08f, (float)(benchmarkTime - 20) * .08f, 0);
            }
            if (benchmarkTime > 5)
                frames.Add(delta * 1000);
            if (benchmarkTime > 35)
            {
                var sorted = frames.Order().ToArray();
                double avg = frames.Average();
                string output = $"GPU: {RenderingServer.GetVideoAdapterName()}\nRenderer: {RenderingServer.GetCurrentRenderingMethod()}\nViewport: {GetViewport().GetVisibleRect().Size}\nAverage FPS: {1000 / avg:F2}\nP95 ms: {sorted[(int)(sorted.Length * .95)]:F2}";
                string path = ProjectSettings.GlobalizePath("user://benchmark.txt");
                File.WriteAllText(path, output);
                File.WriteAllLines(ProjectSettings.GlobalizePath("user://frames.csv"), new[] { "frame_ms" }.Concat(frames.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                GD.Print(output);
                benchmarkTime = -1;
                App.Tell("性能計測完了: " + path);
                if (OS.GetCmdlineUserArgs().Contains("--benchmark"))
                    GetTree().Quit();
            }
        }
    }
    public void BeginBenchmark()
    {
        Orbit();
        benchmarkTime = 0;
        frames.Clear();
    }
    public void AssertScene()
    {
        if (rendered.Count < 10)
            throw new Exception("Scene elements missing");
        if (!rendered.Values.Any(v => v.Node.GetChildren().OfType<StaticBody3D>().Any()))
            throw new Exception("Collision missing");
    }
    public async System.Threading.Tasks.Task AssertWalk()
    {
        Active = true;
        Walk(0);
        walker.Position = new(8.8f, .1f, 7.7f);
        testMovement = new(0, 0, -1);
        for (int i = 0; i < 125; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        testMovement = Vector3.Zero;
        for (int i = 0; i < 15; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        GD.Print("STAIR_WALK_POSITION " + walker.Position);
        if (walker.Position.Y < 2.75f || walker.Position.Z > 4.1f)
            throw new Exception("Stair traversal failed: " + walker.Position);
        walker.Position = new(2, .1f, 1);
        testMovement = new(0, 0, -1);
        for (int i = 0; i < 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (walker.Position.Z < .15f)
            throw new Exception("Passed through exterior wall/window");
        var previous = house;
        var testHouse = House.CreateEmpty();
        for (int f = 0; f < 2; f++)
            testHouse.Elements.Add(new()
            {
                Kind = Kind.Floor,
                Floor = f,
                Height = 180,
                Points = [new(-3000, -3000), new(3000, -3000), new(3000, 3000), new(-3000, 3000)]
            });
        testHouse.Elements.Add(new()
        {
            Kind = Kind.Void,
            Floor = 1,
            Points = [new(-1200, -2200), new(1200, -2200), new(1200, 1800), new(-1200, 1800)]
        });
        testHouse.Elements.Add(new()
        {
            Kind = Kind.ReturnStairs,
            Width = 2000,
            Depth = 3600,
            Height = 2800
        });
        Sync(testHouse);
        mode = "walk";
        walker.Position = new(-.5f, .1f, 2);
        walker.Velocity = Vector3.Zero;
        testMovement = new(0, 0, -1);
        for (int i = 0; i < 95; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        testMovement = new(1, 0, 0);
        for (int i = 0; i < 24; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        testMovement = new(0, 0, 1);
        for (int i = 0; i < 100; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        testMovement = Vector3.Zero;
        for (int i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        GD.Print("RETURN_STAIR_POSITION " + walker.Position);
        if (walker.Position.Y < 2.75f)
            throw new Exception("Return stair traversal failed");
        Sync(previous);
        testMovement = null;
        Active = false;
        Orbit();
    }
}
