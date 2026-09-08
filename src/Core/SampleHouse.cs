namespace HousePlanning.Core;
public static class SampleHouse
{
    public static House Create(bool benchmark = false)
    {
        var h = House.CreateEmpty();
        h.Name = "吹き抜けのある家 / サンプル";
        List<Point> Rect(double x, double y, double w, double d) => [new(x, y), new(x + w, y), new(x + w, y + d), new(x, y + d)];
        Element Wall(int f, Point a, Point b)
        {
            var w = new Element { Kind = Kind.Wall, Floor = f, A = a, B = b, Height = 2400, Color = f == 0 ? "#e4dfd7" : "#d4dce0" };
            h.Elements.Add(w);
            return w;
        }
        for (int f = 0; f < 2; f++)
        {
            h.Elements.Add(new()
            {
                Kind = Kind.Floor,
                Floor = f,
                Points = Rect(0, 0, 10000, 8000),
                Height = 180,
                Color = "#ba9975",
                Material = "wood"
            });
            var south = Wall(f, new(0, 0), new(10000, 0));
            var east = Wall(f, new(10000, 0), new(10000, 8000));
            Wall(f, new(10000, 8000), new(0, 8000));
            Wall(f, new(0, 8000), new(0, 0));
            h.Elements.Add(new()
            {
                Kind = Kind.Window,
                Floor = f,
                HostId = south.Id,
                Offset = 1000,
                Width = 2200,
                Height = 1300,
                Sill = 800
            });
            h.Elements.Add(new()
            {
                Kind = Kind.Window,
                Floor = f,
                HostId = east.Id,
                Offset = 4000,
                Width = 2000,
                Height = 1600,
                Sill = 600
            });
            if (f == 0)
                h.Elements.Add(new()
                {
                    Kind = Kind.Door,
                    HostId = south.Id,
                    Offset = 7000,
                    Width = 1000,
                    Height = 2100,
                    Color = "#705039"
                });
            var inner = Wall(f, new(5000, 0), new(5000, 4500));
            inner.Thickness = 100;
            h.Elements.Add(new()
            {
                Kind = Kind.Door,
                Floor = f,
                HostId = inner.Id,
                Offset = 2900,
                Width = 900,
                Height = 2100
            });
        }
        h.Elements.Add(new()
        {
            Kind = Kind.Void,
            Floor = 1,
            Points = Rect(6000, 4100, 3400, 3400)
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Stairs,
            A = new(8800, 5800),
            Width = 1000,
            Depth = 3400,
            Height = 2800,
            Angle = 0,
            Color = "#b8946c"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Railing,
            Floor = 1,
            A = new(6000, 5900),
            Width = 2900,
            Depth = 60,
            Height = 1100,
            Angle = 90,
            Color = "#505961"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Balcony,
            Floor = 1,
            A = new(2300, -900),
            Width = 4000,
            Depth = 1800,
            Height = 180
        });
        h.Elements.Add(new()
        {
            Kind = Kind.GableRoof,
            Floor = 1,
            A = new(5000, 4000),
            Width = 10600,
            Depth = 8600,
            Height = 900,
            Sill = 2400,
            Color = "#344c58"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Site,
            Points = Rect(-3500, -6000, 16500, 16500),
            Height = 100,
            Color = "#69896c"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Parking,
            A = new(6500, -3000),
            Width = 5500,
            Depth = 5000,
            Height = 50,
            Color = "#a3aaa9"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Car,
            A = new(7000, -3000),
            Width = 1800,
            Depth = 4300,
            Height = 1500,
            Color = "#728fa0"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Sofa,
            A = new(2500, 5500),
            Width = 2300,
            Depth = 900,
            Height = 800,
            Color = "#69818a"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Desk,
            A = new(2500, 3500),
            Width = 1800,
            Depth = 850,
            Height = 720,
            Color = "#ad835b"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Kitchen,
            A = new(1800, 1000),
            Width = 2700,
            Depth = 800,
            Height = 850,
            Color = "#d8d5c9"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Bed,
            Floor = 1,
            A = new(2000, 2500),
            Width = 1600,
            Depth = 2100,
            Height = 550,
            Color = "#829da3"
        });
        h.Elements.Add(new()
        {
            Kind = Kind.Light,
            A = new(2500, 3500),
            Height = 100,
            Width = 200,
            Depth = 200,
            Sill = 2200
        });
        if (benchmark)
            for (int i = 0; i < 100; i++)
                h.Elements.Add(new()
                {
                    Kind = Kind.Chair,
                    Floor = i % 2,
                    A = new(600 + (i % 10) * 420, 550 + (i / 10) * 620),
                    Width = 350,
                    Depth = 400,
                    Height = 800,
                    Color = "#9c876e"
                });
        h.Validate();
        return h;
    }
}
