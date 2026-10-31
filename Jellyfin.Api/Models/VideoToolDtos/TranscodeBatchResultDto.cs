using System.Collections.Generic;

namespace Jellyfin.Api.Models.VideoToolDtos;

/// <summary>
/// Result of queueing the same transcode settings for several items.
/// </summary>
public sealed class TranscodeBatchResultDto
{
    /// <summary>
    /// Gets or sets the number of jobs that were queued.
    /// </summary>
    public int Added { get; set; }

    /// <summary>
    /// Gets or sets the number of items that were skipped.
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Gets or sets the queued jobs.
    /// </summary>
    public List<VideoToolJobDto> Jobs { get; set; } = new();

    /// <summary>
    /// Gets or sets the names of the items that could not be queued.
    /// </summary>
    public List<string> SkippedNames { get; set; } = new();
}
