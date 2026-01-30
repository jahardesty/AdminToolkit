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
        private DepartmentConfig _config;
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
                string json = "";
                string externalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                // 1. Try to load External File first
                if (File.Exists(externalPath))
                {
                    json = File.ReadAllText(externalPath);
                    LogToUI("Loaded configuration from external file.");
                }
                else
                {
                    // 2. Fallback: Load from Embedded Resource
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    // Format is usually: ProjectName.FolderName.FileName.json
                    string resourceName = "AdminToolkit.appsettings.json";

                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) throw new Exception("Embedded config not found.");
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            json = reader.ReadToEnd();
                        }
                    }
                    LogToUI("External config missing. Using embedded 'Shadow' config.");
                }

                // 3. Deserialize and Populate
                var config = JsonSerializer.Deserialize<DepartmentConfig>(json);
                _config = config; // Save to a private field for the skip list logic
                cmbDepartments.ItemsSource = config.Departments;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Critical Error loading configuration: " + ex.Message);
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
            string rootPath = txtSelectedPath.Text.Trim();

            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show("Invalid path. Please check the directory and try again.");
                return;
            }

            // 1. Prepare UI
            btnScan.IsEnabled = false;
            btnStart.IsEnabled = false;
            txtLog.Clear();
            LogToUI(isPurgeMode ? "!!! STARTING PURGE !!!" : "--- STARTING SCAN ---");

            long totalBytesProcessed = 0;

            await Task.Run(() =>
            {
                try
                {
                    // Use the _config we loaded at startup
                    var skipList = _config?.FoldersToSkip ?? new List<string>();

                    string[] userFolders = Directory.GetDirectories(rootPath);
                    LogToUI($"DEBUG: Found {userFolders.Length} folders in {rootPath}");

                    foreach (string userFolder in userFolders)
                    {
                        string folderName = Path.GetFileName(userFolder);

                        // Check Skip List
                        if (skipList.Any(s => s.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                        {
                            LogToUI($"Skipping: {folderName} (Protected)");
                            continue;
                        }

                        string[] foundBins = new string[0];
                        try
                        {
                            // Recursive search for the bin
                            foundBins = Directory.GetDirectories(userFolder, "$RECYCLE.BIN", SearchOption.AllDirectories);
                        }
                        catch { /* Access Denied to subfolders */ }

                        foreach (string recyclePath in foundBins)
                        {
                            long size = GetDirectorySize(recyclePath);
                            totalBytesProcessed += size;

                            if (isPurgeMode && size > 0)
                            {
                                try
                                {
                                    Directory.Delete(recyclePath, true);
                                    LogToUI($"Purged: {folderName} ({FormatSize(size)})");
                                }
                                catch (Exception ex)
                                {
                                    LogToUI($"Could not purge {folderName}: {ex.Message}");
                                }
                            }
                            else if (!isPurgeMode && size > 0)
                            {
                                LogToUI($"Found: {folderName} ({FormatSize(size)})");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"Critical Error: {ex.Message}");
                }
            });

            // 2. Wrap up UI
            Dispatcher.Invoke(() => {
                lblTotalSaved.Text = FormatSize(totalBytesProcessed);
                LogToUI("Task Complete.");
                btnScan.IsEnabled = true;
                btnStart.IsEnabled = true;
            });
        }
    }
}