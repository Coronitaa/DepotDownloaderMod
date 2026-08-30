// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DepotDownloader;

/// <summary>
/// Public API for the depot download engine.
/// Replaces the CLI-based invocation pattern with a direct in-process library call.
/// </summary>
public interface IDepotDownloadEngine : IDisposable
{
    /// <summary>
    /// Downloads the depots specified in the request.
    /// Supports resumption from a previously saved <see cref="DownloadStateSnapshot"/>.
    /// Reports progress via <paramref name="progress"/> at a throttled rate (~500ms).
    /// </summary>
    /// <param name="request">The download request specifying depots, keys, and paths.</param>
    /// <param name="progress">Optional progress reporter for UI updates.</param>
    /// <param name="ct">Cancellation token for pause/cancel support.</param>
    /// <returns>The final state snapshot, useful for verifying completion.</returns>
    Task<DownloadStateSnapshot> DownloadAsync(
        DepotDownloadRequest request,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken ct);

    /// <summary>
    /// Validates the files on disk against the manifest checksums.
    /// </summary>
    /// <param name="request">The download request specifying depots and paths.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if all files match their manifest checksums.</returns>
    Task<bool> ValidateAsync(
        DepotDownloadRequest request,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken ct);
}
