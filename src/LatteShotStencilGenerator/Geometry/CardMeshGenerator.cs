using System.Numerics;
using LibTessDotNet;

namespace LatteShotStencilGenerator.Geometry;

/// <summary>Builds the closed, two-level solid represented by <see cref="CardGeometry"/>.</summary>
public static class CardMeshGenerator
{
    public static Mesh Generate(CardGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!geometry.IsExportable) return new Mesh([]);
        var p = geometry.Preset;
        var w = geometry.WorkingArea;
        var z0 = 0f;
        var low = (float)p.BaseThickness;
        var high = (float)p.RaisedSurfaceZ;
        var x = new[] { 0f, (float)w.X, (float)w.Right, (float)p.CardWidth };
        var y = new[] { 0f, (float)w.Y, (float)w.Bottom, (float)p.CardHeight };
        var triangles = new List<Triangle>();
        IReadOnlyList<BoundaryEdge> captionBaseBoundary = [];

        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            if (row == 1 && column == 1) continue;
            AddQuad(triangles, V(x[column], y[row], z0), V(x[column], y[row + 1], z0),
                V(x[column + 1], y[row + 1], z0), V(x[column + 1], y[row], z0));
        }

        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            if (row == 1 && column == 1) continue;
            if (row == 0 && column == 1 && geometry.Caption is not null)
            {
                captionBaseBoundary = AddCaptionAreaTop(
                    triangles,
                    new RectMm(x[column], y[row], x[column + 1] - x[column], y[row + 1] - y[row]),
                    geometry.Caption,
                    high);
                continue;
            }
            var z = row == 1 && column == 1 ? low : high;
            AddQuad(triangles, V(x[column], y[row], z), V(x[column + 1], y[row], z),
                V(x[column + 1], y[row + 1], z), V(x[column], y[row + 1], z));
        }

        AddWorkingArea(triangles, geometry, z0, low);
        var captionTopBoundary = AddCaption(triangles, geometry.Caption);
        if (geometry.Caption is not null)
            AddCaptionWalls(triangles, captionBaseBoundary, captionTopBoundary);

        // Outer walls are segmented to share the same topology as the top surface.
        for (var i = 0; i < 3; i++)
        {
            AddQuad(triangles, V(x[i], 0, z0), V(x[i + 1], 0, z0), V(x[i + 1], 0, high), V(x[i], 0, high));
            AddQuad(triangles, V(x[i + 1], y[3], z0), V(x[i], y[3], z0), V(x[i], y[3], high), V(x[i + 1], y[3], high));
            AddQuad(triangles, V(x[3], y[i], z0), V(x[3], y[i + 1], z0), V(x[3], y[i + 1], high), V(x[3], y[i], high));
            AddQuad(triangles, V(0, y[i + 1], z0), V(0, y[i], z0), V(0, y[i], high), V(0, y[i + 1], high));
        }

        // The four sides of the recessed working area.
        AddQuad(triangles, V(x[1], y[1], low), V(x[1], y[2], low), V(x[1], y[2], high), V(x[1], y[1], high));
        AddQuad(triangles, V(x[2], y[2], low), V(x[2], y[1], low), V(x[2], y[1], high), V(x[2], y[2], high));
        AddQuad(triangles, V(x[2], y[1], low), V(x[1], y[1], low), V(x[1], y[1], high), V(x[2], y[1], high));
        AddQuad(triangles, V(x[1], y[2], low), V(x[2], y[2], low), V(x[2], y[2], high), V(x[1], y[2], high));

        return new Mesh(triangles);
    }

    private static IReadOnlyList<BoundaryEdge> AddCaption(ICollection<Triangle> triangles, EmbossedCaption? caption)
    {
        if (caption is null) return [];

        var tess = new Tess();
        AddCaptionContours(tess, caption, reverse: false);
        var boundary = new Dictionary<(string First, string Second), (Vector3 Start, Vector3 End, int Count)>();
        foreach (var triangle in TessellateTop(tess, (float)caption.TopZ))
        {
            triangles.Add(triangle);
            AddBoundary(boundary, triangle.A, triangle.B);
            AddBoundary(boundary, triangle.B, triangle.C);
            AddBoundary(boundary, triangle.C, triangle.A);
        }

        return boundary.Values
            .Where(edge => edge.Count == 1)
            .Select(edge => new BoundaryEdge(edge.Start, edge.End))
            .ToArray();
    }

    private static IReadOnlyList<BoundaryEdge> AddCaptionAreaTop(ICollection<Triangle> triangles, RectMm topArea, EmbossedCaption caption, float z)
    {
        var tess = new Tess();
        tess.AddContour(ToVertices(Rectangle(topArea)));
        AddCaptionContours(tess, caption, reverse: true);
        var boundary = new Dictionary<(string First, string Second), (Vector3 Start, Vector3 End, int Count)>();
        foreach (var triangle in TessellateTop(tess, z))
        {
            triangles.Add(triangle);
            AddBoundary(boundary, triangle.A, triangle.B);
            AddBoundary(boundary, triangle.B, triangle.C);
            AddBoundary(boundary, triangle.C, triangle.A);
        }

        return boundary.Values
            .Where(edge => edge.Count == 1 && !IsRectangleEdge(edge.Start, edge.End, topArea))
            .Select(edge => new BoundaryEdge(edge.Start, edge.End))
            .ToArray();
    }

    private static void AddCaptionWalls(
        ICollection<Triangle> triangles,
        IReadOnlyList<BoundaryEdge> baseBoundary,
        IReadOnlyList<BoundaryEdge> topBoundary)
    {
        var baseLoops = BuildBoundaryLoops(baseBoundary.Select(edge => new BoundaryEdge(edge.End, edge.Start)));
        var topLoops = BuildBoundaryLoops(topBoundary);

        foreach (var topLoop in topLoops)
        {
            var baseLoop = baseLoops.FirstOrDefault(candidate => SameBounds(candidate, topLoop));
            if (baseLoop is null)
            {
                static string Describe(IReadOnlyList<Vector3> loop) =>
                    $"[{loop.Min(point => point.X):R},{loop.Min(point => point.Y):R}..{loop.Max(point => point.X):R},{loop.Max(point => point.Y):R}; n={loop.Count}]";
                throw new InvalidOperationException(
                    $"Caption boundary mismatch. top={Describe(topLoop)}; base={string.Join(", ", baseLoops.Select(Describe))}");
            }
            baseLoops.Remove(baseLoop);

            if (MathF.Sign(LoopArea(topLoop)) != MathF.Sign(LoopArea(baseLoop)))
                baseLoop.Reverse();

            var common = topLoop
                .Select((point, index) => (Key: PointKey(point), Index: index))
                .FirstOrDefault(candidate => baseLoop.Any(point => PointKey(point) == candidate.Key));

            if (common.Key is null)
                throw new InvalidOperationException("Caption boundary loops have no common vertex.");

            RotateTo(topLoop, common.Index);
            RotateTo(baseLoop, baseLoop.FindIndex(point => PointKey(point) == common.Key));
            AddWallStrip(triangles, topLoop, baseLoop);
        }
    }

    private static List<List<Vector3>> BuildBoundaryLoops(IEnumerable<BoundaryEdge> edges)
    {
        var remaining = edges.ToDictionary(edge => PointKey(edge.Start));
        var loops = new List<List<Vector3>>();

        while (remaining.Count > 0)
        {
            var first = remaining.First().Value;
            var loop = new List<Vector3> { first.Start };
            var current = first;

            while (true)
            {
                remaining.Remove(PointKey(current.Start));
                loop.Add(current.End);
                if (PointKey(current.End) == PointKey(first.Start)) break;
                if (!remaining.TryGetValue(PointKey(current.End), out current))
                    throw new InvalidOperationException("Caption boundary is not a closed loop.");
            }

            loop.RemoveAt(loop.Count - 1);
            loops.Add(loop);
        }

        return loops;
    }

    private static void AddWallStrip(ICollection<Triangle> triangles, IReadOnlyList<Vector3> top, IReadOnlyList<Vector3> bottom)
    {
        var topDistances = LoopDistances(top);
        var bottomDistances = LoopDistances(bottom);
        if (MathF.Abs(topDistances[^1] - bottomDistances[^1]) > 0.001f)
            throw new InvalidOperationException("Caption top and base boundary lengths differ.");

        var ti = 0;
        var bi = 0;
        while (ti < top.Count || bi < bottom.Count)
        {
            var topNext = topDistances[ti + 1];
            var bottomNext = bottomDistances[bi + 1];

            if (SameDistance(topNext, bottomNext))
            {
                AddQuad(
                    triangles,
                    top[(ti + 1) % top.Count],
                    top[ti % top.Count],
                    bottom[bi % bottom.Count],
                    bottom[(bi + 1) % bottom.Count]);
                ti++;
                bi++;
            }
            else if (topNext < bottomNext)
            {
                triangles.Add(new Triangle(
                    top[(ti + 1) % top.Count],
                    top[ti % top.Count],
                    bottom[bi % bottom.Count]));
                ti++;
            }
            else
            {
                triangles.Add(new Triangle(
                    top[ti % top.Count],
                    bottom[bi % bottom.Count],
                    bottom[(bi + 1) % bottom.Count]));
                bi++;
            }
        }
    }

    private static float[] LoopDistances(IReadOnlyList<Vector3> loop)
    {
        var distances = new float[loop.Count + 1];
        for (var i = 0; i < loop.Count; i++)
        {
            var next = loop[(i + 1) % loop.Count];
            distances[i + 1] = distances[i] + Vector2.Distance(
                new Vector2(loop[i].X, loop[i].Y),
                new Vector2(next.X, next.Y));
        }
        return distances;
    }

    private static float LoopArea(IReadOnlyList<Vector3> loop) =>
        loop.Select((point, index) =>
        {
            var next = loop[(index + 1) % loop.Count];
            return point.X * next.Y - next.X * point.Y;
        }).Sum() / 2f;

    private static bool SameBounds(IReadOnlyList<Vector3> first, IReadOnlyList<Vector3> second) =>
        Same(first.Min(point => point.X), second.Min(point => point.X)) &&
        Same(first.Min(point => point.Y), second.Min(point => point.Y)) &&
        Same(first.Max(point => point.X), second.Max(point => point.X)) &&
        Same(first.Max(point => point.Y), second.Max(point => point.Y));

    private static void RotateTo<T>(List<T> items, int index)
    {
        if (index <= 0) return;
        var prefix = items.Take(index).ToArray();
        items.RemoveRange(0, index);
        items.AddRange(prefix);
    }

    private static bool SameDistance(float first, float second) => MathF.Abs(first - second) <= 0.0001f;
    private static string PointKey(Vector3 point) => $"{point.X:R},{point.Y:R}";


    private static void AddCaptionContours(Tess tess, EmbossedCaption caption, bool reverse)
    {
        foreach (var contour in caption.Contours)
            tess.AddContour(ToVertices(NormalizeWinding(contour, caption.Contours, reverse)));
    }

    private static IEnumerable<Triangle> TessellateTop(Tess tess, float z)
    {
        tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);
        var raw = new List<Triangle>(tess.ElementCount);
        for (var i = 0; i < tess.ElementCount; i++)
        {
            var element = tess.Elements[(i * 3)..((i + 1) * 3)];
            if (element.Any(index => index == Tess.Undef)) continue;

            var a = ToVector(tess.Vertices[element[0]].Position, z);
            var b = ToVector(tess.Vertices[element[1]].Position, z);
            var c = ToVector(tess.Vertices[element[2]].Position, z);
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() <= 1e-12f) continue;
            raw.Add(cross.Z >= 0 ? new Triangle(a, b, c) : new Triangle(a, c, b));
        }

        // LibTess can return a long edge opposite several collinear shorter edges when
        // multiple aligned contours are tessellated together. Split every triangle edge
        // at all tessellation vertices lying on it so internal edges pair exactly.
        var vertices = raw
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();

        foreach (var triangle in raw)
        {
            var boundary = EdgePoints(triangle.A, triangle.B, vertices)
                .Concat(EdgePoints(triangle.B, triangle.C, vertices))
                .Concat(EdgePoints(triangle.C, triangle.A, vertices))
                .ToArray();
            var centre = (triangle.A + triangle.B + triangle.C) / 3f;

            for (var i = 0; i < boundary.Length; i++)
            {
                var next = boundary[(i + 1) % boundary.Length];
                var split = new Triangle(centre, boundary[i], next);
                if (Vector3.Cross(split.B - split.A, split.C - split.A).LengthSquared() > 1e-12f)
                    yield return split;
            }
        }
    }

    private static IEnumerable<Vector3> EdgePoints(Vector3 start, Vector3 end, IReadOnlyList<Vector3> vertices)
    {
        var edge = new Vector2(end.X - start.X, end.Y - start.Y);
        var lengthSquared = edge.LengthSquared();
        if (lengthSquared <= 1e-12f)
        {
            yield return start;
            yield break;
        }

        yield return start;
        foreach (var candidate in vertices
                     .Where(candidate => !SamePoint(candidate, start) && !SamePoint(candidate, end))
                     .Select(candidate => (Point: candidate, T: SegmentParameter(start, edge, lengthSquared, candidate)))
                     .Where(candidate => candidate.T > 0f && candidate.T < 1f && IsOnSegment(start, edge, candidate.Point))
                     .OrderBy(candidate => candidate.T))
        {
            yield return candidate.Point;
        }
    }

    private static float SegmentParameter(Vector3 start, Vector2 edge, float lengthSquared, Vector3 point) =>
        ((point.X - start.X) * edge.X + (point.Y - start.Y) * edge.Y) / lengthSquared;

    private static bool IsOnSegment(Vector3 start, Vector2 edge, Vector3 point)
    {
        var cross = edge.X * (point.Y - start.Y) - edge.Y * (point.X - start.X);
        return MathF.Abs(cross) <= 0.00001f * MathF.Max(1f, edge.Length());
    }

    private static bool SamePoint(Vector3 first, Vector3 second) =>
        Same(first.X, second.X) && Same(first.Y, second.Y);

    private static void AddWorkingArea(ICollection<Triangle> triangles, CardGeometry geometry, float bottom, float top)
    {
        if (!geometry.IsExportable) return;

        if (geometry.OpeningContours.Count == 0 && !geometry.Artwork.Invert)
        {
            var w = geometry.WorkingArea;
            AddQuad(triangles, V((float)w.X, (float)w.Y, bottom), V((float)w.X, (float)w.Bottom, bottom),
                V((float)w.Right, (float)w.Bottom, bottom), V((float)w.Right, (float)w.Y, bottom));
            AddQuad(triangles, V((float)w.X, (float)w.Y, top), V((float)w.Right, (float)w.Y, top),
                V((float)w.Right, (float)w.Bottom, top), V((float)w.X, (float)w.Bottom, top));
            return;
        }

        var tess = new Tess();
        if (!geometry.Artwork.Invert)
            tess.AddContour(ToVertices(Rectangle(geometry.WorkingArea)));

        foreach (var contour in geometry.OpeningContours)
        {
            // The SVG adapter retains fill rules. Even-odd contours alternate winding by nesting;
            // non-zero contours preserve their source winding. Reversing produces through-openings.
            var reverse = !geometry.Artwork.Invert;
            tess.AddContour(ToVertices(NormalizeWinding(contour, geometry.OpeningContours, reverse)));
        }

        tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);
        var boundary = new Dictionary<(string First, string Second), (Vector3 Start, Vector3 End, int Count)>();
        for (var i = 0; i < tess.ElementCount; i++)
        {
            var element = tess.Elements[(i * 3)..((i + 1) * 3)];
            if (element.Any(index => index == Tess.Undef)) continue;
            var a = ToVector(tess.Vertices[element[0]].Position, top);
            var b = ToVector(tess.Vertices[element[1]].Position, top);
            var c = ToVector(tess.Vertices[element[2]].Position, top);
            triangles.Add(new Triangle(a, b, c));
            triangles.Add(new Triangle(ToZ(c, bottom), ToZ(b, bottom), ToZ(a, bottom)));
            AddBoundary(boundary, a, b);
            AddBoundary(boundary, b, c);
            AddBoundary(boundary, c, a);
        }

        foreach (var edge in boundary.Values.Where(edge => edge.Count == 1 && !IsWorkingAreaEdge(edge, geometry.WorkingArea)))
            AddQuad(triangles, edge.End, edge.Start, ToZ(edge.Start, bottom), ToZ(edge.End, bottom));
    }

    private static IEnumerable<PointMm> Rectangle(RectMm rectangle) =>
        [new(rectangle.X, rectangle.Y), new(rectangle.Right, rectangle.Y), new(rectangle.Right, rectangle.Bottom), new(rectangle.X, rectangle.Bottom)];

    private static IEnumerable<PointMm> NormalizeWinding(StencilContour contour, IReadOnlyList<StencilContour> all, bool reverse)
    {
        var points = contour.Points.Take(contour.Points.Count - 1).ToArray();
        var signedArea = Area(points);
        var nested = all.Count(other => !ReferenceEquals(other, contour) && Contains(other.Points, points[0]));
        var wantCounterClockwise = contour.FillRule == StencilFillRule.EvenOdd ? nested % 2 == 0 : signedArea > 0;
        if (reverse) wantCounterClockwise = !wantCounterClockwise;
        return (signedArea > 0) == wantCounterClockwise ? points : points.Reverse();
    }

    private static double Area(IReadOnlyList<PointMm> points) => points.Zip(points.Skip(1).Append(points[0]), (a, b) => a.X * b.Y - b.X * a.Y).Sum() / 2d;

    private static bool Contains(IReadOnlyList<PointMm> polygon, PointMm point)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count - 1; i++)
        {
            var a = polygon[i]; var b = polygon[i + 1];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static ContourVertex[] ToVertices(IEnumerable<PointMm> points) => points.Select(point => new ContourVertex { Position = new Vec3 { X = (float)point.X, Y = (float)point.Y, Z = 0 } }).ToArray();
    private static Vector3 ToVector(Vec3 point, float z) => new(point.X, point.Y, z);
    private static Vector3 ToZ(Vector3 point, float z) => new(point.X, point.Y, z);

    private static void AddBoundary(IDictionary<(string First, string Second), (Vector3 Start, Vector3 End, int Count)> edges, Vector3 start, Vector3 end)
    {
        var startKey = $"{start.X:R},{start.Y:R}";
        var endKey = $"{end.X:R},{end.Y:R}";
        var key = string.CompareOrdinal(startKey, endKey) < 0 ? (startKey, endKey) : (endKey, startKey);
        edges[key] = edges.TryGetValue(key, out var edge) ? (edge.Start, edge.End, edge.Count + 1) : (start, end, 1);
    }

    private readonly record struct BoundaryEdge(Vector3 Start, Vector3 End);

    private static bool IsRectangleEdge(Vector3 start, Vector3 end, RectMm area) =>
        (Same(start.X, area.X) && Same(end.X, area.X)) ||
        (Same(start.X, area.Right) && Same(end.X, area.Right)) ||
        (Same(start.Y, area.Y) && Same(end.Y, area.Y)) ||
        (Same(start.Y, area.Bottom) && Same(end.Y, area.Bottom));

    private static bool IsWorkingAreaEdge((Vector3 Start, Vector3 End, int Count) edge, RectMm area) =>
        (Same(edge.Start.X, area.X) && Same(edge.End.X, area.X)) ||
        (Same(edge.Start.X, area.Right) && Same(edge.End.X, area.Right)) ||
        (Same(edge.Start.Y, area.Y) && Same(edge.End.Y, area.Y)) ||
        (Same(edge.Start.Y, area.Bottom) && Same(edge.End.Y, area.Bottom));

    private static bool Same(float actual, double expected) => MathF.Abs(actual - (float)expected) < 0.0001f;
    private static bool Same(float first, float second) => MathF.Abs(first - second) < 0.0001f;

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static void AddQuad(ICollection<Triangle> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        triangles.Add(new Triangle(a, b, c));
        triangles.Add(new Triangle(a, c, d));
    }
}
