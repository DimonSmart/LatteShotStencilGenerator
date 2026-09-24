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
                AddCaptionAreaTop(
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
        AddCaption(triangles, geometry.Caption, high);

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

    private static void AddCaption(ICollection<Triangle> triangles, EmbossedCaption? caption, float baseZ)
    {
        if (caption is null) return;

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

        foreach (var edge in boundary.Values.Where(edge => edge.Count == 1))
            AddQuad(triangles, edge.End, edge.Start, ToZ(edge.Start, baseZ), ToZ(edge.End, baseZ));
    }

    private static void AddCaptionAreaTop(ICollection<Triangle> triangles, RectMm topArea, EmbossedCaption caption, float z)
    {
        var tess = new Tess();
        tess.AddContour(ToVertices(Rectangle(topArea)));
        AddCaptionContours(tess, caption, reverse: true);
        foreach (var triangle in TessellateTop(tess, z))
            triangles.Add(triangle);
    }

    private static void AddCaptionContours(Tess tess, EmbossedCaption caption, bool reverse)
    {
        foreach (var contour in caption.Contours)
            tess.AddContour(ToVertices(NormalizeWinding(contour, caption.Contours, reverse)));
    }

    private static IEnumerable<Triangle> TessellateTop(Tess tess, float z)
    {
        tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);
        for (var i = 0; i < tess.ElementCount; i++)
        {
            var element = tess.Elements[(i * 3)..((i + 1) * 3)];
            if (element.Any(index => index == Tess.Undef)) continue;

            var a = ToVector(tess.Vertices[element[0]].Position, z);
            var b = ToVector(tess.Vertices[element[1]].Position, z);
            var c = ToVector(tess.Vertices[element[2]].Position, z);
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() <= 1e-12f) continue;
            yield return cross.Z >= 0 ? new Triangle(a, b, c) : new Triangle(a, c, b);
        }
    }

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

    private static bool IsWorkingAreaEdge((Vector3 Start, Vector3 End, int Count) edge, RectMm area) =>
        (Same(edge.Start.X, area.X) && Same(edge.End.X, area.X)) ||
        (Same(edge.Start.X, area.Right) && Same(edge.End.X, area.Right)) ||
        (Same(edge.Start.Y, area.Y) && Same(edge.End.Y, area.Y)) ||
        (Same(edge.Start.Y, area.Bottom) && Same(edge.End.Y, area.Bottom));

    private static bool Same(float actual, double expected) => MathF.Abs(actual - (float)expected) < 0.0001f;

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static void AddQuad(ICollection<Triangle> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        triangles.Add(new Triangle(a, b, c));
        triangles.Add(new Triangle(a, c, d));
    }
}
