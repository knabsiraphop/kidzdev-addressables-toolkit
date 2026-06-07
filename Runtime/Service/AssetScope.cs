using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// A disposable, lifetime-bound owner for Addressable assets and instances. Everything
    /// loaded, instantiated, or pooled through a scope is tracked and released together when
    /// the scope is disposed — so a feature/screen/scene can load freely and never leak: you
    /// release the scope, not each asset. Accepts a plain key <em>or</em> an
    /// <see cref="AssetReference"/> everywhere.
    /// </summary>
    /// <remarks>
    /// Bind a scope to a GameObject with <c>this.GetAssetScope()</c> (see
    /// <see cref="AssetScopeExtensions"/>) and it disposes automatically on destroy — the
    /// single biggest leak-prevention win over calling <see cref="AssetLoader"/> directly.
    /// Not thread-safe; use from the main thread, like the rest of Addressables.
    /// </remarks>
    public sealed class AssetScope : IDisposable
    {
        private readonly List<(object key, Type type)> _borrows = new();
        private readonly List<GameObject> _instances = new();
        private readonly List<GameObject> _pooled = new();
        private bool _disposed;

        /// <summary>True once <see cref="Dispose"/> has run; further loads throw.</summary>
        public bool IsDisposed => _disposed;

        // --- Loading (ref-counted via AssetLoader) ---------------------------

        /// <summary>Load an asset, tracked for release when the scope is disposed.</summary>
        public async UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            var asset = await AssetLoader.LoadAsync<T>(key, ct);

            // If the scope was disposed while the load was in flight, don't leak the borrow.
            if (_disposed)
            {
                AssetLoader.Release<T>(key);
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            _borrows.Add((key, typeof(T)));
            return asset;
        }

        /// <summary>Load the asset behind an <see cref="AssetReference"/>, tracked for release.</summary>
        public UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (reference == null || !reference.RuntimeKeyIsValid())
                throw new ArgumentException("AssetReference is null or unassigned.", nameof(reference));
            return LoadAsync<T>(reference.RuntimeKey, ct);
        }

        // --- Instantiating (Addressables-owned instances) -------------------

        /// <summary>Instantiate a prefab; the instance is released on dispose.</summary>
        public async UniTask<GameObject> InstantiateAsync(object key, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var handle = Addressables.InstantiateAsync(key, parent);
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                if (handle.IsValid()) Addressables.Release(handle);
                throw;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                if (handle.IsValid()) Addressables.Release(handle);
                throw handle.OperationException ?? new InvalidOperationException($"Failed to instantiate '{key}'.");
            }

            var instance = handle.Result;
            if (_disposed)
            {
                Addressables.ReleaseInstance(instance);
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            _instances.Add(instance);
            return instance;
        }

        /// <summary>Instantiate a prefab from an <see cref="AssetReference"/>; released on dispose.</summary>
        public UniTask<GameObject> InstantiateAsync(AssetReference reference, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (reference == null || !reference.RuntimeKeyIsValid())
                throw new ArgumentException("AssetReference is null or unassigned.", nameof(reference));
            return InstantiateAsync(reference.RuntimeKey, parent, ct);
        }

        /// <summary>Instantiate a prefab and return a component on it. Throws if absent.</summary>
        public async UniTask<T> InstantiateAsync<T>(object key, Transform parent = null, CancellationToken ct = default)
            where T : Component
        {
            var go = await InstantiateAsync(key, parent, ct);
            var component = go.GetComponent<T>();
            if (component == null)
            {
                ReleaseInstance(go);
                throw new InvalidOperationException($"Instantiated prefab '{key}' has no {typeof(T).Name}.");
            }
            return component;
        }

        // --- Pooling (returned to AddressablePool) --------------------------

        /// <summary>Borrow a pooled instance; returned to its pool on dispose.</summary>
        public async UniTask<GameObject> SpawnPooledAsync(object key, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var instance = await AddressablePool.GetAsync(key, parent, ct);

            if (_disposed)
            {
                AddressablePool.Release(instance);
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            _pooled.Add(instance);
            return instance;
        }

        // --- Early, explicit release of a single item -----------------------

        /// <summary>Release one instantiated object before the scope is disposed.</summary>
        public void ReleaseInstance(GameObject instance)
        {
            if (instance != null && _instances.Remove(instance))
                Addressables.ReleaseInstance(instance);
        }

        /// <summary>Return one pooled instance before the scope is disposed.</summary>
        public void ReleasePooled(GameObject instance)
        {
            if (_pooled.Remove(instance))
                AddressablePool.Release(instance);
        }

        // --- Teardown -------------------------------------------------------

        /// <summary>Release every asset, instance, and pooled object this scope owns. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Instances and pooled objects first (they hold their own handles), then borrows.
            foreach (var instance in _instances)
                if (instance != null) Addressables.ReleaseInstance(instance);
            _instances.Clear();

            foreach (var instance in _pooled)
                AddressablePool.Release(instance);
            _pooled.Clear();

            foreach (var (key, type) in _borrows)
                AssetLoader.Release(key, type);
            _borrows.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssetScope));
        }
    }
}
