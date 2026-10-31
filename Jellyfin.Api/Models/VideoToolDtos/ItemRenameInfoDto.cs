namespace Jellyfin.Api.Models.VideoToolDtos;

/// <summary>
/// Describes the current on-disk name of an item so the client can prefill a rename dialog.
/// </summary>
public sealed class ItemRenameInfoDto
{
    /// <summary>
    /// Gets or sets the current file or folder name, including the file extension.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the item is a folder on disk.
    /// </summary>
    public bool IsFolder { get; set; }

    /// <summary>
    /// Gets or sets the full path of the item.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
