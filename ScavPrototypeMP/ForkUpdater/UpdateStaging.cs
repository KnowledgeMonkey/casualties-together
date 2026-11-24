using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Together.ForkUpdates;

// Shared with the preloader, which runs before BepInEx loads CasualtiesMP.dll.
internal sealed class UpdateStaging {
    const int HashSize = 32;
    const int StagingFormat = 1;
    const int HeaderSize = sizeof(int) + HashSize * 3;
    internal const int MaxDllBytes = 16 * 1024 * 1024;
    internal const string ArchiveDllPath = "BepInEx/plugins/Together/CasualtiesMP.dll";
    internal const string PatcherName = "CoopFixUpdatePatcher";

    internal readonly string InstalledPath;
    readonly string gameAssemblyPath;
    readonly string pendingPath;
    readonly string lockPath;
    readonly string disabledPath;

    internal UpdateStaging(string pluginPath, string managedPath) {
        InstalledPath = Path.Combine(pluginPath, "Together", "CasualtiesMP.dll");
        gameAssemblyPath = Path.Combine(managedPath, "Assembly-CSharp.dll");
        pendingPath = InstalledPath + ".coopfix-pending";
        lockPath = InstalledPath + ".coopfix-lock";
        disabledPath = InstalledPath + ".coopfix-disabled";
    }

    internal bool Enabled => !File.Exists(disabledPath);
    internal bool HasPending => File.Exists(pendingPath);

    internal void SetEnabled(bool enabled) {
        using var updateLock = Lock();
        if (enabled) {
            File.Delete(disabledPath);
        } else {
            using (File.Create(disabledPath)) { }
        }
    }

    internal (byte[] Installed, byte[] Game) Capture() => (HashFile(InstalledPath), HashFile(gameAssemblyPath));

    internal bool Stage(byte[] dll, (byte[] Installed, byte[] Game) original) {
        if (dll.Length == 0 || dll.Length > MaxDllBytes) {
            throw new InvalidDataException("The update DLL has an invalid size.");
        }
        using var updateLock = Lock();
        if (!Enabled || HasPending || !Matches(original)) {
            return false;
        }

        string partial = pendingPath + ".tmp";
        try {
            using (var stream = File.Create(partial))
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(StagingFormat);
                writer.Write(original.Installed);
                writer.Write(original.Game);
                writer.Write(Hash(dll));
                writer.Write(dll);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(partial, pendingPath);
        } finally {
            File.Delete(partial);
        }
        return true;
    }

    internal bool Apply() {
        if (!HasPending || !Enabled) {
            return false;
        }
        using var updateLock = Lock();
        if (!HasPending || !Enabled) {
            return false;
        }

        string candidate = InstalledPath + ".coopfix-copy";
        try {
            byte[] dll;
            using (var stream = File.OpenRead(pendingPath))
            using (var reader = new BinaryReader(stream)) {
                long length = stream.Length - HeaderSize;
                if (length <= 0 || length > MaxDllBytes || reader.ReadInt32() != StagingFormat) {
                    throw new InvalidDataException("Invalid staged co-op update.");
                }
                var original = (reader.ReadBytes(HashSize), reader.ReadBytes(HashSize));
                byte[] digest = reader.ReadBytes(HashSize);
                dll = reader.ReadBytes((int)length);
                if (!digest.SequenceEqual(Hash(dll))) {
                    throw new InvalidDataException("The staged co-op update failed its checksum.");
                }
                if (!Matches(original)) {
                    dll = null;
                }
            }
            if (dll == null) {
                File.Delete(pendingPath);
                return false;
            }

            using (var stream = File.Create(candidate)) {
                stream.Write(dll, 0, dll.Length);
                stream.Flush(true);
            }
            if (AssemblyName.GetAssemblyName(candidate).Name != "CasualtiesMP") {
                throw new InvalidDataException("The update contains an unexpected assembly.");
            }
            File.Replace(candidate, InstalledPath, InstalledPath + ".coopfix-backup");
            File.Delete(pendingPath);
            return true;
        } catch (Exception e) when (e is InvalidDataException || e is BadImageFormatException) {
            File.Delete(pendingPath);
            throw;
        } finally {
            File.Delete(candidate);
        }
    }

    FileStream Lock() => new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    bool Matches((byte[] Installed, byte[] Game) original) =>
        File.Exists(InstalledPath) && original.Installed.SequenceEqual(HashFile(InstalledPath))
        && File.Exists(gameAssemblyPath) && original.Game.SequenceEqual(HashFile(gameAssemblyPath));

    static byte[] HashFile(string path) {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream);
    }

    internal static byte[] Hash(byte[] data) {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }
}
