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

    [Fact]
    public void Non_empty_caption_mesh_is_watertight_and_serializes_to_stl()
    {
        var preset = Preset with { CaptionArea = new RectMm(20, 5, 48, 20) };
        var caption = Assert.IsType<EmbossedCaption>(EmbossedCaption.Create(preset, new CaptionSettings("LATTE", CaptionFont.Block), Fonts));
        var mesh = CardMeshGenerator.Generate(CardGeometry.Create(preset, StencilArtwork.Empty, caption));

        var validation = MeshValidator.Validate(mesh);
        var invalidEdges = FindInvalidEdges(mesh).ToArray();
        Assert.True(validation.IsValid, validation.Message + Environment.NewLine + string.Join(Environment.NewLine, invalidEdges));

        var bytes = BinaryStlSerializer.Serialize(mesh);
        Assert.Equal(BinaryStlSerializer.HeaderLength + sizeof(uint) + mesh.Triangles.Count * BinaryStlSerializer.TriangleRecordLength, bytes.Length);
        Assert.Equal((uint)mesh.Triangles.Count, BitConverter.ToUInt32(bytes, BinaryStlSerializer.HeaderLength));
    }

    [Fact]
    public void Nested_caption_contours_are_tessellated_as_watertight_relief()
    {
        var outer = Contour((30, 8), (55, 8), (55, 26), (30, 26), StencilFillRule.EvenOdd);
        var inner = Contour((36, 13), (49, 13), (49, 21), (36, 21), StencilFillRule.EvenOdd);
        var caption = new EmbossedCaption(
            new CaptionSettings("O", CaptionFont.Block),
            [outer, inner],
            Preset.RaisedSurfaceZ + EmbossedCaption.EmbossHeight);
        var mesh = CardMeshGenerator.Generate(CardGeometry.Create(Preset, StencilArtwork.Empty, caption));

        var validation = MeshValidator.Validate(mesh);
        Assert.True(validation.IsValid, validation.Message);
    }

    private static IEnumerable<string> FindInvalidEdges(Mesh mesh)
    {
        var edges = new Dictionary<(string First, string Second), (int Count, int Direction)>();
        foreach (var triangle in mesh.Triangles)
        {
            Add(triangle.A, triangle.B);
            Add(triangle.B, triangle.C);
            Add(triangle.C, triangle.A);
        }

        return edges
            .Where(pair => pair.Value.Count != 2 || pair.Value.Direction != 0)
            .Select(pair => $"{pair.Key.First} -> {pair.Key.Second}: count={pair.Value.Count}, direction={pair.Value.Direction}");

        void Add(System.Numerics.Vector3 start, System.Numerics.Vector3 end)
        {
            var first = $"{start.X:R},{start.Y:R},{start.Z:R}";
            var second = $"{end.X:R},{end.Y:R},{end.Z:R}";
            var forwards = string.CompareOrdinal(first, second) <= 0;
            var key = forwards ? (first, second) : (second, first);
            var direction = forwards ? 1 : -1;
            edges[key] = edges.TryGetValue(key, out var value)
                ? (value.Count + 1, value.Direction + direction)
                : (1, direction);
        }
    }

    private static StencilContour Contour(
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c,
        (double X, double Y) d,
        StencilFillRule fillRule) =>
        new([new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y), new(d.X, d.Y), new(a.X, a.Y)], fillRule);
}
