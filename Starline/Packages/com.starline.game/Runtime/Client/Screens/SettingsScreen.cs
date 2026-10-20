using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// Settings, opened from the main menu: a tab for the language and one for the graphics.
    /// Every change takes effect and is saved at once; a new display mode or resolution has to be
    /// confirmed within a few seconds or it is reverted, in case the screen went black.
    /// </summary>
    public sealed class SettingsScreen : UiScreen
    {
        private const string LanguageTab = "language";
        private const string GraphicsTab = "graphics";

        private readonly SettingsConfig _config = SettingsConfig.Current;
        private readonly List<Action> _refreshers = new List<Action>();
        private readonly List<Vector2Int> _resolutions = SettingsApplier.Resolutions();

        /// <summary>The display settings to go back to while a change waits for confirmation.</summary>
        private GameSettings _confirmedDisplay;
        private float _confirmLeft;
        private Label _confirmCountdown;

        public override bool UsesMenuScene => true;

        private GameSettings Settings => App.Settings;

        public SettingsScreen(GameApp app) : base(app, "Settings")
        {
            Button("tab-language", () => ShowTab(LanguageTab));
            Button("tab-graphics", () => ShowTab(GraphicsTab));
            // Not built yet: volume (with the audio framework, M3) and key bindings.
            Find<Button>("tab-audio").SetEnabled(false);
            Find<Button>("tab-controls").SetEnabled(false);
            Button("back", Close);

            BuildLanguages();
            BuildGraphics();
            ShowTab(GraphicsTab);
            Refresh();
        }

        public override void Tick(float deltaTime)
        {
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (_confirmedDisplay != null)
            {
                _confirmLeft -= Time.unscaledDeltaTime;
                UpdateCountdown();
                if (_confirmLeft <= 0f || escape)
                {
                    RevertDisplay();
                }
                return;
            }
            if (escape && !App.Ui.HasModal)
            {
                Close();
            }
        }

        public override void Exit()
        {
            // Leaving while a display change is unconfirmed keeps the old one, as the dialog says.
            if (_confirmedDisplay != null)
            {
                RevertDisplay();
            }
        }

        private void Close() => App.ShowMainMenu();

        private void ShowTab(string tab)
        {
            Find<Button>("tab-language").EnableInClassList("menu-item--on", tab == LanguageTab);
            Find<Button>("tab-graphics").EnableInClassList("menu-item--on", tab == GraphicsTab);
            Find<VisualElement>("page-language").SetVisible(tab == LanguageTab);
            Find<VisualElement>("page-graphics").SetVisible(tab == GraphicsTab);
            Find<Label>("page-title").text = tab == LanguageTab ? "语言" : "画质";
        }

        /// <summary>Saves and updates every chip, stepper and tab subtitle.</summary>
        private void Changed()
        {
            SettingsStore.Save(Settings);
            Refresh();
        }

        private void Refresh()
        {
            foreach (Action refresh in _refreshers)
            {
                refresh();
            }
        }

        // ------------------------------------------------------------------
        // Language

        private void BuildLanguages()
        {
            VisualElement list = Find<VisualElement>("languages");
            foreach (SettingsConfig.Language language in _config.languages)
            {
                string id = language.id;
                Button card = Ui.Button(string.Empty, () =>
                {
                    Settings.language = id;
                    Changed();
                }, "settings-language");
                card.Add(Ui.Label(language.name, "settings-language-name"));
                Label status = Ui.Label(language.status, "settings-language-status");
                card.Add(status);
                card.SetEnabled(language.available);
                list.Add(card);
                _refreshers.Add(() =>
                {
                    bool chosen = Settings.language == id;
                    card.EnableInClassList("settings-language--on", chosen);
                    status.text = chosen ? "使用中" : language.status;
                });
            }
            _refreshers.Add(() => Find<Label>("tab-language-sub").text = _config.FindLanguage(Settings.language)?.name ?? string.Empty);
        }

        // ------------------------------------------------------------------
        // Graphics

        private void BuildGraphics()
        {
            var presets = new List<(string, string)>();
            foreach (SettingsConfig.Preset preset in _config.presets)
            {
                presets.Add((preset.id, preset.name));
            }
            presets.Add((SettingsConfig.CustomPreset, _config.customPresetName));
            // "Custom" is only a marker: lit after changing a single item, never picked directly.
            Choice("opt-preset", presets, () => Settings.preset, id =>
            {
                SettingsConfig.Preset preset = _config.FindPreset(id);
                if (preset != null)
                {
                    Settings.ApplyPreset(preset);
                    SettingsApplier.ApplyGraphics(Settings);
                }
            }, id => id != SettingsConfig.CustomPreset || Settings.preset == SettingsConfig.CustomPreset);

            var modes = new List<(string, string)>();
            foreach (SettingsConfig.Named mode in _config.displayModes)
            {
                modes.Add((mode.id, mode.name));
            }
            Choice("opt-display", modes, () => Settings.displayMode, id => ChangeDisplay(s => s.displayMode = id));

            BuildResolution();

            Choice("opt-vsync", OnOff(), () => OnOffId(Settings.vsync), id =>
            {
                Settings.vsync = id == "on";
                SettingsApplier.ApplyFrameRate(Settings);
            });

            var frameRates = new List<(string, string)>();
            foreach (SettingsConfig.NamedValue rate in _config.frameRates)
            {
                frameRates.Add((rate.value.ToString(), rate.name));
            }
            Choice("opt-frame-rate", frameRates, () => Settings.frameRate.ToString(), id =>
            {
                Settings.frameRate = int.Parse(id);
                SettingsApplier.ApplyFrameRate(Settings);
            }, _ => !Settings.vsync);

            var scales = new List<(string, string)>();
            foreach (SettingsConfig.NamedValue scale in _config.renderScales)
            {
                scales.Add((scale.value.ToString(), scale.name));
            }
            Choice("opt-render-scale", scales, () => Settings.renderScale.ToString(), id => ChangeQuality(s => s.renderScale = int.Parse(id)));

            var antialiasing = new List<(string, string)>();
            foreach (SettingsConfig.Antialiasing aa in _config.antialiasing)
            {
                antialiasing.Add((aa.id, aa.name));
            }
            Choice("opt-antialiasing", antialiasing, () => Settings.antialiasing, id => ChangeQuality(s => s.antialiasing = id));

            var shadows = new List<(string, string)>();
            foreach (SettingsConfig.Shadows level in _config.shadows)
            {
                shadows.Add((level.id, level.name));
            }
            Choice("opt-shadows", shadows, () => Settings.shadows, id => ChangeQuality(s => s.shadows = id));

            Choice("opt-bloom", OnOff(), () => OnOffId(Settings.bloom), id => ChangeQuality(s => s.bloom = id == "on"));

            Button("reset-graphics", ResetGraphics);
            _refreshers.Add(() => Find<Label>("tab-graphics-sub").text = _config.PresetName(Settings.preset));

            Vector2Int native = SettingsApplier.NativeResolution;
            Find<Label>("device").text = SystemInfo.graphicsDeviceName + " · 显示器 " + native.x + " × " + native.y
                + " · " + Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value) + " Hz";
        }

        /// <summary>
        /// Fills a row of chips, one per (id, name) option. <paramref name="enabled"/> greys out
        /// single options (the "custom" preset) or the whole row (frame rate while vsync is on).
        /// </summary>
        private void Choice(string container, List<(string id, string name)> options, Func<string> current, Action<string> pick, Func<string, bool> enabled = null)
        {
            VisualElement row = Find<VisualElement>(container);
            var chips = new List<(string id, Button chip)>();
            foreach ((string id, string name) in options)
            {
                Button chip = Ui.Button(name, () =>
                {
                    pick(id);
                    Changed();
                }, "settings-chip");
                row.Add(chip);
                chips.Add((id, chip));
            }
            _refreshers.Add(() =>
            {
                string chosen = current();
                foreach ((string id, Button chip) in chips)
                {
                    chip.EnableInClassList("settings-chip--on", id == chosen);
                    chip.SetEnabled(enabled == null || enabled(id));
                }
            });
        }

        private List<(string, string)> OnOff() => new List<(string, string)> { ("on", _config.onName), ("off", _config.offName) };

        private static string OnOffId(bool on) => on ? "on" : "off";

        /// <summary>A single quality item changed: the preset becomes whichever one still matches, or custom.</summary>
        private void ChangeQuality(Action<GameSettings> change)
        {
            change(Settings);
            Settings.preset = _config.MatchPreset(Settings);
            SettingsApplier.ApplyGraphics(Settings);
        }

        /// <summary>Preset, vsync and frame rate back to the defaults; the display mode and resolution stay.</summary>
        private void ResetGraphics()
        {
            GameSettings defaults = GameSettings.CreateDefault(_config);
            Settings.ApplyPreset(_config.FindPreset(_config.defaultPreset));
            Settings.vsync = defaults.vsync;
            Settings.frameRate = defaults.frameRate;
            SettingsApplier.ApplyFrameRate(Settings);
            SettingsApplier.ApplyGraphics(Settings);
            Changed();
        }

        // ------------------------------------------------------------------
        // Display mode and resolution

        private void BuildResolution()
        {
            Label value = Find<Label>("res-value");
            Button previous = Button("res-prev", () => StepResolution(+1));
            Button next = Button("res-next", () => StepResolution(-1));
            Find<Label>("resolution-desc").text = Application.isEditor
                ? "在编辑器里由 Game 视图决定，打包后生效"
                : "改动后 " + Mathf.RoundToInt(_config.displayConfirmSeconds) + " 秒内确认，否则自动还原";
            _refreshers.Add(() =>
            {
                Vector2Int size = SettingsApplier.ResolutionOf(Settings);
                value.text = size.x + " × " + size.y;
                int index = _resolutions.IndexOf(size);
                // The list is largest first: "previous" goes down in size, "next" goes up.
                previous.SetEnabled(index < 0 || index < _resolutions.Count - 1);
                next.SetEnabled(index > 0);
            });
        }

        private void StepResolution(int step)
        {
            Vector2Int current = SettingsApplier.ResolutionOf(Settings);
            int index = _resolutions.IndexOf(current);
            index = index < 0 ? 0 : Mathf.Clamp(index + step, 0, _resolutions.Count - 1);
            Vector2Int size = _resolutions[index];
            if (size == current)
            {
                return;
            }
            ChangeDisplay(s =>
            {
                bool native = size == SettingsApplier.NativeResolution;
                s.width = native ? 0 : size.x;
                s.height = native ? 0 : size.y;
            });
            Changed();
        }

        /// <summary>Applies a display change and asks to keep it (not in the editor, where nothing changes).</summary>
        private void ChangeDisplay(Action<GameSettings> change)
        {
            GameSettings before = Settings.Clone();
            change(Settings);
            if (Settings.displayMode == before.displayMode && Settings.width == before.width && Settings.height == before.height)
            {
                return;
            }
            SettingsApplier.ApplyDisplay(Settings);
            if (Application.isEditor)
            {
                return;
            }
            if (_confirmedDisplay == null)
            {
                _confirmedDisplay = before;
            }
            _confirmLeft = _config.displayConfirmSeconds;
            App.Ui.CloseModals();
            VisualElement dialog = Ui.Dialog("保留这个显示设置吗？", string.Empty,
                ("保留", KeepDisplay),
                ("还原", RevertDisplay));
            _confirmCountdown = dialog.Q<Label>(className: "dialog-body");
            UpdateCountdown();
            App.Ui.ShowModal(dialog);
        }

        private void UpdateCountdown() =>
            _confirmCountdown.text = Mathf.CeilToInt(Mathf.Max(0f, _confirmLeft)) + " 秒后还原为之前的显示设置。";

        private void KeepDisplay()
        {
            _confirmedDisplay = null;
            App.Ui.CloseModals();
        }

        private void RevertDisplay()
        {
            GameSettings before = _confirmedDisplay;
            _confirmedDisplay = null;
            App.Ui.CloseModals();
            Settings.displayMode = before.displayMode;
            Settings.width = before.width;
            Settings.height = before.height;
            SettingsApplier.ApplyDisplay(Settings);
            Changed();
        }
    }
}
