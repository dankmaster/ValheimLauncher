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

class Program
{
    private const string GithubApiBaseUrl = "https://api.github.com/repos/dankmaster/vhserver";
    private const string GithubRawBaseUrl = "https://github.com/dankmaster/vhserver/raw";
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
                Console.WriteLine("Press any key to exit and install the update...");
                Console.ReadKey();
                return;
            }

            // Find Valheim installation
            string? pluginsFolder = GetPluginsFolder();
            if (string.IsNullOrEmpty(pluginsFolder))
            {
                Console.WriteLine("❌ Valheim installation not found.");
                WaitForUserExit();
                return;
            }

            Console.WriteLine($"✅ Found Valheim plugins folder: {pluginsFolder}");

            // Check for mod updates
            Console.WriteLine("\nChecking for mod updates...");
            if (await TestUpdateNeeded(pluginsFolder))
            {
                Console.WriteLine("🔄 Mod updates available!");
                await UpdateMods(pluginsFolder);
            }
            else
            {
                Console.WriteLine("✅ Mods are up to date!");
            }

            // Launch game
            Console.WriteLine("\n🎮 Launching Valheim...");
            Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppID}") { UseShellExecute = true });

            WaitForUserExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ An error occurred: {ex.Message}");
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
        Console.WriteLine("   Valheim Mod Launcher v" + Assembly.GetExecutingAssembly().GetName().Version);
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
                Console.WriteLine("⚠️ Could not check for updates. Will continue with current version.");
                return false;
            }

            var releaseJson = await response.Content.ReadAsStringAsync();
            var releaseInfo = JsonSerializer.Deserialize<JsonElement>(releaseJson);
            var latestVersion = releaseInfo.GetProperty("tag_name").GetString()?.TrimStart('v');

            // Get current version and remove 'v' prefix if present for comparison
            var currentVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0";

            Console.WriteLine($"Current version: {currentVersion}");
            Console.WriteLine($"Latest version: {latestVersion}");

            if (latestVersion != null && IsNewerVersion($"v{latestVersion}", currentVersion))
            {
                Console.WriteLine($"🔄 New launcher version available: v{latestVersion}");
                Console.WriteLine("Starting update process...");

                // Download and prepare the update
                var assetUrl = releaseInfo.GetProperty("assets")[0].GetProperty("browser_download_url").GetString();
                if (assetUrl != null)
                {
                    var updatePath = Path.Combine(AppDataPath, "update.zip");
                    await DownloadFileAsync(assetUrl, updatePath);

                    // Create update batch script
                    CreateUpdateScript(updatePath);
                    return true;
                }
            }
            else
            {
                Console.WriteLine("✅ Launcher is up to date!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to check for updates: {ex.Message}");
        }
        return false;
    }

    private static void CreateUpdateScript(string updatePath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (currentExe == null) return;

        var scriptPath = Path.Combine(AppDataPath, "update.bat");
        var script = $@"@echo off
timeout /t 2 /nobreak
del ""{currentExe}""
powershell Expand-Archive -Path ""{updatePath}"" -DestinationPath ""{Path.GetDirectoryName(currentExe)}"" -Force
del ""{updatePath}""
start """" ""{currentExe}""
del ""%~f0""";

        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo(scriptPath) { UseShellExecute = true });
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
            Console.WriteLine("📥 Downloading mod information...");
            await DownloadFileAsync($"{GithubRawBaseUrl}/main/plugins.zip", TempZipPath);

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
            Console.WriteLine($"⚠️ Error checking for updates: {ex.Message}");
            return false;
        }
    }

    private static async Task UpdateMods(string pluginsFolder)
    {
        Console.Write($"\n⚠️ Do you want to update mods in {pluginsFolder}? (Yes/No): ");
        string? response = Console.ReadLine();
        if (string.IsNullOrEmpty(response) || !response.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("❌ Update aborted by user.");
            return;
        }

        Console.WriteLine("\n🔄 Updating mods...");
        Console.WriteLine("Removing old mods...");

        // Delete old files
        foreach (var file in Directory.GetFiles(pluginsFolder, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
            Console.WriteLine($"Removed: {Path.GetFileName(file)}");
        }

        foreach (var dir in Directory.GetDirectories(pluginsFolder))
        {
            Directory.Delete(dir, true);
            Console.WriteLine($"Removed directory: {Path.GetFileName(dir)}");
        }

        Console.WriteLine("\nInstalling new mods...");

        // Copy new files
        CopyAll(new DirectoryInfo(ExtractedTempPath), new DirectoryInfo(pluginsFolder));

        Console.WriteLine("✅ Mods updated successfully!");
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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
                Console.Write($"\rDownload progress: {percentage}%");
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
            Console.WriteLine($"⚠️ Cleanup error: {ex.Message}");
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
                    Console.WriteLine($"Installed: {fi.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to copy {fi.Name}: {ex.Message}");
                }
            }

            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
                Console.WriteLine($"Created directory: {diSourceSubDir.Name}");
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during directory copy: {ex.Message}");
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
        string? steamPath = GetSteamPath();
        if (string.IsNullOrEmpty(steamPath))
        {
            Console.WriteLine("Steam installation not found.");
            return null;
        }

        string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            Console.WriteLine("libraryfolders.vdf not found. Cannot locate Valheim.");
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

        [SupportedOSPlatform("windows")]
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
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

}

