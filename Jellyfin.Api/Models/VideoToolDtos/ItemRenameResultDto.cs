namespace Jellyfin.Api.Models.VideoToolDtos;

/// <summary>
/// Result of a rename request. When <see cref="Conflict"/> is true the target name already exists
/// and the client has to ask the user how to proceed.
/// </summary>
public sealed class ItemRenameResultDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the rename succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the requested name is already taken.
    /// </summary>
    public bool Conflict { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an existing target was overwritten.
    /// </summary>
    public bool Overwritten { get; set; }

    /// <summary>
    /// Gets or sets the resulting (or conflicting) name.
    /// </summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resulting full path.
    /// </summary>
    public string NewPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message when the rename failed.
    /// </summary>
    public string? Error { get; set; }
}
