using UnityEngine.UIElements;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>End of the expedition: the act's victory or defeat text and a short summary.</summary>
    public sealed class RunEndScreen : UiScreen
    {
        public RunEndScreen(GameApp app) : base(app, "RunEnd")
        {
            ExpeditionRun run = app.Run;
            bool won = run.State == RunState.Victory;
            ActStory story = run.Act.Story;
            Find<Label>("title").text = won ? story.VictoryTitle : story.DefeatTitle;
            Find<Label>("story").text = won ? story.Victory : story.Defeat;
            Find<Label>("summary").text = "本次远征：战斗 " + run.BattlesWon + " 胜 / " + run.BattlesFought + " 场，剩余耐久 "
                + run.Durability + "，远征军 " + run.Fleet.Ships.Count + " 支部队。";
            Find<VisualElement>("panel").EnableInClassList("dialog--win", won);
            Find<VisualElement>("panel").EnableInClassList("dialog--loss", !won);
            Button("again", App.StartRun);
            Button("menu", App.ShowMainMenu);
        }
    }
}
