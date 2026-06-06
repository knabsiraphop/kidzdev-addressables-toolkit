using KidzDev.AddressablesToolkit;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Samples
{
    /// <summary>
    /// Minimal runnable demo (loader + pool + download). Mark a prefab addressable,
    /// set its address below, then press Play. See README for setup steps.
    /// </summary>
    public class AddressablesToolkitDemo : MonoBehaviour
    {
        [Header("Set to an address that exists in your Addressables groups")]
        [SerializeField] private string prefabAddress = "demo-prefab";

        private async void Start()
        {
            try
            {
                // 1) AssetLoader
                var prefab = await AssetLoader.LoadAsync<GameObject>(prefabAddress);
                var single = Instantiate(prefab, new Vector3(-2f, 0f, 0f), Quaternion.identity);
                Debug.Log($"[Demo] Loaded and instantiated '{prefabAddress}'.");

                // 2) AddressablePool
                await AddressablePool.Prewarm(prefabAddress, 3);
                for (int i = 0; i < 3; i++)
                {
                    var pooled = await AddressablePool.GetAsync(prefabAddress);
                    pooled.transform.position = new Vector3(i, 0f, 0f);
                }
                Debug.Log("[Demo] Spawned 3 instances from the pool.");

                // 3) DownloadHelper (0 bytes for local assets; real value for remote/CDN)
                long size = await DownloadHelper.GetDownloadSizeAsync(prefabAddress);
                Debug.Log($"[Demo] Download size for '{prefabAddress}': {size} bytes (0 = local).");
                if (size > 0)
                {
                    var progress = new System.Progress<DownloadProgress>(p => Debug.Log($"[Demo] {p.Percent:P0}"));
                    await DownloadHelper.DownloadAsync(prefabAddress, progress);
                }

                Destroy(single);
                AssetLoader.Release(prefabAddress);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Demo] Failed — is '{prefabAddress}' a valid addressable address? {e.Message}");
            }
        }

        private void OnDestroy() => AddressablePool.ClearPool(prefabAddress);
    }
}
