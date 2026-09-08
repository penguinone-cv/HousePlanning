namespace HousePlanning.Core;
public static class Geometry
{
    public static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    public static void ValidatePolygon(IReadOnlyList<Point> p)
    {
        if (p.Count < 3 || p.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y)))
            throw new ArgumentException("多角形は3点以上必要です。");
        double area = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            if (a.DistanceTo(b) < 1)
                throw new ArgumentException("重複する頂点を取り除いてください。");
            area += a.X * b.Y - b.X * a.Y;
            for (int j = i + 1; j < p.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == p.Count - 1))
                    continue;
                var c = p[j];
                var d = p[(j + 1) % p.Count];
                if (Intersects(a, b, c, d))
                    throw new ArgumentException("多角形の辺が交差しています。");
            }
        }
        if (Math.Abs(area) < 1)
            throw new ArgumentException("面積のない多角形です。");
    }
    static bool Intersects(Point a, Point b, Point c, Point d) => Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) && Cross(a, b, c) * Cross(a, b, d) <= 0 && Cross(c, d, a) * Cross(c, d, b) <= 0;
    public static bool Contains(IReadOnlyList<Point> p, Point q)
    {
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) && q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                inside = !inside;
        return inside;
    }
    public static double Along(Point p, Point a, Point b)
    {
        var l = a.DistanceTo(b);
        return ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l;
    }
    public static double SegmentDistance(Point p, Point a, Point b)
    {
        var length = a.DistanceTo(b);
        return length < .001 ? p.DistanceTo(a) : p.DistanceTo(a + (b - a) * (Math.Clamp(Along(p, a, b) / length, 0, 1)));
    }
    // Vertical decomposition supports concave outlines and multiple holes without a bridge heuristic.
    public static List<Point[]> Surface(IReadOnlyList<Point> outline, IEnumerable<IReadOnlyList<Point>> holes)
    {
        var hs = holes.ToList();
        var loops = new List<IReadOnlyList<Point>> { outline };
        loops.AddRange(hs);
        var cuts = loops.SelectMany(p => p.Select(v => v.X)).ToList();
        var allEdges = loops.SelectMany(p => p.Select((v, i) => (A: v, B: p[(i + 1) % p.Count]))).ToArray();
        for (int i = 0; i < allEdges.Length; i++)
            for (int j = i + 1; j < allEdges.Length; j++)
            {
                var (a, b) = allEdges[i];
                var (c, d) = allEdges[j];
                var r = b - a;
                var s = d - c;
                double cross = r.X * s.Y - r.Y * s.X;
                if (Math.Abs(cross) < 1e-10)
                    continue;
                var ca = c - a;
                double t = (ca.X * s.Y - ca.Y * s.X) / cross, u = (ca.X * r.Y - ca.Y * r.X) / cross;
                if (t > 0 && t < 1 && u > 0 && u < 1)
                    cuts.Add(a.X + t * r.X);
            }
        var xs = cuts.Distinct().Order().ToArray();
        var result = new List<Point[]>();
        for (int k = 0; k < xs.Length - 1; k++)
        {
            double left = xs[k], right = xs[k + 1], mid = (left + right) / 2;
            var edges = new List<(Point A, Point B)>();
            foreach (var p in loops)
                for (int i = 0; i < p.Count; i++)
                {
                    var a = p[i];
                    var b = p[(i + 1) % p.Count];
                    if (Math.Min(a.X, b.X) < mid && Math.Max(a.X, b.X) > mid)
                        edges.Add((a, b));
                }
            double Y((Point A, Point B) e, double x) => e.A.Y + (e.B.Y - e.A.Y) * (x - e.A.X) / (e.B.X - e.A.X);
            edges.Sort((a, b) => Y(a, mid).CompareTo(Y(b, mid)));
            for (int i = 0; i < edges.Count - 1; i++)
            {
                var q = new Point(mid, (Y(edges[i], mid) + Y(edges[i + 1], mid)) / 2);
                if (!Contains(outline, q) || hs.Any(h => Contains(h, q)))
                    continue;
                result.Add([new(left, Y(edges[i], left)), new(right, Y(edges[i], right)), new(right, Y(edges[i + 1], right)), new(left, Y(edges[i + 1], left))]);
            }
        }
        return result;
    }
}
