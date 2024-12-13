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
using System.Text;

[JsonSerializable(typeof(ReleaseInfo))]
internal partial class ReleaseContext : JsonSerializerContext { }

internal class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("assets")]
    public List<Asset>? Assets { get; set; } = new List<Asset>();

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class Asset
{
    [JsonPropertyName("browser_download_url")]
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

    private static PluginsStatus cachedPluginsStatus = PluginsStatus.Unknown;
    private static LauncherStatus cachedLauncherStatus = LauncherStatus.Unknown;
    private static string? cachedLatestVersion;
    private static bool isStatusInitialized = false;
    private const string GithubOwner = "dankmaster";
    private const string GithubRepo = "ValheimLauncher";
    private const string GithubBranch = "master";
    private const string GithubApiBaseUrl = $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}";
    private const string GithubModsUrl = $"https://github.com/{GithubOwner}/{GithubRepo}/raw/{GithubBranch}/Mods/plugins.zip";
    private const string BepInExDownloadUrl = "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2202/";
    private const string SteamAppID = "892970";
    private static readonly HttpClient httpClient = new HttpClient();

    private enum BepInExStatus
    {
        NotInstalled,
        Enabled,
        Disabled
    }

    private enum MainMenuOption
    {
        ToggleBepInEx = 1,
        CheckUpdates = 2,
        LaunchGame = 3,
        Exit = 4
    }

    private enum LauncherStatus
    {
        UpToDate,
        UpdateAvailable,
        Unknown
    }

    private enum PluginsStatus
    {
        UpToDate,
        UpdateAvailable,
        Unknown
    }

    static async Task Main()
    {
        if (!isStatusInitialized)
        {
            Console.WriteLine($"{ConsoleSymbols.Info} Initializing launcher statuses...");
            var statusTask = Task.WhenAll(
                Task.Run(async () => (cachedLauncherStatus, cachedLatestVersion) = await GetLauncherStatus()),
                Task.Run(async () => cachedPluginsStatus = await GetPluginsStatus())
            );
            await statusTask;
            isStatusInitialized = true;
        }
        Console.Title = "Valheim Mod Launcher";

        try
        {
            AppPaths.EnsureDirectoriesExist();

            // Check if this is first run
            bool isFirstRun = !File.Exists(AppPaths.FirstRunFlag);
            if (isFirstRun)
            {
                ShowFirstTimeUserInfo();
                File.WriteAllText(AppPaths.FirstRunFlag, DateTime.Now.ToString());
            }

            // Check for launcher updates first
            if (await CheckForLauncherUpdate())
            {
                Console.WriteLine($"{ConsoleSymbols.Info} Press any key to exit and install the update...");
                Console.ReadKey();
                return;
            }

            // Find Valheim installation
            string? valheimPath = GetValheimInstallPath();
            if (string.IsNullOrEmpty(valheimPath))
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Valheim installation not found.");
                WaitForUserExit();
                return;
            }

            Console.WriteLine($"{ConsoleSymbols.Success} Found Valheim installation: {valheimPath}");

            while (true)
            {
                var bepInExStatus = GetBepInExStatus(valheimPath);
                await DisplayMainMenu(bepInExStatus);

                if (!int.TryParse(Console.ReadLine(), out int choice))
                {
                    Console.WriteLine($"{ConsoleSymbols.Error} Invalid input. Please enter a number.");
                    continue;
                }

                if (!Enum.IsDefined(typeof(MainMenuOption), choice))
                {
                    Console.WriteLine($"{ConsoleSymbols.Error} Invalid option. Please select from the menu.");
                    continue;
                }

                var option = (MainMenuOption)choice;

                switch (option)
                {
                    case MainMenuOption.ToggleBepInEx:
                        await HandleBepInExToggle(valheimPath, bepInExStatus);
                        break;

                    case MainMenuOption.CheckUpdates:
                        await HandleModUpdates(valheimPath, bepInExStatus);
                        break;

                    case MainMenuOption.Exit:
                        Console.WriteLine($"{ConsoleSymbols.Info} Exiting launcher...");
                        return;

                    case MainMenuOption.LaunchGame:
                        LaunchGame(valheimPath);
                        break;
                }

                Console.WriteLine("\nPress any key to return to menu...");
                Console.ReadKey();
                Console.Clear();
            }
        }
        catch (Exception ex)
        {
            AppPaths.LogError("Application startup error", ex);
            Console.WriteLine($"{ConsoleSymbols.Error} An error occurred: {ex.Message}");
            WaitForUserExit();
        }
        finally
        {
            AppPaths.CleanupTemp();
        }
    }

    private static void ShowFirstTimeUserInfo()
        {
            Console.Clear();
            Console.WriteLine("=================================");
            Console.WriteLine("Welcome to Valheim Mod Launcher!");
            Console.WriteLine("=================================\n");
            Console.WriteLine("This launcher helps you manage Valheim mods by:");
            Console.WriteLine("1. Installing and managing BepInEx (the mod framework)");
            Console.WriteLine("2. Keeping your mods up to date");
            Console.WriteLine("3. Letting you easily switch between modded/unmodded play\n");
            Console.WriteLine("First, we'll check your Valheim installation...");
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    private static void LaunchGame()
    {
        try
        {
            const string steamAppID = "892970"; // Valheim's Steam App ID
            string steamPath = GetSteamPath();

            if (string.IsNullOrEmpty(steamPath))
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Steam installation not found.");
                return;
            }

            string steamExe = Path.Combine(steamPath, "steam.exe");
            if (!File.Exists(steamExe))
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Steam executable not found.");
                return;
            }

            Console.WriteLine($"{ConsoleSymbols.Info} Launching Valheim via Steam...");
            Process.Start(steamExe, $"-applaunch {steamAppID}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Failed to launch Valheim via Steam: {ex.Message}");
        }
    }


    private static async Task DisplayMainMenu(BepInExStatus bepInExStatus)
    {
        Console.Clear();
        DisplayWelcomeMessage();

        var pluginsStatus = await GetPluginsStatus();
        var (launcherStatus, latestVersion) = await GetLauncherStatus();

        Console.WriteLine($"{ConsoleSymbols.Info} Current Status:");
        Console.WriteLine($"Launcher: {GetLauncherStatusDisplay(cachedLauncherStatus, cachedLatestVersion)}");
        Console.WriteLine($"BepInEx: {GetStatusDisplay(bepInExStatus)}");
        Console.WriteLine($"Plugins: {GetPluginsStatusDisplay(cachedPluginsStatus)}");
        Console.WriteLine($"Mod Repository: {GithubOwner}/{GithubRepo} (branch: {GithubBranch})");

        if (launcherStatus == LauncherStatus.UpdateAvailable)
        {
            Console.WriteLine($"\n{ConsoleSymbols.Warning} Launcher update available! Would you like to update now? (Yes/No): ");
            if (ConfirmAction())
            {
                await HandleLauncherUpdate(latestVersion!);
                return;
            }
        }

        Console.WriteLine("\nAvailable Options:");
        Console.WriteLine($"1. {GetBepInExMenuText(bepInExStatus)}");
        Console.WriteLine("2. Check for Updates");
        Console.WriteLine("3. Launch Game");
        Console.WriteLine("4. Exit Launcher");

        Console.Write("\nSelect an option: ");
    }

    private static async Task HandleLauncherUpdate(string latestVersion)
    {
        try
        {
            Console.WriteLine($"\n{ConsoleSymbols.Progress} Downloading launcher update v{latestVersion}...");
            var response = await httpClient.GetAsync($"{GithubApiBaseUrl}/releases/latest");
            response.EnsureSuccessStatusCode();

            var releaseJson = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var releaseInfo = JsonSerializer.Deserialize<ReleaseInfo>(releaseJson, options);

            var assetUrl = releaseInfo?.Assets?.FirstOrDefault()?.BrowserDownloadUrl;
            if (string.IsNullOrEmpty(assetUrl))
            {
                Console.WriteLine($"{ConsoleSymbols.Error} No download asset found in release.");
                return;
            }

            await DownloadFileAsync(assetUrl, AppPaths.UpdateZip);
            CreateUpdateScript(AppPaths.UpdateZip);

            Console.WriteLine($"{ConsoleSymbols.Info} Press any key to restart and install the update...");
            Console.ReadKey();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ConsoleSymbols.Error} Update failed: {ex.Message}");
            Console.WriteLine($"{ConsoleSymbols.Info} Press any key to continue...");
            Console.ReadKey();
        }
    }

    private static string GetLauncherStatusDisplay(LauncherStatus status, string? latestVersion)
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

        return status switch
        {
            LauncherStatus.UpToDate => $"Up to date (Current: v{currentVersion})",
            LauncherStatus.UpdateAvailable => $"Update available! (Current: v{currentVersion}, Latest: v{latestVersion})",
            LauncherStatus.Unknown => $"Status check failed (Current: v{currentVersion})",
            _ => "Unknown"
        };
    }

    private static string GetPluginsStatusDisplay(PluginsStatus status)
        {
            return status switch
            {
                PluginsStatus.UpToDate => "Up to date",
                PluginsStatus.UpdateAvailable => "Update available",
                PluginsStatus.Unknown => "Status unknown",
                _ => "Unknown"
            };
        }

    private static async Task<(LauncherStatus Status, string? LatestVersion)> GetLauncherStatus()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ValheimLauncher");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

            var response = await httpClient.GetAsync($"{GithubApiBaseUrl}/releases/latest", cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.WriteLine($"{ConsoleSymbols.Warning} No releases found on GitHub.");
                return (LauncherStatus.Unknown, null);
            }

            response.EnsureSuccessStatusCode();
            var releaseJson = await response.Content.ReadAsStringAsync(cts.Token);

            // Debug logging
            var debugLog = new StringBuilder();
            debugLog.AppendLine("=== Release Info Debug ===");
            debugLog.AppendLine($"Raw JSON: {releaseJson}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var releaseInfo = JsonSerializer.Deserialize<ReleaseInfo>(releaseJson, options);

            debugLog.AppendLine($"Deserialized TagName: {releaseInfo?.TagName}");

            // Get current version
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString().TrimEnd('.', '0');
            debugLog.AppendLine($"Current Version: {currentVersion}");

            if (releaseInfo?.TagName == null)
            {
                debugLog.AppendLine("Error: TagName is null");
                File.WriteAllText(Path.Combine(AppPaths.LogsPath, "version_debug.log"), debugLog.ToString());
                return (LauncherStatus.Unknown, null);
            }

            var latestVersion = releaseInfo.TagName.TrimStart('v');
            debugLog.AppendLine($"Latest Version: {latestVersion}");

            // Write debug info to file
            File.WriteAllText(Path.Combine(AppPaths.LogsPath, "version_debug.log"), debugLog.ToString());

            // Normalize versions to three segments
            var currentParts = currentVersion?.Split('.') ?? Array.Empty<string>();
            var currentNormalized = string.Join(".", currentParts.Take(3));
            var latest = Version.Parse(latestVersion);
            var current = Version.Parse(currentNormalized);

            debugLog.AppendLine($"Normalized Current: {currentNormalized}");
            debugLog.AppendLine($"Comparison Result: {latest.CompareTo(current)}");

            File.WriteAllText(Path.Combine(AppPaths.LogsPath, "version_debug.log"), debugLog.ToString());

            if (latest > current)
            {
                return (LauncherStatus.UpdateAvailable, latestVersion);
            }
            else if (latest == current)
            {
                return (LauncherStatus.UpToDate, latestVersion);
            }
            else
            {
                return (LauncherStatus.UpToDate, latestVersion);
            }
        }
        catch (Exception ex)
        {
            AppPaths.LogError("Launcher status check failed", ex);
            File.WriteAllText(
                Path.Combine(AppPaths.LogsPath, "version_error.log"),
                $"Error details: {ex.Message}\nStack trace: {ex.StackTrace}"
            );
            return (LauncherStatus.Unknown, null);
        }
    }

    private static async Task<PluginsStatus> GetPluginsStatus()
        {
            try
            {
                string? valheimPath = GetValheimInstallPath();
                if (string.IsNullOrEmpty(valheimPath))
                {
                    return PluginsStatus.Unknown;
                }

                string pluginsFolder = Path.Combine(valheimPath, "BepInEx", "plugins");
                if (!Directory.Exists(pluginsFolder))
                {
                    return PluginsStatus.Unknown;
                }

                if (await TestUpdateNeeded(pluginsFolder))
                {
                    return PluginsStatus.UpdateAvailable;
                }

                return PluginsStatus.UpToDate;
            }
            catch (Exception)
            {
                return PluginsStatus.Unknown;
            }
        }

        private static string GetStatusDisplay(BepInExStatus status)
        {
            return status switch
            {
                BepInExStatus.NotInstalled => "Not Installed",
                BepInExStatus.Enabled => "Enabled",
                BepInExStatus.Disabled => "Disabled",
                _ => "Unknown"
            };
        }

        private static string GetBepInExMenuText(BepInExStatus status)
        {
            return status switch
            {
                BepInExStatus.NotInstalled => "Install BepInEx",
                BepInExStatus.Enabled => "Disable BepInEx",
                BepInExStatus.Disabled => "Enable BepInEx",
                _ => "Manage BepInEx"
            };
        }

        private static BepInExStatus GetBepInExStatus(string valheimPath)
        {
            string winhttpPath = Path.Combine(valheimPath, "winhttp.dll");
            string winhttpDisabledPath = Path.Combine(valheimPath, "winhttp.dll.disabled");
            string bepInExPath = Path.Combine(valheimPath, "BepInEx");

            if (!Directory.Exists(bepInExPath))
            {
                return BepInExStatus.NotInstalled;
            }

            if (File.Exists(winhttpPath))
            {
                return BepInExStatus.Enabled;
            }

            if (File.Exists(winhttpDisabledPath))
            {
                return BepInExStatus.Disabled;
            }

            return BepInExStatus.NotInstalled;
        }

    private static async Task HandleBepInExToggle(string valheimPath, BepInExStatus currentStatus)
    {
        switch (currentStatus)
        {
            case BepInExStatus.NotInstalled:
                if (PromptBepInExInstallation())
                {
                    await InstallBepInEx(valheimPath);
                }
                break;

            case BepInExStatus.Enabled:
                Console.WriteLine($"{ConsoleSymbols.Info} Disabling BepInEx...");
                DisableBepInEx(valheimPath);
                break;

            case BepInExStatus.Disabled:
                Console.WriteLine($"{ConsoleSymbols.Info} Enabling BepInEx...");
                EnableBepInEx(valheimPath);
                break;
        }
    }


    private static void DisableBepInEx(string valheimPath)
        {
            try
            {
                string winhttpPath = Path.Combine(valheimPath, "winhttp.dll");
                string winhttpDisabledPath = Path.Combine(valheimPath, "winhttp.dll.disabled");

                if (File.Exists(winhttpPath))
                {
                    File.Move(winhttpPath, winhttpDisabledPath, true);
                    Console.WriteLine($"{ConsoleSymbols.Success} BepInEx has been disabled.");
                    Console.WriteLine($"{ConsoleSymbols.Info} Please restart Valheim for changes to take effect.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Failed to disable BepInEx: {ex.Message}");
            }
        }

        private static void EnableBepInEx(string valheimPath)
        {
            try
            {
                string winhttpPath = Path.Combine(valheimPath, "winhttp.dll");
                string winhttpDisabledPath = Path.Combine(valheimPath, "winhttp.dll.disabled");

                if (File.Exists(winhttpDisabledPath))
                {
                    File.Move(winhttpDisabledPath, winhttpPath, true);
                    Console.WriteLine($"{ConsoleSymbols.Success} BepInEx has been enabled.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Failed to enable BepInEx: {ex.Message}");
            }
        }

        private static async Task HandleModUpdates(string valheimPath, BepInExStatus bepInExStatus)
        {
            if (bepInExStatus != BepInExStatus.Enabled)
            {
                Console.WriteLine($"{ConsoleSymbols.Warning} BepInEx must be installed and enabled to manage mods.");
                return;
            }

            string pluginsFolder = Path.Combine(valheimPath, "BepInEx", "plugins");

            Console.WriteLine($"\n{ConsoleSymbols.Info} Checking for mod updates...");
            Console.WriteLine($"{ConsoleSymbols.Info} Using repository: {GithubOwner}/{GithubRepo} (branch: {GithubBranch})");

            if (await TestUpdateNeeded(pluginsFolder))
            {
                Console.WriteLine($"{ConsoleSymbols.Progress} Mod updates available!");
                await UpdateMods(pluginsFolder);
                cachedPluginsStatus = await GetPluginsStatus();

            }
            else
            {
                Console.WriteLine($"{ConsoleSymbols.Success} Mods are up to date!");
            }
        }

        private static bool ConfirmAction()
        {
            string? response = Console.ReadLine();
            return !string.IsNullOrEmpty(response) && response.Equals("Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetValheimInstallPath()
        {
            string? steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                return null;
            }

            string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                return null;
            }

            string content = File.ReadAllText(libraryFoldersPath);
            var matches = Regex.Matches(content, @"""path""\s*""([^""]+)""");

            foreach (Match match in matches)
            {
                string libraryPath = match.Groups[1].Value;
                string valheimPath = Path.Combine(libraryPath, "steamapps", "common", "Valheim");
                if (Directory.Exists(valheimPath))
                {
                    return valheimPath;
                }
            }

            return null;
        }

        private static bool IsBepInExInstalled(string valheimPath)
        {
            string bepInExPath = Path.Combine(valheimPath, "BepInEx");
            string winhttpPath = Path.Combine(valheimPath, "winhttp.dll");

            return Directory.Exists(bepInExPath) &&
                   File.Exists(winhttpPath) &&
                   Directory.Exists(Path.Combine(bepInExPath, "core")) &&
                   Directory.Exists(Path.Combine(bepInExPath, "plugins"));
        }

        private static bool PromptBepInExInstallation()
        {
            Console.Clear();
            Console.WriteLine($"\n{ConsoleSymbols.Info} About BepInEx:");
            Console.WriteLine("BepInEx is the framework required to run Valheim mods.");
            Console.WriteLine("- Required for any modded gameplay");
            Console.WriteLine("- Can be toggled on/off from this launcher\n");

            Console.Write($"{ConsoleSymbols.Warning} Would you like to install BepInEx now? (Yes/No): ");
            string? response = Console.ReadLine();
            return !string.IsNullOrEmpty(response) && response.Equals("Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task InstallBepInEx(string valheimPath)
        {
            try
            {
                Console.WriteLine($"\n{ConsoleSymbols.Progress} Downloading BepInEx...");
                string bepInExZip = Path.Combine(AppPaths.BepInExTempPath, "bepinex.zip");
                await DownloadFileAsync(BepInExDownloadUrl, bepInExZip);

                Console.WriteLine($"{ConsoleSymbols.Progress} Extracting BepInEx...");
                string extractPath = Path.Combine(AppPaths.BepInExTempPath, "extracted");
                ZipFile.ExtractToDirectory(bepInExZip, extractPath, true);

                // Find the BepInExPack_Valheim folder within the extracted content
                string bepInExSourcePath = Path.Combine(extractPath, "BepInExPack_Valheim");

                if (!Directory.Exists(bepInExSourcePath))
                {
                    throw new DirectoryNotFoundException("BepInEx package structure is not as expected.");
                }

                Console.WriteLine($"{ConsoleSymbols.Progress} Installing BepInEx to Valheim...");

                // Copy all contents from the BepInExPack_Valheim folder to Valheim installation directory
                foreach (string dirPath in Directory.GetDirectories(bepInExSourcePath, "*", SearchOption.AllDirectories))
                {
                    string newPath = dirPath.Replace(bepInExSourcePath, valheimPath);
                    Directory.CreateDirectory(newPath);
                    Console.WriteLine($"{ConsoleSymbols.Arrow} Created directory: {Path.GetFileName(newPath)}");
                }

                foreach (string filePath in Directory.GetFiles(bepInExSourcePath, "*.*", SearchOption.AllDirectories))
                {
                    string newPath = filePath.Replace(bepInExSourcePath, valheimPath);
                    File.Copy(filePath, newPath, true);
                    Console.WriteLine($"{ConsoleSymbols.Arrow} Installed: {Path.GetFileName(newPath)}");
                }

                Console.WriteLine($"{ConsoleSymbols.Success} BepInEx installation complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ConsoleSymbols.Error} Failed to install BepInEx: {ex.Message}");
                throw;
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
                // Add timeout to HTTP client
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ValheimLauncher");
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

                var response = await httpClient.GetAsync($"{GithubApiBaseUrl}/releases/latest", cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"{ConsoleSymbols.Warning} No releases found. Continuing with current version.");
                    return false;
                }

                response.EnsureSuccessStatusCode();

                var releaseJson = await response.Content.ReadAsStringAsync(cts.Token);

                // Log the response for debugging
                File.WriteAllText(AppPaths.LastUpdateCheckFile, releaseJson);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var releaseInfo = JsonSerializer.Deserialize<ReleaseInfo>(releaseJson, options);

                if (releaseInfo?.TagName == null)
                {
                    Console.WriteLine($"{ConsoleSymbols.Warning} Invalid release format received.");
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

                    var assetUrl = releaseInfo.Assets?.FirstOrDefault()?.BrowserDownloadUrl;
                    if (string.IsNullOrEmpty(assetUrl))
                    {
                        Console.WriteLine($"{ConsoleSymbols.Error} No download asset found in release.");
                        return false;
                    }

                    Console.WriteLine($"{ConsoleSymbols.Info} Starting update process...");
                    await DownloadFileAsync(assetUrl, AppPaths.UpdateZip);
                    CreateUpdateScript(AppPaths.UpdateZip);
                    return true;
                }
                else
                {
                    Console.WriteLine($"{ConsoleSymbols.Success} Launcher is up to date!");
                }
            }
            catch (Exception ex)
            {
                AppPaths.LogError("Update check failed", ex);
                Console.WriteLine($"{ConsoleSymbols.Error} Update check failed: {ex.Message}");
            }
            return false;
        }

        private static void CreateUpdateScript(string updatePath)
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            var currentPid = Process.GetCurrentProcess().Id;
            if (currentExe == null) return;

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

            File.WriteAllText(AppPaths.UpdateScript, script);
            var startInfo = new ProcessStartInfo(AppPaths.UpdateScript)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo);
        }

        private static bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                // Remove 'v' prefix if present
                latestVersion = latestVersion.TrimStart('v');
                currentVersion = currentVersion.TrimStart('v');

                // Parse versions into Version objects
                Version latest = Version.Parse(latestVersion);
                Version current = Version.Parse(currentVersion);

                // Compare versions
                return latest > current;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ConsoleSymbols.Warning} Version comparison error: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> TestUpdateNeeded(string pluginsFolder)
        {
            try
            {
                Console.WriteLine($"{ConsoleSymbols.Progress} Downloading mod information...");
                await DownloadFileAsync(GithubModsUrl, AppPaths.TempModsZip);

                if (Directory.Exists(AppPaths.ExtractedModsPath))
                {
                    Directory.Delete(AppPaths.ExtractedModsPath, true);
                }

                ZipFile.ExtractToDirectory(AppPaths.TempModsZip, AppPaths.ExtractedModsPath);

                string remoteChecksum = CalculateFolderChecksum(AppPaths.ExtractedModsPath);
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
            await Task.Run(() => CopyAll(
                new DirectoryInfo(AppPaths.ExtractedModsPath),
                new DirectoryInfo(pluginsFolder)
            ));
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


static class AppPaths
{
    private static readonly string BaseAppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "danklauncher",
        "Valheim"
    );

    public static readonly string LauncherPath = Path.Combine(BaseAppDataPath, "Launcher");
    public static readonly string LogsPath = Path.Combine(BaseAppDataPath, "Logs");
    public static readonly string TempPath = Path.Combine(BaseAppDataPath, "Temp");

    // Launcher specific files
    public static readonly string FirstRunFlag = Path.Combine(LauncherPath, ".firstrun");
    public static readonly string VersionFile = Path.Combine(LauncherPath, "version.json");
    public static readonly string UpdateScript = Path.Combine(LauncherPath, "update.bat");
    public static readonly string UpdateZip = Path.Combine(LauncherPath, "update.zip");

    // Temporary files
    public static readonly string TempModsPath = Path.Combine(TempPath, "mods");
    public static readonly string TempModsZip = Path.Combine(TempPath, "mods.zip");
    public static readonly string ExtractedModsPath = Path.Combine(TempPath, "mods_extracted");
    public static readonly string BepInExTempPath = Path.Combine(TempPath, "bepinex_temp");

    // Logs
    public static readonly string UpdateLogFile = Path.Combine(LogsPath, "update.log");
    public static readonly string ErrorLogFile = Path.Combine(LogsPath, "error.log");
    public static readonly string LastUpdateCheckFile = Path.Combine(LogsPath, "last_update_check.json");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(LauncherPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(TempModsPath);
        Directory.CreateDirectory(BepInExTempPath);
    }

    public static void CleanupTemp()
    {
        try
        {
            if (Directory.Exists(ExtractedModsPath))
            {
                Directory.Delete(ExtractedModsPath, true);
            }

            if (File.Exists(TempModsZip))
            {
                File.Delete(TempModsZip);
            }

            // Only clean BepInEx temp if it exists
            if (Directory.Exists(BepInExTempPath))
            {
                Directory.Delete(BepInExTempPath, true);
            }
        }
        catch (Exception ex)
        {
            LogError("Cleanup error", ex);
        }
    }

    public static void LogError(string message, Exception ex)
    {
        try
        {
            string logEntry = $"[{DateTime.Now}] {message}\n{ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(ErrorLogFile, logEntry);
        }
        catch
        {
            // If we can't log, we can't log...
        }
    }
}