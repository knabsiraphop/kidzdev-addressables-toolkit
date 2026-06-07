using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>Lifecycle of the Addressables runtime, as driven by <see cref="AddressablesService"/>.</summary>
    public enum AddressablesState
    {
        Uninitialized,
        Initializing,
        UpdatingCatalog,
        DownloadingContent,
        Ready,
        Failed
    }

    /// <summary>
    /// The single entry point that takes Addressables from launch to ready-to-use, driven
    /// entirely by <see cref="AddressablesToolkitSettings"/>:
    /// <list type="number">
    ///   <item>install the CDN override (remote + overrideRemoteUrl),</item>
    ///   <item><c>Addressables.InitializeAsync</c>,</item>
    ///   <item>check &amp; apply catalog updates (remote),</item>
    ///   <item>predownload the preload labels (remote),</item>
    ///   <item>transition to <see cref="AddressablesState.Ready"/>.</item>
    /// </list>
    /// Calls are idempotent and join a single in-flight initialization, so any number of
    /// systems can <c>await AddressablesService.InitializeAsync()</c> before touching content.
    /// Local content skips the CDN/catalog/download steps entirely.
    /// </summary>
    public static class AddressablesService
    {
        /// <summary>Current lifecycle state.</summary>
        public static AddressablesState State { get; private set; } = AddressablesState.Uninitialized;

        /// <summary>True once initialization has completed and content can be loaded.</summary>
        public static bool IsReady => State == AddressablesState.Ready;

        /// <summary>Raised on every state transition (on the calling/main thread).</summary>
        public static event Action<AddressablesState> StateChanged;

        /// <summary>Result of the most recent preload-download step (default if none ran).</summary>
        public static DownloadResult LastDownloadResult { get; private set; }

        // A nullable preserved task so concurrent callers join one init; cleared on failure to allow retry.
        private static UniTask<bool>? _inFlight;

        /// <summary>Initialize using the active <see cref="AddressablesToolkitSettings.Instance"/>.</summary>
        public static UniTask<bool> InitializeAsync(
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default)
            => InitializeAsync(AddressablesToolkitSettings.Instance, progress, confirm, ct);

        /// <summary>
        /// Initialize using explicit settings. Returns true when the runtime reaches
        /// <see cref="AddressablesState.Ready"/>; false on failure, cancellation, or a declined
        /// download (inspect <see cref="LastDownloadResult"/> to branch). Safe to call repeatedly:
        /// a ready service returns immediately and concurrent callers share one init.
        /// </summary>
        public static UniTask<bool> InitializeAsync(
            AddressablesToolkitSettings settings,
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default)
        {
            if (IsReady)
                return UniTask.FromResult(true);

            if (_inFlight.HasValue)
                return _inFlight.Value;

            var task = RunAsync(settings, progress, confirm, ct).Preserve();
            _inFlight = task;
            return AwaitAndUnlatch(task);
        }

        private static async UniTask<bool> AwaitAndUnlatch(UniTask<bool> task)
        {
            var ok = await task;
            if (!ok)
                _inFlight = null; // failed/declined: allow a fresh attempt (e.g. after reconnect)
            return ok;
        }

        private static async UniTask<bool> RunAsync(
            AddressablesToolkitSettings settings,
            IProgress<DownloadProgress> progress,
            RemoteContentUpdater.ConfirmDownload confirm,
            CancellationToken ct)
        {
            if (settings == null)
            {
                Debug.LogError("[AddressablesToolkit] InitializeAsync called with null settings.");
                SetState(AddressablesState.Failed);
                return false;
            }

            var remote = settings.contentSource == ContentSource.Remote;

            try
            {
                SetState(AddressablesState.Initializing);

                // 1) Point Addressables at the active environment's CDN, if requested.
                if (remote && settings.overrideRemoteUrl)
                {
                    var env = settings.ResolveEnvironment();
                    if (env == null || string.IsNullOrEmpty(env.CdnBaseUrl))
                    {
                        Debug.LogError("[AddressablesToolkit] overrideRemoteUrl is on but no environment / CDN URL is configured.");
                        SetState(AddressablesState.Failed);
                        return false;
                    }

                    var version = settings.ResolveVersion();
                    AddressableCdn.Install(env.CdnBaseUrl, version, settings.ResolvePlatformFolder());
                    Log(settings, $"CDN override → {env.Name}: {env.CdnBaseUrl} (v{version}).");
                }

                // 2) Initialize the Addressables system.
                await Addressables.InitializeAsync().ToUniTask(cancellationToken: ct);
                Log(settings, "Addressables initialized.");

                // 3) Apply catalog updates so sizing/downloading see the latest bundles.
                if (remote && settings.checkCatalogUpdates)
                {
                    SetState(AddressablesState.UpdatingCatalog);
                    var updated = await RemoteContentUpdater.CheckAndUpdateCatalogsAsync(ct);
                    Log(settings, updated.Count > 0
                        ? $"Updated {updated.Count} catalog(s)."
                        : "Catalogs already current.");
                }

                // 4) Predownload the configured preload labels.
                if (remote && settings.predownloadPreloadContent)
                {
                    var keys = settings.GetPreloadKeys();
                    if (keys.Count > 0)
                    {
                        SetState(AddressablesState.DownloadingContent);
                        var result = await RemoteContentUpdater.RunAsync(keys, progress, confirm, ct);
                        LastDownloadResult = result;
                        Log(settings, $"Preload: {result.Outcome} ({result.Bytes} bytes).");

                        if (!result.IsSuccess)
                        {
                            SetState(AddressablesState.Failed);
                            return false;
                        }
                    }
                }

                SetState(AddressablesState.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                Log(settings, "Initialization cancelled.");
                SetState(AddressablesState.Failed);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AddressablesToolkit] Initialization failed: {e}");
                SetState(AddressablesState.Failed);
                return false;
            }
        }

        /// <summary>
        /// Reset to <see cref="AddressablesState.Uninitialized"/> so a later
        /// <see cref="InitializeAsync(IProgress{DownloadProgress},RemoteContentUpdater.ConfirmDownload,CancellationToken)"/>
        /// re-runs the flow. Does not release loaded assets — use <see cref="AssetScope"/> /
        /// <see cref="AssetLoader.ReleaseAll"/> for that.
        /// </summary>
        public static void Reset()
        {
            _inFlight = null;
            SetState(AddressablesState.Uninitialized);
        }

        private static void SetState(AddressablesState state)
        {
            if (State == state)
                return;

            State = state;
            try
            {
                StateChanged?.Invoke(state);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AddressablesToolkit] A StateChanged handler threw: {e}");
            }
        }

        private static void Log(AddressablesToolkitSettings settings, string message)
        {
            if (settings != null && settings.verboseLogging)
                Debug.Log($"[AddressablesToolkit] {message}");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            var settings = AddressablesToolkitSettings.Instance;
            if (settings != null && settings.autoInitializeOnLaunch)
                InitializeAsync(settings).Forget();
        }
    }
}
