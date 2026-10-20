using System;
using System.IO;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// The player's settings, shared by all save slots. Stored as
    /// Application.persistentDataPath/settings.json; option ids come from settings.json in
    /// StarlineData (<see cref="SettingsConfig"/>).
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public string language;

        /// <summary>A preset id, or <see cref="SettingsConfig.CustomPreset"/> after changing a single item.</summary>
        public string preset;
        public string displayMode;
        /// <summary>Screen size; 0 means the display's native resolution.</summary>
        public int width;
        public int height;
        public bool vsync;
        /// <summary>Frames per second when vsync is off; 0 means unlimited.</summary>
        public int frameRate;
        /// <summary>3D render resolution in percent of the screen; the UI is always drawn at full resolution.</summary>
        public int renderScale;
        public string antialiasing;
        public string shadows;
        public bool bloom;

        public static GameSettings CreateDefault(SettingsConfig config)
        {
            var settings = new GameSettings
            {
                language = config.LanguageForSystem(),
                displayMode = config.defaultDisplayMode,
                vsync = config.defaultVsync,
                frameRate = config.defaultFrameRate,
            };
            settings.ApplyPreset(config.FindPreset(config.defaultPreset));
            return settings;
        }

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public void ApplyPreset(SettingsConfig.Preset values)
        {
            preset = values.id;
            renderScale = values.renderScale;
            antialiasing = values.antialiasing;
            shadows = values.shadows;
            bloom = values.bloom;
        }

        /// <summary>Replaces values a newer or edited settings.json no longer offers with the defaults.</summary>
        public void Sanitize(SettingsConfig config)
        {
            GameSettings defaults = CreateDefault(config);
            SettingsConfig.Language chosen = config.FindLanguage(language);
            if (chosen == null || !chosen.available)
            {
                language = defaults.language;
            }
            if (Array.Find(config.displayModes, m => m.id == displayMode) == null)
            {
                displayMode = defaults.displayMode;
            }
            if (width < 0 || height < 0)
            {
                width = height = 0;
            }
            if (Array.Find(config.frameRates, f => f.value == frameRate) == null)
            {
                frameRate = defaults.frameRate;
            }
            if (Array.Find(config.renderScales, r => r.value == renderScale) == null)
            {
                renderScale = defaults.renderScale;
            }
            if (config.FindAntialiasing(antialiasing) == null)
            {
                antialiasing = defaults.antialiasing;
            }
            if (config.FindShadows(shadows) == null)
            {
                shadows = defaults.shadows;
            }
            preset = config.MatchPreset(this);
        }
    }

    /// <summary>Reads and writes <see cref="GameSettings"/>; a missing or broken file gives the defaults.</summary>
    public static class SettingsStore
    {
        private static string PathOnDisk => Path.Combine(Application.persistentDataPath, "settings.json");

        public static GameSettings Load()
        {
            SettingsConfig config = SettingsConfig.Current;
            string path = PathOnDisk;
            if (!File.Exists(path))
            {
                return GameSettings.CreateDefault(config);
            }
            try
            {
                GameSettings settings = JsonUtility.FromJson<GameSettings>(File.ReadAllText(path));
                if (settings == null)
                {
                    return GameSettings.CreateDefault(config);
                }
                settings.Sanitize(config);
                return settings;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Starline：读取设置 " + path + " 失败，使用默认设置：" + e.Message);
                return GameSettings.CreateDefault(config);
            }
        }

        /// <summary>Writes to a temporary file first, like the save slots.</summary>
        public static void Save(GameSettings settings)
        {
            try
            {
                string path = PathOnDisk;
                string temp = path + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, JsonUtility.ToJson(settings, true));
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Starline：保存设置失败：" + e.Message);
            }
        }
    }
}
