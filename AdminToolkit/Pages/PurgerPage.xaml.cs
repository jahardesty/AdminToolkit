using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Linq; 

namespace AdminToolkit.Pages
{
    public partial class PurgerPage : Page
    {
        public PurgerPage()
        {
            InitializeComponent();
            if (ConfigManager.AppSettings != null)
            {
                cmbDepartments.ItemsSource = ConfigManager.AppSettings.Departments;
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

            btnScan.IsEnabled = false;
            btnStart.IsEnabled = false;
            txtLog.Clear();
            LogToUI(isPurgeMode ? "!!! STARTING PURGE !!!" : "--- STARTING SCAN ---");

            long totalBytesProcessed = 0;

            await Task.Run(() =>
            {
                try
                {
                    // REACHING INTO THE GLOBAL CONFIG HERE
                    var skipList = ConfigManager.AppSettings?.FoldersToSkip ?? new List<string>();

                    string[] userFolders = Directory.GetDirectories(rootPath);

                    foreach (string userFolder in userFolders)
                    {
                        string folderName = Path.GetFileName(userFolder);

                        if (skipList.Any(s => s.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                        {
                            LogToUI($"Skipping: {folderName} (Protected)");
                            continue;
                        }

                        string[] foundBins = new string[0];
                        try
                        {
                            foundBins = Directory.GetDirectories(userFolder, "$RECYCLE.BIN", SearchOption.AllDirectories);
                        }
                        catch { }

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
                                    LogToUI($"Could not purge {userFolder}: {ex.Message}");
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

            Dispatcher.Invoke(() => {
                lblTotalSaved.Text = FormatSize(totalBytesProcessed);
                LogToUI("Task Complete.");
                btnScan.IsEnabled = true;
                btnStart.IsEnabled = true;
            });
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string title = "Recycle Bin Purge Guide";
            string instructions = @"The Purger tool scans redirected user folders for hidden $RECYCLE.BIN directories.

HOW TO USE:
• Choose a department from the list to set the path.
• 'Scan Only' will calculate potential space savings without deleting anything.
• 'Purge All' will permanently empty the bins.

SAFETY:
• Folders listed in your 'FoldersToSkip' config will be ignored.
• The tool automatically skips folders you don't have permissions to access.";

            var helpWin = new ReadmeWindow(title, instructions);
            helpWin.Owner = Window.GetWindow(this);
            helpWin.ShowDialog();
        }
    }
}