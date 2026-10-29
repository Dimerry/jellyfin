using System;

namespace Jellyfin.Api.Models.ImageToolDtos;

/// <summary>
/// Result of rotating an image.
/// </summary>
public sealed class ImageRotateResultDto
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rotation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message when the rotation failed.
    /// </summary>
    public string? Error { get; set; }
}
