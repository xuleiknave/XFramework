using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>Title screen (the home port in later versions): start an expedition, watch saved replays, or quit.</summary>
    public sealed class MainMenuScreen : UiScreen
    {
        public MainMenuScreen(GameApp app) : base(app, "MainMenu")
        {
            Button("start", App.StartRun);
            Button("replays", App.ShowReplays);
            Find<Button>("continue").SetEnabled(false);
            Find<Button>("settings").SetEnabled(false);
            Button("quit", App.Quit);
            Find<Label>("version").text = "版本 " + Application.version + " · 第一幕试玩";
        }
    }
}
