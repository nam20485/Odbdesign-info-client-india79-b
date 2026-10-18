namespace OdbDesign.ProductModel;

/// <summary>Unit conversions shared by the product-model reader.</summary>
public static class UnitsHelper
{
    /// <summary>
    /// Converts an ODB++ linear unit name to a scale factor that maps it to millimeters.
    /// Unknown or unspecified units are treated as millimeters (scale 1.0).
    /// </summary>
    /// <param name="units">The unit name from a file header (e.g. "inch", "mil", "mm").</param>
    /// <returns>The multiplier that converts a coordinate in <paramref name="units"/> to millimeters.</returns>
    public static float UnitsToMmScale(string? units)
    {
        return units?.ToLowerInvariant() switch
        {
            "inch" => 25.4f,
            "mil" => 0.0254f,
            "cm" => 10f,
            "micron" or "um" => 0.001f,
            _ => 1.0f,
        };
    }
}
