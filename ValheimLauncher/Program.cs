using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Win32;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(ReleaseInfo))]
internal partial class ReleaseContext : JsonSerializerContext { }

internal class ReleaseInfo
{
    public string? TagName { get; set; }
    public Asset[] Assets { get; set; } = Array.Empty<Asset>();
}

internal class Asset
{
    public string? BrowserDownloadUrl { get; set; }
}

[SupportedOSPlatform("windows")]
class Program
{
    private static class ConsoleSymbols
    {
        public const string Success = "[+]";
        public const string Error = "[!]";
        public const string Info = "[*]";
        public const string Warning = "[!]";
        public const string Arrow = "=>";
        public const string Progress = "[-]";
    }

    private const string GithubOwner = "dankmaster";
    private const string GithubRepo = "ValheimLauncher";
    private const string GithubBranch = "master";
    private const string GithubApiBaseUrl = $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}";
    private const string GithubModsUrl = $"https://github.com/{GithubOwner}/{GithubRepo}/raw/{GithubBranch}/Mods/plugins.zip";
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ValheimLauncher"
    );
    private static readonly string LauncherVersionFile = Path.Combine(AppDataPath, "version.json");
    private static readonly string TempFolderPath = Path.Combine(Path.GetTempPath(), "ValheimMods");
    private static readonly string TempZipPath = Path.Combine(TempFolderPath, "mods.zip");
    private static readonly string ExtractedTempPath = Path.Combine(TempFolderPath, "mods_extracted");
    private const string SteamAppID = "892970";
    private static readonly HttpClient httpClient = new HttpClient();

    static async Task Main()
    {
        Console.Title = "Valheim Mod Launcher";
        DisplayWelcomeMessage();

        try
        {
            // Ensure directories exist
            Directory.CreateDirectory(TempFolderPath);
            Directory.CreateDirectory(AppDataPath);

            // Check for launcher updates first
            if (await CheckForLauncherUpdate())
            {
                Console.WriteLine($"{ConsoleSymbols.Info} Press any key to exit and install the update...");
                Console.ReadKey();
                return;
            }

            // Find Valheim installation
            string? pluginsFolder = GetPluginsFolder();
            if (string.IsNullOrEmpty(pluginsFolder))
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Valheim installation not found.");
                WaitForUserExit();
                return;
            }

            Console.WriteLine($"{ConsoleSymbols.Success} Found Valheim plugins folder: {pluginsFolder}");

            // Check for mod updates
            Console.WriteLine($"\n{ConsoleSymbols.Info} Checking for mod updates...");
            if (await TestUpdateNeeded(pluginsFolder))
            {
                Console.WriteLine($"{ConsoleSymbols.Progress} Mod updates available!");
                await UpdateMods(pluginsFolder);
            }
            else
            {
                Console.WriteLine($"{ConsoleSymbols.Success} Mods are up to date!");
            }

            // Launch game
            Console.WriteLine($"\n{ConsoleSymbols.Progress} Launching Valheim...");
            Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppID}") { UseShellExecute = true });

            WaitForUserExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n{ConsoleSymbols.Error} An error occurred: {ex.Message}");
            Console.WriteLine("\nStack trace:");
            Console.WriteLine(ex.StackTrace);
            WaitForUserExit();
        }
        finally
        {
            Cleanup();
        }
    }

    private static void DisplayWelcomeMessage()
    {
        Console.WriteLine("=================================");
        Console.WriteLine($"{ConsoleSymbols.Info} Valheim Mod Launcher v" + Assembly.GetExecutingAssembly().GetName().Version);
        Console.WriteLine("=================================\n");
    }

    private static async Task<bool> CheckForLauncherUpdate()
    {
        try
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ValheimLauncher");
            var response = await httpClient.GetAsync($"{GithubApiBaseUrl}/releases/latest");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"{ConsoleSymbols.Warning} Could not check for updates. Will continue with current version.");
                return false;
            }

            var releaseJson = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var releaseInfo = JsonSerializer.Deserialize<ReleaseInfo>(releaseJson, options);

            if (releaseInfo?.TagName == null)
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Invalid release information received.");
                return false;
            }

            var latestVersion = releaseInfo.TagName.TrimStart('v');
            var currentVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0";

            Console.WriteLine($"{ConsoleSymbols.Info} Current version: {currentVersion}");
            Console.WriteLine($"{ConsoleSymbols.Info} Latest version: {latestVersion}");

            if (IsNewerVersion($"v{latestVersion}", currentVersion))
            {
                Console.WriteLine($"{ConsoleSymbols.Progress} New launcher version available: v{latestVersion}");
                Console.WriteLine($"{ConsoleSymbols.Info} Starting update process...");

                var assetUrl = releaseInfo.Assets.FirstOrDefault()?.BrowserDownloadUrl;
                if (assetUrl != null)
                {
                    var updatePath = Path.Combine(AppDataPath, "update.zip");
                    await DownloadFileAsync(assetUrl, updatePath);
                    CreateUpdateScript(updatePath);
                    return true;
                }
            }
            else
            {
                Console.WriteLine($"{ConsoleSymbols.Success} Launcher is up to date!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Failed to check for updates: {ex.Message}");
        }
        return false;
    }

    private static void CreateUpdateScript(string updatePath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        var currentPid = Process.GetCurrentProcess().Id;
        if (currentExe == null) return;

        var scriptPath = Path.Combine(AppDataPath, "update.bat");
        var script = $@"@echo off
echo Waiting for launcher to close...
taskkill /F /PID {currentPid} >nul 2>&1
timeout /t 3 /nobreak
echo Starting update process...

:retry_delete
del ""{currentExe}"" 2>nul
if exist ""{currentExe}"" (
    echo Waiting for file to be free...
    timeout /t 2 /nobreak
    goto retry_delete
)

echo Extracting update...
powershell Expand-Archive -Path ""{updatePath}"" -DestinationPath ""{Path.GetDirectoryName(currentExe)}"" -Force
if errorlevel 1 (
    echo Failed to extract update.
    pause
    exit /b 1
)

echo Cleaning up...
del ""{updatePath}""

echo Update complete! Starting launcher...
timeout /t 2 /nobreak
start """" ""{currentExe}""
del ""%~f0""";

        File.WriteAllText(scriptPath, script);
        var startInfo = new ProcessStartInfo(scriptPath)
        {
            UseShellExecute = true,
            Verb = "runas" // Run as administrator to ensure we can kill the process
        };
        Process.Start(startInfo);
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        var latest = new Version(latestVersion.TrimStart('v'));
        var current = new Version(currentVersion);
        return latest > current;
    }

    private static async Task<bool> TestUpdateNeeded(string pluginsFolder)
    {
        try
        {
            Console.WriteLine($"{ConsoleSymbols.Progress} Downloading mod information...");
            await DownloadFileAsync(GithubModsUrl, TempZipPath);

            if (Directory.Exists(ExtractedTempPath))
            {
                Directory.Delete(ExtractedTempPath, true);
            }

            ZipFile.ExtractToDirectory(TempZipPath, ExtractedTempPath);

            string remoteChecksum = CalculateFolderChecksum(ExtractedTempPath);
            string localChecksum = CalculateFolderChecksum(pluginsFolder);

            return remoteChecksum != localChecksum;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Error checking for updates: {ex.Message}");
            return false;
        }
    }

    private static async Task UpdateMods(string pluginsFolder)
    {
        Console.Write($"\n{ConsoleSymbols.Warning} Do you want to update mods in {pluginsFolder}? (Yes/No): ");
        string? response = Console.ReadLine();
        if (string.IsNullOrEmpty(response) || !response.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Update aborted by user.");
            return;
        }
    
        Console.WriteLine($"\n{ConsoleSymbols.Progress} Updating mods...");
        Console.WriteLine($"{ConsoleSymbols.Info} Removing old mods...");
    
        // Make file operations async
        await Task.Run(() =>
        {
            foreach (var file in Directory.GetFiles(pluginsFolder, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
                Console.WriteLine($"{ConsoleSymbols.Arrow} Removed: {Path.GetFileName(file)}");
            }
    
            foreach (var dir in Directory.GetDirectories(pluginsFolder))
            {
                Directory.Delete(dir, true);
                Console.WriteLine($"{ConsoleSymbols.Arrow} Removed directory: {Path.GetFileName(dir)}");
            }
        });
    
        Console.WriteLine($"\n{ConsoleSymbols.Info} Installing new mods...");
        await Task.Run(() => CopyAll(new DirectoryInfo(ExtractedTempPath), new DirectoryInfo(pluginsFolder)));
        Console.WriteLine($"{ConsoleSymbols.Success} Mods updated successfully!");
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ValheimLauncher");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new Exception("Download file not found. Please check if the file exists in the repository.");
        }

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        var totalBytesRead = 0L;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;

            if (totalBytes != -1)
            {
                var percentage = (int)((totalBytesRead * 100) / totalBytes);
                Console.Write($"\r{ConsoleSymbols.Progress} Download progress: {percentage}%");
            }
        }
        Console.WriteLine();
    }

    private static void Cleanup()
    {
        try
        {
            if (Directory.Exists(ExtractedTempPath))
            {
                Directory.Delete(ExtractedTempPath, true);
            }

            if (File.Exists(TempZipPath))
            {
                File.Delete(TempZipPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ConsoleSymbols.Warning} Cleanup error: {ex.Message}");
        }
    }

    private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        try
        {
            if (!Directory.Exists(target.FullName))
            {
                Directory.CreateDirectory(target.FullName);
            }

            foreach (FileInfo fi in source.GetFiles())
            {
                try
                {
                    string destinationPath = Path.Combine(target.FullName, fi.Name);
                    fi.CopyTo(destinationPath, true);
                    Console.WriteLine($"{ConsoleSymbols.Arrow} Installed: {fi.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ConsoleSymbols.Warning} Failed to copy {fi.Name}: {ex.Message}");
                }
            }

            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
                Console.WriteLine($"{ConsoleSymbols.Arrow} Created directory: {diSourceSubDir.Name}");
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Error during directory copy: {ex.Message}");
            throw;
        }
    }

    private static string CalculateFolderChecksum(string folderPath)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).OrderBy(p => p).ToList();
            List<byte> combinedHashBytes = new List<byte>();

            foreach (string file in files)
            {
                byte[] fileBytes = File.ReadAllBytes(file);
                byte[] fileHash = sha256.ComputeHash(fileBytes);
                combinedHashBytes.AddRange(fileHash);
            }

            byte[] finalHash = sha256.ComputeHash(combinedHashBytes.ToArray());
            return BitConverter.ToString(finalHash).Replace("-", "");
        }
    }

    private static string? GetPluginsFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException($"{ConsoleSymbols.Error} This application only supports Windows.");
        }
    
        string? steamPath = GetSteamPath();
        if (string.IsNullOrEmpty(steamPath))
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Steam installation not found.");
            return null;
        }
    
        string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            Console.WriteLine($"{ConsoleSymbols.Error} libraryfolders.vdf not found. Cannot locate Valheim.");
            return null;
        }
    
        string content = File.ReadAllText(libraryFoldersPath);
    
        var matches = Regex.Matches(content, @"""path""\s*""([^""]+)""");
        foreach (Match match in matches)
        {
            string libraryPath = match.Groups[1].Value;
            string pluginsPath = Path.Combine(libraryPath, "steamapps", "common", "Valheim", "BepInEx", "plugins");
            if (Directory.Exists(pluginsPath))
            {
                return pluginsPath;
            }
        }
    
        return null;
    }

    private static string? GetSteamPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("This feature is only supported on Windows.");
        }

        string[] registryPaths = {
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam",
        @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam"
    };

        foreach (string regPath in registryPaths)
        {
            try
            {
                string? installPath = Registry.GetValue(regPath, "InstallPath", null) as string;
                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                {
                    return installPath;
                }
            }
            catch
            {
                // Ignore and continue
            }
        }

        return null;
    }

    private static void WaitForUserExit()
    {
        Console.WriteLine($"\n{ConsoleSymbols.Info} Press any key to exit...");
        Console.ReadKey();
    }

}
