using System.Numerics;
using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class MeshPreviewModelTests
{
    [Fact]
    public void Create_IndexesTheExactTriangleVerticesInStableFirstSeenOrder()
    {
        var mesh = new Mesh([
            new Triangle(new Vector3(1, 2, 3), new Vector3(4, 2, 3), new Vector3(4, 6, 3)),
            new Triangle(new Vector3(1, 2, 3), new Vector3(4, 6, 3), new Vector3(1, 6, 3))
        ]);

        var preview = MeshPreviewModel.Create(mesh);

        Assert.Equal(new float[] { 1, 2, 3, 4, 2, 3, 4, 6, 3, 1, 6, 3 }, preview.Positions);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, preview.TriangleIndices);
        Assert.Equal(new MeshPreviewBounds(1, 2, 3, 4, 6, 3), preview.Bounds);
    }

    [Fact]
    public void Create_RejectsEmptyAndInvalidMeshes()
    {
        Assert.Throws<ArgumentException>(() => MeshPreviewModel.Create(new Mesh([])));
        var invalid = new Mesh([new Triangle(Vector3.Zero, new Vector3(float.NaN, 1, 0), Vector3.One)]);
        Assert.Throws<ArgumentException>(() => MeshPreviewModel.Create(invalid));
    }

    [Fact]
    public void Create_RepresentsEveryGeneratedMeshTriangle()
    {
        var mesh = CardMeshGenerator.Generate(CardGeometry.Create(StencilPreset.ReferenceDonut));

        var preview = MeshPreviewModel.Create(mesh);

        Assert.Equal(mesh.Triangles.Count * 3, preview.TriangleIndices.Count);
        for (var triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
        {
            var source = mesh.Triangles[triangleIndex];
            Assert.Equal(source.A, Vertex(preview, preview.TriangleIndices[triangleIndex * 3]));
            Assert.Equal(source.B, Vertex(preview, preview.TriangleIndices[triangleIndex * 3 + 1]));
            Assert.Equal(source.C, Vertex(preview, preview.TriangleIndices[triangleIndex * 3 + 2]));
        }
    }

    private static Vector3 Vertex(MeshPreviewModel preview, int index) => new(
        preview.Positions[index * 3], preview.Positions[index * 3 + 1], preview.Positions[index * 3 + 2]);
}
