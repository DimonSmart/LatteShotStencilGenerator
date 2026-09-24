using System.Numerics;

namespace LatteShotStencilGenerator.Geometry;

public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C)
{
    public Vector3 Normal => Vector3.Normalize(Vector3.Cross(B - A, C - A));
}

public sealed record Mesh(IReadOnlyList<Triangle> Triangles);
