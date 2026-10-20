using System;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Options of the settings screen and the graphics presets, read from
    /// Resources/StarlineData/settings.json. Client-only, so it uses JsonUtility instead of the
    /// kernel's GameData.
    /// </summary>
    [Serializable]
    public sealed class SettingsConfig
    {
        [Serializable]
        public sealed class Language
        {
            public string id;
            public string name;
            /// <summary>False while the translation is not done yet; shown greyed out with <see cref="status"/>.</summary>
            public bool available;
            public string status;
            /// <summary>UnityEngine.SystemLanguage names that pick this language on first launch.</summary>
            public string[] systemLanguages;
        }

        [Serializable]
        public sealed class Preset
        {
            public string id;
            public string name;
            public int renderScale;
            public string antialiasing;
            public string shadows;
            public bool bloom;
        }

        [Serializable]
        public sealed class Named
        {
            public string id;
            public string name;
        }

        [Serializable]
        public sealed class NamedValue
        {
            public int value;
            public string name;
        }

        [Serializable]
        public sealed class Antialiasing
        {
            public string id;
            public string name;
            /// <summary>MSAA sample count of the render pipeline (1 = off).</summary>
            public int msaa;
        }

        [Serializable]
        public sealed class Shadows
        {
            public string id;
            public string name;
            /// <summary>Shadow distance in world units; 0 turns the sun's shadows off.</summary>
            public float distance;
            public int resolution;
        }

        public const string CustomPreset = "custom";

        public string defaultLanguage;
        public Language[] languages;
        public string onName;
        public string offName;
        public string customPresetName;
        public string defaultPreset;
        public Preset[] presets;
        public string defaultDisplayMode;
        public Named[] displayModes;
        public float displayConfirmSeconds;
        public bool defaultVsync;
        public int defaultFrameRate;
        public NamedValue[] frameRates;
        public NamedValue[] renderScales;
        public Antialiasing[] antialiasing;
        public Shadows[] shadows;

        private static SettingsConfig _current;

        public static SettingsConfig Current => _current ?? (_current = Load());

        private static SettingsConfig Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ClientData.ResourceFolder + "/settings");
            if (asset == null)
            {
                throw new InvalidOperationException("找不到设置配置 Resources/" + ClientData.ResourceFolder + "/settings.json");
            }
            return JsonUtility.FromJson<SettingsConfig>(asset.text);
        }

        public Language FindLanguage(string id) => Array.Find(languages, l => l.id == id);

        public Preset FindPreset(string id) => Array.Find(presets, p => p.id == id);

        public Antialiasing FindAntialiasing(string id) => Array.Find(antialiasing, a => a.id == id);

        public Shadows FindShadows(string id) => Array.Find(shadows, s => s.id == id);

        public string PresetName(string id) => FindPreset(id)?.name ?? customPresetName;

        /// <summary>The available language matching the operating system, or the default one.</summary>
        public string LanguageForSystem()
        {
            string system = Application.systemLanguage.ToString();
            foreach (Language language in languages)
            {
                if (language.available && language.systemLanguages != null && Array.IndexOf(language.systemLanguages, system) >= 0)
                {
                    return language.id;
                }
            }
            return defaultLanguage;
        }

        /// <summary>The preset whose values all match, or <see cref="CustomPreset"/>.</summary>
        public string MatchPreset(GameSettings settings)
        {
            foreach (Preset preset in presets)
            {
                if (preset.renderScale == settings.renderScale && preset.antialiasing == settings.antialiasing
                    && preset.shadows == settings.shadows && preset.bloom == settings.bloom)
                {
                    return preset.id;
                }
            }
            return CustomPreset;
        }
    }
}
