using System.Text;
using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class GeometryLimitTests
{
    [Fact]
    public void Reference_preset_has_valid_default_geometry_limits()
    {
        var preset = StencilPreset.ReferenceDonut;

        Assert.True(preset.MaximumFlattenedSvgSegments > 0);
        Assert.True(preset.MaximumMeshTriangles > 0);
        Assert.Empty(StencilPresetValidator.Validate(preset));
        Assert.NotEmpty(CardMeshGenerator.Generate(CardGeometry.Create(preset)).Triangles);
    }

    [Fact]
    public void Flattened_segment_limit_fails_deterministically_during_import()
    {
        var adapter = new SvgImportAdapter();
        var svg = Encoding.UTF8.GetBytes("<svg><rect width='1' height='1'/></svg>");

        var first = adapter.Import("square.svg", svg, 3);
        var second = adapter.Import("square.svg", svg, 3);

        Assert.False(first.IsSuccess);
        Assert.Equal(first.Error!.Message, second.Error!.Message);
        Assert.Contains("flattened segment limit of 3", first.Error.Message);
    }

    [Fact]
    public void Custom_limit_persists_and_invalidates_previously_imported_artwork()
    {
        var state = new StencilConfigurationState();
        state.Edit(preset => preset with { MaximumFlattenedSvgSegments = 3, MaximumMeshTriangles = 17 });
        state.SelectReference();
        state.SelectCustom();

        Assert.Equal(3, state.ActivePreset.MaximumFlattenedSvgSegments);
        Assert.Equal(17, state.ActivePreset.MaximumMeshTriangles);

        var workspace = new SvgUploadWorkspace(new SvgImportAdapter(), new BundledCaptionFontOutlineAdapter(), StencilPreset.ReferenceDonut);
        Assert.True(workspace.TryImport("square.svg", Encoding.UTF8.GetBytes("<svg><rect width='1' height='1'/></svg>")));
        workspace.ApplyPreset(state.ActivePreset);

        Assert.Contains("flattened segments", workspace.ImportError);
    }

    [Fact]
    public void Mesh_triangle_limit_blocks_preview_generation_and_stl_export()
    {
        var preset = StencilPreset.ReferenceDonut with { MaximumMeshTriangles = 1 };
        var geometry = CardGeometry.Create(preset);

        var exception = Assert.ThrowsAny<InvalidOperationException>(() => CardMeshGenerator.Generate(geometry));
        var export = StlExportGenerator.Generate(geometry, "art.svg", "ART");

        Assert.Contains("triangle limit of 1", exception.Message);
        Assert.False(export.IsSuccess);
        Assert.Contains("triangle limit of 1", export.Error);
    }
}
