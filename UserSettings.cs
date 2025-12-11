using System;
using System.IO;
using System.Text.Json;

namespace AccessGSX
{
    internal sealed class UserSettings
    {
        public bool SpeakMenu { get; set; }
        public bool SpeakTooltip { get; set; }

        private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessGSX", "settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var data = JsonSerializer.Deserialize<UserSettings>(json);
                    if (data != null)
                        return data;
                }
            }
            catch
            {
                // ignore and return defaults
            }

            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // ignore persistence errors
            }
        }
    }
}
