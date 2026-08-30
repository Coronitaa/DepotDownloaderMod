// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDownloader;

/// <summary>
/// Persistable snapshot of an active download's state.
/// Serialized to JSON and saved to disk periodically so downloads can be
/// resumed after application restarts.
/// </summary>
public sealed class DownloadStateSnapshot
{
    /// <summary>BlueStar instance ID this download belongs to.</summary>
    public Guid InstanceId { get; set; }

    /// <summary>Steam AppId being downloaded.</summary>
    public uint AppId { get; set; }

    /// <summary>Per-depot download progress.</summary>
    public List<DepotDownloadState> Depots { get; set; } = [];

    /// <summary>When the download was first started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Last time this snapshot was updated.</summary>
    public DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>Target installation directory.</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>Path to the depot keys file.</summary>
    public string? DepotKeysFilePath { get; set; }

    /// <summary>Path to manifest files directory.</summary>
    public string? ManifestDirectory { get; set; }

    /// <summary>Total bytes across all depots.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Total bytes downloaded across all depots.</summary>
    public long DownloadedBytes { get; set; }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Saves this snapshot to a JSON file atomically (write-then-rename).</summary>
    public void SaveToFile(string filePath)
    {
        LastUpdatedAt = DateTimeOffset.UtcNow;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmpPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(this, _jsonOptions);
        File.WriteAllText(tmpPath, json);

        // Atomic rename
        File.Move(tmpPath, filePath, overwrite: true);
    }

    /// <summary>Loads a snapshot from a JSON file. Returns null if file doesn't exist or is corrupt.</summary>
    public static DownloadStateSnapshot? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<DownloadStateSnapshot>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes the state file if it exists.</summary>
    public static void DeleteFile(string filePath)
    {
        try { File.Delete(filePath); } catch { }
        try { File.Delete(filePath + ".tmp"); } catch { }
    }
}

/// <summary>
/// Tracks the download state of an individual depot, including which chunks have been completed.
/// </summary>
public sealed class DepotDownloadState
{
    /// <summary>Steam Depot ID.</summary>
    public uint DepotId { get; set; }

    /// <summary>Manifest ID.</summary>
    public ulong ManifestId { get; set; }

    /// <summary>Total size in bytes for this depot.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Bytes downloaded so far.</summary>
    public long DownloadedBytes { get; set; }

    /// <summary>Total chunks in the manifest.</summary>
    public int TotalChunks { get; set; }

    /// <summary>Chunks completed so far.</summary>
    public int CompletedChunks { get; set; }

    /// <summary>Set of chunk IDs (hex strings) that have been successfully downloaded and written.</summary>
    public HashSet<string> CompletedChunkIds { get; set; } = [];

    /// <summary>Whether this depot is fully downloaded.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Path to the manifest file for this depot.</summary>
    public string? ManifestFilePath { get; set; }
}
