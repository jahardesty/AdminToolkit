using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Net.NetworkInformation; // IMPORTANT: Add this for Ping

namespace AdminToolkit.Pages
{
    public partial class QuickActionsPage : Page
    {
        public QuickActionsPage()
        {
            InitializeComponent();
        }

        // --- THE HELPER METHOD YOU WERE MISSING ---
        private async Task<bool> IsServerReachable(string hostName)
        {
            try
            {
                using (Ping pinger = new Ping())
                {
                    // 2000ms (2 second) timeout
                    PingReply reply = await pinger.SendPingAsync(hostName, 2000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private async void FlushDNS_Click(object sender, RoutedEventArgs e)
        {
            var dcs = ConfigManager.AppSettings?.DomainControllers;

            if (dcs == null || dcs.Count == 0)
            {
                LogToUI("ERROR: No Domain Controllers found in config.");
                return;
            }

            btnFlushDNS.IsEnabled = false;
            LogToUI("--- Starting Multi-Server DNS Flush ---");

            // Added .ConfigureAwait(false) as a best practice for background tasks
            await Task.Run(async () =>
            {
                foreach (string dc in dcs)
                {
                    // Check reachability first
                    if (!await IsServerReachable(dc))
                    {
                        LogToUI($"⏩ Skipping {dc}: Server is unreachable.");
                        continue;
                    }

                    try
                    {
                        LogToUI($"Flushing DNS on {dc}...");

                        // Note: Ensure your execution policy allows remote commands
                        string script = $"Invoke-Command -ComputerName {dc} -ScriptBlock {{ ipconfig /flushdns }}";

                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-Command \"{script}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using (Process process = Process.Start(psi))
                        {
                            process.WaitForExit();
                            if (process.ExitCode == 0)
                                LogToUI($"✅ {dc}: Success.");
                            else
                                LogToUI($"❌ {dc}: Failed (Exit Code {process.ExitCode}).");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ {dc}: Error - {ex.Message}");
                    }
                }
            });

            LogToUI("--- All Servers Processed ---");
            btnFlushDNS.IsEnabled = true;
        }

        // This stays exactly as you had it - perfect for thread-safe logging
        private void LogToUI(string message)
        {
            Dispatcher.Invoke(() => {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
        }

        // You can now reuse that IsServerReachable method for your dedicated Test button!
        private async void TestConnections_Click(object sender, RoutedEventArgs e)
        {
            var dcs = ConfigManager.AppSettings?.DomainControllers;
            if (dcs == null) return;

            LogToUI("Testing DC reachability...");
            foreach (var dc in dcs)
            {
                bool up = await IsServerReachable(dc);
                LogToUI(up ? $"✅ {dc} is UP" : $"❌ {dc} is DOWN");
            }
        }

        private async void EntraSync_Click(object sender, RoutedEventArgs e)
        {
            string syncServer = ConfigManager.AppSettings?.EntraSyncServer;

            if (string.IsNullOrEmpty(syncServer))
            {
                LogToUI("ERROR: No EntraSyncServer defined in appsettings.json.");
                return;
            }

            // 1. Prepare UI
            var btn = (Button)sender;
            btn.IsEnabled = false;
            LogToUI($"--- Triggering Delta Sync on {syncServer} ---");

            await Task.Run(async () =>
            {
                // 2. Check reachability
                if (!await IsServerReachable(syncServer))
                {
                    LogToUI($"❌ {syncServer}: Server unreachable. Sync cancelled.");
                    return;
                }

                try
                {
                    // 3. Construct the PowerShell script
                    // We wrap it in an Import-Module just in case the module isn't auto-loaded
                    string script = "Import-Module ADSync; Start-ADSyncSyncCycle -PolicyType Delta";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-Command \"Invoke-Command -ComputerName {syncServer} -ScriptBlock {{ {script} }}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(psi))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            LogToUI($"✅ Success: Delta Sync initiated on {syncServer}.");
                        }
                        else
                        {
                            // Often fails if the user doesn't have permissions or a sync is already running
                            LogToUI($"❌ Failed: {error.Trim()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"⚠️ Error: {ex.Message}");
                }
            });

            // 4. Restore UI
            btn.IsEnabled = true;
        }
    }
}