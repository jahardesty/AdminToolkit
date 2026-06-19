using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Net.NetworkInformation;

namespace AdminToolkit.Pages
{
    public partial class QuickActionsPage : Page
    {
        public QuickActionsPage()
        {
            InitializeComponent();
        }
        private async Task<bool> IsServerReachable(string hostName)
        {
            try
            {
                using (Ping pinger = new Ping())
                { 
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

            await Task.Run(async () =>
            {
                foreach (string dc in dcs)
                {
                    if (!await IsServerReachable(dc))
                    {
                        LogToUI($"⏩ Skipping {dc}: Server is unreachable.");
                        continue;
                    }

                    try
                    {
                        LogToUI($"Flushing DNS on {dc}...");

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

        private void LogToUI(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
        }
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
         var btn = (Button)sender;
            btn.IsEnabled = false;
            LogToUI($"--- Triggering Delta Sync on {syncServer} ---");

            await Task.Run(async () =>
            { 
                if (!await IsServerReachable(syncServer))
                {
                    LogToUI($"❌ {syncServer}: Server unreachable. Sync cancelled.");
                    return;
                }

                try
                {
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
                            if (error.Contains("AAD is busy"))
                            {
                                LogToUI($"⚠️ Warning: {syncServer} is currently busy. Try again later.");
                            }
                            else
                            {
                                LogToUI($"❌ Failed: {error.Trim()}");
                            }

                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"⚠️ Error: {ex.Message}");
                }
            });

            btn.IsEnabled = true;
        }
        private async void CheckDAServices_Click(object sender, RoutedEventArgs e)
        {
            var servers = ConfigManager.AppSettings?.DesktopAuthorityServers;
            var services = ConfigManager.AppSettings?.DesktopAuthorityServices;
            if (servers == null || services == null || servers.Count == 0)
            {
                LogToUI("ERROR: Desktop Authority configuration missing in appsettings.json");
                return;
            }

            btnCheckDAServices.IsEnabled = false;
            LogToUI("--- Auditing Desktop Authority Services ---");

            await Task.Run(async () =>
            {
                string serviceList = "'" + string.Join("','", services) + "'";

                foreach (string server in servers)
                {
                    if (!await IsServerReachable(server))
                    {
                        LogToUI($"⏩ {server}: Offline. Skipping.");
                        continue;
                    }

                    try
                    {
                        string[] daServices = { "SLManagerService", "Quest.DesktopAuthority.Execution" };
                        string services = "'" + string.Join("','", daServices) + "'";

                        // This script checks each service and starts it if it's stopped
                        string script = $@"
    $services = {serviceList}
    foreach ($svcName in $services) {{
        $s = Get-Service -DisplayName $svcName -ErrorAction SilentlyContinue
        if ($null -eq $s) {{
            Write-Output ""NOT_INSTALLED: $svcName""
        }} elseif ($s.Status -ne 'Running') {{
            try {{
                Start-Service -DisplayName $svcName
                Write-Output ""RESTARTED: $svcName""
            }} catch {{
                Write-Output ""FAILED_TO_START: $svcName""
            }}
        }} else {{
            Write-Output ""ALREADY_RUNNING: $svcName""
        }}
    }}";

                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-Command \"Invoke-Command -ComputerName {server} -ScriptBlock {{ {script} }}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using (Process process = Process.Start(psi))
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();

                            LogToUI($"Report for {server}:");
                            var results = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                            foreach (var result in results)
                            {
                                if (result.Contains("ALREADY_RUNNING")) LogToUI($"  ✅ {result}");
                                else if (result.Contains("RESTARTED")) LogToUI($"  🛠️ {result} (Was stopped, now started)");
                                else if (result.Contains("FAILED")) LogToUI($"  ❌ {result} (Manual intervention needed)");
                                else LogToUI($"  ❓ {result}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"  ⚠️ {server} Error: {ex.Message}");
                    }
                }
            });

            LogToUI("--- Desktop Authority Audit Complete ---");
            btnCheckDAServices.IsEnabled = true;
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
        }
    }
}