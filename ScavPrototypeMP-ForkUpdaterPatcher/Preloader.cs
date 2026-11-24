using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace Together.ForkUpdates;

public static class Preloader {
    public static IEnumerable<string> TargetDLLs => Array.Empty<string>();

    public static void Patch(AssemblyDefinition assembly) { }

    public static void Initialize() {
        var log = Logger.CreateLogSource("Co-op Fix Updater");
        try {
            var staging = new UpdateStaging(Paths.PluginPath, Paths.ManagedPath);
            if (staging.Apply()) {
                log.LogInfo("Installed the staged co-op update.");
            }
        } catch (Exception ex) {
            log.LogWarning("Could not apply the staged co-op update: " + ex.Message);
        }
    }
}
