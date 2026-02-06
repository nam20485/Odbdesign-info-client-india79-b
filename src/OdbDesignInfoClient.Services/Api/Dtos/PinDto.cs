// Copyright (c) OdbDesignInfoClient Contributors. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Pin JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Pin.
/// </summary>
public sealed class PinDto
{
    /// <summary>
    /// Gets the pin name/number (e.g., "1", "A1", "GND").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the zero-based index of the pin in the package.
    /// </summary>
    [JsonPropertyName("index")]
    public uint? Index { get; init; }

    /// <summary>
    /// Gets the X coordinate of pin center relative to component center.
    /// Units depend on design settings (typically mm or mils).
    /// </summary>
    [JsonPropertyName("x")]
    public float? X { get; init; }

    /// <summary>
    /// Gets the Y coordinate of pin center relative to component center.
    /// </summary>
    [JsonPropertyName("y")]
    public float? Y { get; init; }
}
