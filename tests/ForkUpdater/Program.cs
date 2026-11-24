using System.IO.Compression;
using System.Net;
using System.Reflection;
using BepInEx;
using BepInEx.Preloader.Patching;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using Together.ForkUpdates;

const string CurrentVersion = "5.5.5-coopfix.3";
const string GameVersion = "1.0.2";
int passed = 0;

void Check(bool condition, string description) {
    if (!condition) {
        throw new InvalidOperationException(description);
    }
    Console.WriteLine("PASS " + description);
    passed++;
}

byte[] installed = MakeDll("CasualtiesMP", 1);
byte[] newer = MakeDll("CasualtiesMP", 2);
byte[] third = MakeDll("CasualtiesMP", 3);
Check(ForkReleaseClient.IsForkVersion(CurrentVersion) && !ForkReleaseClient.IsForkVersion("5.5.5-fork.4")
    && !ForkReleaseClient.IsForkVersion("5.5.5-coopfix.03"), "only canonical fork versions are recognized");

byte[] archive = MakeZip(newer);
var correct = Release("5.5.5-coopfix.10", archive);
var feed = new JArray(
    Release("5.5.5-coopfix.11", archive, game: "1.1.0"),
    Release("5.5.5-coopfix.99", archive, draft: true),
    Release("5.5.5-coopfix.17", archive, badUrl: true),
    Release("5.5.5-coopfix.14", archive, noDigest: true),
    Release(CurrentVersion, archive),
    correct,
    Release("5.5.5-coopfix.9", MakeZip(third)));
using (var http = HttpFor(feed, archive)) {
    var (tag, dll) = await new ForkReleaseClient(http).DownloadNewer(CurrentVersion, GameVersion, default);
    Check(tag == "v5.5.5-coopfix.10" && dll.SequenceEqual(newer), "newest compatible fork release selected by numeric version and verified");
}
using (var http = HttpFor(new JArray(Release(CurrentVersion, archive)), null)) {
    var result = await new ForkReleaseClient(http).DownloadNewer(CurrentVersion, GameVersion, default);
    Check(result.Dll == null, "current version does not download or downgrade");
}

byte[] corruptedArchive = archive.ToArray();
corruptedArchive[0] ^= 0x01;
using (var http = HttpFor(new JArray(correct), corruptedArchive)) {
    await ExpectBadArchive(http, "download with a mismatching GitHub checksum is rejected");
}
using (var http = HttpFor(new JArray(correct), archive.Concat(new byte[] { 1 }).ToArray())) {
    await ExpectBadArchive(http, "download larger than the advertised size is rejected");
}
byte[] duplicate = MakeZip(newer, duplicate: true);
using (var http = HttpFor(new JArray(Release("5.5.5-coopfix.4", duplicate)), duplicate)) {
    await ExpectBadArchive(http, "ambiguous DLL entry is rejected even with a matching ZIP checksum");
}

using var game = new InstallFixture(installed);
var files = game.Files;
Check(!files.Apply() && files.Enabled, "fresh install has updates enabled and nothing to apply");
Check(files.Stage(newer, files.Capture()) && files.HasPending && game.IsInstalled(installed), "download stages without changing the live DLL");
Check(!files.Stage(third, files.Capture()), "a concurrent download cannot overwrite a staged update");
files.SetEnabled(false);
Check(!files.Apply() && !files.Stage(third, files.Capture()) && game.IsInstalled(installed), "turning updates off blocks staging and the next launch");
files.SetEnabled(true);
Check(files.Apply() && game.IsInstalled(newer) && game.IsBackup(installed) && !files.HasPending, "restart applies the update and backs up the old DLL");
Check(!files.Apply(), "applying again is a no-op");
Check(files.Stage(third, files.Capture()) && files.Apply() && game.IsInstalled(third) && game.IsBackup(newer),
    "another release replaces the backup with the previous build");

Check(files.Stage(newer, files.Capture()), "manual-install conflict staged for test");
File.WriteAllBytes(files.InstalledPath, installed);
Check(!files.Apply() && !files.HasPending && game.IsInstalled(installed), "a manual install after staging is preserved");
var beforeGameUpdate = files.Capture();
Check(files.Stage(newer, beforeGameUpdate), "game-version conflict staged for test");
File.AppendAllText(game.AssemblyPath, "new game build");
Check(!files.Apply() && !files.Stage(third, beforeGameUpdate) && !files.HasPending && game.IsInstalled(installed),
    "a changed base game invalidates both staged and in-flight updates");

Check(files.Stage(newer, files.Capture()), "checksum conflict staged for test");
string pending = files.InstalledPath + ".coopfix-pending";
byte[] bad = File.ReadAllBytes(pending);
bad[bad.Length - 1] ^= 0xff;
File.WriteAllBytes(pending, bad);
ExpectInvalidStage("staged file corruption leaves the installed DLL intact");
Check(files.Stage(MakeDll("OtherAssembly", 1), files.Capture()), "wrong assembly staged for test");
ExpectInvalidStage("an unexpected assembly name leaves the installed DLL intact");

if (OperatingSystem.IsWindows()) {
    Check(files.Stage(third, files.Capture()), "locked-install conflict staged for test");
    bool blocked = false;
    using (File.Open(files.InstalledPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
        try {
            files.Apply();
        } catch (IOException) {
            blocked = true;
        }
    }
    Check(blocked && files.HasPending && game.IsInstalled(installed), "an in-use DLL leaves the download staged for another launch");
    Check(files.Apply() && game.IsInstalled(third), "retry succeeds once the installed file is free");
    File.WriteAllBytes(files.InstalledPath, installed);
}

if (args.Length > 0) {
    game.SetBepInExPaths();
    Directory.CreateDirectory(Paths.PatcherPluginPath);
    File.Copy(args[0], Path.Combine(Paths.PatcherPluginPath, UpdateStaging.PatcherName + ".dll"));
    Check(files.Stage(newer, files.Capture()), "update prepared for the shipped preloader");
    AssemblyPatcher.AddPatchersFromDirectory(Paths.PatcherPluginPath);
    typeof(AssemblyPatcher).GetMethod("InitializePatchers", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    Check(game.IsInstalled(newer) && game.IsBackup(installed) && !files.HasPending, "BepInEx discovers and runs the shipped preloader");
}
if (args.Length > 1) {
    using var client = new HttpClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var (tag, dll) = await new ForkReleaseClient(client).DownloadNewer("5.5.5-coopfix.2", GameVersion, timeout.Token);
    using var zip = ZipFile.OpenRead(args[1]);
    using Stream packaged = zip.GetEntry(UpdateStaging.ArchiveDllPath)!.Open();
    using var copied = new MemoryStream();
    await packaged.CopyToAsync(copied);
    Check(tag == "v" + CurrentVersion && dll.SequenceEqual(copied.ToArray()), "public GitHub download matches the published Windows build");
    Check(files.Stage(dll, files.Capture()), "public GitHub build stages in the isolated install");
    typeof(AssemblyPatcher).GetMethod("InitializePatchers", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    Check(game.IsInstalled(dll) && game.IsBackup(newer) && !files.HasPending, "the shipped preloader applies the public GitHub build");
}

Console.WriteLine($"FORK_UPDATER_CHECKS passed={passed}");

void ExpectInvalidStage(string description) {
    bool rejected = false;
    try {
        files.Apply();
    } catch (InvalidDataException) {
        rejected = true;
    }
    Check(rejected && !files.HasPending && game.IsInstalled(installed), description);
}

async Task ExpectBadArchive(HttpClient http, string description) {
    bool rejected = false;
    try {
        await new ForkReleaseClient(http).DownloadNewer(CurrentVersion, GameVersion, default);
    } catch (InvalidDataException) {
        rejected = true;
    }
    Check(rejected, description);
}

static byte[] MakeDll(string name, int revision) {
    using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0, 0, revision)), name, ModuleKind.Dll);
    using var stream = new MemoryStream();
    assembly.Write(stream);
    return stream.ToArray();
}

static byte[] MakeZip(byte[] dll, bool duplicate = false) {
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
        using (Stream entry = zip.CreateEntry(UpdateStaging.ArchiveDllPath).Open()) {
            entry.Write(dll);
        }
        if (duplicate) {
            zip.CreateEntry(UpdateStaging.ArchiveDllPath);
        }
        zip.CreateEntry("readme.txt");
    }
    return stream.ToArray();
}

static JObject Release(string version, byte[] zip, string game = GameVersion, bool draft = false, bool badUrl = false, bool noDigest = false) {
    string name = $"Casualties-Together-Coop-Fix-{version}-Windows-{game}.zip";
    string owner = badUrl ? "somewhere-else" : "KnowledgeMonkey";
    return new JObject {
        ["tag_name"] = "v" + version,
        ["draft"] = draft,
        ["prerelease"] = true,
        ["assets"] = new JArray(new JObject {
            ["name"] = name,
            ["state"] = "uploaded",
            ["browser_download_url"] = $"https://github.com/{owner}/casualties-together/releases/download/v{version}/{name}",
            ["size"] = zip.Length,
            ["digest"] = noDigest ? null : "sha256:" + Convert.ToHexString(UpdateStaging.Hash(zip)).ToLowerInvariant(),
        }),
    };
}

static HttpClient HttpFor(JArray feed, byte[] zip) => new HttpClient(new FakeReleaseServer(feed, zip));

sealed class FakeReleaseServer(JArray feed, byte[] zip) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        HttpContent content = request.RequestUri.Host == "api.github.com"
            ? new StringContent(feed.ToString())
            : zip != null ? new ByteArrayContent(zip) : throw new InvalidOperationException("Unexpected download.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content });
    }
}

sealed class InstallFixture : IDisposable {
    readonly DirectoryInfo root = Directory.CreateTempSubdirectory("ct-updater-");
    readonly string managedPath;

    internal UpdateStaging Files { get; }
    internal string AssemblyPath { get; }

    internal InstallFixture(byte[] installed) {
        managedPath = Path.Combine(root.FullName, "Managed");
        Directory.CreateDirectory(managedPath);
        AssemblyPath = Path.Combine(managedPath, "Assembly-CSharp.dll");
        File.WriteAllText(AssemblyPath, "game build");
        Files = new UpdateStaging(Path.Combine(root.FullName, "BepInEx", "plugins"), managedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Files.InstalledPath)!);
        File.WriteAllBytes(Files.InstalledPath, installed);
    }

    internal void SetBepInExPaths() {
        typeof(Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { Path.Combine(root.FullName, "CasualtiesUnknown.exe"), null, managedPath, null });
    }

    internal bool IsInstalled(byte[] dll) => File.ReadAllBytes(Files.InstalledPath).SequenceEqual(dll);
    internal bool IsBackup(byte[] dll) => File.ReadAllBytes(Files.InstalledPath + ".coopfix-backup").SequenceEqual(dll);

    public void Dispose() {
        // The real preloader can keep its assembly locked for the lifetime of this test process.
        try {
            root.Delete(recursive: true);
        } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
