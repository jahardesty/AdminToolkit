using System;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AdminToolkit.Pages
{
    public partial class ArchiveUserPage : Page
    {
        // Notice: The Classes and _config field are GONE. 
        // We use ConfigManager.AppSettings instead.

        public ArchiveUserPage()
        {
            InitializeComponent();

            // Populate the dropdown from our Shared Config
            if (ConfigManager.AppSettings != null)
            {
                cmbDepartments.ItemsSource = ConfigManager.AppSettings.Departments;
            }
        }

        private void CmbDepartments_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbDepartments.SelectedValue != null)
            {
                txtSourcePath.Text = cmbDepartments.SelectedValue.ToString();
            }
        }

        private void LogToUI(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
        }

        private void BrowseArchive_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Archive Destination"
            };
            if (!string.IsNullOrWhiteSpace(txtArchivePath.Text) && System.IO.Directory.Exists(txtArchivePath.Text))
            {
                dialog.InitialDirectory = txtArchivePath.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                txtArchivePath.Text = dialog.FolderName;
                LogToUI($"Archive destination set to: {dialog.FolderName}");
            }
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            string source = txtSourcePath.Text;
            if (!int.TryParse(txtDays.Text, out int daysThreshold))
            {
                MessageBox.Show("Please enter a valid number for days.");
                return;
            }

            txtLog.Clear();
            LogToUI($"SCAN STARTED: Filtering for {daysThreshold}+ days...");

            await Task.Run(() =>
            {
                try
                {
                    var skipList = ConfigManager.AppSettings?.FoldersToSkip ?? new System.Collections.Generic.List<string>();

                    using (var context = new PrincipalContext(ContextType.Domain))
                    {
                        var userPrincipal = new UserPrincipal(context);
                        var searcher = new PrincipalSearcher(userPrincipal);

                        foreach (var result in searcher.FindAll())
                        {
                            var user = result as AuthenticablePrincipal;
                            if (user != null && user.LastLogon.HasValue)
                            {
                                if (user.LastLogon.Value.Year < 1700) continue;

                                double inactiveDays = (DateTime.Now - user.LastLogon.Value).TotalDays;
                                int roundedDays = (int)Math.Round(inactiveDays);

                                if (roundedDays >= daysThreshold)
                                {
                                    // CHECK SKIP LIST
                                    if (skipList.Any(s => s.Equals(user.SamAccountName, StringComparison.OrdinalIgnoreCase))) continue;

                                    string userFolderPath = Path.Combine(source, user.SamAccountName);
                                    if (Directory.Exists(userFolderPath))
                                    {
                                        LogToUI($"MATCH: {user.SamAccountName} | Inactive: {roundedDays} days | Last: {user.LastLogon.Value:MM/dd/yy}");
                                    }
                                }
                            }
                        }
                    }
                    LogToUI("--- SCAN COMPLETE ---");
                }
                catch (Exception ex)
                {
                    LogToUI($"CRITICAL ERROR: {ex.Message}");
                }
            });
        }

        private async void FindDeletedUsers_Click(object sender, RoutedEventArgs e)
        {
            string source = txtSourcePath.Text;
            if (!Directory.Exists(source)) { MessageBox.Show("Source path invalid."); return; }

            LogToUI("Searching folders with no matching AD user...");

            await Task.Run(() =>
            {
                var skipList = ConfigManager.AppSettings?.FoldersToSkip ?? new System.Collections.Generic.List<string>();

                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    string[] folders = Directory.GetDirectories(source);
                    foreach (var folderPath in folders)
                    {
                        string folderName = Path.GetFileName(folderPath);

                        // SKIP LIST CHECK
                        if (skipList.Any(s => s.Equals(folderName, StringComparison.OrdinalIgnoreCase))) continue;

                        var user = UserPrincipal.FindByIdentity(context, folderName);
                        if (user == null)
                        {
                            LogToUI($"DELETED USER FOLDER FOUND: {folderName} (No AD Account)");
                        }
                    }
                    LogToUI($" ---- Scan Complete ---- ");
                }
            });
        }

        private async void StartArchive_Click(object sender, RoutedEventArgs e)
        {
            string source = txtSourcePath.Text;
            string archive = txtArchivePath.Text;

            if (!Directory.Exists(source) || !Directory.Exists(archive))
            {
                MessageBox.Show("Please ensure both Source and Archive paths exist.");
                return;
            }

            btnArchive.IsEnabled = false;
            archiveProgressBar.Value = 0;
            LogToUI("--- ARCHIVE OPERATION STARTED ---");

            await Task.Run(() =>
            {
                try
                {
                    var skipList = ConfigManager.AppSettings?.FoldersToSkip ?? new System.Collections.Generic.List<string>();

                    using (var context = new PrincipalContext(ContextType.Domain))
                    {
                        string[] folderPaths = Directory.GetDirectories(source);
                        int totalFolders = folderPaths.Length;
                        int processedCount = 0;

                        foreach (var folderPath in folderPaths)
                        {
                            string folderName = Path.GetFileName(folderPath);
                            processedCount++;
                            double percentage = ((double)processedCount / totalFolders) * 100;

                            Dispatcher.Invoke(() =>
                            {
                                archiveProgressBar.Value = percentage;
                                lblProgressStatus.Text = $"Processing: {folderName} ({processedCount}/{totalFolders})";
                            });

                            // SKIP LIST CHECK
                            if (skipList.Any(s => s.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                            {
                                LogToUI($"Skipping Protected Folder: {folderName}");
                                continue;
                            }

                            try
                            {
                                var user = UserPrincipal.FindByIdentity(context, folderName);
                                if (user == null) // Orphan found
                                {
                                    string userDest = Path.Combine(archive, folderName);
                                    LogToUI($"ORPHAN FOUND: {folderName}. Starting copy...");

                                    MoveDirectory(folderPath, userDest);

                                    LogToUI($"SUCCESS: Archived folder: {folderName}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogToUI($"ERROR checking {folderName}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"CRITICAL AD ERROR: {ex.Message}");
                }

                LogToUI("--- ORPHAN ARCHIVE OPERATION COMPLETE ---");
            });

            lblProgressStatus.Text = "Archive Complete";
            btnArchive.IsEnabled = true;
            btnOpenArchive.Background = System.Windows.Media.Brushes.LightGreen;
        }

        private void MoveDirectory(string source, string target)
        {
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string targetFile = file.Replace(source, target);
                string targetDir = Path.GetDirectoryName(targetFile);

                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                File.Copy(file, targetFile, true);
            }
        }

        private void OpenArchive_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(txtArchivePath.Text))
            {
                Process.Start(new ProcessStartInfo { FileName = txtArchivePath.Text, UseShellExecute = true });
            }
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string title = "Archive User Tool Guide";
            string instructions = @"This tool searches for users that are either deleted or deactivated.

HOW TO USE:
• Select a Department to set the Source path.
• Destination: Where you want the archived data to go.
• Days: Threshold for 'Inactive' users based on AD Last Logon.

BUTTONS:
• Scan for Deleted: Finds folders where the AD account is gone.
• Scan for Inactive: Finds folders for users who haven't logged in recently.
• Archive: Copies identified orphan folders to the destination.

SAFETY:
• Folders in your Skip List (e.g., Administrator) are automatically protected.
• This performs a COPY; manually verify before deleting source folders.";

            var helpWin = new ReadmeWindow(title, instructions);
            helpWin.Owner = Window.GetWindow(this);
            helpWin.ShowDialog();
        }
    }
}