namespace OdbDesignInfoClient.Core.Models;

/// <summary>
/// Represents an ODB++ design loaded on the server.
/// </summary>
public record Design
{
    /// <summary>
    /// Gets or sets the unique identifier for the design.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the design.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the design on the server.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the date the design was loaded.
    /// </summary>
    public DateTime LoadedDate { get; init; }

    /// <summary>
    /// Gets or sets the list of steps in the design.
    /// </summary>
    public IReadOnlyList<string> Steps { get; init; } = [];
}

/// <summary>
/// Represents a component in an ODB++ design.
/// </summary>
public record Component
{
    /// <summary>
    /// Gets or sets the reference designator.
    /// </summary>
    public string RefDes { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the part name.
    /// </summary>
    public string PartName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the package/footprint name.
    /// </summary>
    public string Package { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the board side (Top/Bottom).
    /// </summary>
    public string Side { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double Rotation { get; init; }

    /// <summary>
    /// Gets or sets the X coordinate.
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Gets or sets the Y coordinate.
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// Gets or sets the list of pins for this component.
    /// </summary>
    public IReadOnlyList<Pin> Pins { get; init; } = [];
}

/// <summary>
/// Represents a pin on a component.
/// </summary>
public record Pin
{
    /// <summary>
    /// Gets or sets the pin name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the pin number.
    /// </summary>
    public int Number { get; init; }

    /// <summary>
    /// Gets or sets the net name connected to this pin.
    /// </summary>
    public string NetName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the electrical type.
    /// </summary>
    public string ElectricalType { get; init; } = string.Empty;
}

/// <summary>
/// Represents a net in an ODB++ design.
/// </summary>
public record Net
{
    /// <summary>
    /// Gets or sets the net name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the total pin count.
    /// </summary>
    public int PinCount { get; init; }

    /// <summary>
    /// Gets or sets the total via count.
    /// </summary>
    public int ViaCount { get; init; }

    /// <summary>
    /// Gets or sets the connected features.
    /// </summary>
    public IReadOnlyList<NetFeature> Features { get; init; } = [];
}

/// <summary>
/// Represents a feature connected to a net.
/// </summary>
public record NetFeature
{
    /// <summary>
    /// Gets or sets the feature type (Pin, Via, TestPoint).
    /// </summary>
    public string FeatureType { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the feature identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the component reference (for pins).
    /// </summary>
    public string ComponentRef { get; init; } = string.Empty;
}

/// <summary>
/// Represents a layer in the PCB stackup.
/// </summary>
public record Layer
{
    /// <summary>
    /// Gets or sets the layer ID.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets or sets the layer name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the layer type (Signal, Power, Dielectric, Drill).
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the layer polarity (Positive, Negative).
    /// </summary>
    public string Polarity { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the layer thickness in mils or mm.
    /// </summary>
    public double Thickness { get; init; }

    /// <summary>
    /// Gets or sets the material name (for dielectric layers).
    /// </summary>
    public string Material { get; init; } = string.Empty;
}

/// <summary>
/// Represents a drill tool in an ODB++ design.
/// </summary>
public record DrillTool
{
    /// <summary>
    /// Gets or sets the tool number.
    /// </summary>
    public int ToolNumber { get; init; }

    /// <summary>
    /// Gets or sets the drill diameter.
    /// </summary>
    public double Diameter { get; init; }

    /// <summary>
    /// Gets or sets the drill shape.
    /// </summary>
    public string Shape { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the drill is plated.
    /// </summary>
    public bool IsPlated { get; init; }

    /// <summary>
    /// Gets or sets the number of hits for this drill tool.
    /// </summary>
    public int HitCount { get; init; }
}

/// <summary>
/// Represents a package/footprint in an ODB++ design.
/// </summary>
public record Package
{
    /// <summary>
    /// Gets or sets the package name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the package pitch.
    /// </summary>
    public double Pitch { get; init; }

    /// <summary>
    /// Gets or sets the number of pins in the package.
    /// </summary>
    public int PinCount { get; init; }

    /// <summary>
    /// Gets or sets the package width.
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// Gets or sets the package height.
    /// </summary>
    public double Height { get; init; }
}

/// <summary>
/// Represents a part definition in an ODB++ design.
/// </summary>
public record Part
{
    /// <summary>
    /// Gets or sets the part number.
    /// </summary>
    public string PartNumber { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the manufacturer name.
    /// </summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the part description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of times this part is used in the design.
    /// </summary>
    public int UsageCount { get; init; }
}
