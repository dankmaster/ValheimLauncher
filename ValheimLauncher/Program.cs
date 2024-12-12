using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

class Program
{
    private static string GithubZipUrl = "https://github.com/dankmaster/vhserver/raw/refs/heads/main/plugins.zip";
    private static string TempFolderPath = Path.Combine(Path.GetTempPath(), "ValheimMods");
    private static string TempZipPath = Path.Combine(TempFolderPath, "mods.zip");
    private static string ExtractedTempPath = Path.Combine(TempFolderPath, "mods_extracted");
    private static string SteamAppID = "892970";

    static async Task Main()
    {
        try
        {
            Directory.CreateDirectory(TempFolderPath);

            string? pluginsFolder = GetPluginsFolder();
            if (string.IsNullOrEmpty(pluginsFolder))
            {
                Console.WriteLine("Valheim installation not found. Exiting.");
                return;
            }

            if (await TestUpdateNeeded(pluginsFolder))
            {
                UpdateMods(pluginsFolder);
            }

            Console.WriteLine("Launching Valheim...");
            Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppID}") { UseShellExecute = true });

            Cleanup();
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using (HttpClient client = new HttpClient())
        using (HttpResponseMessage response = await client.GetAsync(url))
        using (Stream stream = await response.Content.ReadAsStreamAsync())
        {
            using (FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fileStream);
            }
        }
    }

    private static void Cleanup()
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

    private static async Task<bool> TestUpdateNeeded(string pluginsFolder)
    {
        Console.WriteLine("Downloading mods for checksum verification...");
        await DownloadFileAsync(GithubZipUrl, TempZipPath);

        if (Directory.Exists(ExtractedTempPath))
        {
            Directory.Delete(ExtractedTempPath, true);
        }

        ZipFile.ExtractToDirectory(TempZipPath, ExtractedTempPath);

        string remoteChecksum = CalculateFolderChecksum(ExtractedTempPath);
        string localChecksum = CalculateFolderChecksum(pluginsFolder);

        return remoteChecksum != localChecksum;
    }

    private static void UpdateMods(string pluginsFolder)
    {
        Console.Write($"Do you want to delete existing mods in {pluginsFolder}? (Yes/No): ");
        string? response = Console.ReadLine();
        if (string.IsNullOrEmpty(response) || !response.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Update aborted by the user.");
            return;
        }

        foreach (var file in Directory.GetFiles(pluginsFolder, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(pluginsFolder))
        {
            Directory.Delete(dir, true);
        }

        string? extractedMainDir = Directory.GetDirectories(ExtractedTempPath).FirstOrDefault();
        if (string.IsNullOrEmpty(extractedMainDir))
        {
            Console.WriteLine("No extracted directory found. Cannot update.");
            return;
        }

        CopyAll(new DirectoryInfo(ExtractedTempPath), new DirectoryInfo(pluginsFolder));
    }

    private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        if (!Directory.Exists(target.FullName))
        {
            Directory.CreateDirectory(target.FullName);
        }

        foreach (FileInfo fi in source.GetFiles())
        {
            fi.CopyTo(Path.Combine(target.ToString(), fi.Name), true);
        }

        foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
        {
            DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
            CopyAll(diSourceSubDir, nextTargetSubDir);
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
}
