namespace LatteShotStencilGenerator.Geometry;

/// <summary>Normalized card geometry shared by the workspace preview and mesh generator.</summary>
public sealed record CardGeometry(
    StencilPreset Preset,
    RectMm CardBoundary,
    RectMm WorkingArea,
    StencilArtwork Artwork,
    EmbossedCaption? Caption,
    string? ValidationMessage)
{
    public bool IsExportable => ValidationMessage is null;
    /// <summary>Contours shown by the preview and used as mesh opening boundaries.</summary>
    public IReadOnlyList<StencilContour> OpeningContours => Artwork.Contours;

    public static CardGeometry Create(StencilPreset preset)
        => Create(preset, StencilArtwork.Empty, null);

    public static CardGeometry Create(StencilPreset preset, StencilArtwork artwork, EmbossedCaption? caption = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(artwork);
        var card = new RectMm(0, 0, preset.CardWidth, preset.CardHeight);
        if (preset.WorkingArea.X < 0 || preset.WorkingArea.Y < 0 ||
            preset.WorkingArea.Right > card.Right || preset.WorkingArea.Bottom > card.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), "The working area must be contained by the card.");
        }

        var validation = ValidateArtwork(artwork, preset.WorkingArea);
        return new CardGeometry(preset, card, preset.WorkingArea, artwork, caption, validation);
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
