using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

class Program
{
    // Configuration
    private static string GithubZipUrl = "https://github.com/dankmaster/vhserver/raw/refs/heads/main/plugins.zip";
    private static string TempFolderPath = Path.Combine(Path.GetTempPath(), "ValheimMods");
    private static string TempZipPath = Path.Combine(TempFolderPath, "mods.zip");
    private static string ExtractedTempPath = Path.Combine(TempFolderPath, "mods_extracted");
    private static string SteamAppID = "892970"; // Valheim Steam AppID

    static void Main()
    {
        try
        {
            Directory.CreateDirectory(TempFolderPath);

            string pluginsFolder = GetPluginsFolder();
            if (string.IsNullOrEmpty(pluginsFolder))
            {
                Console.WriteLine("Valheim installation not found. Exiting.");
                return;
            }

            // Check if update is needed
            if (TestUpdateNeeded(pluginsFolder))
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

    private static bool TestUpdateNeeded(string pluginsFolder)
    {
        Console.WriteLine("Downloading mods for checksum verification...");
        using (WebClient wc = new WebClient())
        {
            wc.DownloadFile(GithubZipUrl, TempZipPath);
        }

        if (Directory.Exists(ExtractedTempPath))
        {
            Directory.Delete(ExtractedTempPath, true);
        }

        ZipFile.ExtractToDirectory(TempZipPath, ExtractedTempPath);

        // Debug: List directories and files
        Console.WriteLine("Extracted the following directories:");
        foreach (var dir in Directory.GetDirectories(ExtractedTempPath))
        {
            Console.WriteLine("Dir: " + dir);
        }

        Console.WriteLine("Extracted the following files:");
        foreach (var file in Directory.GetFiles(ExtractedTempPath))
        {
            Console.WriteLine("File: " + file);
        }

        // The extracted structure may be something like "vhserver-main/..." 
        // Adjust if needed. Assuming the root folder inside zip is "vhserver-main".
        // If the actual mods are one folder deeper, adjust accordingly.

        string extractedMainDir = Directory.GetDirectories(ExtractedTempPath).FirstOrDefault();
        if (extractedMainDir == null)
        {
            Console.WriteLine("No extracted directory found in the ZIP.");
            return false;
        }

        // We now consider ExtractedTempPath as the root of all extracted mods.
        string remoteChecksum = CalculateFolderChecksum(ExtractedTempPath);
        string localChecksum = CalculateFolderChecksum(pluginsFolder);

        if (remoteChecksum != localChecksum)
        {
            Console.WriteLine("Checksum mismatch. Update required.");
            return true;
        }

        Console.WriteLine("No update required.");
        return false;
    }

    private static void UpdateMods(string pluginsFolder)
    {
        Console.Write($"Do you want to delete existing mods in {pluginsFolder}? (Yes/No): ");
        string response = Console.ReadLine();
        if (!response.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Update aborted by the user.");
            return;
        }

        Console.WriteLine("Removing old plugins...");
        foreach (var file in Directory.GetFiles(pluginsFolder, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(pluginsFolder))
        {
            Directory.Delete(dir, true);
        }

        string extractedMainDir = Directory.GetDirectories(ExtractedTempPath).FirstOrDefault();
        if (extractedMainDir == null)
        {
            Console.WriteLine("No extracted directory found. Cannot update.");
            return;
        }

        Console.WriteLine("Copying new mods to plugins folder...");
        // Copy everything from ExtractedTempPath directly into the plugins folder
        CopyAll(new DirectoryInfo(ExtractedTempPath), new DirectoryInfo(pluginsFolder));

        Console.WriteLine("Mods updated successfully.");
    }

    private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        if (!Directory.Exists(target.FullName))
        {
            Directory.CreateDirectory(target.FullName);
        }

        // Copy each file
        foreach (FileInfo fi in source.GetFiles())
        {
            fi.CopyTo(Path.Combine(target.ToString(), fi.Name), true);
        }

        // Copy each subdirectory
        foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
        {
            DirectoryInfo nextTargetSubDir =
                target.CreateSubdirectory(diSourceSubDir.Name);
            CopyAll(diSourceSubDir, nextTargetSubDir);
        }
    }

    private static string CalculateFolderChecksum(string folderPath)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            // Get all files
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                                 .OrderBy(p => p) // ensure consistent order
                                 .ToList();

            // Combine all file hashes
            List<byte> combinedHashBytes = new List<byte>();

            foreach (string file in files)
            {
                byte[] fileBytes = File.ReadAllBytes(file);
                byte[] fileHash = sha256.ComputeHash(fileBytes);
                combinedHashBytes.AddRange(fileHash);
            }

            // Compute a final hash of all combined hashes
            byte[] finalHash = sha256.ComputeHash(combinedHashBytes.ToArray());

            return BitConverter.ToString(finalHash).Replace("-", "");
        }
    }

    private static string GetPluginsFolder()
    {
        string steamPath = GetSteamPath();
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

        // Extract library paths using a regex
        // Lines usually look like: "1"  "D:\\SteamLibrary"
        // We'll look for lines with "path" "somepath"
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

    private static string GetSteamPath()
    {
        // Check registry keys
        string[] registryPaths = {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam",
            @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam"
        };

        foreach (string regPath in registryPaths)
        {
            try
            {
                var installPath = Registry.GetValue(regPath, "InstallPath", null) as string;
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
