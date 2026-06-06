using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Reference-counted async loader for Addressable assets.
    /// Identical keys share one handle; the asset is released only when
    /// every borrower has released.
    /// </summary>
    public static class AssetLoader
    {
        private sealed class Entry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
        }

        private static readonly Dictionary<object, Entry> _cache = new();

        public static async Task<T> LoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
            }
            else
            {
                entry = new Entry { Handle = Addressables.LoadAssetAsync<T>(key), RefCount = 1 };
                _cache[key] = entry;
            }

            try
            {
                await entry.Handle.Task;
            }
            catch
            {
                Release(key);
                throw;
            }

            if (ct.IsCancellationRequested)
            {
                Release(key);
                ct.ThrowIfCancellationRequested();
            }

            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Release(key);
                throw new InvalidOperationException($"Failed to load addressable '{key}'.");
            }

            return entry.Handle.Result as T;
        }

        public static void Release(object key)
        {
            if (!_cache.TryGetValue(key, out var entry))
                return;

            if (--entry.RefCount > 0)
                return;

            _cache.Remove(key);
            Addressables.Release(entry.Handle);
        }

        public static bool IsLoaded(object key) => _cache.ContainsKey(key);

        public static void ReleaseAll()
        {
            foreach (var entry in _cache.Values)
                Addressables.Release(entry.Handle);
            _cache.Clear();
        }
    }
}
