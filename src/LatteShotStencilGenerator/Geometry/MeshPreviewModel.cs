using System.Numerics;

namespace LatteShotStencilGenerator.Geometry;

/// <summary>Browser-neutral, indexed view of the exact triangles rendered and exported by the workspace.</summary>
public sealed record MeshPreviewModel(
    IReadOnlyList<float> Positions,
    IReadOnlyList<int> TriangleIndices,
    MeshPreviewBounds Bounds)
{
    public static MeshPreviewModel Create(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Triangles.Count == 0)
            throw new ArgumentException("A preview mesh must contain at least one triangle.", nameof(mesh));

        var vertices = new List<Vector3>();
        var vertexIndices = new Dictionary<Vector3, int>();
        var indices = new List<int>(mesh.Triangles.Count * 3);

        foreach (var triangle in mesh.Triangles)
        {
            AddVertex(triangle.A, vertices, vertexIndices, indices);
            AddVertex(triangle.B, vertices, vertexIndices, indices);
            AddVertex(triangle.C, vertices, vertexIndices, indices);
        }

        var positions = vertices.SelectMany(vertex => new[] { vertex.X, vertex.Y, vertex.Z }).ToArray();
        var bounds = new MeshPreviewBounds(
            vertices.Min(vertex => vertex.X), vertices.Min(vertex => vertex.Y), vertices.Min(vertex => vertex.Z),
            vertices.Max(vertex => vertex.X), vertices.Max(vertex => vertex.Y), vertices.Max(vertex => vertex.Z));

        return new MeshPreviewModel(positions, indices, bounds);
    }

    private static void AddVertex(
        Vector3 vertex,
        ICollection<Vector3> vertices,
        IDictionary<Vector3, int> vertexIndices,
        ICollection<int> indices)
    {
        if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            throw new ArgumentException("A preview mesh cannot contain invalid coordinates.");

        if (!vertexIndices.TryGetValue(vertex, out var index))
        {
            index = vertices.Count;
            vertices.Add(vertex);
            vertexIndices.Add(vertex, index);
        }

        indices.Add(index);
    }
}

public readonly record struct MeshPreviewBounds(
    float MinimumX,
    float MinimumY,
    float MinimumZ,
    float MaximumX,
    float MaximumY,
    float MaximumZ);
