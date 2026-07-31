using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Lp100a.App.Services;

public sealed class UpdateInfo
{
    public string CurrentVersion { get; init; } = "";
    public string LatestTag { get; set; } = "";
    public bool UpdateAvailable { get; set; }
    public string ReleaseUrl { get; set; } = $"https://github.com/{UpdateService.Repo}/releases/latest";
    public string? AssetUrl { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// W2-style in-app updater: checks the GitHub latest release, downloads the build for this
/// platform, and (since a running executable can't overwrite itself) stages a helper that
/// waits for exit, swaps the exe, and relaunches. Windows is the primary target.
/// </summary>
public static class UpdateService
{
    public const string Repo = "gsa700/lp100a-monitor";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    /// <summary>Runtime identifier used in the release asset name, e.g. "win-x64".</summary>
    public static string Rid()
    {
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
        return $"linux-{arch}";
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        var info = new UpdateInfo { CurrentVersion = CurrentVersion };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Lp100aMonitor-UpdateCheck", "1.0"));
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            info.LatestTag = root.GetProperty("tag_name").GetString() ?? "";
            if (root.TryGetProperty("html_url", out var hu) && hu.GetString() is { } url) info.ReleaseUrl = url;

            var assetName = $"Lp100aMonitor-{Rid()}.zip";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.GetProperty("name").GetString() == assetName)
                    {
                        info.AssetUrl = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            var cur = ParseVer(CurrentVersion);
            var lat = ParseVer(info.LatestTag);
            info.UpdateAvailable = cur is not null && lat is not null && lat > cur;
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }
        return info;
    }

    /// <summary>
    /// Temp directory the update is downloaded and unpacked into. The relaunched app must never have
    /// this as its working directory: a directory in use as one cannot be deleted, so the next
    /// update's clean-up below would throw and updating twice without a restart would fail.
    /// </summary>
    private static string StageRoot => Path.Combine(Path.GetTempPath(), "Lp100aMonitor-update");

    /// <summary>Download the asset zip, extract it, and return the path to the staged executable.</summary>
    public static async Task<string> DownloadAndStageAsync(string assetUrl)
    {
        var tmp = StageRoot;
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);

        var zip = Path.Combine(tmp, "update.zip");
        using (var req = new HttpRequestMessage(HttpMethod.Get, assetUrl))
        {
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Lp100aMonitor-UpdateInstall", "1.0"));
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zip);
            await resp.Content.CopyToAsync(fs);
        }

        var ex = Path.Combine(tmp, "ex");
        ZipFile.ExtractToDirectory(zip, ex, overwriteFiles: true);

        var exeName = OperatingSystem.IsWindows() ? "Lp100aMonitor.exe" : "Lp100aMonitor";
        var staged = Directory.GetFiles(ex, exeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException($"{exeName} not found in the downloaded package.");
        return staged;
    }

    /// <summary>
    /// Launch a detached helper that waits for this process to exit, replaces the current
    /// executable with the staged one, and relaunches it. The caller must then exit the app.
    /// </summary>
    public static void ApplyAndRestart(string stagedExe)
    {
        var target = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the current executable path.");
        var pid = Environment.ProcessId;
        var targetDir = Path.GetDirectoryName(target)!;

        // The helper lives in the temp root, not in the staging directory — it deletes that
        // directory, and a script cannot sit in the folder it is removing. Same shape as the
        // uninstall trampoline, which is in temp because the install directory is its target.
        static string Q(string p) => p.Replace("'", "''");

        if (OperatingSystem.IsWindows())
        {
            var ps1 = Path.Combine(Path.GetTempPath(), "lp100a-apply-update.ps1");
            File.WriteAllText(ps1,
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}\n" +
                $"Copy-Item -LiteralPath '{Q(stagedExe)}' -Destination '{Q(target)}' -Force\n" +
                // -WorkingDirectory matters: without it the relaunched app inherits this script's
                // directory. That used to be the staging folder, which the app then held open — so
                // it could not be deleted, and the next update's clean-up of it threw, meaning you
                // could not update twice without restarting first.
                $"Start-Process -FilePath '{Q(target)}' -WorkingDirectory '{Q(targetDir)}'\n" +
                $"Remove-Item -LiteralPath '{Q(StageRoot)}' -Recurse -Force -ErrorAction SilentlyContinue\n" +
                // Take the helper with it, so an update doesn't leave its own tooling behind in temp.
                $"Remove-Item -LiteralPath '{Q(ps1)}' -Force -ErrorAction SilentlyContinue\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var sh = Path.Combine(Path.GetTempPath(), "lp100a-apply-update.sh");
            File.WriteAllText(sh,
                "#!/bin/sh\n" +
                $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done\n" +
                $"cp -f '{Q(stagedExe)}' '{Q(target)}'\n" +
                $"chmod +x '{Q(target)}'\n" +
                // cd first, for the same reason -WorkingDirectory is set on Windows.
                $"(cd '{Q(targetDir)}' && '{Q(target)}' &)\n" +
                $"rm -rf '{Q(StageRoot)}'\n" +
                $"rm -f '{Q(sh)}'\n");
            Process.Start(new ProcessStartInfo { FileName = "/bin/sh", Arguments = $"\"{sh}\"", UseShellExecute = false });
        }
    }

    private static Version? ParseVer(string s)
    {
        var t = s.TrimStart('v', 'V');
        var dash = t.IndexOf('-');
        if (dash >= 0) t = t[..dash];
        return Version.TryParse(t, out var v) ? v : null;
    }
}
