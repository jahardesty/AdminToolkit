using System;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AdminToolkit.Pages
{
    public partial class ArchiveUserPage : Page
    {
        public ArchiveUserPage()
        {
            InitializeComponent();
        }

        private void LogToUI(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
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
                    using (var context = new PrincipalContext(ContextType.Domain))
                    {
                        var userPrincipal = new UserPrincipal(context);
                        var searcher = new PrincipalSearcher(userPrincipal);

                        foreach (var result in searcher.FindAll())
                        {
                            // Cast as AuthenticablePrincipal to expose LastLogon better
                            var user = result as AuthenticablePrincipal;

                            if (user != null && user.LastLogon.HasValue)
                            {
                                // AD often returns 1/1/1601 for users who have NEVER logged in
                                if (user.LastLogon.Value.Year < 1700) continue;

                                double inactiveDays = (DateTime.Now - user.LastLogon.Value).TotalDays;
                                int roundedDays = (int)Math.Round(inactiveDays);

                                if (roundedDays >= daysThreshold)
                                {
                                    string userFolderPath = System.IO.Path.Combine(source, user.SamAccountName);
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
            LogToUI("Searching folders with no matching AD user...");

            await Task.Run(() =>
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    string[] folders = Directory.GetDirectories(source);

                    foreach (var folderPath in folders)
                    {
                        string folderName = System.IO.Path.GetFileName(folderPath);
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

        private void OpenArchive_Click(object sender, RoutedEventArgs e)
        {
            string archivePath = txtArchivePath.Text;

            if (Directory.Exists(archivePath))
            {
                // This opens the Windows File Explorer at the specified path
                Process.Start(new ProcessStartInfo
                {
                    FileName = archivePath,
                    UseShellExecute = true,
                    Verb = "open"
                });

                LogToUI($"Opening Archive: {archivePath}");
            }
            else
            {
                MessageBox.Show("The Archive path does not exist or is invalid.");
            }
        }

        private async void StartArchive_Click(object sender, RoutedEventArgs e)
        {
            string source = txtSourcePath.Text;
            string archive = txtArchivePath.Text;

            // Basic validation
            if (!Directory.Exists(source) || !Directory.Exists(archive))
            {
                MessageBox.Show("Please ensure both Source and Archive paths exist.");
                return;
            }

            btnArchive.IsEnabled = false;
            archiveProgressBar.Value = 0;
            LogToUI("--- ARCHIVE OPERATION STARTED ---");
            LogToUI("Searching for folders with no matching Active Directory account...");

            await Task.Run(() =>
            {
                try
                {
                    using (var context = new PrincipalContext(ContextType.Domain))
                    {
                        // 1. Get all directories in the source path
                        string[] folderPaths = Directory.GetDirectories(source);
                        int totalFolders = folderPaths.Length;
                        int processedCount = 0;

                        foreach (var folderPath in folderPaths)
                        {
                            // Get just the folder name (e.g., "jhardesty")
                            
                            string folderName = System.IO.Path.GetFileName(folderPath);
                            double percentage = ((double)processedCount / totalFolders) * 100;

                            Dispatcher.Invoke(() =>
                            {
                                archiveProgressBar.Value = percentage;
                                lblProgressStatus.Text = $"Processing: {folderName} ({processedCount}/{totalFolders})";
                            });

                            try
                            {
                                // 2. Look for this name in AD
                                var user = UserPrincipal.FindByIdentity(context, folderName);

                                // 3. If user is null, the account no longer exists in AD
                                if (user == null)
                                {
                                    string userDest = System.IO.Path.Combine(archive, folderName);

                                    LogToUI($"ORPHAN FOUND: {folderName}. Starting copy...");

                                    // 4. Call your helper to move/copy the data
                                    MoveDirectory(folderPath, userDest);

                                    LogToUI($"SUCCESS: Archived orphaned folder: {folderName}");
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
        }

        private void MoveDirectory(string source, string target)
        {
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);

            // Move all files including those in subdirectories
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string targetFile = file.Replace(source, target);
                string targetDir = System.IO.Path.GetDirectoryName(targetFile);

                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                File.Copy(file, targetFile, true);
                //File.Move(file, targetFile, true);
            }
            LogToUI($"TEST: Copied data from {System.IO.Path.GetFileName(source)} to archive.");
            // Cleanup: Delete the now-empty source directory
            // Directory.Delete(source, true);
        }
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string title = "Archive User Tool Guide";
            string instructions =
                "This tool searches for users that are either deleted or deactivated.\n\n" +

                "How to use:\n\n" +
                "• Source (Redirected Folders): Root folder where user folders live.\n" +
                "• Destination: Where you want the archived data to go.\n" +
                "• Days: The threshold for 'Inactive' users based on their last logon in AD.\n\n " +
                "Buttons:\n" +
                "• Scan for Deleted: Finds folders where the user account no longer exists in AD.\n " +
                "• Scan for Inactive: Finds users who haven't logged in for the specified number of days.\n " +
                "• Archive: Begins moving the identified folders to the destination.\n\n" +
                "Safety:\n\n" +
                "• This tool performs a COPY and then a DELETE to ensure data integrity.\n" +
                "• Folders like 'Public' or 'Administrator' are automatically excluded.";

            var helpWin = new ReadmeWindow(title, instructions);
            helpWin.Owner = Window.GetWindow(this); // Centers it to the main app
            helpWin.ShowDialog();
        }
    }
}