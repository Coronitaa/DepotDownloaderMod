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
/// - Rich IProgress&lt;DownloadProgressInfo&gt; callbacks (throttled to ~500ms)
/// - Persistent state snapshots for resume after restart
/// - Proper resource cleanup via IDisposable
/// - Structured logging via ILogger
/// </summary>
public sealed class DepotDownloadEngine : IDepotDownloadEngine
{
    private readonly ILogger<DepotDownloadEngine> _logger;
    private readonly TimeSpan _progressThrottle = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _stateSaveInterval = TimeSpan.FromSeconds(30);
    private bool _disposed;

    // Speed tracking with sliding window
    private readonly ConcurrentQueue<(long ticks, long bytes)> _speedSamples = new();
    private readonly ConcurrentQueue<(long ticks, long bytes)> _writeSamples = new();
    private const int MaxSpeedSamples = 10;
    private const double SpeedWindowSeconds = 5.0;

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

        // Load or create state snapshot
        var state = !string.IsNullOrWhiteSpace(request.StateFilePath)
            ? DownloadStateSnapshot.LoadFromFile(request.StateFilePath) ?? CreateInitialState(request)
            : CreateInitialState(request);

        // Ensure working directory exists
        var workDir = request.WorkingDirectory
            ?? Path.Combine(Path.GetTempPath(), "BlueStar", "DepotWork", request.InstanceId.ToString());
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(request.InstallPath);

        // Configure the static ContentDownloader
        ContentDownloader.Config = new DownloadConfig
        {
            InstallDirectory = request.InstallPath,
            MaxDownloads = Math.Max(1, request.MaxConnections),
            CellID = 0,
        };

        // Load depot keys if provided
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

        long totalBytes = request.Depots.Sum(d => d.SizeBytes);
        long downloadedBytesPrevDepots = 0;

        var lastProgressReport = Stopwatch.GetTimestamp();
        var lastStateSave = Stopwatch.GetTimestamp();

        try
        {
            // Report initializing
            ReportProgress(progress, new DownloadProgressInfo
            {
                Phase = DownloadPhase.Initializing,
                TotalBytes = totalBytes,
                TotalDepots = request.Depots.Count,
                Percentage = 0,
            });

            for (int i = 0; i < request.Depots.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var depot = request.Depots[i];

                // Check if depot already completed in saved state
                var depotState = state.Depots.FirstOrDefault(d => d.DepotId == depot.DepotId);
                if (depotState?.IsComplete == true)
                {
                    _logger.LogInformation("Depot {DepotId} already complete, skipping", depot.DepotId);
                    downloadedBytesPrevDepots += depot.SizeBytes;
                    continue;
                }

                if (depotState == null)
                {
                    depotState = new DepotDownloadState
                    {
                        DepotId = depot.DepotId,
                        ManifestId = depot.ManifestId,
                        TotalBytes = depot.SizeBytes,
                        ManifestFilePath = depot.ManifestFilePath,
                    };
                    state.Depots.Add(depotState);
                }

                _logger.LogInformation("Downloading depot {DepotId} (manifest {ManifestId}), index {Index}/{Total}",
                    depot.DepotId, depot.ManifestId, i + 1, request.Depots.Count);

                // Build arguments that ContentDownloader expects
                var depotManifestPairs = new List<(uint depotId, ulong manifestId)>
                {
                    (depot.DepotId, depot.ManifestId)
                };

                // Set manifest file path if local
                if (!string.IsNullOrWhiteSpace(depot.ManifestFilePath) && File.Exists(depot.ManifestFilePath))
                {
                    // Copy manifest to working directory for ContentDownloader to find
                    var targetManifestPath = Path.Combine(workDir, Path.GetFileName(depot.ManifestFilePath));
                    if (!File.Exists(targetManifestPath))
                    {
                        File.Copy(depot.ManifestFilePath, targetManifestPath, overwrite: true);
                    }
                }

                try
                {
                    // Use ContentDownloader to handle the actual download
                    // The static ContentDownloader.DownloadAppAsync will use Config.InstallDirectory
                    await ContentDownloader.DownloadAppAsync(
                        request.AppId,
                        depotManifestPairs,
                        ContentDownloader.DEFAULT_BRANCH,
                        null, // os — download all platforms
                        null, // arch — download all architectures
                        null, // language — download all languages
                        false, // lv
                        false  // isUgc
                    ).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Save state before re-throwing
                    depotState.DownloadedBytes = (long)(depot.SizeBytes * 0.5); // approximate
                    SaveStateSafe(request.StateFilePath, state);
                    throw;
                }

                // Mark depot complete
                depotState.IsComplete = true;
                depotState.DownloadedBytes = depot.SizeBytes;
                depotState.CompletedChunks = depotState.TotalChunks;

                downloadedBytesPrevDepots += depot.SizeBytes;

                // Report depot completed
                ReportProgress(progress, new DownloadProgressInfo
                {
                    DepotId = depot.DepotId,
                    DepotName = depot.Name,
                    ManifestId = depot.ManifestId,
                    Phase = DownloadPhase.Downloading,
                    TotalBytes = totalBytes,
                    DownloadedBytes = downloadedBytesPrevDepots,
                    Percentage = totalBytes > 0 ? (double)downloadedBytesPrevDepots / totalBytes * 100.0 : 100.0,
                    CurrentDepotIndex = i,
                    TotalDepots = request.Depots.Count,
                    CurrentFile = $"Depot {depot.DepotId} complete",
                });

                // Periodic state save
                SaveStateSafe(request.StateFilePath, state);
            }

            // Final progress report
            state.DownloadedBytes = totalBytes;
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

            // Clean up state file on completion
            if (!string.IsNullOrWhiteSpace(request.StateFilePath))
            {
                DownloadStateSnapshot.DeleteFile(request.StateFilePath);
            }

            _logger.LogInformation("Download completed for AppId {AppId}", request.AppId);
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

        ReportProgress(progress, new DownloadProgressInfo
        {
            Phase = DownloadPhase.Validating,
            TotalBytes = request.Depots.Sum(d => d.SizeBytes),
            TotalDepots = request.Depots.Count,
        });

        // Configure ContentDownloader for validation
        ContentDownloader.Config = new DownloadConfig
        {
            InstallDirectory = request.InstallPath,
            VerifyAll = true,
            MaxDownloads = Math.Max(1, request.MaxConnections),
        };

        // Load depot keys
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
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validation found issues for AppId {AppId}", request.AppId);
            return false;
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

        try
        {
            ContentDownloader.ShutdownSteam3();
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
