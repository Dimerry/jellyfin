using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Api.Models.VideoToolDtos;

/// <summary>
/// Represents the state of a custom video tool (transcode or cut) job.
/// </summary>
public sealed class VideoToolJobDto
{
    /// <summary>
    /// Gets or sets the job identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the kind of job (Transcode or Cut).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source item name.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target video codec, if this is a transcode job.
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Gets or sets the maximum output resolution, if this is a transcode job.
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Gets or sets the target video bitrate in kbps, if this is a transcode job.
    /// </summary>
    public int? BitrateKbps { get; set; }

    /// <summary>
    /// Gets or sets the queue position for queued jobs; null once running or finished.
    /// </summary>
    public int? QueuePosition { get; set; }

    /// <summary>
    /// Gets or sets the job status (Queued, Running, Completed, Failed).
    /// </summary>
    public string Status { get; set; } = "Queued";

    /// <summary>
    /// Gets or sets the output file path.
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the progress percentage (0-100), or null when unknown.
    /// </summary>
    public double? Progress { get; set; }

    /// <summary>
    /// Gets or sets the error message when the job fails.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the completion time.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the temporary file ffmpeg writes to. Moved to <see cref="OutputPath"/> when the job succeeds.
    /// Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public string WorkPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source file path. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source duration in seconds. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public double SourceDurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cancellation was requested. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public bool CancelRequested { get; set; }

    /// <summary>
    /// Gets or sets the number of seconds removed from the start for cut jobs. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public double CutStartSeconds { get; set; }

    /// <summary>
    /// Gets or sets the number of seconds removed from the end for cut jobs. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public double CutEndSeconds { get; set; }

    /// <summary>
    /// Gets or sets the clockwise rotation in degrees applied by a cut job. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public int CutRotation { get; set; }

    /// <summary>
    /// Gets or sets the ffmpeg process id. Not exposed over the API.
    /// </summary>
    [JsonIgnore]
    public int ProcessId { get; set; }
}
