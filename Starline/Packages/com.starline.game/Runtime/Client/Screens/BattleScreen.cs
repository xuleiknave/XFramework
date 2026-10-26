using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>
    /// The auto-battle: counts and time on top, event feed on the right, supply points and
    /// commander skills at the bottom, and the result dialog when it ends.
    /// </summary>
    public sealed class BattleScreen : UiScreen
    {
        private const int SupplyPips = 10;

        private readonly BattleController _battle;
        private readonly List<Button> _speedButtons = new List<Button>();
        private readonly List<VisualElement> _pips = new List<VisualElement>();
        private readonly List<(CommanderSkillDef def, Button button, Label state)> _skills = new List<(CommanderSkillDef, Button, Label)>();
        private readonly Label _player, _enemy, _time, _points, _playerNet, _enemyNet;
        private readonly VisualElement _feed;
        private bool _reported;
        private string _feedShown;

        public BattleScreen(GameApp app, BattleController battle) : base(app, "Battle")
        {
            _battle = battle;
            _player = Find<Label>("player-count");
            _enemy = Find<Label>("enemy-count");
            _time = Find<Label>("time");
            _points = Find<Label>("points");
            _playerNet = Find<Label>("player-net");
            _enemyNet = Find<Label>("enemy-net");
            _feed = Find<VisualElement>("feed");
            Button("skip", Skip);

            VisualElement speeds = Find<VisualElement>("speeds");
            for (int i = 0; i < BattleController.Speeds.Length; i++)
            {
                int index = i;
                Button b = Ui.Button(BattleController.Speeds[i] + "x", () => _battle.SpeedIndex = index, "btn-small");
                _speedButtons.Add(b);
                speeds.Add(b);
            }

            VisualElement pips = Find<VisualElement>("pips");
            for (int i = 0; i < SupplyPips; i++)
            {
                VisualElement pip = Ui.Box("pip");
                _pips.Add(pip);
                pips.Add(pip);
            }

            VisualElement skills = Find<VisualElement>("skills");
            foreach (CommanderSkillDef def in CommanderSkillDef.All)
            {
                CommanderSkillDef skill = def;
                Button button = Ui.Button("", () => _battle.Cast(skill), "skill");
                button.Add(Ui.Label(Loc.T(skill.Name), "skill-name"));
                button.Add(Ui.Label(Loc.Ui("battle.skill_cost", skill.Cost), "small", "muted"));
                Label state = Ui.Label("", "small");
                button.Add(state);
                button.tooltip = Loc.T(skill.Description);
                skills.Add(button);
                _skills.Add((skill, button, state));
            }
        }

        public override void Enter() => App.Overlay.SetBattleMode(true);

        public override void Tick(float deltaTime)
        {
            Mouse mouse = Mouse.current;
            bool overUi = mouse != null && App.IsPointerOverUi(mouse.position.ReadValue());
            _battle.Tick(deltaTime, overUi);
            Refresh();
            if (_battle.Finished && !_reported)
            {
                _reported = true;
                ShowResult();
            }
        }

        private void Skip()
        {
            if (!_battle.Battle.IsOver)
            {
                _battle.Skip();
            }
        }

        private void Refresh()
        {
            BattleSim sim = _battle.Battle;
            _player.text = Loc.Ui("battle.player_count", sim.CountCombatants(BoardLayout.PlayerSide), sim.CountInitialCombatants(BoardLayout.PlayerSide));
            _enemy.text = Loc.Ui("battle.enemy_count", sim.CountCombatants(BoardLayout.EnemySide), sim.CountInitialCombatants(BoardLayout.EnemySide));
            _time.text = Loc.Ui("battle.time", Mathf.FloorToInt(sim.ElapsedSeconds.ToFloat()), _battle.Encounter.TimeLimitSeconds);
            for (int i = 0; i < _speedButtons.Count; i++)
            {
                _speedButtons[i].EnableInClassList("btn-toggled", _battle.SpeedIndex == i);
            }

            string feedNow = string.Join("\n", _battle.Feed);
            if (feedNow != _feedShown)
            {
                _feedShown = feedNow;
                _feed.Clear();
                foreach (string line in _battle.Feed)
                {
                    Label label = Ui.Label(line, "small");
                    label.pickingMode = PickingMode.Ignore;
                    _feed.Add(label);
                }
            }

            float points = sim.SupplyPoints(BoardLayout.PlayerSide).ToFloat();
            _points.text = Loc.Ui("battle.points", points.ToString("0.0"));
            for (int i = 0; i < _pips.Count; i++)
            {
                _pips[i].EnableInClassList("pip--full", i < Mathf.FloorToInt(points));
            }
            _playerNet.text = _battle.NetworkSummary(BoardLayout.PlayerSide);
            _enemyNet.text = _battle.NetworkSummary(BoardLayout.EnemySide);

            foreach ((CommanderSkillDef def, Button button, Label state) in _skills)
            {
                int cd = sim.SkillCooldownTicks(BoardLayout.PlayerSide, def.Skill);
                bool can = sim.CanCast(BoardLayout.PlayerSide, def.Skill);
                string text = cd > 0 ? Loc.Ui("battle.cooldown", Mathf.CeilToInt(cd / (float)BattleSim.TicksPerSecond))
                    : Loc.Ui(can ? (def.NeedsTarget ? "battle.pick_target" : "battle.ready") : "battle.no_points");
                bool armed = def.NeedsTarget && _battle.StrikeArmed;
                if (armed)
                {
                    text = Loc.Ui("battle.aiming");
                }
                state.text = text;
                button.SetEnabled(can || armed);
                button.EnableInClassList("skill--armed", armed);
            }
        }

        private void ShowResult()
        {
            ExpeditionRun run = App.Run;
            BattleSim sim = _battle.Battle;
            bool won = _battle.PlayerWon;
            run.ReportBattle(won);
            App.Music.PlayStinger(won ? "win" : "lose");

            var lines = new List<string>
            {
                Loc.Ui("result.time", Mathf.FloorToInt(sim.ElapsedSeconds.ToFloat())),
                Loc.Ui("result.losses", sim.CountInitialCombatants(BoardLayout.PlayerSide) - sim.CountCombatants(BoardLayout.PlayerSide),
                    sim.CountInitialCombatants(BoardLayout.EnemySide) - sim.CountCombatants(BoardLayout.EnemySide))
                    + (won ? Loc.Ui("result.recovered") : ""),
            };
            SideStats stats = sim.Stats[BoardLayout.PlayerSide];
            if (stats.RelaysLost > 0)
            {
                lines.Add(Loc.Ui("result.relays_lost", stats.RelaysLost));
            }
            if (stats.RanDry.Count > 0)
            {
                var names = new List<string>();
                foreach (string id in stats.RanDry)
                {
                    names.Add(Loc.T(UnitCatalog.Get(id).Name));
                }
                lines.Add(Loc.Ui("result.ran_dry", string.Join(Loc.Ui("list.separator"), names)));
            }
            lines.AddRange(run.Messages.ConvertAll(Loc.T));

            string next;
            switch (run.State)
            {
                case RunState.Reward: next = Loc.Ui("result.next_reward"); break;
                case RunState.Victory: next = Loc.Ui("result.next_victory"); break;
                case RunState.Defeat: next = Loc.Ui("result.next_defeat"); break;
                case RunState.Battle: next = Loc.Ui("result.next_retry"); break;
                default: next = Loc.Ui("result.next_map"); break;
            }
            string title = Loc.Ui(won ? "result.won" : sim.Winner == BoardLayout.EnemySide ? "result.lost" : "result.draw");
            VisualElement dialog = Ui.Dialog(title, string.Join("\n", lines), (next, App.AfterBattle));
            dialog.Q(className: "dialog").AddToClassList(won ? "dialog--win" : "dialog--loss");
            App.Ui.ShowModal(dialog);
        }
    }
}
