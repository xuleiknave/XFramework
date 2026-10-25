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
                button.Add(Ui.Label(skill.DisplayName, "skill-name"));
                button.Add(Ui.Label("补给 " + skill.Cost, "small", "muted"));
                Label state = Ui.Label("", "small");
                button.Add(state);
                button.tooltip = skill.Description;
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
            _player.text = "我方 " + sim.CountCombatants(BoardLayout.PlayerSide) + " / " + sim.CountInitialCombatants(BoardLayout.PlayerSide);
            _enemy.text = "敌方 " + sim.CountCombatants(BoardLayout.EnemySide) + " / " + sim.CountInitialCombatants(BoardLayout.EnemySide);
            _time.text = Mathf.FloorToInt(sim.ElapsedSeconds.ToFloat()) + " 秒 / " + _battle.Encounter.TimeLimitSeconds;
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
            _points.text = "法术点数  " + points.ToString("0.0") + " / 10   （每秒 +1）";
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
                string text = cd > 0 ? "冷却 " + Mathf.CeilToInt(cd / (float)BattleSim.TicksPerSecond) + " 秒"
                    : can ? (def.NeedsTarget ? "点击后选择落点" : "可释放") : "法术点数不足";
                bool armed = def.NeedsTarget && _battle.StrikeArmed;
                if (armed)
                {
                    text = "在战场上点击落点（右键取消）";
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
                "用时 " + Mathf.FloorToInt(sim.ElapsedSeconds.ToFloat()) + " 秒",
                "我方损失 " + (sim.CountInitialCombatants(BoardLayout.PlayerSide) - sim.CountCombatants(BoardLayout.PlayerSide))
                    + " 名，击败敌方 " + (sim.CountInitialCombatants(BoardLayout.EnemySide) - sim.CountCombatants(BoardLayout.EnemySide)) + " 个"
                    + (won ? "（倒下的角色战后全部恢复）" : ""),
            };
            SideStats stats = sim.Stats[BoardLayout.PlayerSide];
            if (stats.RelaysLost > 0)
            {
                lines.Add("我方有 " + stats.RelaysLost + " 个魔网节点被摧毁");
            }
            if (stats.RanDry.Count > 0)
            {
                var names = new List<string>();
                foreach (string id in stats.RanDry)
                {
                    names.Add(UnitCatalog.Get(id).DisplayName);
                }
                lines.Add("法力耗尽过：" + string.Join("、", names));
            }
            lines.AddRange(run.Messages);

            string next;
            switch (run.State)
            {
                case RunState.Reward: next = "领取战利品"; break;
                case RunState.Victory: next = "要塞陷落！"; break;
                case RunState.Defeat: next = "撤回灯塔城"; break;
                case RunState.Battle: next = "调整布阵，再次挑战 Boss"; break;
                default: next = "返回地图"; break;
            }
            string title = won ? "胜利" : sim.Winner == BoardLayout.EnemySide ? "失败" : "平局（算作失败）";
            VisualElement dialog = Ui.Dialog(title, string.Join("\n", lines), (next, App.AfterBattle));
            dialog.Q(className: "dialog").AddToClassList(won ? "dialog--win" : "dialog--loss");
            App.Ui.ShowModal(dialog);
        }
    }
}
