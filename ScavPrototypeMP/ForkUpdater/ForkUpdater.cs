using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;

namespace Together.ForkUpdates;

internal static class ForkUpdater {
    static readonly ForkReleaseClient releases = new ForkReleaseClient(new HttpClient());
    static UpdateStaging staging;
    static CancellationTokenSource checking;

    internal static string Status { get; private set; }
    internal static bool Installed => staging != null;
    internal static bool Enabled => Installed && staging.Enabled;
    internal static bool CanCheck => Enabled && checking == null && !staging.HasPending;

    internal static void Initialize() {
#if FULLGAME && RELEASE && !PLAYTEST
        if (Application.platform != RuntimePlatform.WindowsPlayer || !Plugin.TARGET_GAME_VERSION.Contains(Application.version)
            || !ForkReleaseClient.IsForkVersion(Plugin.MOD_VERSION)) {
            return;
        }
        var files = new UpdateStaging(Paths.PluginPath, Paths.ManagedPath);
        string patcher = Path.Combine(Paths.PatcherPluginPath, UpdateStaging.PatcherName + ".dll");
        if (!File.Exists(patcher) || !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == UpdateStaging.PatcherName)
            || !string.Equals(typeof(Plugin).Assembly.Location, files.InstalledPath, StringComparison.OrdinalIgnoreCase)) {
            Status = "fork_update_install";
            return;
        }
        staging = files;
        RefreshStatus();
#endif
    }

    internal static void SetEnabled(bool value) {
        if (!Installed) {
            return;
        }
        try {
            staging.SetEnabled(value);
            RefreshStatus();
        } catch (Exception ex) {
            Status = "fork_update_failed";
            Plugin.log.LogWarning("Could not change the co-op update setting: " + ex.Message);
        }
    }

    static void RefreshStatus() {
        if (!Enabled) {
            Status = "fork_update_off";
        } else if (staging.HasPending) {
            Status = "fork_update_ready";
        } else if (checking != null) {
            Status = "fork_update_checking";
        } else {
            Check();
        }
    }

    internal static void Check() {
        if (CanCheck) {
            _ = CheckAsync(Application.version);
        }
    }

    static async Task CheckAsync(string gameVersion) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        checking = timeout;
        Status = "fork_update_checking";
        try {
            var original = staging.Capture();
            var (tag, dll) = await releases.DownloadNewer(Plugin.MOD_VERSION, gameVersion, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (!Enabled) {
                Status = "fork_update_off";
            } else if (dll == null) {
                Status = "fork_update_current";
            } else if (staging.Stage(dll, original)) {
                Status = "fork_update_ready";
                Plugin.log.LogInfo($"Co-op update {tag} is ready for restart.");
            } else if (Enabled && staging.HasPending) {
                Status = "fork_update_ready";
            } else {
                Status = Enabled ? "fork_update_failed" : "fork_update_off";
            }
        } catch (Exception ex) {
            Status = Enabled ? "fork_update_failed" : "fork_update_off";
            Plugin.log.LogWarning("Co-op update check failed: " + ex.Message);
        } finally {
            checking = null;
        }
    }

    internal static void Stop() => checking?.Cancel();
}
