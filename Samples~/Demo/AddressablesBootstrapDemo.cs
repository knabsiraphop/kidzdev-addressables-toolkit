using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Samples
{
    /// <summary>
    /// End-to-end "real project" flow: drive initialization from settings, then load/instantiate
    /// through a GameObject-bound <see cref="AssetScope"/> that auto-releases on destroy.
    ///
    /// Setup: create the settings asset (Tools > Addressables Toolkit > Settings), configure your
    /// content source / environment / preload labels, then put this on a GameObject and press Play.
    /// </summary>
    public class AddressablesBootstrapDemo : MonoBehaviour
    {
        [Header("Set to an address that exists in your Addressables groups")]
        [SerializeField] private string prefabAddress = "demo-prefab";

        private async UniTaskVoid Start()
        {
            // Watch state transitions (drive your loading-screen UI from this).
            AddressablesService.StateChanged += OnStateChanged;

            try
            {
                // 1) Initialize using the settings asset: CDN override → init → catalog → preload.
                var progress = new Progress<DownloadProgress>(p => Debug.Log($"[Bootstrap] download {p.Percent:P0}"));
                bool ready = await AddressablesService.InitializeAsync(
                    progress: progress,
                    confirm: bytes => ShowConfirmAsync(bytes));

                if (!ready)
                {
                    Debug.LogError($"[Bootstrap] Not ready: {AddressablesService.LastDownloadResult.Outcome}.");
                    return;
                }

                // 2) Load/instantiate through a scope bound to THIS GameObject. Everything below
                //    is released automatically when this object is destroyed — no manual cleanup.
                var scope = this.GetAssetScope();

                var instance = await scope.InstantiateAsync(prefabAddress, parent: transform);
                instance.transform.position = Vector3.zero;
                Debug.Log($"[Bootstrap] Instantiated '{prefabAddress}' inside a lifetime-bound scope.");

                // Pooled spawns work the same way and return to the pool on dispose.
                for (int i = 0; i < 3; i++)
                {
                    var pooled = await scope.SpawnPooledAsync(prefabAddress);
                    pooled.transform.position = new Vector3(i + 1, 0f, 0f);
                }
                Debug.Log("[Bootstrap] Spawned 3 pooled instances. Destroy this object to release everything.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bootstrap] Failed — is '{prefabAddress}' valid and initialization configured? {e.Message}");
            }
        }

        private static UniTask<bool> ShowConfirmAsync(long bytes)
        {
            // Replace with a real popup. Auto-confirm here for the demo.
            Debug.Log($"[Bootstrap] Confirm download of {bytes} bytes (auto-yes in demo).");
            return UniTask.FromResult(true);
        }

        private static void OnStateChanged(AddressablesState state)
            => Debug.Log($"[Bootstrap] State → {state}");

        private void OnDestroy()
        {
            AddressablesService.StateChanged -= OnStateChanged;
            // No asset cleanup needed: the AssetScope bound to this GameObject disposes itself.
        }
    }
}
