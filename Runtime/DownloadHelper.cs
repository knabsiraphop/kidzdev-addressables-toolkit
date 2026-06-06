using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>Progress snapshot for a remote-content download.</summary>
    public readonly struct DownloadProgress
    {
        public readonly float Percent;        // 0..1
        public readonly long DownloadedBytes;
        public readonly long TotalBytes;

        internal DownloadProgress(DownloadStatus status)
        {
            Percent = status.Percent;
            DownloadedBytes = status.DownloadedBytes;
            TotalBytes = status.TotalBytes;
        }
    }

    /// <summary>
    /// Helpers for remote Addressable content: query download size, predownload
    /// bundles with progress, and clear the download cache.
    /// </summary>
    public static class DownloadHelper
    {
        /// <summary>Bytes still needing download for a key/label (0 = already cached).</summary>
        public static async Task<long> GetDownloadSizeAsync(object key)
        {
            var handle = Addressables.GetDownloadSizeAsync(key);
            try
            {
                await handle.Task;
                return handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : 0L;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        /// <summary>Download bundles for a key/label, reporting progress until done.</summary>
        public static async Task DownloadAsync(object key, IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            var handle = Addressables.DownloadDependenciesAsync(key, false);
            try
            {
                while (!handle.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new DownloadProgress(handle.GetDownloadStatus()));
                    await Task.Yield();
                }

                await handle.Task;
                progress?.Report(new DownloadProgress(handle.GetDownloadStatus()));

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException($"Failed to download dependencies for '{key}'.");
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        /// <summary>Clear cached bundles for a key/label. Returns true on success.</summary>
        public static async Task<bool> ClearCacheAsync(object key)
        {
            var handle = Addressables.ClearDependencyCacheAsync(key, false);
            try
            {
                await handle.Task;
                return handle.Status == AsyncOperationStatus.Succeeded && handle.Result;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }
    }
}
