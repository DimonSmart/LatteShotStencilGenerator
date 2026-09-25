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
        var triangles = new LimitedTriangleCollection(p.MaximumMeshTriangles);
        IReadOnlyList<BoundaryEdge> captionBaseBoundary = [];

        AddBaseLayer(triangles, geometry, z0, low);

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

        AddWorkingArea(triangles, geometry, low);
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

        return new Mesh(ConformTriangleEdges(triangles.Items, p.MaximumMeshTriangles).Select(RotateToLowestVertex).ToArray());
    }

    private static Triangle RotateToLowestVertex(Triangle triangle)
    {
        var vertices = new[] { triangle.A, triangle.B, triangle.C };
        var first = Enumerable.Range(0, vertices.Length)
            .OrderBy(index => vertices[index].X)
            .ThenBy(index => vertices[index].Y)
            .ThenBy(index => vertices[index].Z)
            .First();
        return first switch
        {
            1 => new Triangle(triangle.B, triangle.C, triangle.A),
            2 => new Triangle(triangle.C, triangle.A, triangle.B),
            _ => triangle
        };
    }

    private static IReadOnlyList<Triangle> ConformTriangleEdges(IReadOnlyList<Triangle> triangles, int maximumTriangles)
    {
        var vertices = triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();
        var result = new LimitedTriangleCollection(maximumTriangles);

        foreach (var triangle in triangles)
        {
            var boundary = EdgePoints3D(triangle.A, triangle.B, vertices)
                .Concat(EdgePoints3D(triangle.B, triangle.C, vertices))
                .Concat(EdgePoints3D(triangle.C, triangle.A, vertices))
                .ToArray();
            if (boundary.Length == 3)
            {
                result.Add(triangle);
                continue;
            }

            var centre = (triangle.A + triangle.B + triangle.C) / 3f;
            for (var index = 0; index < boundary.Length; index++)
            {
                var split = new Triangle(centre, boundary[index], boundary[(index + 1) % boundary.Length]);
                if (Vector3.Cross(split.B - split.A, split.C - split.A).LengthSquared() > 1e-12f)
                    result.Add(split);
            }
        }
        return result.Items;
    }

    private static IEnumerable<Vector3> EdgePoints3D(Vector3 start, Vector3 end, IReadOnlyList<Vector3> vertices)
    {
        var edge = end - start;
        var lengthSquared = edge.LengthSquared();
        yield return start;
        if (lengthSquared <= 1e-12f) yield break;

        foreach (var candidate in vertices
                     .Where(candidate => candidate != start && candidate != end)
                     .Select(candidate => (Point: candidate, T: Vector3.Dot(candidate - start, edge) / lengthSquared))
                     .Where(candidate => candidate.T > 0f && candidate.T < 1f &&
                                         Vector3.Cross(edge, candidate.Point - start).Length() <= 0.00001f * MathF.Max(1f, edge.Length()))
                     .OrderBy(candidate => candidate.T))
        {
            yield return candidate.Point;
        }
    }

    private static IReadOnlyList<BoundaryEdge> AddCaption(ICollection<Triangle> triangles, EmbossedCaption? caption)
    {
        if (caption is null) return [];

        var tess = new Tess();
        AddCanonicalRegions(tess, caption.Regions, reverse: false);
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
        AddCanonicalRegions(tess, caption.Regions, reverse: true);
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
                throw new InvalidOperationException("Caption top and base boundaries do not describe the same contours.");
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
        var topLength = topDistances[^1];
        var bottomLength = bottomDistances[^1];
        if (MathF.Abs(topLength - bottomLength) > 0.001f)
            throw new InvalidOperationException("Caption top and base boundary lengths differ.");

        var ti = 0;
        var bi = 0;
        while (ti < top.Count && bi < bottom.Count)
        {
            var topNext = topDistances[ti + 1] / topLength;
            var bottomNext = bottomDistances[bi + 1] / bottomLength;

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

        if (ti != top.Count || bi != bottom.Count)
            throw new InvalidOperationException("Caption wall boundary subdivision could not be stitched.");
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

    private static void AddWorkingArea(ICollection<Triangle> triangles, CardGeometry geometry, float top)
    {
        if (!geometry.IsExportable) return;

        if (geometry.ResolvedTopology.OpeningRegions.Count == 0)
        {
            var w = geometry.WorkingArea;
            AddQuad(triangles, V((float)w.X, (float)w.Y, top), V((float)w.Right, (float)w.Y, top),
                V((float)w.Right, (float)w.Bottom, top), V((float)w.X, (float)w.Bottom, top));
            return;
        }

        var tess = new Tess();
        tess.AddContour(ToVertices(Rectangle(geometry.WorkingArea)));

        AddCanonicalRegions(tess, geometry.ResolvedTopology.OpeningRegions, reverse: true);

        foreach (var triangle in TessellateTop(tess, top))
            triangles.Add(triangle);

    }

    private static void AddBaseLayer(ICollection<Triangle> triangles, CardGeometry geometry, float bottom, float wallTop)
    {
        var tess = new Tess();
        tess.AddContour(ToVertices(Rectangle(geometry.CardBoundary)));

        AddCanonicalRegions(tess, geometry.ResolvedTopology.OpeningRegions, reverse: true);

        var boundary = new Dictionary<(string First, string Second), (Vector3 Start, Vector3 End, int Count)>();
        foreach (var topTriangle in TessellateTop(tess, wallTop))
        {
            triangles.Add(new Triangle(ToZ(topTriangle.C, bottom), ToZ(topTriangle.B, bottom), ToZ(topTriangle.A, bottom)));
            AddBoundary(boundary, topTriangle.A, topTriangle.B);
            AddBoundary(boundary, topTriangle.B, topTriangle.C);
            AddBoundary(boundary, topTriangle.C, topTriangle.A);
        }

        foreach (var edge in boundary.Values.Where(edge => edge.Count == 1 && !IsRectangleEdge(edge.Start, edge.End, geometry.CardBoundary)))
            AddQuad(triangles, edge.End, edge.Start, ToZ(edge.Start, bottom), ToZ(edge.End, bottom));
    }


    private static IEnumerable<PointMm> Rectangle(RectMm rectangle) =>
        [new(rectangle.X, rectangle.Y), new(rectangle.Right, rectangle.Y), new(rectangle.Right, rectangle.Bottom), new(rectangle.X, rectangle.Bottom)];

    private static void AddCanonicalRegions(Tess tess, IReadOnlyList<PlanarPolygon> polygons, bool reverse)
    {
        foreach (var polygon in polygons)
        {
            tess.AddContour(ToVertices(reverse ? polygon.Outer.Reverse() : polygon.Outer));
            foreach (var hole in polygon.Holes)
                tess.AddContour(ToVertices(reverse ? hole.Reverse() : hole));
        }
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

    private static bool Same(float actual, double expected) => MathF.Abs(actual - (float)expected) < 0.0001f;
    private static bool Same(float first, float second) => MathF.Abs(first - second) < 0.0001f;

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static void AddQuad(ICollection<Triangle> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        triangles.Add(new Triangle(a, b, c));
        triangles.Add(new Triangle(a, c, d));
    }

    private sealed class LimitedTriangleCollection(int maximumTriangles) : ICollection<Triangle>
    {
        private readonly List<Triangle> _items = [];

        public IReadOnlyList<Triangle> Items => _items;
        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public void Add(Triangle item)
        {
            if (_items.Count >= maximumTriangles)
                throw new GeometryLimitExceededException($"Generated mesh exceeds the configured triangle limit of {maximumTriangles:N0}. Simplify the artwork, caption, or bridges, or raise the limit.");
            _items.Add(item);
        }

        public void Clear() => _items.Clear();
        public bool Contains(Triangle item) => _items.Contains(item);
        public void CopyTo(Triangle[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public bool Remove(Triangle item) => _items.Remove(item);
        public IEnumerator<Triangle> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GeometryLimitExceededException(string message) : InvalidOperationException(message);
}
