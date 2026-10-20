using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>Home screen shown at launch (the home port in later versions): start an expedition, switch save slots or quit.</summary>
    public sealed class MainMenuScreen : UiScreen
    {
        public MainMenuScreen(GameApp app) : base(app, "MainMenu")
        {
            Button("start", App.StartRunOrCreateCommander);
            Find<Button>("continue").SetEnabled(false);
            Find<Button>("settings").SetEnabled(false);
            CommanderProfile profile = App.Profile;
            Find<Label>("commander").text = profile != null
                ? "指挥官 " + profile.name + " · 存档 " + (profile.slot + 1) + " · 游戏时长 " + CommanderProfile.FormatPlayTime(profile.playSeconds)
                : "还没有指挥官：开始远征时先起个名字";
            Button("saves", App.ShowSaveSlots);
            Button("quit", App.Quit);
            Find<Label>("version").text = "版本 " + Application.version + " · 第一幕试玩";
        }
    }
}
