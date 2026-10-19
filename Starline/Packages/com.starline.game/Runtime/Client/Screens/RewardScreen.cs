using UnityEngine.UIElements;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>Three-choice battle reward: a ship (merging when it completes a set of three) or a relay beacon.</summary>
    public sealed class RewardScreen : UiScreen
    {
        private readonly ExpeditionRun _run;

        public RewardScreen(GameApp app) : base(app, "Reward")
        {
            _run = app.Run;
            VisualElement cards = Find<VisualElement>("cards");
            for (int i = 0; i < _run.Rewards.Count; i++)
            {
                cards.Add(Card(_run.Rewards[i], i));
            }
            Button("skip", () => Choose(-1));
        }

        private VisualElement Card(RewardOption option, int index)
        {
            VisualElement card = Ui.Box("panel", "reward-card");
            card.Add(Ui.Label(option.Kind == RewardKind.Relay ? "中继信标 +1" : UnitCatalog.Get(option.UnitId).DisplayName + " " + Ui.Stars(option.Star), "h2"));
            if (option.Kind == RewardKind.Relay)
            {
                card.Add(Ui.Label("补给节点 · 放在格子交叉点 · 指挥 1", "small", "muted"));
                card.Add(Ui.Label("覆盖周围 2 格，同一条格线上 3 格内的节点自动连成补给线。多一个信标，阵型就能铺得更开。", "body"));
                card.Add(Ui.Label("当前 " + _run.Fleet.Relays + " 个", "small", "muted"));
            }
            else
            {
                UnitDef def = UnitCatalog.Get(option.UnitId);
                card.Add(Ui.Label(Wording.RoleName(def.Role) + (def.Size >= 2 ? " · 2×2" : "") + " · 指挥 " + def.CommandCost, "small", "muted"));
                if (!string.IsNullOrEmpty(def.Description))
                {
                    card.Add(Ui.Label(def.Description, "body"));
                }
                card.Add(Ui.Label(Wording.ShipStats(def, option.Star), "body", "stats"));
                int owned = _run.Fleet.CopiesOwned(option.UnitId, option.Star);
                if (option.Star < UnitDef.MaxStar && owned > 0)
                {
                    card.Add(owned >= 2
                        ? Ui.Label("已有 2 艘同星 → 选它立即合成 " + Ui.Stars(option.Star + 1) + "！", "small", "good")
                        : Ui.Label("已有 1 艘同星（3 艘合成 " + Ui.Stars(option.Star + 1) + "）", "small", "muted"));
                }
            }
            card.Add(Ui.Box("spacer"));
            card.Add(Ui.Button("选择", () => Choose(index), "btn-primary"));
            return card;
        }

        private void Choose(int index)
        {
            _run.ChooseReward(index);
            App.ShowMap();
        }
    }
}
