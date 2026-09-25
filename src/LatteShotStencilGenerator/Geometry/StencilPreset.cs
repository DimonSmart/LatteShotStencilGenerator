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
    CaptionFont DefaultCaptionFont,
    BridgeConfiguration BridgeConfiguration,
    double MinimumWallThicknessMm,
    double MinimumFeatureSizeMm,
    double GenerationResolutionMm,
    int MaximumFlattenedSvgSegments,
    int MaximumMeshTriangles)
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
        CaptionFont.Block,
        new BridgeConfiguration(BridgeMode.Auto, 1.5, 1.0),
        0.8,
        0.8,
        0.2,
        50_000,
        500_000);

    public double RaisedSurfaceZ => BaseThickness + RaisedLayerThickness;
}
