using System;
using System.Collections.Generic;
using System.Management;
using System.Net; // <-- Critical for DNS resolution
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AdminToolkit.Pages
{
    /// <summary>
    /// Interaction logic for ServerStoragePage.xaml
    /// </summary>
    public partial class ServerStoragePage : Page
    {
        public ServerStoragePage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Displays instructions when the help button is clicked.
        /// </summary>
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Enter a server name and a minimum storage threshold, then click 'Check Storage' or press Enter to scan.",
                "Instructions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// Allows the user to press Enter in the text box to fire the scan immediately.
        /// </summary>
        private void TxtServerName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Prevent the default beep sound when hitting enter in a single-line textbox
                e.Handled = true;

                // Fire the check storage logic
                CheckStorage_Click(this, new RoutedEventArgs());
            }
        }

        /// <summary>
        /// Handles the core scanning logic asynchronously to prevent UI freezing.
        /// </summary>
        private async void CheckStorage_Click(object sender, RoutedEventArgs e)
        {
            string serverNameInput = txtServerName.Text.Trim();

            if (string.IsNullOrEmpty(serverNameInput))
            {
                MessageBox.Show("Please enter a valid server name.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                statusArea.Visibility = Visibility.Visible;
                btnCheckStorage.IsEnabled = false;
                dgStorageResults.ItemsSource = null;

                lblStatus.Text = $"Resolving DNS for '{serverNameInput}'...";
                string resolvedIp = "Unknown";

                // 1. Resolve Hostname to IP
                try
                {
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(serverNameInput);
                    foreach (var addr in addresses)
                    {
                        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            resolvedIp = addr.ToString();
                            break;
                        }
                    }
                }
                catch (System.Net.Sockets.SocketException)
                {
                    MessageBox.Show($"Could not resolve DNS for host '{serverNameInput}'.", "DNS Lookup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                lblStatus.Text = $"Querying storage on {serverNameInput} ({resolvedIp})...";
                List<ServerStorageInfo> scanResults = new List<ServerStorageInfo>();

                // 2. Query the actual Windows Server over the network
                await Task.Run(() =>
                {
                    // Set up the scope path targeting the remote machine's root CIM namespace
                    // (Uses the resolved IP to ensure we target the exact machine)
                    ManagementScope scope = new ManagementScope($@"\\{resolvedIp}\root\cimv2");

                    // Configure connection options (uses current logged-in user credentials by default)
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Impersonation = ImpersonationLevel.Impersonate,
                        Authentication = AuthenticationLevel.PacketPrivacy // Standard for modern OS connection security
                    };
                    scope.Options = options;
                    scope.Connect();

                    // Query specifically for Local Fixed Disks (DriveType = 3) to skip network maps and DVD drives
                    SelectQuery query = new SelectQuery("SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3");

                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                    using (ManagementObjectCollection queryCollection = searcher.Get())
                    {
                        foreach (ManagementObject m in queryCollection)
                        {
                            // WMI/CIM returns bytes as raw ulong values. Converting to GB:
                            ulong rawTotalBytes = (ulong)m["Size"];
                            ulong rawFreeBytes = (ulong)m["FreeSpace"];

                            double totalGB = Math.Round((double)rawTotalBytes / 1024 / 1024 / 1024, 1);
                            double freeGB = Math.Round((double)rawFreeBytes / 1024 / 1024 / 1024, 1);
                            double usedGB = Math.Round(totalGB - freeGB, 1);

                            double percentUsedCalc = (totalGB > 0) ? (usedGB / totalGB) * 100 : 0;

                            scanResults.Add(new ServerStorageInfo
                            {
                                // Combines Hostname + Drive Letter (e.g., AUDITOR - C:)
                                ServerName = $"{serverNameInput.ToUpper()} ({m["DeviceID"]})",
                                IpAddress = resolvedIp,
                                TotalStorage = totalGB,
                                UsedStorage = usedGB,
                                FreeStorage = freeGB,
                                PercentUsed = $"{Math.Round(percentUsedCalc, 0)}%"
                            });
                        }
                    }
                });

                // 3. Update the UI DataGrid with the live rows
                dgStorageResults.ItemsSource = scanResults;

                if (scanResults.Count == 0)
                {
                    MessageBox.Show($"Connected to '{serverNameInput}', but no local fixed drives were found.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"Access Denied. Your current account doesn't have administrative permissions on '{serverNameInput}'.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect or query server: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                statusArea.Visibility = Visibility.Collapsed;
                lblStatus.Text = "Scanning storage disks...";
                btnCheckStorage.IsEnabled = true;
            }
        }
    } // Closes ServerStoragePage Class

    public class ServerStorageInfo
    {
        public string ServerName { get; set; }
        public string IpAddress { get; set; }
        public double TotalStorage { get; set; }
        public double UsedStorage { get; set; }
        public double FreeStorage { get; set; }
        public string PercentUsed { get; set; }
    }
} // Closes Namespace