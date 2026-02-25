using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AdminToolkit
{
    // These classes stay here so they aren't repeated in every Page
    public class DepartmentConfig
    {
        public List<Department> Departments { get; set; } = new List<Department>();
        public List<string> FoldersToSkip { get; set; } = new List<string>();
        public List<string> DomainControllers { get; set; } = new List<string>();
        public List<string> DesktopAuthorityServers { get; set; } = new List<string>();
        public List<string> DesktopAuthorityServices { get; set; } = new List<string>();
        public string EntraSyncServer { get; set; }
    }

    public class Department
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public static class ConfigManager
    {
        public static DepartmentConfig AppSettings { get; private set; }

        public static void Load()
        {
            try
            {
                string json = "";
                string externalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                if (File.Exists(externalPath))
                {
                    json = File.ReadAllText(externalPath);
                }
                else
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    string resourceName = "AdminToolkit.appsettings.json";
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) return;
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            json = reader.ReadToEnd();
                        }
                    }
                }

                AppSettings = JsonSerializer.Deserialize<DepartmentConfig>(json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Configuration Error: " + ex.Message);
            }
        }
    }
}
