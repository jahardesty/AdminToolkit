using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Windows;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;


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
            int days = int.Parse(txtDays.Text);
            string source = txtSourcePath.Text;

            txtLog.Clear();
            LogToUI($"Searching for users who haven't logged in for {days} days...");

            await Task.Run(() =>
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var userPrincipal = new UserPrincipal(context);
                    var searcher = new PrincipalSearcher(userPrincipal);

                    foreach (var result in searcher.FindAll())
                    {
                        var user = result as UserPrincipal;
                        if (user != null && user.LastLogon != null)
                        {
                            TimeSpan inactiveFor = DateTime.Now - user.LastLogon.Value;
                            if (inactiveFor.TotalDays > days)
                            {
                                // Check if they have a folder in the source path
                                string userFolderPath = Path.Combine(source, user.SamAccountName);
                                if (Directory.Exists(userFolderPath))
                                {
                                    LogToUI($"FOUND: {user.SamAccountName} (Last login: {user.LastLogon})");
                                }
                            }
                        }
                    }
                }
            });
        }
        private async void StartArchive_Click(object sender, RoutedEventArgs e)
        {
            string source = txtSourcePath.Text;
            string archive = txtArchivePath.Text;
            int days = int.Parse(txtDays.Text);

            if (!Directory.Exists(source) || !Directory.Exists(archive))
            {
                MessageBox.Show("Please ensure both Source and Archive paths exist.");
                return;
            }

            btnArchive.IsEnabled = false;
            LogToUI("--- ARCHIVE OPERATION STARTED ---");

            await Task.Run(() =>
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var userPrincipal = new UserPrincipal(context);
                    var searcher = new PrincipalSearcher(userPrincipal);

                    foreach (var result in searcher.FindAll())
                    {
                        var user = result as UserPrincipal;
                        if (user != null && user.LastLogon != null)
                        {
                            if ((DateTime.Now - user.LastLogon.Value).TotalDays > days)
                            {
                                string userSource = Path.Combine(source, user.SamAccountName);
                                string userDest = Path.Combine(archive, user.SamAccountName);

                                if (Directory.Exists(userSource))
                                {
                                    try
                                    {
                                        LogToUI($"Moving {user.SamAccountName}...");
                                        // This is the "Safe Move"
                                        // If moving across drives, we use this custom logic:
                                        MoveDirectory(userSource, userDest);
                                        LogToUI($"SUCCESS: Archived {user.SamAccountName}");
                                    }
                                    catch (Exception ex)
                                    {
                                        LogToUI($"ERROR: Failed to move {user.SamAccountName}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                LogToUI("--- ARCHIVE OPERATION COMPLETE ---");
            });
            btnArchive.IsEnabled = true;
        }

        // Helper to handle moves across different drives (D: to E:)
        private void MoveDirectory(string source, string target)
        {
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string targetFile = file.Replace(source, target);
                string targetDir = Path.GetDirectoryName(targetFile);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                File.Move(file, targetFile, true);
            }
            // Delete original folder after all files are moved
            Directory.Delete(source, true);
        }
    }
}