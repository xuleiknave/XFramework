using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>
    /// Deployment: fleet roster on the left, the board in the middle (handled by
    /// <see cref="DeployController"/>), briefing and pre-battle forecast on the right.
    /// </summary>
    public sealed class DeployScreen : UiScreen
    {
        private readonly ExpeditionRun _run;
        private readonly Encounter _encounter;
        private readonly DeployController _deploy;
        private readonly ScrollView _roster;
        private readonly Button _forecastButton;
        private readonly Button _fightButton;

        public DeployScreen(GameApp app) : base(app, "Deploy")
        {
            _run = app.Run;
            _encounter = _run.CurrentEncounter;
            _deploy = new DeployController(_run, _encounter, app.Stage);
            _deploy.Changed += Refresh;

            Find<Label>("title").text = _run.Act.Title + " · 第 " + (_run.Current.Layer + 1) + " 跳    " + _encounter.Name;
            Find<Label>("enc-name").text = _encounter.Name;
            Find<Label>("enc-briefing").text = _encounter.Briefing;
            _roster = Find<ScrollView>("roster");
            Button("auto", _deploy.AutoFill);
            Button("clear", _deploy.ClearFormation);
            _forecastButton = Button("forecast", _deploy.RunForecast);
            _fightButton = Button("fight", Fight);
            FrameBoardArea();
        }

        public override void Enter()
        {
            App.Overlay.SetBattleMode(false);
            _deploy.Begin();
        }

        public override void Exit() => _deploy.Changed -= Refresh;

        public override void Tick(float deltaTime)
        {
            Mouse mouse = Mouse.current;
            bool overUi = mouse != null && App.IsPointerOverUi(mouse.position.ReadValue());
            _deploy.Tick(overUi);
        }

        private void Fight()
        {
            if (!_deploy.CanFight)
            {
                return;
            }
            App.ShowBattle(_encounter, _deploy.Finish());
        }

        private void Refresh()
        {
            int max = ExpeditionRun.MaxDurability;
            Label durability = Find<Label>("durability");
            durability.text = "耐久 " + Ui.Hearts(_run.Durability, max);
            durability.EnableInClassList("bad", _run.Durability <= 2);

            Find<Label>("command").text = "编队    指挥点 " + _deploy.Formation.CommandPointsUsed() + " / " + _deploy.CommandLimit;
            BuildRoster();

            Label supply = Find<Label>("supply");
            supply.text = "魔网：" + _deploy.SuppliedShips + " / " + _deploy.DeployedShips + " 名角色已接入";
            supply.EnableInClassList("warn", _deploy.SuppliedShips < _deploy.DeployedShips);
            supply.EnableInClassList("good", _deploy.SuppliedShips >= _deploy.DeployedShips);
            Label notice = Find<Label>("notice");
            notice.text = _deploy.Notice ?? "";
            notice.SetVisible(_deploy.Notice != null);

            RefreshForecast();
        }

        private void BuildRoster()
        {
            _roster.Clear();
            int relaysLeft = _deploy.Fleet.RelaysLeft;
            bool relaySelected = _deploy.Selection == DeploySelection.Relay;
            Button relay = RosterCard("法力水晶", "剩 " + relaysLeft + " / " + _deploy.Fleet.Relays + "   指挥 1", "魔网节点 · 放在格子交叉点",
                () =>
                {
                    if (relaySelected)
                    {
                        _deploy.CancelSelection();
                    }
                    else
                    {
                        _deploy.SelectRelay();
                    }
                });
            relay.EnableInClassList("roster-card--selected", relaySelected);
            relay.SetEnabled(relaysLeft > 0 || relaySelected);
            _roster.Add(relay);

            var ships = new List<OwnedShip>(_deploy.Fleet.Ships);
            ships.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.UnitId, b.UnitId);
                return byName != 0 ? byName : b.Star - a.Star;
            });
            foreach (OwnedShip ship in ships)
            {
                UnitDef def = ship.Def;
                bool deployed = _deploy.Fleet.IsDeployed(ship.Id);
                bool selected = _deploy.Selection == DeploySelection.Ship && _deploy.SelectedShipId == ship.Id;
                int copies = _deploy.Fleet.CopiesOwned(ship.UnitId, ship.Star);
                int id = ship.Id;
                Button card = RosterCard(def.DisplayName + " " + Ui.Stars(ship.Star),
                    (deployed ? "已上阵" : selected ? "放置中" : "后备") + "   指挥 " + def.CommandCost,
                    Wording.RoleName(def.Role) + (def.Size >= 2 ? " · 2×2" : "") + (ship.Star < UnitDef.MaxStar ? " · 合成 " + copies + "/3" : ""),
                    () =>
                    {
                        if (selected)
                        {
                            _deploy.CancelSelection();
                        }
                        else
                        {
                            _deploy.SelectShip(id);
                        }
                    });
                card.EnableInClassList("roster-card--deployed", deployed);
                card.EnableInClassList("roster-card--selected", selected);
                _roster.Add(card);
            }
        }

        private static Button RosterCard(string title, string state, string detail, System.Action onClick)
        {
            Button card = Ui.Button("", onClick, "roster-card");
            VisualElement head = Ui.Box("roster-card-head");
            head.Add(Ui.Label(title, "roster-card-title"));
            head.Add(Ui.Label(state, "roster-card-state"));
            card.Add(head);
            card.Add(Ui.Label(detail, "small", "muted"));
            return card;
        }

        private void RefreshForecast()
        {
            Find<Label>("intel").text = "战前推演    情报点 " + _deploy.Intel + " / " + _encounter.IntelPoints;
            ForecastResult f = _deploy.Forecast;
            Find<VisualElement>("forecast-empty").SetVisible(f == null);
            Find<VisualElement>("forecast-result").SetVisible(f != null);
            if (f != null)
            {
                Find<Label>("win-rate").text = UnityEngine.Mathf.RoundToInt((float)f.WinRate * 100f) + "%";
                Find<Label>("win-detail").text = "胜率（±" + UnityEngine.Mathf.RoundToInt((float)f.Margin * 100f) + "%）    平均 "
                    + UnityEngine.Mathf.RoundToInt((float)f.AverageSeconds) + " 秒    平均损失 " + f.AverageLosses.ToString("0.0") + " 名";
                VisualElement reasons = Find<VisualElement>("reasons");
                reasons.Clear();
                if (f.LossReasons.Count > 0)
                {
                    reasons.Add(Ui.Label("常见败因", "small", "muted"));
                    foreach (string reason in f.LossReasons)
                    {
                        reasons.Add(Ui.Label("· " + reason, "small", "warn"));
                    }
                }
                Label advice = Find<Label>("advice");
                advice.text = string.IsNullOrEmpty(f.Advice) ? "" : "参谋建议：" + f.Advice;
                Find<Label>("history").text = _deploy.ForecastHistory.Count > 1
                    ? "推演记录：" + string.Join(" → ", _deploy.ForecastHistory.ConvertAll(v => v + "%"))
                    : "";
            }

            VisualElement errors = Find<VisualElement>("errors");
            errors.Clear();
            foreach (string error in _deploy.Errors)
            {
                errors.Add(Ui.Label("✕ " + error, "small", "bad"));
            }
            _forecastButton.text = _deploy.Intel > 0 ? "战前推演（情报 −1）" : "情报点已用完";
            _forecastButton.SetEnabled(_deploy.Intel > 0 && _deploy.CanFight);
            _fightButton.SetEnabled(_deploy.CanFight);
        }
    }
}
