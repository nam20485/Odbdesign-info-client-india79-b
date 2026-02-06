// Copyright (c) OdbDesignInfoClient Contributors. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Package JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Package.
/// </summary>
/// <remarks>
/// Represents a component footprint/package definition from the library.
/// </remarks>
public sealed class PackageDto
{
    /// <summary>
    /// Gets the package name (e.g., "LQFP-100", "0402", "BGA-256").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the pin pitch in design units (typically mm).
    /// </summary>
    [JsonPropertyName("pitch")]
    public float? Pitch { get; init; }

    /// <summary>
    /// Gets the bounding box minimum X coordinate.
    /// </summary>
    [JsonPropertyName("xMin")]
    public float? XMin { get; init; }

    /// <summary>
    /// Gets the bounding box maximum X coordinate.
    /// </summary>
    [JsonPropertyName("xMax")]
    public float? XMax { get; init; }

    /// <summary>
    /// Gets the bounding box minimum Y coordinate.
    /// </summary>
    [JsonPropertyName("yMin")]
    public float? YMin { get; init; }

    /// <summary>
    /// Gets the bounding box maximum Y coordinate.
    /// </summary>
    [JsonPropertyName("yMax")]
    public float? YMax { get; init; }

    /// <summary>
    /// Gets the list of pins in this package.
    /// </summary>
    [JsonPropertyName("pins")]
    public List<PinDto>? Pins { get; init; }
}
