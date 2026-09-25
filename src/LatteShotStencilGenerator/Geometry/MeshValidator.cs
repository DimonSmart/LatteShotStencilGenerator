using System.Globalization;
using System.Numerics;

namespace LatteShotStencilGenerator.Geometry;

public sealed record MeshValidationResult(bool IsValid, string Message)
{
    public static MeshValidationResult Valid { get; } = new(true, "Mesh is watertight and manifold.");
}

public static class MeshValidator
{
    public static MeshValidationResult Validate(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Triangles.Count == 0) return new(false, "Mesh has no triangles.");

        var edges = new Dictionary<(string First, string Second), (int Count, int Direction)>();
        foreach (var triangle in mesh.Triangles)
        {
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C))
                return new(false, "Mesh has invalid coordinates.");
            if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() <= 1e-12f)
                return new(false, "Mesh has a zero-area triangle.");

            AddEdge(edges, triangle.A, triangle.B);
            AddEdge(edges, triangle.B, triangle.C);
            AddEdge(edges, triangle.C, triangle.A);
        }

        var invalidEdges = edges
            .Where(edge => edge.Value.Count != 2 || edge.Value.Direction != 0)
            .ToArray();
        if (invalidEdges.Length == 0)
            return MeshValidationResult.Valid;

        var openOrNonManifold = invalidEdges.Count(edge => edge.Value.Count != 2);
        var inconsistentWinding = invalidEdges.Count(edge => edge.Value.Count == 2 && edge.Value.Direction != 0);
        var samples = string.Join("; ", invalidEdges.Take(4).Select(edge =>
            $"{edge.Key.First} -> {edge.Key.Second} (count={edge.Value.Count}, direction={edge.Value.Direction})"));
        return new(false,
            $"Mesh has {invalidEdges.Length:N0} invalid edge(s): {openOrNonManifold:N0} open/non-manifold and {inconsistentWinding:N0} inconsistently wound. Sample: {samples}");
    }

    private static bool IsFinite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private static void AddEdge(IDictionary<(string First, string Second), (int Count, int Direction)> edges, Vector3 start, Vector3 end)
    {
        var first = VertexKey(start);
        var second = VertexKey(end);
        var forwards = string.CompareOrdinal(first, second) <= 0;
        var edge = forwards ? (first, second) : (second, first);
        var direction = forwards ? 1 : -1;
        edges[edge] = edges.TryGetValue(edge, out var value)
            ? (value.Count + 1, value.Direction + direction)
            : (1, direction);
    }

    private static string VertexKey(Vector3 point) => string.Create(CultureInfo.InvariantCulture, $"{point.X:R},{point.Y:R},{point.Z:R}");

}
