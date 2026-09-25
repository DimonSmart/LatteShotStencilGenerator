using System.IO;

namespace LatteShotStencilGenerator.Geometry;

public sealed record StlExportResult(string? FileName, byte[]? Content, string? Error)
{
    public bool IsSuccess => FileName is not null && Content is not null && Error is null;

    public static StlExportResult Success(string fileName, byte[] content) => new(fileName, content, null);
    public static StlExportResult Failure(string error) => new(null, null, error);
}

/// <summary>Creates a validated STL payload from the current card geometry.</summary>
public static class StlExportGenerator
{
    public static StlExportResult Generate(CardGeometry geometry, string? sourceFileName, string? caption)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (string.IsNullOrWhiteSpace(sourceFileName))
            return StlExportResult.Failure("Import an SVG file before generating the STL.");
        if (!geometry.IsExportable)
            return StlExportResult.Failure($"STL export is blocked: {geometry.ValidationMessage}");

        try
        {
            var mesh = CardMeshGenerator.Generate(geometry);
            var validation = MeshValidator.Validate(mesh);
            if (!validation.IsValid)
                return StlExportResult.Failure($"STL export is blocked: {validation.Message}");

            return StlExportResult.Success(
                DeriveFileName(caption, sourceFileName),
                BinaryStlSerializer.Serialize(mesh));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return StlExportResult.Failure(
                $"STL generation failed: {exception.Message} Adjust the artwork placement, caption, or bridges and try again.");
        }
    }

    public static string DeriveFileName(string? caption, string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        var trimmedCaption = caption?.Trim();
        if (!string.IsNullOrEmpty(trimmedCaption))
            return $"{trimmedCaption.ToUpperInvariant()}.stl";

        var sourceName = Path.GetFileNameWithoutExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("The imported SVG must have a filename.", nameof(sourceFileName));
        return $"{sourceName}-card.stl";
    }
}
