// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;

namespace DepotDownloader;

/// <summary>
/// Rich progress information reported during depot downloads.
/// Designed for direct consumption by BlueStar's UI layer.
/// </summary>
public sealed record DownloadProgressInfo
{
    /// <summary>Current depot being downloaded.</summary>
    public uint DepotId { get; init; }

    /// <summary>Human-readable name of the current depot.</summary>
    public string? DepotName { get; init; }

    /// <summary>Manifest ID being downloaded.</summary>
    public ulong ManifestId { get; init; }

    /// <summary>Total bytes across all depots in this download request.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Total bytes downloaded so far across all depots.</summary>
    public long DownloadedBytes { get; init; }

    /// <summary>Total bytes written to disk (after decompression).</summary>
    public long WrittenBytes { get; init; }

    /// <summary>Current download speed in bytes per second.</summary>
    public double DownloadBytesPerSec { get; init; }

    /// <summary>Current disk write speed in bytes per second.</summary>
    public double WriteBytesPerSec { get; init; }

    /// <summary>Overall percentage (0–100) across all depots.</summary>
    public double Percentage { get; init; }

    /// <summary>Name of the file currently being written.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Number of active CDN connections.</summary>
    public int ActiveConnections { get; init; }

    /// <summary>Total number of chunks to download in the current depot.</summary>
    public int TotalChunks { get; init; }

    /// <summary>Number of chunks already completed.</summary>
    public int CompletedChunks { get; init; }

    /// <summary>Estimated time remaining based on sliding-window speed average.</summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>Current phase of the download operation.</summary>
    public DownloadPhase Phase { get; init; }

    /// <summary>Index of the current depot being processed (0-based).</summary>
    public int CurrentDepotIndex { get; init; }

    /// <summary>Total number of depots in the request.</summary>
    public int TotalDepots { get; init; }
}

/// <summary>
/// Represents the phase of an ongoing download operation.
/// </summary>
public enum DownloadPhase
{
    /// <summary>Setting up session, resolving manifests.</summary>
    Initializing,

    /// <summary>Fetching manifest data from Steam CDN.</summary>
    FetchingManifest,

    /// <summary>Actively downloading file chunks.</summary>
    Downloading,

    /// <summary>Validating downloaded files against manifest checksums.</summary>
    Validating,

    /// <summary>Download completed successfully.</summary>
    Completed,

    /// <summary>Download was paused by the user.</summary>
    Paused,

    /// <summary>Download failed with an error.</summary>
    Failed
}
