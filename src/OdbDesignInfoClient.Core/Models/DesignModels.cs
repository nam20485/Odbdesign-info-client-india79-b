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

    /// <summary>
    /// Gets or sets the pin's component-local X coordinate in millimeters
    /// (relative to the component origin, before the placement transform).
    /// Zero when the source projection does not expose pin geometry.
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Gets or sets the pin's component-local Y coordinate in millimeters.
    /// Zero when the source projection does not expose pin geometry.
    /// </summary>
    public double Y { get; init; }
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
/// <remarks>
/// Populated from the server's design-matrix projection, which is the authoritative
/// source for layer <see cref="Type"/>, physical <see cref="StackOrder"/>, and display
/// <see cref="ColorHex"/>. The server's matrix does not expose thickness, material, or
/// polarity, so <see cref="Thickness"/> and <see cref="Material"/> remain unset and the
/// UI renders them as unavailable rather than fabricating defaults.
/// </remarks>
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
    /// Gets or sets the layer type (Signal, Dielectric, Drill, SolderMask, SilkScreen, Component, Document, Rout, …).
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the layer polarity (Positive, Negative). Not exposed by the current server matrix.
    /// </summary>
    public string Polarity { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the layer thickness in mils or mm. Null when the server does not report a value.
    /// </summary>
    public double? Thickness { get; init; }

    /// <summary>
    /// Gets or sets the material name (for dielectric layers). Null when the server does not report a value.
    /// </summary>
    public string? Material { get; init; }

    /// <summary>
    /// Gets or sets the 1-based physical stack order (matrix row), top to bottom.
    /// </summary>
    public int StackOrder { get; init; }

    /// <summary>
    /// Gets or sets the display color as a hex string (e.g. "#636337").
    /// Empty when the design declares no preferred color for this layer.
    /// </summary>
    public string ColorHex { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the upper layer name of a drill span. Null for non-drill layers.
    /// </summary>
    public string? StartLayer { get; init; }

    /// <summary>
    /// Gets or sets the lower layer name of a drill span. Null for non-drill layers.
    /// </summary>
    public string? EndLayer { get; init; }
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
/// Represents a usage of an entity (package or part) by a placed component.
/// </summary>
public record EntityUsage
{
    /// <summary>
    /// Gets or sets the reference designator of the component using the entity.
    /// </summary>
    public string ComponentRefDes { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the part name of the component using the entity, when known.
    /// Null for part usages (the part number is the parent row).
    /// </summary>
    public string? PartName { get; init; }
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

    /// <summary>
    /// Gets or sets the components that use this package. Empty when the server
    /// projection does not expose usage data (REST fallback).
    /// </summary>
    public IReadOnlyList<EntityUsage> Usages { get; init; } = [];
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

    /// <summary>
    /// Gets or sets the components that use this part. Empty when the server
    /// projection does not expose usage data (REST fallback).
    /// </summary>
    public IReadOnlyList<EntityUsage> Usages { get; init; } = [];
}

/// <summary>
/// Represents one step of a design with the cheaply-available step-header metadata.
/// </summary>
/// <remarks>
/// Populated from the server's step list plus the per-step <c>stephdr</c> projection.
/// The server exposes a flat step list only (no job/panel nesting), so the step
/// hierarchy is one level deep; <see cref="RepeatCount"/> surfaces the step-repeat
/// records when the header declares any.
/// </remarks>
public record StepSummary
{
    /// <summary>
    /// Gets or sets the step name (the server's step key, e.g. "step").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the step ID from the step header. Null when unavailable.
    /// </summary>
    public int? Id { get; init; }

    /// <summary>
    /// Gets or sets the X origin from the step header. Null when unavailable.
    /// </summary>
    public double? XOrigin { get; init; }

    /// <summary>
    /// Gets or sets the Y origin from the step header. Null when unavailable.
    /// </summary>
    public double? YOrigin { get; init; }

    /// <summary>
    /// Gets or sets the X datum from the step header. Null when unavailable.
    /// </summary>
    public double? XDatum { get; init; }

    /// <summary>
    /// Gets or sets the Y datum from the step header. Null when unavailable.
    /// </summary>
    public double? YDatum { get; init; }

    /// <summary>
    /// Gets or sets the number of step-repeat records declared in the step header.
    /// Zero when the design has no repeated (panelized) steps.
    /// </summary>
    public int RepeatCount { get; init; }
}

/// <summary>
/// Represents one symbol from the design's symbols library.
/// </summary>
/// <remarks>
/// The server's symbols projection exposes names only; dimension/type data is
/// not part of the response and is never fabricated.
/// </remarks>
public record SymbolSummary
{
    /// <summary>
    /// Gets or sets the symbol name (e.g. "r10", "pads_th_square74x62x45x4x15").
    /// </summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Represents the via subnets attached to one net, derived from the EDA net records.
/// </summary>
public record ViaSummary
{
    /// <summary>
    /// Gets or sets the net name.
    /// </summary>
    public string NetName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the net's index in the EDA net list.
    /// </summary>
    public uint NetIndex { get; init; }

    /// <summary>
    /// Gets or sets the number of VIA subnets on the net.
    /// </summary>
    public int ViaCount { get; init; }

    /// <summary>
    /// Gets or sets the number of component-pin (toeprint) connections on the net.
    /// </summary>
    public int PinCount { get; init; }
}

/// <summary>
/// Represents one EDA net record summarized for the EDA Data tab.
/// </summary>
public record EdaNetSummary
{
    /// <summary>
    /// Gets or sets the net name (raw record name; may be empty for unnamed nets).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the net's index field.
    /// </summary>
    public uint Index { get; init; }

    /// <summary>
    /// Gets or sets the number of TOEPRINT subnets (component pin connections).
    /// </summary>
    public int ToeprintCount { get; init; }

    /// <summary>
    /// Gets or sets the number of TRACE subnets.
    /// </summary>
    public int TraceCount { get; init; }

    /// <summary>
    /// Gets or sets the number of VIA subnets.
    /// </summary>
    public int ViaCount { get; init; }

    /// <summary>
    /// Gets or sets the number of PLANE subnets.
    /// </summary>
    public int PlaneCount { get; init; }

    /// <summary>
    /// Gets or sets the number of attributes (property records plus attribute-lookup entries).
    /// </summary>
    public int AttributeCount { get; init; }
}

/// <summary>
/// Summary of a step's EDA data file: header fields plus per-net subnet/attribute counts.
/// </summary>
public record EdaDataSummary
{
    /// <summary>
    /// Gets or sets the file's units (e.g. "INCH"). Empty when unavailable.
    /// </summary>
    public string Units { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the EDA source tool (e.g. "Mentor PowerPCB file"). Empty when unavailable.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the server-side path of the eda/data file. Empty when unavailable.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of layer names the EDA file references.
    /// </summary>
    public int LayerCount { get; init; }

    /// <summary>
    /// Gets or sets the file-level attribute names, when the file declares any.
    /// </summary>
    public IReadOnlyList<string> AttributeNames { get; init; } = [];

    /// <summary>
    /// Gets or sets the per-net summaries, one per EDA net record.
    /// </summary>
    public IReadOnlyList<EdaNetSummary> Nets { get; init; } = [];
}
