namespace LatteShotStencilGenerator.Geometry;

/// <summary>Owns the selected preset and its independent Custom working copy.</summary>
public sealed class StencilConfigurationState
{
    public const string CustomName = "Custom";

    private StencilPreset _customPreset = AsCustom(StencilPreset.ReferenceDonut);
    private bool _isCustom;

    public StencilPreset ActivePreset => _isCustom ? _customPreset : StencilPreset.ReferenceDonut;
    public string SelectedPresetName => _isCustom ? CustomName : StencilPreset.ReferenceDonut.Name;
    public IReadOnlyList<string> ValidationErrors => StencilPresetValidator.Validate(ActivePreset);
    public bool IsValid => ValidationErrors.Count == 0;

    public void Edit(Func<StencilPreset, StencilPreset> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        _customPreset = AsCustom(edit(ActivePreset));
        _isCustom = true;
    }

    public void SelectReference() => _isCustom = false;

    public void SelectCustom() => _isCustom = true;

    public void ResetToReference()
    {
        _customPreset = AsCustom(StencilPreset.ReferenceDonut);
        _isCustom = false;
    }

    private static StencilPreset AsCustom(StencilPreset preset) => preset with { Name = CustomName };
}

public static class StencilPresetValidator
{
    public static IReadOnlyList<string> Validate(StencilPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var errors = new List<string>();

        RequirePositive(preset.CardWidth, "Card width", errors);
        RequirePositive(preset.CardHeight, "Card height", errors);
        RequirePositive(preset.BaseThickness, "Base thickness", errors);
        RequirePositive(preset.RaisedLayerThickness, "Raised-layer thickness", errors);
        RequireRectangle(preset.WorkingArea, "Working area", errors);
        RequireRectangle(preset.CaptionArea, "Caption area", errors);

        if (!IsPositiveFinite(preset.ArtworkPadding))
            errors.Add("Artwork padding must be a positive finite value.");
        RequirePositive(preset.MinimumWallThicknessMm, "Minimum wall thickness", errors);
        RequirePositive(preset.MinimumFeatureSizeMm, "Minimum feature size", errors);
        RequirePositive(preset.GenerationResolutionMm, "Generation resolution", errors);
        RequirePositive(preset.MaximumFlattenedSvgSegments, "Flattened SVG segment limit", errors);
        RequirePositive(preset.MaximumMeshTriangles, "Mesh triangle limit", errors);

        var cardIsUsable = IsPositiveFinite(preset.CardWidth) && IsPositiveFinite(preset.CardHeight);
        if (cardIsUsable && IsFinite(preset.WorkingArea) && !IsInsideCard(preset.WorkingArea, preset.CardWidth, preset.CardHeight))
            errors.Add("Working area must remain completely inside the card.");
        if (cardIsUsable && IsFinite(preset.CaptionArea) && !IsInsideCard(preset.CaptionArea, preset.CardWidth, preset.CardHeight))
            errors.Add("Caption area must remain completely inside the card.");

        if (IsPositiveRectangle(preset.WorkingArea) && IsPositiveRectangle(preset.CaptionArea) && Overlaps(preset.WorkingArea, preset.CaptionArea))
            errors.Add("Working area and caption area must not overlap.");

        if (IsPositiveFinite(preset.ArtworkPadding) && IsPositiveRectangle(preset.WorkingArea) &&
            (preset.ArtworkPadding * 2 >= preset.WorkingArea.Width || preset.ArtworkPadding * 2 >= preset.WorkingArea.Height))
        {
            errors.Add("Artwork padding must leave a positive artwork area inside the working area.");
        }

        return errors;
    }

    private static void RequirePositive(double value, string label, ICollection<string> errors)
    {
        if (!IsPositiveFinite(value)) errors.Add($"{label} must be a positive finite value.");
    }

    private static void RequirePositive(int value, string label, ICollection<string> errors)
    {
        if (value <= 0) errors.Add($"{label} must be a positive whole number.");
    }

    private static void RequireRectangle(RectMm rectangle, string label, ICollection<string> errors)
    {
        if (!double.IsFinite(rectangle.X) || !double.IsFinite(rectangle.Y))
            errors.Add($"{label} position must use finite values.");
        if (!IsPositiveFinite(rectangle.Width) || !IsPositiveFinite(rectangle.Height))
            errors.Add($"{label} width and height must be positive finite values.");
    }

    private static bool IsInsideCard(RectMm area, double width, double height) =>
        area.X >= 0 && area.Y >= 0 && area.Right <= width && area.Bottom <= height;

    private static bool Overlaps(RectMm first, RectMm second) =>
        first.X < second.Right && first.Right > second.X && first.Y < second.Bottom && first.Bottom > second.Y;

    private static bool IsFinite(RectMm rectangle) =>
        double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y) &&
        double.IsFinite(rectangle.Width) && double.IsFinite(rectangle.Height);

    private static bool IsPositiveRectangle(RectMm rectangle) =>
        IsFinite(rectangle) && rectangle.Width > 0 && rectangle.Height > 0;

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
