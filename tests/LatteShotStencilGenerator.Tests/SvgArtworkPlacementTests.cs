using System.Text;
using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class SvgArtworkPlacementTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;
    private readonly SvgImportAdapter _adapter = new();

    [Fact]
    public void Fit_contains_and_centres_wide_artwork_inside_padded_area()
    {
        var placement = Fit("<svg><rect width='200' height='100'/></svg>");

        Assert.Equal(100, placement.ScalePercent);
        Assert.InRange(placement.PlacedBounds.Width, 68.988999, 68.989001);
        Assert.InRange(placement.PlacedBounds.Height, 34.494499, 34.494501);
        Assert.InRange(placement.PlacedBounds.X, 9.677999, 9.678001);
        Assert.InRange(placement.PlacedBounds.Y, 53.276249, 53.276251);
        Assert.True(placement.IsWithinWorkingArea);
    }

    [Fact]
    public void Fit_preserves_aspect_ratio_and_scales_from_source_bounds()
    {
        var placement = Fit("<svg><rect x='10' y='20' width='20' height='80'/></svg>");

        Assert.InRange(placement.PlacedBounds.Height / placement.PlacedBounds.Width, 3.999999, 4.000001);
        Assert.True(placement.IsWithinWorkingArea);
    }

    [Fact]
    public void Reset_restores_contained_default_after_adjustments()
    {
        var placement = Fit("<svg><rect width='10' height='10'/></svg>").WithAdjustments(150, 8, -4);

        var reset = placement.Reset();

        Assert.Equal(100, reset.ScalePercent);
        Assert.Equal(0, reset.OffsetX);
        Assert.Equal(0, reset.OffsetY);
        Assert.True(reset.IsWithinWorkingArea);
    }

    [Fact]
    public void Reports_validation_error_when_adjustment_leaves_working_area()
    {
        var placement = Fit("<svg><rect width='10' height='10'/></svg>").WithAdjustments(100, 4, 0);

        Assert.False(placement.IsWithinWorkingArea);
        Assert.Contains("extends outside the working area", placement.ValidationMessage);
    }

    [Fact]
    public void Workspace_retains_successful_import_and_reports_later_error()
    {
        var workspace = new SvgUploadWorkspace(_adapter, new BundledCaptionFontOutlineAdapter(), Preset);

        Assert.True(workspace.TryImport("leaf.svg", Encoding.UTF8.GetBytes("<svg><rect width='10' height='20'/></svg>")));
        Assert.Equal("LEAF", workspace.CaptionDefault);
        var successfulPlacement = workspace.Placement;

        Assert.False(workspace.TryImport("bad.svg", Encoding.UTF8.GetBytes("<svg><path d='M0 0 L1 1' stroke='black' fill='none'/></svg>")));

        Assert.NotNull(workspace.ImportedArtwork);
        Assert.Same(successfulPlacement, workspace.Placement);
        Assert.Contains("Convert strokes to filled paths", workspace.ImportError);
    }

    private SvgArtworkPlacement Fit(string svg)
    {
        var result = Assert.IsType<SvgImportResult>(_adapter.Import("art.svg", Encoding.UTF8.GetBytes(svg)).Value);
        return SvgArtworkPlacement.Fit(result, Preset.WorkingArea, Preset.ArtworkPadding);
    }
}
