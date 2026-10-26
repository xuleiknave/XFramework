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
            card.Add(Ui.Label(Loc.T(option.Title), "h2"));
            if (option.Kind == RewardKind.Relay)
            {
                card.Add(Ui.Label(Loc.Ui("reward.relay_kind"), "small", "muted"));
                card.Add(Ui.Label(Loc.Ui("reward.relay_body"), "body"));
                card.Add(Ui.Label(Loc.Ui("reward.relay_owned", _run.Fleet.Relays), "small", "muted"));
            }
            else
            {
                UnitDef def = UnitCatalog.Get(option.UnitId);
                card.Add(Ui.Label(Wording.RoleName(def.Role) + (def.Size >= 2 ? " · 2×2" : "") + Loc.Ui("reward.command", def.CommandCost), "small", "muted"));
                if (Loc.Has(def.Description))
                {
                    card.Add(Ui.Label(Loc.T(def.Description), "body"));
                }
                card.Add(Ui.Label(Wording.ShipStats(def, option.Star), "body", "stats"));
                int owned = _run.Fleet.CopiesOwned(option.UnitId, option.Star);
                if (option.Star < UnitDef.MaxMergeStar && owned > 0)
                {
                    card.Add(owned >= 2
                        ? Ui.Label(Loc.Ui("reward.merge_now", Ui.Stars(option.Star + 1)), "small", "good")
                        : Ui.Label(Loc.Ui("reward.merge_later", Ui.Stars(option.Star + 1)), "small", "muted"));
                }
            }
            card.Add(Ui.Box("spacer"));
            card.Add(Ui.Button(Loc.Ui("reward.choose"), () => Choose(index), "btn-primary"));
            return card;
        }

        private void Choose(int index)
        {
            _run.ChooseReward(index);
            App.ShowMap();
        }
    }
}
