// Copyright (c) OdbDesignInfoClient Contributors. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for PinConnection JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.PinConnection.
/// </summary>
/// <remarks>
/// Represents a connection between a component pin and a net.
/// </remarks>
public sealed class PinConnectionDto
{
    /// <summary>
    /// Gets the connection name/identifier (e.g., "U1-1-GND").
    /// Format: {RefDes}-{PinNumber}-{NetName}.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the reference to the parent component.
    /// May contain partial data (just refDes) or full component data.
    /// </summary>
    [JsonPropertyName("component")]
    public ComponentDto? Component { get; init; }

    /// <summary>
    /// Gets the reference to the physical pin on the component.
    /// </summary>
    [JsonPropertyName("pin")]
    public PinDto? Pin { get; init; }
}
