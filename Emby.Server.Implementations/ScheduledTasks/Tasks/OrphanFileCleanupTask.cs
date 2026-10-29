using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

/// <summary>
/// Finds sidecar files (posters, fanart, subtitles, nfo) that no longer belong to any media
/// file and moves them out of the library into a dated quarantine folder.
/// </summary>
public class OrphanFileCleanupTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <summary>
    /// Files younger than this are left alone, so freshly copied sidecars are never touched.
    /// </summary>
    private static readonly TimeSpan _minimumAge = TimeSpan.FromHours(24);

    private static readonly HashSet<string> _subtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt", ".smi", ".mpl2", ".sami"
    };

    private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tbn"
    };

    /// <summary>
    /// Suffixes that turn "Name" into a sidecar of "Name", e.g. "Name-poster.jpg" or "Name.poster.jpg".
    /// </summary>
    private static readonly string[] _artworkSuffixes =
    {
        "-poster", "-fanart", "-thumb", "-banner", "-logo", "-disc", "-discart", "-backdrop",
        "-landscape", "-art", "-clearart", "-clearlogo", "-folder", "-cover", "-small",
        ".poster", ".fanart", ".thumb", ".banner", ".logo", ".backdrop", ".landscape", ".art",
        ".clearart", ".clearlogo", ".cover", "-default", ".default"
    };

    /// <summary>
    /// Names that belong to the folder itself rather than to a media file.
    /// </summary>
    private static readonly HashSet<string> _folderLevelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "poster", "fanart", "folder", "backdrop", "banner", "logo", "thumb", "landscape",
        "clearart", "clearlogo", "disc", "discart", "cover", "artist", "album", "movie",
        "tvshow", "season", "series", "collection", "playlist", "theme", "albumartists"
    };

    private static readonly HashSet<string> _skippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir", "#recycle", ".recycle", "recycle", "$RECYCLE.BIN", "System Volume Information",
        ".git", ".stfolder", ".sync"
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<OrphanFileCleanupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanFileCleanupTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public OrphanFileCleanupTask(
        ILibraryManager libraryManager,
        IApplicationPaths applicationPaths,
        ILocalizationManager localization,
        ILogger<OrphanFileCleanupTask> logger)
    {
        _libraryManager = libraryManager;
        _applicationPaths = applicationPaths;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("TaskCleanupOrphanFiles");

    /// <inheritdoc />
    public string Description => _localization.GetLocalizedString("TaskCleanupOrphanFilesDescription");

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");

    /// <inheritdoc />
    public string Key => nameof(OrphanFileCleanupTask);

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(30).Ticks
        };
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var roots = _libraryManager.GetVirtualFolders()
            .SelectMany(f => f.Locations ?? Array.Empty<string>())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roots.Count == 0)
        {
            _logger.LogInformation("Orphan file cleanup: no library folders to scan");
            progress.Report(100);
            return Task.CompletedTask;
        }

        var directories = new List<string>();
        foreach (var root in roots)
        {
            CollectDirectories(root, directories, cancellationToken);
        }

        var quarantineRoot = Path.Combine(
            _applicationPaths.DataPath,
            "orphan-files",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        var scanned = 0;
        var moved = 0;

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                moved += CleanDirectory(directory, quarantineRoot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Orphan file cleanup failed in {Directory}", directory);
            }

            scanned++;
            progress.Report(100.0 * scanned / directories.Count);
        }

        if (moved > 0)
        {
            _logger.LogInformation(
                "Orphan file cleanup moved {Moved} file(s) from {Scanned} folder(s) to {Quarantine}",
                moved,
                scanned,
                quarantineRoot);
        }
        else
        {
            _logger.LogInformation("Orphan file cleanup found nothing to move in {Scanned} folder(s)", scanned);
        }

        progress.Report(100);
        return Task.CompletedTask;
    }

    private static void CollectDirectories(string path, List<string> directories, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        directories.Add(path);

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(path);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || _skippedDirectoryNames.Contains(name))
            {
                continue;
            }

            CollectDirectories(child, directories, cancellationToken);
        }
    }

    private int CleanDirectory(string directory, string quarantineRoot, CancellationToken cancellationToken)
    {
        string[] files;
        string[] subDirectories;
        try
        {
            files = Directory.GetFiles(directory);
            subDirectories = Directory.GetDirectories(directory);
        }
        catch (Exception)
        {
            return 0;
        }

        // Every media file is known by its name without the extension, and every folder can own artwork too.
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            knownNames.Add(Path.GetFileNameWithoutExtension(file));
        }

        foreach (var subDirectory in subDirectories)
        {
            knownNames.Add(Path.GetFileName(subDirectory));
        }

        var moved = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.'))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            var isSubtitle = _subtitleExtensions.Contains(extension);
            var isImage = _imageExtensions.Contains(extension);
            var isNfo = string.Equals(extension, ".nfo", StringComparison.OrdinalIgnoreCase);
            if (!isSubtitle && !isImage && !isNfo)
            {
                continue;
            }

            var baseName = GetSidecarBaseName(Path.GetFileNameWithoutExtension(file), isImage, isNfo);
            if (string.IsNullOrEmpty(baseName) || _folderLevelNames.Contains(baseName))
            {
                continue;
            }

            if (knownNames.Contains(baseName))
            {
                continue;
            }

            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < _minimumAge)
                {
                    continue;
                }

                var target = GetQuarantinePath(quarantineRoot, directory, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target);
                _logger.LogInformation("Moved orphan file {File} to {Target}", file, target);
                moved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not move orphan file {File}", file);
            }
        }

        return moved;
    }

    private static string GetSidecarBaseName(string nameWithoutExtension, bool isImage, bool isNfo)
    {
        if (isImage)
        {
            foreach (var suffix in _artworkSuffixes)
            {
                if (nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var baseName = nameWithoutExtension[..^suffix.Length];
                    return baseName.Length == 0 ? string.Empty : baseName;
                }
            }

            return string.Empty;
        }

        if (isNfo)
        {
            return nameWithoutExtension;
        }

        // Subtitles: "Name.zh-CN.forced.srt" belongs to "Name".
        var trimmed = nameWithoutExtension;
        while (true)
        {
            var dot = trimmed.LastIndexOf('.');
            if (dot <= 0)
            {
                break;
            }

            var candidate = trimmed[(dot + 1)..];
            var isLanguage = candidate.Length is 2 or 3 or 5
                && candidate.All(c => char.IsLetter(c) || c == '-' || c == '_');
            var isFlag = candidate.Equals("forced", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("default", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("sdh", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("cc", StringComparison.OrdinalIgnoreCase);

            if (!isLanguage && !isFlag)
            {
                break;
            }

            trimmed = trimmed[..dot];
        }

        return trimmed;
    }

    private static string GetQuarantinePath(string quarantineRoot, string directory, string fileName)
    {
        var safeDirectoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(safeDirectoryName))
        {
            safeDirectoryName = "root";
        }

        var targetDirectory = Path.Combine(quarantineRoot, safeDirectoryName);
        var target = Path.Combine(targetDirectory, fileName);

        var index = 1;
        while (File.Exists(target))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            target = Path.Combine(targetDirectory, $"{name} ({index}){extension}");
            index++;
        }

        return target;
    }
}
