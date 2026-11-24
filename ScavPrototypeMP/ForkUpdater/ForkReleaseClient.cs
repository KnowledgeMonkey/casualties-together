using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Together.ForkUpdates;

internal sealed class ForkReleaseClient {
    const string Repository = "KnowledgeMonkey/casualties-together";
    const string ReleasesUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=30";
    const string DownloadUrl = "https://github.com/" + Repository + "/releases/download/";
    const string VersionSeparator = "-coopfix.";
    const int MaxFeedBytes = 4 * 1024 * 1024;
    const int MaxArchiveBytes = 24 * 1024 * 1024;
    readonly HttpClient client;

    internal ForkReleaseClient(HttpClient client) {
        this.client = client;
    }

    internal static bool IsForkVersion(string version) => TryParseVersion(version, out _);

    internal async Task<(string Tag, byte[] Dll)> DownloadNewer(string currentVersion, string gameVersion, CancellationToken cancellation) {
        if (!TryParseVersion(currentVersion, out Version current)) {
            return default;
        }
        byte[] feed = await GetBytes(new Uri(ReleasesUrl), MaxFeedBytes, cancellation).ConfigureAwait(false);
        Version newest = current;
        string newestTag = null;
        Uri package = null;
        string checksum = null;
        int packageSize = 0;
        foreach (JObject release in JArray.Parse(Encoding.UTF8.GetString(feed)).OfType<JObject>()) {
            string tag = (string)release["tag_name"];
            if ((bool?)release["draft"] != false || tag == null || !tag.StartsWith("v", StringComparison.Ordinal)
                || !TryParseVersion(tag.Substring(1), out Version version) || version <= newest
                || !(release["assets"] is JArray assets)) {
                continue;
            }
            string fileName = $"Casualties-Together-Coop-Fix-{tag.Substring(1)}-Windows-{gameVersion}.zip";
            string url = DownloadUrl + tag + "/" + fileName;
            foreach (JObject asset in assets.OfType<JObject>()) {
                string digest = (string)asset["digest"];
                long? size = (long?)asset["size"];
                if ((string)asset["name"] != fileName || (string)asset["state"] != "uploaded"
                    || (string)asset["browser_download_url"] != url || size == null || size < 1 || size > MaxArchiveBytes
                    || digest == null || digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                    || !digest.Substring(7).All(Uri.IsHexDigit)) {
                    continue;
                }
                newest = version;
                newestTag = tag;
                package = new Uri(url);
                checksum = digest.Substring(7);
                packageSize = (int)size.Value;
                break;
            }
        }

        if (package == null) {
            return default;
        }
        byte[] archive = await GetBytes(package, packageSize, cancellation).ConfigureAwait(false);
        if (archive.Length != packageSize || !string.Equals(checksum,
                BitConverter.ToString(UpdateStaging.Hash(archive)).Replace("-", ""), StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("GitHub's size or checksum did not match the downloaded update.");
        }
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var entries = zip.Entries.Where(e => e.FullName == UpdateStaging.ArchiveDllPath).Take(2).ToArray();
        if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > UpdateStaging.MaxDllBytes) {
            throw new InvalidDataException("The update ZIP is missing a valid CasualtiesMP.dll.");
        }
        using Stream dllStream = entries[0].Open();
        byte[] dll = await ReadBounded(dllStream, (int)entries[0].Length, cancellation).ConfigureAwait(false);
        if (dll.Length != entries[0].Length) {
            throw new InvalidDataException("The update DLL is incomplete.");
        }
        return (newestTag, dll);
    }

    async Task<byte[]> GetBytes(Uri uri, int maxBytes, CancellationToken cancellation) {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CasualtiesTogetherCoopFix-Updater/1.0");
        if (uri.Host == "api.github.com") {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
        }
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps || response.Content.Headers.ContentLength > maxBytes) {
            throw new InvalidDataException("The update response was insecure or too large.");
        }
        using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await ReadBounded(stream, maxBytes, cancellation).ConfigureAwait(false);
    }

    static async Task<byte[]> ReadBounded(Stream stream, int maxBytes, CancellationToken cancellation) {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0) {
            if (output.Length + read > maxBytes) {
                throw new InvalidDataException("The update response exceeded its size limit.");
            }
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    static bool TryParseVersion(string text, out Version version) {
        version = null;
        if (text == null) {
            return false;
        }
        int separator = text.IndexOf(VersionSeparator, StringComparison.Ordinal);
        if (separator < 0 || !Version.TryParse(text.Substring(0, separator), out Version basis)
            || basis.Build < 0 || basis.Revision >= 0
            || !int.TryParse(text.Substring(separator + VersionSeparator.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int revision)
            || revision < 1 || text != basis.ToString(3) + VersionSeparator + revision.ToString(CultureInfo.InvariantCulture)) {
            return false;
        }
        version = new Version(basis.Major, basis.Minor, basis.Build, revision);
        return true;
    }
}
