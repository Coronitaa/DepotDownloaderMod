// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;

namespace DepotDownloader;

/// <summary>
/// Describes a download request for one or more Steam depots.
/// Used by <see cref="IDepotDownloadEngine"/> as the input parameter.
/// </summary>
public sealed class DepotDownloadRequest
{
    /// <summary>BlueStar instance ID for state tracking.</summary>
    public Guid InstanceId { get; init; }

    /// <summary>Steam App ID.</summary>
    public uint AppId { get; init; }

    /// <summary>Target installation directory.</summary>
    public required string InstallPath { get; init; }

    /// <summary>List of depots to download.</summary>
    public required IReadOnlyList<DepotDownloadItem> Depots { get; init; }

    /// <summary>Maximum number of concurrent download connections (default: 8).</summary>
    public int MaxConnections { get; init; } = 8;

    /// <summary>
    /// Path to a file containing depot keys in "depotId;hexKey" format per line.
    /// When set, anonymous download with pre-supplied keys is used.
    /// </summary>
    public string? DepotKeysFilePath { get; init; }

    /// <summary>Directory to use for working/temporary files.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Path to the download state file for resume support.
    /// If the file exists, the engine will attempt to resume from it.
    /// </summary>
    public string? StateFilePath { get; init; }

    /// <summary>If true, validate existing files against manifest checksums (re-download corrupted chunks only).</summary>
    public bool ValidateExisting { get; init; } = true;
}

/// <summary>
/// Describes a single depot to download.
/// </summary>
public sealed record DepotDownloadItem
{
    /// <summary>Steam Depot ID.</summary>
    public uint DepotId { get; init; }

    /// <summary>Steam Manifest ID.</summary>
    public ulong ManifestId { get; init; }

    /// <summary>Expected depot size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Human-readable depot name.</summary>
    public string? Name { get; init; }

    /// <summary>Hex-encoded depot decryption key.</summary>
    public string? DepotKey { get; init; }

    /// <summary>
    /// Absolute path to the .manifest file for this depot.
    /// When set, the engine uses this local manifest instead of downloading from Steam.
    /// </summary>
    public string? ManifestFilePath { get; init; }
}
