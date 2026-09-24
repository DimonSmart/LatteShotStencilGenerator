namespace LatteShotStencilGenerator.Geometry;

/// <summary>Immutable dimensional input for a stencil-card layout, in millimetres.</summary>
public sealed record StencilPreset(
    string Name,
    double CardWidth,
    double CardHeight,
    double BaseThickness,
    double RaisedLayerThickness,
    RectMm WorkingArea,
    RectMm CaptionArea,
    double ArtworkPadding,
    CaptionFont DefaultCaptionFont)
{
    public static StencilPreset ReferenceDonut { get; } = new(
        "Reference / Donut",
        88.345,
        113.882,
        0.998,
        0.200,
        new RectMm(6.678, 33.029, 74.989, 74.989),
        new RectMm(6.678, 0, 74.989, 33.029),
        3.0,
        CaptionFont.Block);

    public double RaisedSurfaceZ => BaseThickness + RaisedLayerThickness;
}
