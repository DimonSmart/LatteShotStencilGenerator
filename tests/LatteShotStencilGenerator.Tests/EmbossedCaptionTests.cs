using System.Text;
using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class EmbossedCaptionTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;
    private static readonly BundledCaptionFontOutlineAdapter Fonts = new();

    [Fact]
    public void Import_defaults_caption_to_uppercase_filename_and_persists_font_selection()
    {
        var workspace = new SvgUploadWorkspace(new SvgImportAdapter(), Fonts, Preset);

        Assert.True(workspace.TryImport("donut.svg", Encoding.UTF8.GetBytes("<svg><rect width='1' height='1'/></svg>")));
        workspace.SetCaptionFont(CaptionFont.Wide);
        workspace.SetCaption("custom");

        Assert.Equal("DONUT", workspace.CaptionDefault);
        Assert.Equal("custom", workspace.Caption.Text);
        Assert.Equal(CaptionFont.Wide, workspace.Caption.Font);
    }

    [Fact]
    public void Empty_caption_adds_no_geometry()
    {
        var caption = EmbossedCaption.Create(Preset, new CaptionSettings("", CaptionFont.Block), Fonts);
        var withoutCaption = CardMeshGenerator.Generate(CardGeometry.Create(Preset));
        var withCaption = CardMeshGenerator.Generate(CardGeometry.Create(Preset, StencilArtwork.Empty, caption));

        Assert.Null(caption);
        Assert.Equal(withoutCaption.Triangles, withCaption.Triangles);
    }

    [Fact]
    public void Caption_is_centred_and_shrinks_to_caption_width()
    {
        var caption = Assert.IsType<EmbossedCaption>(EmbossedCaption.Create(Preset, new CaptionSettings(new string('A', 40), CaptionFont.Block), Fonts));
        var points = caption.Contours.SelectMany(contour => contour.Points).ToArray();
        var left = points.Min(point => point.X); var right = points.Max(point => point.X);
        var top = points.Min(point => point.Y); var bottom = points.Max(point => point.Y);

        Assert.InRange(left, Preset.CaptionArea.X - .0001, Preset.CaptionArea.Right);
        Assert.InRange(right, Preset.CaptionArea.X, Preset.CaptionArea.Right + .0001);
        Assert.InRange((left + right) / 2, Preset.CaptionArea.X + Preset.CaptionArea.Width / 2 - .0001, Preset.CaptionArea.X + Preset.CaptionArea.Width / 2 + .0001);
        Assert.InRange((top + bottom) / 2, Preset.CaptionArea.Y + Preset.CaptionArea.Height / 2 - .0001, Preset.CaptionArea.Y + Preset.CaptionArea.Height / 2 + .0001);
        Assert.True(bottom - top <= EmbossedCaption.TargetHeight + .0001);
    }

    [Fact]
    public void Bundled_font_outlines_are_closed_and_deterministic()
    {
        var first = Fonts.GetOutlines("DONUT", CaptionFont.Block);
        var second = Fonts.GetOutlines("DONUT", CaptionFont.Block);

        Assert.NotEmpty(first.Contours);
        Assert.All(first.Contours, contour => Assert.Equal(contour.Points[0], contour.Points[^1]));
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Contours.SelectMany(contour => contour.Points), second.Contours.SelectMany(contour => contour.Points));
    }

    [Fact]
    public void Caption_mesh_reaches_expected_raised_z_level()
    {
        var caption = Assert.IsType<EmbossedCaption>(EmbossedCaption.Create(Preset, new CaptionSettings("DONUT", CaptionFont.Block), Fonts));
        var mesh = CardMeshGenerator.Generate(CardGeometry.Create(Preset, StencilArtwork.Empty, caption));

        Assert.InRange(mesh.Triangles.SelectMany(triangle => new[] { triangle.A.Z, triangle.B.Z, triangle.C.Z }).Max(), 1.5519f, 1.5521f);
    }
}
