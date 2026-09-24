using System.IO;
using LatteShotStencilGenerator.Geometry;

namespace LatteShotStencilGenerator.Svg;

/// <summary>Tab-local import state, independent of browser file APIs.</summary>
public sealed class SvgUploadWorkspace(ISvgImportAdapter svgImporter, ICaptionFontOutlineAdapter fontOutlines, StencilPreset preset)
{
    public SvgImportResult? ImportedArtwork { get; private set; }
    public SvgArtworkPlacement? Placement { get; private set; }
    public string CaptionDefault { get; private set; } = string.Empty;
    public CaptionSettings Caption { get; private set; } = new(string.Empty, preset.DefaultCaptionFont);
    public string? ImportError { get; private set; }

    public bool TryImport(string? sourceFileName, ReadOnlyMemory<byte> content)
    {
        var outcome = svgImporter.Import(sourceFileName, content);
        if (!outcome.IsSuccess)
        {
            ImportError = outcome.Error!.Message;
            return false;
        }

        ImportedArtwork = outcome.Value!;
        Placement = SvgArtworkPlacement.Fit(ImportedArtwork, preset.WorkingArea, preset.ArtworkPadding);
        CaptionDefault = Path.GetFileNameWithoutExtension(ImportedArtwork.SourceFileName).ToUpperInvariant();
        Caption = new CaptionSettings(CaptionDefault, preset.DefaultCaptionFont);
        ImportError = null;
        return true;
    }

    public void SetCaption(string? text) => Caption = Caption with { Text = text ?? string.Empty };

    public void SetCaptionFont(CaptionFont font) => Caption = Caption with { Font = font };

    public EmbossedCaption? CreateCaptionGeometry() => EmbossedCaption.Create(preset, Caption, fontOutlines);

    public void AdjustPlacement(double scalePercent, double offsetX, double offsetY)
    {
        if (Placement is not null) Placement = Placement.WithAdjustments(scalePercent, offsetX, offsetY);
    }

    public void ResetPlacement()
    {
        if (Placement is not null) Placement = Placement.Reset();
    }
}
