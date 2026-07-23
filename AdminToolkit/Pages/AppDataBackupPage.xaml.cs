using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FontAwesome.Sharp;

namespace AdminToolkit.Pages
{
    public partial class AppDataBackupPage : Page
    {
        private const string BaseDestination =
            @"\\storinator\ccis\appdatabackup";
        private string _lastBackupFolder;
        public AppDataBackupPage()
        {
            InitializeComponent();
            Loaded += AppDataBackupPage_Loaded;
        }

        private void AppDataBackupPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);

            if (window != null)
            {
                window.Width = 1200;
                window.Height = 615;
            }
        }

        private void TargetMode_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (PanelSingle == null ||
                PanelOu == null ||
                PanelUser == null)
            {
                return;
            }

            PanelSingle.Visibility = Visibility.Collapsed;
            PanelOu.Visibility = Visibility.Collapsed;
            PanelUser.Visibility = Visibility.Collapsed;

            if (RbSingle.IsChecked == true)
            {
                PanelSingle.Visibility = Visibility.Visible;
            }
        }

        private void ComboPreset_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (PanelCustomPath == null)
            {
                return;
            }

            PanelCustomPath.Visibility =
                ComboPreset.SelectedIndex == 2
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private async void BtnStartBackup_Click(
            object sender,
            RoutedEventArgs e)
        {
            RtbLogConsole.Document.Blocks.Clear();
            BtnStartBackup.IsEnabled = false;
            BtnOpenAppDataArchive.Visibility = Visibility.Collapsed;
            _lastBackupFolder = null;
            OverallProgress.IsIndeterminate = false;
            OverallProgress.Minimum = 0;
            OverallProgress.Maximum = 100;
            OverallProgress.Value = 0;

            TxtProgress.Text = "Preparing backup...";

            try
            {
                string relativeSourcePath = GetRelativeSourcePath();
                List<BackupTarget> targets = await ResolveTargetsAsync();

                if (targets.Count == 0)
                {
                    TxtProgress.Text = "No valid target found";

                    LogToConsole(
                        "[WARN] No valid target was found. Backup canceled.");

                    return;
                }

                LogToConsole("[INFO] Preparing backup queue...");
                LogSeparator();

                foreach (BackupTarget target in targets)
                {
                    string folderSuffix =
                        GetDestinationFolderSuffix();

                    string profileDescription =
                        GetProfileDescription();

                    string remoteSource =
                        $@"\\{target.ComputerName}\c$\Users\{target.Username}\{relativeSourcePath}";

                    string finalDestination = Path.Combine(
                        BaseDestination,
                        target.Username,
                        $"{target.ComputerName}_{folderSuffix}");
                    _lastBackupFolder = finalDestination;

                    LogToConsole("[JOB START] Starting backup");

                    LogDetail(
                        IconChar.Desktop,
                        "Computer",
                        target.ComputerName,
                        Brushes.DeepSkyBlue);

                    LogDetail(
                        IconChar.User,
                        "User",
                        target.Username,
                        Brushes.MediumPurple);

                    LogDetail(
                        IconChar.FolderOpen,
                        "Profile",
                        profileDescription,
                        Brushes.Goldenrod);

                    // Verify the source before invoking Robocopy.
                    if (!Directory.Exists(remoteSource))
                    {
                        OverallProgress.IsIndeterminate = false;
                        OverallProgress.Value = 0;
                        TxtProgress.Text = "Source folder unavailable";

                        LogToConsole(
                            "[ERROR] The selected profile folder does not exist or cannot be accessed.");

                        LogDetail(
                            IconChar.FolderOpen,
                            "Source",
                            remoteSource,
                            Brushes.IndianRed);

                        continue;
                    }

                    // Verify that the destination can be created.
                    try
                    {
                        Directory.CreateDirectory(finalDestination);
                    }
                    catch (Exception ex)
                    {
                        OverallProgress.IsIndeterminate = false;
                        OverallProgress.Value = 0;
                        TxtProgress.Text = "Destination unavailable";

                        LogToConsole(
                            $"[ERROR] The destination folder could not be created: {ex.Message}");

                        LogDetail(
                            IconChar.FolderOpen,
                            "Destination",
                            finalDestination,
                            Brushes.IndianRed);

                        continue;
                    }

                    OverallProgress.IsIndeterminate = true;

                    TxtProgress.Text =
                        $"Copying {target.Username}'s data from {target.ComputerName}...";

                    RobocopyResult result =
                        await RunRobocopyAsync(
                            remoteSource,
                            finalDestination);

                    OverallProgress.IsIndeterminate = false;

                    if (result.IsSuccess)
                    {
                        OverallProgress.Value = 100;

                        if (result.HasWarnings)
                        {
                            TxtProgress.Text =
                                "Backup completed with warnings";

                            LogToConsole(
                                "[WARN] Backup completed, but some files may not have copied.");
                        }
                        else
                        {
                            TxtProgress.Text = "Backup complete";

                            LogToConsole(
                                "[SUCCESS] Backup completed successfully");
                        }
                    }
                    else
                    {
                        OverallProgress.Value = 0;
                        TxtProgress.Text = "Backup failed";

                        LogToConsole(
                            $"[FAILURE] Backup failed with Robocopy exit code {result.ExitCode}.");
                    }
                }
                
                LogSeparator();

                LogToConsole(
                    "[FINISHED] All backup jobs completed");
            }
            catch (Exception ex)
            {
                OverallProgress.IsIndeterminate = false;
                OverallProgress.Value = 0;
                TxtProgress.Text = "Backup workflow failed";

                LogToConsole(
                    $"[CRITICAL ERROR] Backup workflow failed: {ex.Message}");
            }
            finally
            {
                BtnStartBackup.IsEnabled = true;
                OverallProgress.IsIndeterminate = false;
                BtnOpenAppDataArchive.Visibility = Visibility.Visible;
            }
        }

        #region Target Resolution

        private Task<List<BackupTarget>> ResolveTargetsAsync()
        {
            var targets = new List<BackupTarget>();

            string computerName =
                TxtSingleComputer.Text.Trim();

            string username =
                TxtSingleUser.Text.Trim();

            if (string.IsNullOrWhiteSpace(computerName) ||
                string.IsNullOrWhiteSpace(username))
            {
                LogToConsole(
                    "[ERROR] Computer Name and Username are required.");

                return Task.FromResult(targets);
            }

            targets.Add(
                new BackupTarget
                {
                    ComputerName = computerName,
                    Username = username
                });

            return Task.FromResult(targets);
        }

        #endregion

        #region Path and Profile Routing

        private string GetRelativeSourcePath()
        {
            return ComboPreset.SelectedIndex switch
            {
                0 => @"AppData\Local\Google\Chrome\User Data\Default",

                1 => @"AppData\Local\Microsoft\Edge\User Data\Default",

                2 => TxtCustomRelativePath.Text
                    .Trim()
                    .TrimStart('\\'),

                _ => throw new InvalidOperationException(
                    "Unknown source-profile selection.")
            };
        }

        private string GetDestinationFolderSuffix()
        {
            return ComboPreset.SelectedIndex switch
            {
                0 => "ChromeBackup",
                1 => "EdgeBackup",
                2 => "CustomBackup",
                _ => "AppDataBackup"
            };
        }

        private string GetProfileDescription()
        {
            return ComboPreset.SelectedIndex switch
            {
                0 => "Google Chrome — Default Profile",
                1 => "Microsoft Edge — Default Profile",
                2 => "Custom AppData Profile",
                _ => "Application Data Profile"
            };
        }

        #endregion

        #region Robocopy

        private async Task<RobocopyResult> RunRobocopyAsync(
            string source,
            string target)
        {
            string arguments =
                $"\"{source}\" \"{target}\" " +
                "/E /R:1 /W:2 /XJD /MT:8 " +
                "/NFL /NDL /NJH /NJS";

            var startInfo = new ProcessStartInfo
            {
                FileName = "robocopy.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            return await Task.Run(() =>
            {
                long totalBytes = 0;

                try
                {
                    totalBytes = GetDirectorySize(source);

                    string formattedSize =
                        FormatFileSize(totalBytes);

                    LogDetail(
                        IconChar.FloppyDisk,
                        "Data",
                        $"{formattedSize} to copy",
                        Brushes.CornflowerBlue);
                }
                catch (Exception ex)
                {
                    LogToConsole(
                        $"[WARN] Source size could not be calculated: {ex.Message}");
                }

                using var process = new Process
                {
                    StartInfo = startInfo
                };

                try
                {
                    process.Start();

                    string standardOutput =
                        process.StandardOutput.ReadToEnd();

                    string standardError =
                        process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    int exitCode = process.ExitCode;

                    /*
                     * Robocopy exit codes:
                     *
                     * 0–7 = Successful operation
                     * 8+  = At least one failure
                     *
                     * Exit code 9 is 8 + 1:
                     * files copied, but one or more failures occurred.
                     * This application treats code 9 as completed with warnings.
                     */

                    bool hasWarnings = exitCode == 9;

                    bool isSuccess =
                        exitCode < 8 || hasWarnings;

                    if (!isSuccess)
                    {
                        LogRobocopyErrorOutput(
                            standardOutput,
                            standardError);
                    }

                    return new RobocopyResult
                    {
                        IsSuccess = isSuccess,
                        HasWarnings = hasWarnings,
                        ExitCode = exitCode,
                        TotalBytes = totalBytes
                    };
                }
                catch (Exception ex)
                {
                    LogToConsole(
                        $"[ERROR] Robocopy could not be started: {ex.Message}");

                    return new RobocopyResult
                    {
                        IsSuccess = false,
                        HasWarnings = false,
                        ExitCode = -1,
                        TotalBytes = totalBytes
                    };
                }
            });
        }

        private void LogRobocopyErrorOutput(
            string standardOutput,
            string standardError)
        {
            string usefulOutput =
                GetUsefulRobocopyError(standardOutput);

            if (!string.IsNullOrWhiteSpace(usefulOutput))
            {
                LogToConsole(
                    $"[ERROR] {usefulOutput}");
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                LogToConsole(
                    $"[ERROR] {standardError.Trim()}");
            }
        }

        private static string GetUsefulRobocopyError(
            string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            string[] lines = output.Split(
                new[]
                {
                    "\r\n",
                    "\n"
                },
                StringSplitOptions.RemoveEmptyEntries);

            var usefulLines = new List<string>();

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.Contains(
                        "ERROR",
                        StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.Contains(
                        "Access is denied",
                        StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.Contains(
                        "network path",
                        StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.Contains(
                        "cannot find",
                        StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.Contains(
                        "invalid",
                        StringComparison.OrdinalIgnoreCase))
                {
                    usefulLines.Add(trimmedLine);
                }
            }

            if (usefulLines.Count > 0)
            {
                return string.Join(
                    Environment.NewLine,
                    usefulLines);
            }

            return output.Trim();
        }

        private long GetDirectorySize(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return 0;
            }

            long totalSize = 0;
            var directory = new DirectoryInfo(folderPath);

            foreach (FileInfo file in directory.EnumerateFiles(
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    totalSize += file.Length;
                }
                catch
                {
               
                }
            }

            return totalSize;
        }

        private static string FormatFileSize(long bytes)
        {
            const double kilobyte = 1024;
            const double megabyte = kilobyte * 1024;
            const double gigabyte = megabyte * 1024;

            if (bytes >= gigabyte)
            {
                return $"{bytes / gigabyte:N2} GB";
            }

            if (bytes >= megabyte)
            {
                return $"{bytes / megabyte:N2} MB";
            }

            if (bytes >= kilobyte)
            {
                return $"{bytes / kilobyte:N2} KB";
            }

            return $"{bytes:N0} bytes";
        }

        #endregion

        #region Console Formatting

        public void LogToConsole(string text)
        {
            Dispatcher.Invoke(() =>
            {
                Brush color = Brushes.White;
                IconChar selectedIcon = IconChar.CircleInfo;

                if (text.StartsWith("[SUCCESS]"))
                {
                    color = Brushes.LimeGreen;
                    selectedIcon = IconChar.Check;
                }
                else if (
                    text.StartsWith("[ERROR]") ||
                    text.StartsWith("[FAILURE]") ||
                    text.StartsWith("[CRITICAL ERROR]"))
                {
                    color = Brushes.IndianRed;
                    selectedIcon = IconChar.Xmark;
                }
                else if (text.StartsWith("[WARN]"))
                {
                    color = Brushes.Orange;
                    selectedIcon =
                        IconChar.TriangleExclamation;
                }
                else if (text.StartsWith("[INFO]"))
                {
                    color = Brushes.DeepSkyBlue;
                    selectedIcon = IconChar.CircleInfo;
                }
                else if (text.StartsWith("[JOB START]"))
                {
                    color = Brushes.Cyan;
                    selectedIcon = IconChar.Play;
                }
                else if (text.StartsWith("[FINISHED]"))
                {
                    color = Brushes.LimeGreen;
                    selectedIcon = IconChar.FlagCheckered;
                }

                string displayText = Regex.Replace(
                    text,
                    @"^\[[^\]]+\]\s*",
                    "");

                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 4, 0, 4)
                };

                paragraph.Inlines.Add(
                    new Run($"[{DateTime.Now:HH:mm}] ")
                    {
                        Foreground = Brushes.Gray
                    });

                var icon = new IconBlock
                {
                    Icon = selectedIcon,
                    Foreground = color,
                    Width = 15,
                    Height = 15,
                    Margin = new Thickness(0, 0, 8, -2)
                };

                paragraph.Inlines.Add(
                    new InlineUIContainer(icon)
                    {
                        BaselineAlignment =
                            BaselineAlignment.Center
                    });

                paragraph.Inlines.Add(
                    new Run(displayText)
                    {
                        Foreground = color
                    });

                RtbLogConsole.Document.Blocks.Add(paragraph);
                RtbLogConsole.ScrollToEnd();
            });
        }

        private void LogDetail(
            IconChar iconChar,
            string label,
            string value,
            Brush iconColor)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph
                {
                    // Indents detail rows beneath timestamped entries.
                    Margin = new Thickness(105, 2, 0, 2)
                };

                var icon = new IconBlock
                {
                    Icon = iconChar,
                    Foreground = iconColor,
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, -2)
                };

                paragraph.Inlines.Add(
                    new InlineUIContainer(icon)
                    {
                        BaselineAlignment =
                            BaselineAlignment.Center
                    });

                paragraph.Inlines.Add(
                    new Run($"{label,-14}: ")
                    {
                        Foreground = Brushes.Gray,
                        FontWeight = FontWeights.SemiBold
                    });

                paragraph.Inlines.Add(
                    new Run(value)
                    {
                        Foreground = Brushes.White
                    });

                RtbLogConsole.Document.Blocks.Add(paragraph);
                RtbLogConsole.ScrollToEnd();
            });
        }

        private void LogSeparator()
        {
            Dispatcher.Invoke(() =>
            {
                var separator = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            75,
                            75,
                            75)),
                    Margin = new Thickness(0, 10, 0, 10)
                };

                var container =
                    new BlockUIContainer(separator);

                RtbLogConsole.Document.Blocks.Add(container);
                RtbLogConsole.ScrollToEnd();
            });
        }
        
        private void OpenAppDataArchive_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastBackupFolder))
            {
                MessageBox.Show("No backup has been performed.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_lastBackupFolder}\"",
                UseShellExecute = true
            });
        }

        #endregion
    }

    public class BackupTarget
    {
        public string ComputerName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;
    }

    public class RobocopyResult
    {
        public bool IsSuccess { get; set; }

        public bool HasWarnings { get; set; }

        public int ExitCode { get; set; }

        public long TotalBytes { get; set; }
    }
}