using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdminToolkit.Pages
{
    /// <summary>
    /// Interaction logic for PurgerPage.xaml
    /// </summary>
    public partial class PurgerPage : Page
    {
        public PurgerPage()
        {
            InitializeComponent();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                txtRootPath.Text = dialog.FolderName;
            }
        }
        private long totalBytesSaved = 0;
        private long GetDirectorySize(string path)
        {
            long size = 0;
            DirectoryInfo di = new DirectoryInfo(path);
            foreach (FileInfo fi in di.GetFiles("*", SearchOption.AllDirectories))
            {
                size += fi.Length;
            }
            return size;
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            string root = txtRootPath.Text;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            btnScan.IsEnabled = false;
            btnStart.IsEnabled = false;
            totalBytesSaved = 0;
            UpdateSpaceUI(0);
            txtLog.Clear();
            LogToUI("--- SCANNING ONLY (No Deletion) ---");

            await Task.Run(() =>
            {
                try
                {
                    var userDirs = Directory.GetDirectories(root);
                    foreach (var userDir in userDirs)
                    {
                        var bins = Directory.GetDirectories(userDir, "$Recycle.Bin", SearchOption.AllDirectories);
                        foreach (var bin in bins)
                        {
                            long binSize = GetDirectorySize(bin);
                            totalBytesSaved += binSize;
                            UpdateSpaceUI(totalBytesSaved);
                            LogToUI($" FOUND: {bin} ({FormatBytes(binSize)})");
                        }
                    }
                    LogToUI($"--- SCAN COMPLETE: {FormatBytes(totalBytesSaved)} can be recovered ---");
                }
                catch (Exception ex) { LogToUI($"ERROR: {ex.Message}"); }
            });

            btnScan.IsEnabled = true;
            btnStart.IsEnabled = true;
        }
        private async void StartPurge_Click(object sender, RoutedEventArgs e)
        {
            string root = txtRootPath.Text;
            totalBytesSaved = 0;
            UpdateSpaceUI(0);

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                MessageBox.Show("Please select a valid root directory first.", "Invalid Path");
                return;
            }

            btnStart.IsEnabled = false;
            txtLog.Clear();
            LogToUI("Starting purge process...");

            await Task.Run(() =>
            {
                try
                {
                    var userDirs = Directory.GetDirectories(root);
                    foreach (var userDir in userDirs)
                    {
                        string userName = Path.GetFileName(userDir);
                        LogToUI($"Checking user: {userName}");

                        var bins = Directory.GetDirectories(userDir, "$Recycle.Bin", SearchOption.AllDirectories);

                        foreach (var bin in bins)
                        {
                            try
                            {
                                long binSize = GetDirectorySize(bin);
                                Directory.Delete(bin, true);
                                totalBytesSaved += binSize;
                                UpdateSpaceUI(totalBytesSaved);
                                LogToUI($" SUCCESS: Deleted {bin} ({FormatBytes(binSize)})");

                            }
                            catch (Exception ex)
                            {
                                LogToUI($" SKIP: Could not delete {bin} (File may be in use)");
                            }
                        }
                    }
                LogToUI("---FINISHED---");
            }
            catch (Exception ex)
                {
                LogToUI($"CRITICAL ERROR: {ex.Message}");
                }
            });
        btnStart.IsEnabled = true;
        }

        private void UpdateSpaceUI(long bytes)
        {
            Dispatcher.Invoke(() =>
            {
                lblTotalSaved.Text = FormatBytes(bytes);
            });
        }

        private string FormatBytes(long bytes)
        {
            string[] Suffix = { "Bytes", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for ( i =0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
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

    }
}
