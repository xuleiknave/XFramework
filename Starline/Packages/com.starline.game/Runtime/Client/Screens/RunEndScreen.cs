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
            Find<Label>("title").text = Loc.T(won ? story.VictoryTitle : story.DefeatTitle);
            Find<Label>("story").text = Loc.T(won ? story.Victory : story.Defeat);
            Find<Label>("summary").text = Loc.Ui("run_end.summary", run.BattlesWon, run.BattlesFought, run.Durability, run.Fleet.Ships.Count);
            Find<VisualElement>("panel").EnableInClassList("dialog--win", won);
            Find<VisualElement>("panel").EnableInClassList("dialog--loss", !won);
            Button("again", App.StartRun);
            Button("menu", App.ShowMainMenu);
        }
    }
}
