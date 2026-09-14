// Copyright (c) OdbDesignInfoClient Contributors. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for the design matrix (stackup) JSON response from OdbDesignServer.
/// Maps to the hand-rolled <c>/filemodels/{name}/matrix/matrix</c> projection.
/// </summary>
/// <remarks>
/// Unlike protobuf-generated endpoints, this response is produced by the server's own JSON
/// serializer: layer <c>type</c> is a string ("Dielectric", "SolderMask", …), missing type means
/// a Signal layer, and optional fields (drill span, document context) are only present when set.
/// All fields are nullable to handle the sparse, per-type optional members gracefully.
/// </remarks>
public sealed class MatrixDto
{
    /// <summary>
    /// Gets the step columns defined by the matrix.
    /// </summary>
    [JsonPropertyName("steps")]
    public List<MatrixStepDto>? Steps { get; init; }

    /// <summary>
    /// Gets the layer rows that make up the stackup.
    /// </summary>
    [JsonPropertyName("layers")]
    public List<MatrixLayerDto>? Layers { get; init; }
}

/// <summary>
/// A step column in the design matrix.
/// </summary>
public sealed class MatrixStepDto
{
    /// <summary>
    /// Gets the matrix column index for this step.
    /// </summary>
    [JsonPropertyName("column")]
    public uint Column { get; init; }

    /// <summary>
    /// Gets the step identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public uint Id { get; init; }

    /// <summary>
    /// Gets the step name (e.g., "STEP").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// A single layer row in the design matrix.
/// </summary>
public sealed class MatrixLayerDto
{
    /// <summary>
    /// Gets the 1-based stackup row index (physical layer order, top to bottom).
    /// </summary>
    [JsonPropertyName("row")]
    public uint Row { get; init; }

    /// <summary>
    /// Gets the layer context ("Board" or "Misc"). Empty means the board default.
    /// </summary>
    [JsonPropertyName("context")]
    public string? Context { get; init; }

    /// <summary>
    /// Gets the authoritative layer type ("Component", "Dielectric", "SolderMask", "SilkScreen",
    /// "SolderPaste", "Mixed", "Drill", "Rout", "Document", "PowerGround", …). Null means Signal.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Gets the layer name (e.g., "LAYER-1", "SOLDERMASK-TOP").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the dielectric subtype code (present only for Dielectric layers).
    /// </summary>
    [JsonPropertyName("dielectricType")]
    public uint? DielectricType { get; init; }

    /// <summary>
    /// Gets the upper layer name of a drill span (present only for Drill layers).
    /// </summary>
    [JsonPropertyName("startName")]
    public string? StartName { get; init; }

    /// <summary>
    /// Gets the lower layer name of a drill span (present only for Drill layers).
    /// </summary>
    [JsonPropertyName("endName")]
    public string? EndName { get; init; }

    /// <summary>
    /// Gets the display color assigned to this layer in the design.
    /// </summary>
    [JsonPropertyName("color")]
    public MatrixColorDto? Color { get; init; }
}

/// <summary>
/// An RGB display color in the design matrix.
/// </summary>
public sealed class MatrixColorDto
{
    /// <summary>
    /// Gets the red channel (0-255).
    /// </summary>
    [JsonPropertyName("red")]
    public uint Red { get; init; }

    /// <summary>
    /// Gets the green channel (0-255).
    /// </summary>
    [JsonPropertyName("green")]
    public uint Green { get; init; }

    /// <summary>
    /// Gets the blue channel (0-255).
    /// </summary>
    [JsonPropertyName("blue")]
    public uint Blue { get; init; }

    /// <summary>
    /// Gets a value indicating whether the design specifies no preferred color
    /// (in which case a type-based default is used instead).
    /// </summary>
    [JsonPropertyName("noPreference")]
    public bool NoPreference { get; init; }
}
