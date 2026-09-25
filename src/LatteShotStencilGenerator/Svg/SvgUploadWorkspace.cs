using System.IO;
using LatteShotStencilGenerator.Geometry;

namespace LatteShotStencilGenerator.Svg;

/// <summary>Tab-local import state, independent of browser file APIs.</summary>
public sealed class SvgUploadWorkspace
{
    private readonly ISvgImportAdapter _svgImporter;
    private readonly ICaptionFontOutlineAdapter _fontOutlines;
    private StencilPreset _preset;

    public SvgUploadWorkspace(ISvgImportAdapter svgImporter, ICaptionFontOutlineAdapter fontOutlines, StencilPreset preset)
    {
        _svgImporter = svgImporter;
        _fontOutlines = fontOutlines;
        _preset = preset;
        Caption = new CaptionSettings(string.Empty, preset.DefaultCaptionFont);
        BridgeConfiguration = preset.BridgeConfiguration;
    }

    public SvgImportResult? ImportedArtwork { get; private set; }
    public SvgArtworkPlacement? Placement { get; private set; }
    public string CaptionDefault { get; private set; } = string.Empty;
    public CaptionSettings Caption { get; private set; }
    public BridgeConfiguration BridgeConfiguration { get; private set; }
    public string? ImportError { get; private set; }

    public bool TryImport(string? sourceFileName, ReadOnlyMemory<byte> content)
    {
        var outcome = _svgImporter.Import(sourceFileName, content, _preset.MaximumFlattenedSvgSegments);
        if (!outcome.IsSuccess)
        {
            ImportError = outcome.Error!.Message;
            return false;
        }

        ImportedArtwork = outcome.Value!;
        Placement = SvgArtworkPlacement.Fit(ImportedArtwork, _preset.WorkingArea, _preset.ArtworkPadding);
        CaptionDefault = Path.GetFileNameWithoutExtension(ImportedArtwork.SourceFileName).ToUpperInvariant();
        Caption = new CaptionSettings(CaptionDefault, _preset.DefaultCaptionFont);
        ImportError = null;
        return true;
    }

    public void SetCaption(string? text) => Caption = Caption with { Text = text ?? string.Empty };

    public void SetCaptionFont(CaptionFont font) => Caption = Caption with { Font = font };

    public void SetBridgeConfiguration(BridgeMode mode, double widthMm) =>
        BridgeConfiguration = new BridgeConfiguration(mode, widthMm, _preset.BridgeConfiguration.MinimumBridgeWidthMm);

    public EmbossedCaption? CreateCaptionGeometry() => EmbossedCaption.Create(_preset, Caption, _fontOutlines);

    public void ApplyPreset(StencilPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var errors = StencilPresetValidator.Validate(preset);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(preset));

        _preset = preset;
        BridgeConfiguration = preset.BridgeConfiguration;
        if (ImportedArtwork is not null)
        {
            Placement = SvgArtworkPlacement.Fit(ImportedArtwork, preset.WorkingArea, preset.ArtworkPadding);
            try
            {
                _ = VectorFlattener.Flatten(Placement.Artwork.Artwork, preset.GenerationResolutionMm / 4d, preset.MaximumFlattenedSvgSegments);
                ImportError = null;
            }
            catch (SvgFlattenLimitException exception) { ImportError = exception.Message; }
        }
    }

    public void AdjustPlacement(double scalePercent, double offsetX, double offsetY)
    {
        if (Placement is not null) Placement = Placement.WithAdjustments(scalePercent, offsetX, offsetY);
    }

    public void ResetPlacement()
    {
        if (Placement is not null) Placement = Placement.Reset();
    }
}
