using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Models.ImageToolDtos;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Custom image tools: rotate a photo in place, in 90 degree steps.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[Tags("ImageTools")]
public class ImageToolsController : BaseJellyfinApiController
{
    private static readonly SemaphoreSlim _rotateLock = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IFileSystem _fileSystem;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<ImageToolsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageToolsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="libraryMonitor">Instance of the <see cref="ILibraryMonitor"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public ImageToolsController(
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        IMediaEncoder mediaEncoder,
        IFileSystem fileSystem,
        IProviderManager providerManager,
        ILogger<ImageToolsController> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _mediaEncoder = mediaEncoder;
        _fileSystem = fileSystem;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>
    /// Rotates a single image in place.
    /// </summary>
    /// <param name="itemId">The item id of the image.</param>
    /// <param name="degrees">Counter-clockwise rotation in degrees (90, 180 or 270).</param>
    /// <returns>The rotation result.</returns>
    [HttpPost("{itemId}/Rotate")]
    public async Task<ActionResult<ImageRotateResultDto>> RotateImage(
        [FromRoute] Guid itemId,
        [FromQuery] int degrees = 90)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var result = await RotateAsync(item, degrees).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Rotates several images in place, one after another.
    /// </summary>
    /// <param name="itemIds">The item ids of the images.</param>
    /// <param name="degrees">Counter-clockwise rotation in degrees (90, 180 or 270).</param>
    /// <returns>One result per requested item.</returns>
    [HttpPost("Rotate")]
    public async Task<ActionResult<List<ImageRotateResultDto>>> RotateImages(
        [FromBody] Guid[] itemIds,
        [FromQuery] int degrees = 90)
    {
        if (itemIds is null || itemIds.Length == 0)
        {
            return BadRequest("No items were selected.");
        }

        if (!IsValidRotation(degrees))
        {
            return BadRequest("Rotation must be 90, 180 or 270 degrees.");
        }

        var results = new List<ImageRotateResultDto>(itemIds.Length);
        foreach (var id in itemIds)
        {
            var item = _libraryManager.GetItemById(id);
            if (item is null)
            {
                results.Add(new ImageRotateResultDto { ItemId = id, Success = false, Error = "Item not found." });
                continue;
            }

            results.Add(await RotateAsync(item, degrees).ConfigureAwait(false));
        }

        return Ok(results);
    }

    private static bool IsValidRotation(int degrees)
        => degrees is 90 or 180 or 270;

    private static string GetFilter(int degrees) => degrees switch
    {
        // transpose=2 turns the picture 90 degrees counter-clockwise, transpose=1 clockwise.
        90 => "transpose=2",
        180 => "transpose=2,transpose=2",
        _ => "transpose=1"
    };

    private static void AddEncoderArgs(List<string> args, string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                // 2 is the highest quality step of the mjpeg encoder
                args.Add("-q:v");
                args.Add("2");
                break;
            case ".png":
                args.Add("-compression_level");
                args.Add("6");
                break;
            case ".webp":
                args.Add("-c:v");
                args.Add("libwebp");
                args.Add("-quality");
                args.Add("95");
                break;
            case ".gif":
                args.Add("-loop");
                args.Add("0");
                break;
            default:
                break;
        }
    }

    private async Task<ImageRotateResultDto> RotateAsync(BaseItem item, int degrees)
    {
        var result = new ImageRotateResultDto { ItemId = item.Id };
        var path = item.Path;

        if (!IsValidRotation(degrees))
        {
            result.Error = "Rotation must be 90, 180 or 270 degrees.";
            return result;
        }

        if (item is not Photo)
        {
            result.Error = "Only images can be rotated.";
            return result;
        }

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            result.Error = "The image file could not be found.";
            return result;
        }

        var extension = Path.GetExtension(path);
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, "." + Path.GetFileNameWithoutExtension(path) + ".rotating" + extension);

        var args = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-i", path,
            // Drop metadata: keeping a stale EXIF orientation tag would make viewers
            // rotate the already rotated pixels a second time.
            "-map_metadata", "-1",
            "-vf", GetFilter(degrees)
        };
        AddEncoderArgs(args, extension);
        args.Add(tempPath);

        await _rotateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var (exitCode, error) = await RunFfmpegAsync(args).ConfigureAwait(false);
            if (exitCode != 0)
            {
                result.Error = string.IsNullOrWhiteSpace(error) ? "ffmpeg failed." : error;
                TryDelete(tempPath);
                _logger.LogWarning("Rotating {Path} failed: {Error}", path, result.Error);
                return result;
            }

            System.IO.File.Move(tempPath, path, true);
            result.Success = true;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            result.Error = ex.Message;
            _logger.LogError(ex, "Failed to rotate {Path}", path);
            return result;
        }
        finally
        {
            _rotateLock.Release();
        }

        // Re-probe the item so the stored dimensions and the cached thumbnails follow the new pixels.
        try
        {
            _providerManager.QueueRefresh(
                item.Id,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                    ForceSave = true,
                    IsAutomated = false
                },
                RefreshPriority.High);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not queue a refresh for {Path}", path);
        }

        // Let the library monitor pick the change up so dimensions and cached thumbnails are refreshed.
        try
        {
            _libraryMonitor.ReportFileSystemChanged(directory);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not report the change of {Directory}", directory);
        }

        return result;
    }

    private async Task<(int ExitCode, string Error)> RunFfmpegAsync(List<string> args)
    {
        var startInfo = new ProcessStartInfo(_mediaEncoder.EncoderPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode == 0)
        {
            return (0, string.Empty);
        }

        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tail = lines.Length > 5 ? string.Join('\n', lines[^5..]) : stderr;
        return (process.ExitCode, tail.Trim());
    }

    private void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete {Path}", path);
        }
    }
}
