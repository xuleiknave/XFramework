using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>Main menu after choosing a save slot (the home port in later versions): start an expedition or quit.</summary>
    public sealed class MainMenuScreen : UiScreen
    {
        public MainMenuScreen(GameApp app) : base(app, "MainMenu")
        {
            Button("start", App.StartRun);
            Find<Button>("continue").SetEnabled(false);
            Find<Button>("settings").SetEnabled(false);
            Find<Label>("commander").text = "指挥官 " + App.Profile.name;
            Button("switch", App.ShowLogin);
            Button("quit", App.Quit);
            Find<Label>("version").text = "版本 " + Application.version + " · 第一幕试玩";
        }
    }
}
