using System;
using System.Text.Json.Serialization;

namespace SinuxBoard;

/// <summary>
/// Represents a single clipboard history record.
/// Currently only "Text" is supported for Type, but the shape is
/// intentionally generic so future formats (Image, Files, ...) can
/// reuse the same table/model without a schema rewrite.
/// </summary>
public sealed class ClipboardEntry
{
    [JsonPropertyName("Id")]
    public long Id { get; set; }

    [JsonPropertyName("Content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Clipboard content type. Only "Text" is used in v1.
    /// </summary>
    [JsonPropertyName("Type")]
    public string Type { get; set; } = "Text";

    /// <summary>
    /// UTC timestamp of when the entry was captured.
    /// </summary>
    [JsonPropertyName("CreatedAt")]
    public DateTime CreatedAtUtc { get; set; }
}
