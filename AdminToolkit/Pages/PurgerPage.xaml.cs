using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace AdminToolkit.Pages
{
    public partial class PurgerPage : Page
    {
        public class DepartmentConfig
        {
            public List<Department> Departments { get; set; }
            public List<string> FoldersToSkip { get; set; } = new List<string>();
        }
        public class Department
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }

        public PurgerPage()
        {
            InitializeComponent();
            LoadDepartments();
        }

        private void LoadDepartments()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!File.Exists(configPath)) return;

                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<DepartmentConfig>(json);
                cmbDepartments.ItemsSource = config.Departments;
            }
            catch (Exception ex)
            {
                LogToUI($"Error loading departments: {ex.Message}");
            }
        }

        private void CmbDepartments_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbDepartments.SelectedValue != null)
            {
                txtSelectedPath.Text = cmbDepartments.SelectedValue.ToString();
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                txtSelectedPath.Text = dialog.FolderName;
            }
        }

        private long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(path);
                foreach (FileInfo fi in di.GetFiles("*", SearchOption.AllDirectories))
                {
                    size += fi.Length;
                }
            }
            catch { /* Skip folders where access is denied */ }
            return size;
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            await ExecutePurgerLogic(isPurgeMode: false);
        }

        private async void StartPurge_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to permanently delete all files in these recycle bins?",
                                         "Confirm Purge", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await ExecutePurgerLogic(isPurgeMode: true);
            }
        }

        private string FormatSize(long bytes)
        {
            string[] Suffix = { "Bytes", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < Suffix.Length - 1)
            {
                i++;
                dblSByte /= 1024;
            }
            return $"{Math.Round(dblSByte, 2)} {Suffix[i]}";
        }

        private void LogToUI(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
        }

        private async Task ExecutePurgerLogic(bool isPurgeMode)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            string json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<DepartmentConfig>(json);
            var skipList = config.FoldersToSkip ?? new List<string>();
            string rootPath = txtSelectedPath.Text.Trim();
            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show("Invalid path. Please check the directory and try again.");
                return;
            }

            btnScan.IsEnabled = false;
            btnStart.IsEnabled = false;
            txtLog.Clear();
            LogToUI(isPurgeMode ? "!!! STARTING PURGE !!!" : "--- STARTING SCAN ---");

            long totalBytesProcessed = 0;

            await Task.Run(() =>
            {
                try
                {
                    string[] userFolders = Directory.GetDirectories(rootPath);
                    LogToUI($"DEBUG: Found {userFolders.Length} user folders in {rootPath}");

                    foreach (string userFolder in userFolders)
                    {
                        string folderName = Path.GetFileName(userFolder);
                        bool shouldSkip = skipList.Any(s => s.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                        if (shouldSkip)
                        {
                            LogToUI($"Skipping: {folderName} (on skip list)");
                            continue;
                        }
                        // SEARCH OPTION: We use a try-catch search to find the bin even if it's buried
                        string[] foundBins = new string[0];
                        try
                        {
                            // This looks for any folder named $RECYCLE.BIN inside the user's directory
                            foundBins = Directory.GetDirectories(userFolder, "$RECYCLE.BIN", SearchOption.AllDirectories);
                        }
                        catch { /* Access Denied on certain subfolders */ }

                        foreach (string recyclePath in foundBins)
                        {
                            long size = GetDirectorySize(recyclePath);
                            totalBytesProcessed += size;

                            if (isPurgeMode && size > 0)
                            {
                                try { Directory.Delete(recyclePath, true); }
                                catch (Exception ex) { LogToUI($"Could not purge {recyclePath}: {ex.Message}"); }
                            }

                            string userName = Path.GetFileName(userFolder);
                            LogToUI($"{(isPurgeMode ? "Purged" : "Found")}: {userName} ({FormatSize(size)})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"Critical Error: {ex.Message}");
                }
            });

            lblTotalSaved.Text = FormatSize(totalBytesProcessed);
            LogToUI("Task Complete.");
            btnScan.IsEnabled = true;
            btnStart.IsEnabled = true;
        }
    }
}