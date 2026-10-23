using UnityEngine.UIElements;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>"我的录像": each stage's best saved win, with a button to watch it.</summary>
    public sealed class ReplayListScreen : UiScreen
    {
        public ReplayListScreen(GameApp app) : base(app, "ReplayList")
        {
            Button("back", App.ShowMainMenu);
            ScrollView list = Find<ScrollView>("list");
            var replays = App.Replays.All();
            Find<Label>("empty").SetVisible(replays.Count == 0);
            foreach (Replay replay in replays)
            {
                list.Add(Card(replay));
            }
        }

        private VisualElement Card(Replay replay)
        {
            bool known = EncounterLibrary.Exists(replay.Stage);
            bool playable = known && replay.IsPlayable;

            VisualElement card = Ui.Box("panel", "replay-card");
            VisualElement text = Ui.Box("grow");
            text.Add(Ui.Label(known ? EncounterLibrary.Create(replay.Stage).Name : replay.Stage, "replay-card-title"));
            int seconds = replay.Ticks / BattleSim.TicksPerSecond;
            text.Add(Ui.Label("战力 " + replay.Power + " · 用时 " + seconds + " 秒 · "
                + (replay.UsesSkills ? "使用了 " + replay.Commands.Count + " 次指挥官技能" : "没有使用指挥官技能"), "small", "muted"));
            if (!playable)
            {
                text.Add(Ui.Label(known ? "旧版本的录像：数值或规则已更新，无法回放。再打赢一次这关会自动替换。" : "这个关卡已不存在", "small", "bad"));
            }
            card.Add(text);

            Button watch = Ui.Button("观看", () => App.ShowReplay(replay, "返回录像列表", App.ShowReplays), "btn-primary");
            watch.SetEnabled(playable);
            card.Add(watch);
            return card;
        }
    }
}
