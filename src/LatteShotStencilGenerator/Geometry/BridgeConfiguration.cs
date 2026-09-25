namespace LatteShotStencilGenerator.Geometry;

public enum BridgeMode
{
    Auto,
    Off
}

/// <summary>Immutable manufacturability settings used when stencil topology is resolved.</summary>
public sealed record BridgeConfiguration
{
    public BridgeConfiguration(BridgeMode mode, double bridgeWidthMm, double minimumBridgeWidthMm)
    {
        if (!double.IsFinite(bridgeWidthMm) || bridgeWidthMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bridgeWidthMm), "Bridge width must be a positive finite value.");
        if (!double.IsFinite(minimumBridgeWidthMm) || minimumBridgeWidthMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumBridgeWidthMm), "Minimum bridge width must be a positive finite value.");

        Mode = mode;
        BridgeWidthMm = bridgeWidthMm;
        MinimumBridgeWidthMm = minimumBridgeWidthMm;
    }

    public BridgeMode Mode { get; }
    public double BridgeWidthMm { get; }
    public double MinimumBridgeWidthMm { get; }
    public double ResolvedWidthMm => Math.Max(BridgeWidthMm, MinimumBridgeWidthMm);
}
