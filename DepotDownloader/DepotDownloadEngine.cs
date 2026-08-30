// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamKit2;
using SteamKit2.CDN;

namespace DepotDownloader;

/// <summary>
/// In-process implementation of <see cref="IDepotDownloadEngine"/>.
/// Wraps the existing ContentDownloader static class, adding:
/// - Rich IProgress&lt;DownloadProgressInfo&gt; callbacks
/// - Persistent state snapshots for resume after restart
/// - Proper resource cleanup via IDisposable
/// - Structured logging via ILogger
/// </summary>
public sealed class DepotDownloadEngine : IDepotDownloadEngine
{
    private readonly ILogger<DepotDownloadEngine> _logger;
    private bool _disposed;

    public DepotDownloadEngine(ILogger<DepotDownloadEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<DepotDownloadEngine>.Instance;
    }

    /// <inheritdoc />
    public async Task<DownloadStateSnapshot> DownloadAsync(
        DepotDownloadRequest request,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InstallPath))
            throw new ArgumentException("InstallPath is required.", nameof(request));
        if (request.Depots.Count == 0)
            throw new ArgumentException("At least one depot is required.", nameof(request));

        _logger.LogInformation("Starting download for AppId {AppId} with {DepotCount} depots to {Path}",
            request.AppId, request.Depots.Count, request.InstallPath);

        // 1. Ensure AccountSettingsStore is initialized
        EnsureAccountSettingsLoaded();

        // 2. Ensure Steam3 session is initialized
        EnsureSteam3Initialized();

        // 3. Load or create state snapshot
        var state = !string.IsNullOrWhiteSpace(request.StateFilePath)
            ? DownloadStateSnapshot.LoadFromFile(request.StateFilePath) ?? CreateInitialState(request)
            : CreateInitialState(request);

        // 4. Ensure working directory and target directories exist
        var workDir = request.WorkingDirectory
            ?? Path.Combine(Path.GetTempPath(), "BlueStar", "DepotWork", request.InstanceId.ToString());
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(request.InstallPath);

        var configDir = Path.Combine(request.InstallPath, ".DepotDownloader");
        Directory.CreateDirectory(configDir);

        // 5. Configure the static ContentDownloader
        ContentDownloader.Config = new DownloadConfig
        {
            InstallDirectory = request.InstallPath,
            MaxDownloads = Math.Max(1, request.MaxConnections),
            CellID = 0,
            VerifyAll = request.ValidateExisting,
        };

        // 6. Load depot keys if provided from file
        if (!string.IsNullOrWhiteSpace(request.DepotKeysFilePath) && File.Exists(request.DepotKeysFilePath))
        {
            var keyLines = await File.ReadAllLinesAsync(request.DepotKeysFilePath, ct).ConfigureAwait(false);
            foreach (var line in keyLines)
            {
                var parts = line.Split(';', 2);
                if (parts.Length == 2 && uint.TryParse(parts[0], out var depotId))
                {
                    var keyBytes = Convert.FromHexString(parts[1].Trim());
                    DepotKeyStore.Add(depotId, keyBytes);
                }
            }
        }

        // Also load per-depot keys from the request
        foreach (var depot in request.Depots)
        {
            if (!string.IsNullOrWhiteSpace(depot.DepotKey))
            {
                var keyBytes = Convert.FromHexString(depot.DepotKey);
                DepotKeyStore.Add(depot.DepotId, keyBytes);
            }
        }

        // 7. Ensure local manifest files and .sha checksums are present in .DepotDownloader directory
        foreach (var depot in request.Depots)
        {
            if (!string.IsNullOrWhiteSpace(depot.ManifestFilePath) && File.Exists(depot.ManifestFilePath))
            {
                var manifestFileName = $"{depot.DepotId}_{depot.ManifestId}.manifest";
                var targetInConfig = Path.Combine(configDir, manifestFileName);
                if (!File.Exists(targetInConfig) || new FileInfo(targetInConfig).Length <= 32)
                {
                    File.Copy(depot.ManifestFilePath, targetInConfig, overwrite: true);
                }

                var shaFile = targetInConfig + ".sha";
                if (!File.Exists(shaFile))
                {
                    try
                    {
                        var hash = Util.FileSHAHash(targetInConfig);
                        File.WriteAllBytes(shaFile, hash);
                    }
                    catch { }
                }

                var targetInWork = Path.Combine(workDir, manifestFileName);
                if (!File.Exists(targetInWork))
                {
                    try { File.Copy(targetInConfig, targetInWork, overwrite: true); } catch { }
                }
            }
        }

        long totalBytes = request.Depots.Sum(d => d.SizeBytes);

        try
        {
            // Report Initializing
            ReportProgress(progress, new DownloadProgressInfo
            {
                Phase = DownloadPhase.Initializing,
                TotalBytes = totalBytes,
                TotalDepots = request.Depots.Count,
                Percentage = 0,
                CurrentFile = "Connecting to Steam..."
            });

            var depotManifestPairs = request.Depots
                .Select(d => (d.DepotId, d.ManifestId))
                .ToList();

            // Setup real-time progress hook
            long lastReportedTicks = 0;
            var speedWindow = new Queue<(long ticks, long bytes)>();
            var writeWindow = new Queue<(long ticks, long bytes)>();

            ContentDownloader.ProgressCallback = (downloadedUncompressed, totalUncompressed, networkBytes, currentFile) =>
            {
                var nowTicks = Stopwatch.GetTimestamp();
                var total = totalUncompressed > 0 ? (long)totalUncompressed : totalBytes;
                var downloaded = (long)downloadedUncompressed;

                lock (speedWindow)
                {
                    speedWindow.Enqueue((nowTicks, (long)networkBytes));
                    writeWindow.Enqueue((nowTicks, downloaded));

                    // Keep samples from last 4 seconds for smooth rate calculation
                    var cutoff = nowTicks - (long)(4.0 * Stopwatch.Frequency);
                    while (speedWindow.Count > 2 && speedWindow.Peek().ticks < cutoff)
                        speedWindow.Dequeue();
                    while (writeWindow.Count > 2 && writeWindow.Peek().ticks < cutoff)
                        writeWindow.Dequeue();

                    long netSpeed = 0;
                    if (speedWindow.Count >= 2)
                    {
                        var first = speedWindow.Peek();
                        var last = speedWindow.Last();
                        var elapsedSec = (double)(last.ticks - first.ticks) / Stopwatch.Frequency;
                        if (elapsedSec > 0.05)
                            netSpeed = (long)((last.bytes - first.bytes) / elapsedSec);
                    }

                    long writeSpeed = 0;
                    if (writeWindow.Count >= 2)
                    {
                        var first = writeWindow.Peek();
                        var last = writeWindow.Last();
                        var elapsedSec = (double)(last.ticks - first.ticks) / Stopwatch.Frequency;
                        if (elapsedSec > 0.05)
                            writeSpeed = (long)((last.bytes - first.bytes) / elapsedSec);
                    }

                    var timeSinceLastReport = (double)(nowTicks - lastReportedTicks) / Stopwatch.Frequency;
                    if (timeSinceLastReport >= 0.25 || downloaded >= total)
                    {
                        lastReportedTicks = nowTicks;

                        TimeSpan? eta = null;
                        if (netSpeed > 0 && total > downloaded)
                        {
                            var remainingBytes = total - downloaded;
                            eta = TimeSpan.FromSeconds((double)remainingBytes / netSpeed);
                        }

                        var pct = total > 0 ? Math.Min(99.9, (double)downloaded / total * 100.0) : 0.0;

                        state.DownloadedBytes = downloaded;
                        state.TotalBytes = total;

                        ReportProgress(progress, new DownloadProgressInfo
                        {
                            Phase = DownloadPhase.Downloading,
                            TotalBytes = total,
                            DownloadedBytes = downloaded,
                            Percentage = pct,
                            DownloadBytesPerSec = Math.Max(0, netSpeed),
                            WriteBytesPerSec = Math.Max(0, writeSpeed),
                            EstimatedTimeRemaining = eta,
                            CurrentFile = (currentFile.StartsWith("Preparing") || currentFile.StartsWith("Processing") || currentFile.StartsWith("Allocating")) 
                                ? currentFile 
                                : (Path.GetFileName(currentFile) ?? currentFile),
                            TotalDepots = request.Depots.Count,
                        });
                    }
                }
            };

            try
            {
                ContentDownloader.CancellationToken = ct;

                await ContentDownloader.DownloadAppAsync(
                    request.AppId,
                    depotManifestPairs,
                    ContentDownloader.DEFAULT_BRANCH,
                    null,
                    null,
                    null,
                    false,
                    false
                ).ConfigureAwait(false);
            }
            finally
            {
                ContentDownloader.CancellationToken = CancellationToken.None;
                ContentDownloader.ProgressCallback = null;
            }

            // Mark all depots complete in state
            foreach (var d in state.Depots)
            {
                d.IsComplete = true;
                d.DownloadedBytes = d.TotalBytes;
            }
            state.DownloadedBytes = totalBytes;

            // Final progress report
            ReportProgress(progress, new DownloadProgressInfo
            {
                Phase = DownloadPhase.Completed,
                TotalBytes = totalBytes,
                DownloadedBytes = totalBytes,
                Percentage = 100.0,
                TotalDepots = request.Depots.Count,
                CurrentDepotIndex = request.Depots.Count - 1,
                CurrentFile = "Download completed",
            });

            // Clean up state file on success
            if (!string.IsNullOrWhiteSpace(request.StateFilePath))
            {
                DownloadStateSnapshot.DeleteFile(request.StateFilePath);
            }

            _logger.LogInformation("Download completed successfully for AppId {AppId}", request.AppId);
            return state;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download cancelled for AppId {AppId}", request.AppId);
            SaveStateSafe(request.StateFilePath, state);

            ReportProgress(progress, new DownloadProgressInfo
            {
                Phase = DownloadPhase.Paused,
                TotalBytes = totalBytes,
                DownloadedBytes = state.DownloadedBytes,
                Percentage = totalBytes > 0 ? (double)state.DownloadedBytes / totalBytes * 100.0 : 0,
                CurrentFile = "Download paused"
            });

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for AppId {AppId}: {Message}", request.AppId, ex.Message);
            SaveStateSafe(request.StateFilePath, state);

            ReportProgress(progress, new DownloadProgressInfo
            {
                Phase = DownloadPhase.Failed,
                TotalBytes = totalBytes,
                DownloadedBytes = state.DownloadedBytes,
                CurrentFile = $"Error: {ex.Message}",
            });

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(
        DepotDownloadRequest request,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("Validating files for AppId {AppId}", request.AppId);

        EnsureAccountSettingsLoaded();
        EnsureSteam3Initialized();

        ReportProgress(progress, new DownloadProgressInfo
        {
            Phase = DownloadPhase.Validating,
            TotalBytes = request.Depots.Sum(d => d.SizeBytes),
            TotalDepots = request.Depots.Count,
            CurrentFile = "Validating game files..."
        });

        ContentDownloader.Config = new DownloadConfig
        {
            InstallDirectory = request.InstallPath,
            VerifyAll = true,
            MaxDownloads = Math.Max(1, request.MaxConnections),
        };

        if (!string.IsNullOrWhiteSpace(request.DepotKeysFilePath) && File.Exists(request.DepotKeysFilePath))
        {
            var keyLines = await File.ReadAllLinesAsync(request.DepotKeysFilePath, ct).ConfigureAwait(false);
            foreach (var line in keyLines)
            {
                var parts = line.Split(';', 2);
                if (parts.Length == 2 && uint.TryParse(parts[0], out var depotId))
                {
                    var keyBytes = Convert.FromHexString(parts[1].Trim());
                    DepotKeyStore.Add(depotId, keyBytes);
                }
            }
        }

        try
        {
            var depotManifestPairs = request.Depots
                .Select(d => (d.DepotId, d.ManifestId))
                .ToList();

            await ContentDownloader.DownloadAppAsync(
                request.AppId,
                depotManifestPairs,
                ContentDownloader.DEFAULT_BRANCH,
                null, null, null, false, false
            ).ConfigureAwait(false);

            ReportProgress(progress, new DownloadProgressInfo
            {
                Phase = DownloadPhase.Completed,
                Percentage = 100,
                TotalBytes = request.Depots.Sum(d => d.SizeBytes),
                DownloadedBytes = request.Depots.Sum(d => d.SizeBytes),
                CurrentFile = "Validation successful"
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validation found issues for AppId {AppId}", request.AppId);
            return false;
        }
    }

    private static void EnsureAccountSettingsLoaded()
    {
        if (AccountSettingsStore.Instance == null)
        {
            try
            {
                AccountSettingsStore.LoadFromFile("account.config");
            }
            catch
            {
                // Fallback if already loaded
            }
        }
    }

    private void EnsureSteam3Initialized()
    {
        if (!ContentDownloader.InitializeSteam3(null, null))
        {
            _logger.LogError("Unable to get Steam3 credentials or connect to Steam network.");
            throw new InvalidOperationException("Failed to establish anonymous connection to Steam3.");
        }
    }

    private static DownloadStateSnapshot CreateInitialState(DepotDownloadRequest request)
    {
        return new DownloadStateSnapshot
        {
            InstanceId = request.InstanceId,
            AppId = request.AppId,
            InstallPath = request.InstallPath,
            DepotKeysFilePath = request.DepotKeysFilePath,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TotalBytes = request.Depots.Sum(d => d.SizeBytes),
            Depots = request.Depots.Select(d => new DepotDownloadState
            {
                DepotId = d.DepotId,
                ManifestId = d.ManifestId,
                TotalBytes = d.SizeBytes,
                ManifestFilePath = d.ManifestFilePath,
            }).ToList(),
        };
    }

    private void SaveStateSafe(string? stateFilePath, DownloadStateSnapshot state)
    {
        if (string.IsNullOrWhiteSpace(stateFilePath)) return;
        try
        {
            state.SaveToFile(stateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save download state to {Path}", stateFilePath);
        }
    }

    private static void ReportProgress(IProgress<DownloadProgressInfo>? progress, DownloadProgressInfo info)
    {
        progress?.Report(info);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
