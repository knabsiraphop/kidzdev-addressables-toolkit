# Addressables Toolkit — Demo

Three scripts, from a full interactive tour down to minimal copy-paste references.

| Script | What it shows | Style |
| --- | --- | --- |
| `AddressablesToolkitFullDemo` | The whole toolkit end to end | Interactive (on-screen buttons) |
| `AddressablesBootstrapDemo` | High-level flow: `AddressablesService` + `AssetScope` | Auto-runs on Play |
| `AddressablesToolkitDemo` | Low-level tools used directly | Auto-runs on Play |

Use **one** script per GameObject. Start with the full demo.

## Setup (once)

1. Create a prefab named `demo-prefab` (e.g. a Cube). Optionally a sprite named `demo-sprite`.
2. Select it → right-click → **Addressables Toolkit > Mark Addressable (address = name)**.
3. Create the settings asset: **Tools > Addressables Toolkit > Settings**
   (lands in `Assets/Resources/`). Leave **Content Source = Local** to try it offline.

## `AddressablesToolkitFullDemo` — interactive

The recommended starting point. It builds its own UI with IMGUI, so it needs **no scene, canvas,
or UI prefabs** — just the addressable keys.

1. Add an empty GameObject, attach `AddressablesToolkitFullDemo`.
2. Set `prefabAddress` / `spriteAddress` to your addresses (optionally assign an `AssetReference`).
3. Press Play. Use the on-screen panel:
   - **1 · Initialize** — runs `AddressablesService.InitializeAsync` from your settings, with a live
     progress bar and a download-confirm dialog. Shows the resolved environment/CDN/version.
   - **2 · Load / Instantiate / Release** — instantiate (by key *and* by `AssetReference`), spawn
     pooled, load a sprite (drawn top-right), release one item early, or **Dispose scope** to release
     everything at once.

Every borrow goes through a single `AssetScope`; disposing it (or destroying the demo object)
releases all of it — the toolkit's memory-safety model in action.

## `AddressablesBootstrapDemo` — high-level, minimal

The same flow without UI: `await AddressablesService.InitializeAsync(...)`, then load/instantiate
through `this.GetAssetScope()` (auto-released when the GameObject is destroyed). Watch the Console.

## `AddressablesToolkitDemo` — low-level, minimal

Uses `AssetLoader`, `AddressablePool`, `DownloadHelper`, and `RemoteContentUpdater` directly, with
manual release. Local assets report download size `0` — `DownloadHelper`/`RemoteContentUpdater` are
for remote (CDN) content.

## ComponentReference

```csharp
[Serializable] public class RigidbodyRef : ComponentReference<Rigidbody> { }

[SerializeField] RigidbodyRef body;
var handle = body.InstantiateComponentAsync(transform);
Rigidbody rb = await handle;
body.ReleaseInstance(handle);
```
