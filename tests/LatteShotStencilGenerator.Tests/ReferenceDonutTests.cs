using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class ReferenceDonutTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;
    private static readonly CardGeometry Geometry = CardGeometry.Create(Preset);
    private static readonly Mesh Mesh = CardMeshGenerator.Generate(Geometry);

    [Fact]
    public void Reference_card_has_expected_bounding_box()
    {
        var bounds = Bounds(Mesh);
        Assert.Equal(0f, bounds.MinX);
        Assert.Equal(0f, bounds.MinY);
        Assert.Equal(0f, bounds.MinZ);
        Assert.Equal(88.345f, bounds.MaxX, 3);
        Assert.Equal(113.882f, bounds.MaxY, 3);
    }

    [Fact]
    public void Working_area_has_reference_placement()
    {
        Assert.Equal(6.678, Geometry.WorkingArea.X, 3);
        Assert.Equal(33.029, Geometry.WorkingArea.Y, 3);
        Assert.Equal(74.989, Geometry.WorkingArea.Width, 3);
        Assert.Equal(74.989, Geometry.WorkingArea.Height, 3);
    }

    [Fact]
    public void Base_and_raised_surface_have_reference_z_levels()
    {
        var zLevels = Mesh.Triangles.SelectMany(triangle => new[] { triangle.A.Z, triangle.B.Z, triangle.C.Z }).Distinct().Order().ToArray();
        Assert.Equal(new[] { 0f, 0.998f, 1.198f }, zLevels, new FloatToleranceComparer(0.0001f));
    }

    [Fact]
    public void Generated_mesh_is_watertight_and_manifold()
    {
        var validation = MeshValidator.Validate(Mesh);
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void Binary_stl_contains_each_triangle_in_millimetres()
    {
        var bytes = BinaryStlSerializer.Serialize(Mesh);
        Assert.Equal(BinaryStlSerializer.HeaderLength + sizeof(uint) + Mesh.Triangles.Count * BinaryStlSerializer.TriangleRecordLength, bytes.Length);
        Assert.Equal((uint)Mesh.Triangles.Count, BitConverter.ToUInt32(bytes, BinaryStlSerializer.HeaderLength));
        var firstVertexX = BitConverter.ToSingle(bytes, BinaryStlSerializer.HeaderLength + sizeof(uint) + 12);
        Assert.Equal(0f, firstVertexX);
    }

    private static (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Bounds(Mesh mesh)
    {
        var vertices = mesh.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();
        return (vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z), vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
    }

    private sealed class FloatToleranceComparer(float tolerance) : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) <= tolerance;
        public int GetHashCode(float value) => 0;
    }
}
