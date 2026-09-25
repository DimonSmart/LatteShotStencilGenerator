using System.Text;
using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class StencilConfigurationStateTests
{
    [Fact]
    public void Reference_is_selected_by_default_and_edits_create_an_independent_custom_copy()
    {
        var original = StencilPreset.ReferenceDonut;
        var state = new StencilConfigurationState();

        state.Edit(preset => preset with { CardWidth = 100 });

        Assert.Equal(StencilConfigurationState.CustomName, state.SelectedPresetName);
        Assert.Equal(100, state.ActivePreset.CardWidth);
        Assert.Equal(88.345, StencilPreset.ReferenceDonut.CardWidth);
        Assert.Same(original, StencilPreset.ReferenceDonut);
    }

    [Fact]
    public void Reselecting_or_resetting_reference_restores_the_exact_reference_configuration()
    {
        var state = new StencilConfigurationState();
        state.Edit(preset => preset with { CardWidth = 100, ArtworkPadding = 4 });

        state.SelectReference();

        Assert.Same(StencilPreset.ReferenceDonut, state.ActivePreset);
        Assert.Equal(StencilPreset.ReferenceDonut.Name, state.SelectedPresetName);

        state.SelectCustom();
        Assert.Equal(100, state.ActivePreset.CardWidth);

        state.ResetToReference();
        Assert.Same(StencilPreset.ReferenceDonut, state.ActivePreset);
        state.SelectCustom();
        Assert.Equal(StencilPreset.ReferenceDonut with { Name = StencilConfigurationState.CustomName }, state.ActivePreset);
    }

    [Fact]
    public void Invalid_dimensions_containment_overlap_and_padding_are_actionable()
    {
        var state = new StencilConfigurationState();
        state.Edit(preset => preset with
        {
            CardWidth = double.NaN,
            BaseThickness = 0,
            WorkingArea = new RectMm(5, 5, 80, 40),
            CaptionArea = new RectMm(10, 10, 20, 20),
            ArtworkPadding = 25
        });

        Assert.False(state.IsValid);
        Assert.Contains(state.ValidationErrors, error => error.Contains("Card width"));
        Assert.Contains(state.ValidationErrors, error => error.Contains("Base thickness"));
        Assert.Contains(state.ValidationErrors, error => error.Contains("must not overlap"));
        Assert.Contains(state.ValidationErrors, error => error.Contains("positive artwork area"));
    }

    [Fact]
    public void Invalid_configuration_blocks_geometry_and_stl_export()
    {
        var invalid = StencilPreset.ReferenceDonut with
        {
            Name = StencilConfigurationState.CustomName,
            CaptionArea = new RectMm(10, 40, 20, 20)
        };

        var geometry = CardGeometry.Create(invalid);
        var export = StlExportGenerator.Generate(geometry, "art.svg", "ART");

        Assert.False(geometry.IsExportable);
        Assert.Contains("must not overlap", geometry.ValidationMessage);
        Assert.False(export.IsSuccess);
        Assert.Contains("must not overlap", export.Error);
    }

    [Fact]
    public void Applying_valid_configuration_refits_imported_artwork()
    {
        var workspace = new SvgUploadWorkspace(new SvgImportAdapter(), new BundledCaptionFontOutlineAdapter(new ClipperPolygonEngine()), StencilPreset.ReferenceDonut);
        Assert.True(workspace.TryImport("wide.svg", Encoding.UTF8.GetBytes("<svg><rect width='200' height='100'/></svg>")));
        var originalBounds = workspace.Placement!.PlacedBounds;
        var custom = StencilPreset.ReferenceDonut with
        {
            Name = StencilConfigurationState.CustomName,
            WorkingArea = new RectMm(10, 40, 50, 50),
            CaptionArea = new RectMm(10, 5, 50, 30),
            ArtworkPadding = 5
        };

        workspace.ApplyPreset(custom);

        Assert.NotEqual(originalBounds, workspace.Placement!.PlacedBounds);
        Assert.Equal(custom.WorkingArea, workspace.Placement.WorkingArea);
        Assert.True(workspace.Placement.IsWithinWorkingArea);
        Assert.Equal(40, workspace.Placement.PlacedBounds.Width, 6);
    }

    [Fact]
    public void Geometry_and_export_use_the_active_custom_configuration()
    {
        var state = new StencilConfigurationState();
        state.Edit(preset => preset with
        {
            CardWidth = 96,
            CardHeight = 120,
            WorkingArea = new RectMm(8, 36, 80, 76),
            CaptionArea = new RectMm(8, 4, 80, 28),
            BaseThickness = 1.1,
            RaisedLayerThickness = 0.25
        });
        Assert.True(state.IsValid, string.Join(" ", state.ValidationErrors));

        var geometry = CardGeometry.Create(state.ActivePreset);
        var mesh = CardMeshGenerator.Generate(geometry);
        var export = StlExportGenerator.Generate(geometry, "art.svg", string.Empty);
        var vertices = mesh.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();

        Assert.Equal(96f, vertices.Max(vertex => vertex.X));
        Assert.Equal(120f, vertices.Max(vertex => vertex.Y));
        Assert.Equal(1.35f, vertices.Max(vertex => vertex.Z), 3);
        Assert.True(export.IsSuccess, export.Error);
    }
}
