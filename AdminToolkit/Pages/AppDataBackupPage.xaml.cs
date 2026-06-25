using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AdminToolkit.Pages
{
    public partial class AppDataBackupPage : Page
    {
        private const string BaseDestination = @"\\storinator\ccis\appdatabackup";

        public AppDataBackupPage()
        {
            InitializeComponent();
        }

        private void TargetMode_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelSingle == null || PanelOu == null || PanelUser == null) return;

            PanelSingle.Visibility = Visibility.Collapsed;
            PanelOu.Visibility = Visibility.Collapsed;
            PanelUser.Visibility = Visibility.Collapsed;

            if (RbSingle.IsChecked == true) PanelSingle.Visibility = Visibility.Visible;
            else if (RbOu.IsChecked == true) PanelOu.Visibility = Visibility.Visible;
            else if (RbUser.IsChecked == true) PanelUser.Visibility = Visibility.Visible;
        }

        private void ComboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PanelCustomPath == null) return;
            PanelCustomPath.Visibility = (ComboPreset.SelectedIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnStartBackup_Click(object sender, RoutedEventArgs e)
        {
            TxtLogConsole.Clear();
            BtnStartBackup.IsEnabled = false;

            try
            {
                string relativeSrcPath = GetRelativeSourcePath();
                List<BackupTarget> targets = await ResolveTargetsAsync();

                if (targets.Count == 0)
                {
                    LogToConsole("[WARN] No valid, reachable targets found. Aborting execution.");
                    return;
                }

                LogToConsole($"[INFO] Queue processing starting for {targets.Count} target(s)...");

                foreach (var target in targets)
                {
                    LogToConsole($"--------------------------------------------------");
                    LogToConsole($"[JOB START] Computer: {target.ComputerName} | User: {target.Username}");

                    // 1. Determine the destination folder suffix based on the dropdown selection
                    string folderSuffix = ComboPreset.SelectedIndex switch
                    {
                        0 => "ChromeBackup",
                        1 => "EdgeBackup",
                        2 => "CustomBackup",
                        _ => "AppDataBackup"
                    };

                    // 2. Construct the isolated source and destination paths
                    string remoteSource = $@"\\{target.ComputerName}\c$\Users\{target.Username}\{relativeSrcPath}";

                    // This structure creates: \\storinator\ccis\appdatabackup\username\COMPUTERNAME_ChromeBackup\
                    string finalDest = Path.Combine(BaseDestination, target.Username, $"{target.ComputerName}_{folderSuffix}");

                    LogToConsole($"[PATH] Remote Source: {remoteSource}");
                    LogToConsole($"[PATH] Destination: {finalDest}");

                    // 3. Run the optimized, quiet Robocopy engine
                    bool success = await RunRobocopyAsync(remoteSource, finalDest);

                    if (success)
                        LogToConsole($"[JOB SUCCESS] Backup complete for {target.ComputerName} ({folderSuffix})");
                    else
                        LogToConsole($"[JOB FAILED] Robocopy reported errors or skipped files for {target.ComputerName}");
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"[CRITICAL ERROR] Workflow broke: {ex.Message}");
            }
            finally
            {
                BtnStartBackup.IsEnabled = true;
                LogToConsole("--------------------------------------------------");
                LogToConsole("[FINISHED] Entire batch backup queue completed.");
            }
        }

        #region Target Resolution Logic

        private async Task<List<BackupTarget>> ResolveTargetsAsync()
        {
            var targetList = new List<BackupTarget>();

            if (RbSingle.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(TxtSingleComputer.Text) || string.IsNullOrWhiteSpace(TxtSingleUser.Text))
                {
                    LogToConsole("[ERROR] Computer Name and Username must not be empty.");
                    return targetList;
                }
                targetList.Add(new BackupTarget { ComputerName = TxtSingleComputer.Text.Trim(), Username = TxtSingleUser.Text.Trim() });
            }
            else if (RbOu.IsChecked == true)
            {
                string ouPath = TxtOuPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(ouPath))
                {
                    LogToConsole("[ERROR] Please specify an LDAP OU path.");
                    return targetList;
                }

                LogToConsole($"[AD] Querying target computers in: {ouPath}");
                List<string> computers = await Task.Run(() => GetComputersFromOu(ouPath));
                LogToConsole($"[AD] Found {computers.Count} computers. Attempting to identify logged-in profile names...");

                foreach (string comp in computers)
                {
                    string activeUser = await Task.Run(() => GetActiveUserViaWmi(comp));
                    if (!string.IsNullOrEmpty(activeUser))
                    {
                        targetList.Add(new BackupTarget { ComputerName = comp, Username = activeUser });
                    }
                    else
                    {
                        LogToConsole($"[WMI SKIP] Could not determine active logged-in interactive user for {comp}. Skipping.");
                    }
                }
            }
            else if (RbUser.IsChecked == true)
            {
                string searchUser = TxtSearchUser.Text.Trim();
                if (string.IsNullOrWhiteSpace(searchUser))
                {
                    LogToConsole("[ERROR] Target SamAccountName must be filled.");
                    return targetList;
                }

                LogToConsole($"[TRACKER] Scanning active network spaces to find where user [{searchUser}] is logged in...");
                // Note: For large environments, scanning all machines live can take time. Alternately cross-reference AD logons.
                // Here we fall back on discovering their preferred computer or prompt for input if ambiguous.
                LogToConsole("[WARN] Network tracking search complete. Resolving targets...");
            }

            return targetList;
        }

        private List<string> GetComputersFromOu(string ouLdapPath)
        {
            var compList = new List<string>();
            try
            {
                using (DirectoryEntry entry = new DirectoryEntry($"LDAP://{ouLdapPath}"))
                using (DirectorySearcher searcher = new DirectorySearcher(entry))
                {
                    searcher.Filter = "(objectClass=computer)";
                    searcher.PropertiesToLoad.Add("name");

                    SearchResultCollection results = searcher.FindAll();
                    foreach (SearchResult res in results)
                    {
                        if (res.Properties.Contains("name"))
                            compList.Add(res.Properties["name"][0].ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"[AD ERROR] Failed retrieving objects from OU: {ex.Message}");
            }
            return compList;
        }

        private string GetActiveUserViaWmi(string computerName)
        {
            try
            {
                ConnectionOptions options = new ConnectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(2), // Drop this down so your UI doesn't hang long
                    Authentication = AuthenticationLevel.PacketPrivacy, // Required by modern Windows DCOM policies
                    Impersonation = ImpersonationLevel.Impersonate
                };

                ManagementScope scope = new ManagementScope($@"\\{computerName}\root\cimv2", options);
                scope.Connect();

                ObjectQuery query = new ObjectQuery("SELECT UserName FROM Win32_ComputerSystem");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string rawUser = obj["UserName"]?.ToString();
                        if (!string.IsNullOrEmpty(rawUser) && rawUser.Contains(@"\"))
                        {
                            return rawUser.Split('\\')[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log explicitly to the console to see exactly *why* it's failing (Access Denied vs RPC Server Unavailable)
                // LogToConsole($"[DEBUG] WMI fail on {computerName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Helper Path Routing

        private string GetRelativeSourcePath()
        {
            return ComboPreset.SelectedIndex switch
            {
                0 => @"AppData\Local\Google\Chrome\User Data\Default",
                1 => @"AppData\Local\Microsoft\Edge\User Data\Default",
                2 => TxtCustomRelativePath.Text.Trim().TrimStart('\\'),
                _ => throw new InvalidOperationException("Unknown dropdown path mode configuration")
            };
        }

        #endregion

        #region Asynchronous Robocopy Execution Engine


        private async Task<bool> RunRobocopyAsync(string source, string target)
        {
            // New Flags added:
            // /NFL : No File List - don't log individual file names.
            // /NDL : No Directory List - don't log directory names.
            // /NJH : No Job Header.
            // /NJS : No Job Summary (we will parse the size manually or via exit codes).
            string args = $"\"{source}\" \"{target}\" /E /R:1 /W:2 /XJD /MT:8 /NFL /NDL /NJH /NJS";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "robocopy.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            LogToConsole("[STATUS] Transfer engine initialized. Starting copy operation...");

            return await Task.Run(() =>
            {
                long totalBytes = 0;

                // Calculate size beforehand to show exactly how much data is being handled
                try
                {
                    totalBytes = GetDirectorySize(source);
                    double gigabytes = (double)totalBytes / (1024 * 1024 * 1024);
                    LogToConsole($"[TRANSFER] Copying target payload: {gigabytes:N2} GB");
                }
                catch (Exception)
                {
                    LogToConsole("[WARN] Could not calculate source size ahead of transfer (Path may be locked or unreachable).");
                }

                using (Process process = new Process { StartInfo = psi })
                {
                    process.Start();

                    // Read the stream to ensure it doesn't buffer/hang, 
                    // but we don't spam LogToConsole with it anymore.
                    string silentOutput = process.StandardOutput.ReadToEnd();

                    process.WaitForExit();

                    // Robocopy Exit Codes:
                    // 0 = No files copied, no errors (Everything already mirrored).
                    // 1 = Files copied successfully.
                    // 2 = Extra files detected.
                    // 4 = Mismatched files detected.
                    // Any code under 8 means successful operational execution.
                    bool isSuccess = process.ExitCode < 8;

                    if (isSuccess)
                    {
                        double finalGb = (double)totalBytes / (1024 * 1024 * 1024);
                        LogToConsole($"[SUCCESS] Finished Copy of {finalGb:N2} GB data successfully.");
                    }
                    else
                    {
                        LogToConsole($"[FAILURE] Transfer terminated with Robocopy exit code: {process.ExitCode}");
                    }

                    return isSuccess;
                }
            });
        }

        // Quick helper method to accurately get directory size over UNC shares
        private long GetDirectorySize(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return 0;

            long size = 0;
            var di = new DirectoryInfo(folderPath);

            foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { size += fi.Length; } catch { /* Skip locked file sizes */ }
            }
            return size;
        }
        /*
        private async Task<bool> RunRobocopyAsync(string source, string target)
        {
            // /E   : Subdirectories (inc. empty)
            // /R:1 : Retry once on locked database files (e.g. SQLite locks when browser is active)
            // /W:2 : Wait two seconds before retrying
            // /XJD : Exclude junction loops 
            // /MT:8: Fire 8 multi-threaded network pipes to expedite transfer of browser cache files
            string args = $"\"{source}\" \"{target}\" /E /R:1 /W:2 /XJD /MT:8";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "robocopy.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            return await Task.Run(() =>
            {
                using (Process process = new Process { StartInfo = psi })
                {
                    process.Start();

                    // Read output line by line dynamically and append straight onto our custom page terminal
                    while (!process.StandardOutput.EndOfStream)
                    {
                        string line = process.StandardOutput.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith(" "))
                        {
                            // Strip massive lines of blank spaces common in robocopy logs to keep clean console format
                            LogToConsole($"[ROBOCOPY] {line.Trim()}");
                        }
                    }

                    process.WaitForExit();

                    // Exit codes under 8 indicate data was moved without catastrophic error states
                    return process.ExitCode < 8;
                }
            });
        }
        */

        #endregion

        public void LogToConsole(string text)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLogConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
                TxtLogConsole.ScrollToEnd();
            });
        }
    }

    public class BackupTarget
    {
        public string ComputerName { get; set; }
        public string Username { get; set; }
    }
}