// Copyright (c) OdbDesignInfoClient Contributors. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Part JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Part.
/// </summary>
/// <remarks>
/// Represents a part library entry with component attributes.
/// </remarks>
public sealed class PartDto
{
    /// <summary>
    /// Gets the part name/number in the library.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the key-value attributes (e.g., VALUE, MANUFACTURER, TOLERANCE).
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; init; }
}
