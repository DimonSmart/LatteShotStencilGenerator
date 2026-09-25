namespace LatteShotStencilGenerator.Geometry;

/// <summary>Normalized card geometry shared by the workspace preview and mesh generator.</summary>
public sealed record CardGeometry(
    StencilPreset Preset,
    RectMm CardBoundary,
    RectMm WorkingArea,
    StencilArtwork Artwork,
    BridgeConfiguration BridgeConfiguration,
    ResolvedStencilTopology ResolvedTopology,
    EmbossedCaption? Caption,
    string? ValidationMessage,
    IReadOnlyList<ManufacturabilityFinding> ManufacturabilityFindings)
{
    public bool IsExportable => ValidationMessage is null;
    /// <summary>Contours shown by the preview and used as mesh opening boundaries.</summary>
    public IReadOnlyList<StencilContour> OpeningContours => ResolvedTopology.OpeningContours;
    public IReadOnlyList<StencilBridge> Bridges => ResolvedTopology.Bridges;

    public static CardGeometry Create(StencilPreset preset)
        => Create(preset, StencilArtwork.Empty, null);

    public static CardGeometry Create(StencilPreset preset, StencilArtwork artwork, EmbossedCaption? caption = null)
        => Create(preset, artwork, preset.BridgeConfiguration, caption);

    public static CardGeometry Create(
        StencilPreset preset,
        StencilArtwork artwork,
        BridgeConfiguration bridgeConfiguration,
        EmbossedCaption? caption = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(bridgeConfiguration);
        var card = new RectMm(0, 0, preset.CardWidth, preset.CardHeight);
        var configurationErrors = StencilPresetValidator.Validate(preset);
        if (configurationErrors.Count > 0)
        {
            return new CardGeometry(
                preset,
                card,
                preset.WorkingArea,
                artwork,
                bridgeConfiguration,
                new ResolvedStencilTopology(artwork.Contours, [], 0, 0, []),
                caption,
                string.Join(" ", configurationErrors),
                []);
        }

        var validation = ValidateArtwork(artwork, preset.WorkingArea);
        ResolvedStencilTopology topology;
        if (validation is not null)
        {
            topology = new ResolvedStencilTopology(artwork.Contours, [], 0, 0, []);
        }
        else
        {
            try
            {
                topology = StencilTopologyResolver.Resolve(artwork, preset.WorkingArea, bridgeConfiguration);
            }
            catch (InvalidOperationException exception)
            {
                topology = new ResolvedStencilTopology(artwork.Contours, [], 0, 0, []);
                validation = exception.Message;
            }
        }

        if (validation is null && bridgeConfiguration.Mode == BridgeMode.Off && topology.DetachedComponentCount > 0)
            validation = $"Artwork contains {topology.DetachedComponentCount} detached material island(s). Enable automatic bridges before export.";

        var findings = validation is null
            ? ManufacturabilityAnalyzer.Analyze(preset, artwork, topology)
            : [];
        return new CardGeometry(preset, card, preset.WorkingArea, artwork, bridgeConfiguration, topology, caption, validation, findings);
    }

    private static string? ValidateArtwork(StencilArtwork artwork, RectMm workingArea)
    {
        foreach (var contour in artwork.Contours)
        {
            if (contour.Points.Count < 4 || contour.Points[0] != contour.Points[^1])
                return "Artwork contains an invalid closed contour.";
            if (contour.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                return "Artwork contains invalid coordinates.";
            if (contour.Points.Any(point => point.X < workingArea.X || point.X > workingArea.Right || point.Y < workingArea.Y || point.Y > workingArea.Bottom))
                return "Artwork extends outside the working area. Reset or adjust its scale and offsets before export.";
        }

        return null;
    }
}
