// Copyright (c) OdbDesignInfoClient Contributors. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Net JSON responses from OdbDesignServer REST API.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Net.
/// </summary>
/// <remarks>
/// Represents an electrical signal connecting multiple component pins.
/// </remarks>
public sealed class NetDto
{
    /// <summary>
    /// Gets the net name (e.g., "GND", "+3.3V", "DDR_DQ0").
    /// Unique identifier for the electrical signal.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the zero-based index of the net in the design's netlist.
    /// </summary>
    [JsonPropertyName("index")]
    public uint? Index { get; init; }

    /// <summary>
    /// Gets the list of pin connections belonging to this net.
    /// Each connection links a component pin to this net.
    /// </summary>
    [JsonPropertyName("pinConnections")]
    public List<PinConnectionDto>? PinConnections { get; init; }
}
