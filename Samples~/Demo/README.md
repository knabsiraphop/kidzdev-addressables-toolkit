# Addressables Toolkit — Demo

Shows AssetLoader, AddressablePool, and DownloadHelper.

## Run
1. Create a prefab named `demo-prefab` (e.g. a Cube).
2. Select it, right-click > Addressables Toolkit > Mark Addressable (address = name).
3. Open a scene, add an empty GameObject, attach `AddressablesToolkitDemo`.
4. Press Play and watch the Console.

Local assets report download size 0 — DownloadHelper is for remote (CDN) content.

## ComponentReference
    [Serializable] public class RigidbodyRef : ComponentReference<Rigidbody> { }

    [SerializeField] RigidbodyRef body;
    var handle = body.InstantiateComponentAsync(transform);
    Rigidbody rb = await handle.Task;
    body.ReleaseInstance(handle);
