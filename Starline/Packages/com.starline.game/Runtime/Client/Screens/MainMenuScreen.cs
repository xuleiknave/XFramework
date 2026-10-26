using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>Home screen shown at launch (the home port in later versions): start an expedition, switch save slots or quit.</summary>
    public sealed class MainMenuScreen : UiScreen
    {
        public override bool UsesMenuScene => true;

        public MainMenuScreen(GameApp app) : base(app, "MainMenu")
        {
            Button("start", App.StartRunOrCreateCommander);
            // Not built yet: saved expeditions (M3), the endless tower, the workshop and the archive (P3).
            Find<Button>("continue").SetEnabled(false);
            Find<Button>("tower").SetEnabled(false);
            Find<Button>("workshop").SetEnabled(false);
            Find<Button>("archive").SetEnabled(false);
            CommanderProfile profile = App.Profile;
            Find<Label>("commander").text = profile != null
                ? Loc.Ui("menu.commander", profile.name, profile.slot + 1, CommanderProfile.FormatPlayTime(profile.playSeconds))
                : Loc.Ui("menu.no_commander");
            Button("saves", App.ShowSaveSlots);
            Button("settings", App.ShowSettings);
            Button("quit", App.Quit);
            Find<Label>("version").text = Loc.Ui("menu.version", Application.version);
        }
    }
}
