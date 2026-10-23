using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Runs a battle in real time: fixed-step simulation at the chosen speed, commander
    /// skills (the orbital strike is aimed with the mouse), skip, and the event feed.
    /// </summary>
    public sealed class BattleController
    {
        public static readonly float[] Speeds = { 1f, 2f, 4f };
        private const int FeedLength = 6;

        private readonly BattleStage _stage;
        private float _accumulator;
        private float _endDelay = 1.5f;

        public BattleController(Encounter encounter, Formation player, BattleStage stage)
        {
            _stage = stage;
            Encounter = encounter;
            // Stage battles use the stage's fixed seed so they can be recorded and shared as replays
            // (replay-server.md section 4); hand-built encounters fall back to a time-based seed outside
            // the forecast range.
            ulong seed = encounter.Id != null ? StageSeed.For(encounter.Id) : (ulong)DateTime.Now.Ticks % 900000UL + 1UL;
            Battle = new BattleSim(encounter.CreateSetup(player, seed));
            _stage.BeginBattle(Battle);
            _stage.Rig.GoToBattle();
            AddFeed("开战！");
        }

        public Encounter Encounter { get; }
        public BattleSim Battle { get; }
        public int SpeedIndex { get; set; }
        public bool StrikeArmed { get; private set; }
        public List<string> Feed { get; } = new List<string>();

        /// <summary>True once the battle is over and the short closing pause has passed.</summary>
        public bool Finished { get; private set; }

        public bool PlayerWon => Battle.Winner == BoardLayout.PlayerSide;

        public void ToggleStrike() => StrikeArmed = !StrikeArmed && Battle.CanCast(BoardLayout.PlayerSide, CommanderSkill.OrbitalStrike);

        public void Cast(CommanderSkillDef skill)
        {
            if (skill.NeedsTarget)
            {
                ToggleStrike();
            }
            else
            {
                Battle.Issue(Command.Cast(BoardLayout.PlayerSide, skill.Skill));
            }
        }

        public void Skip()
        {
            while (!Battle.IsOver)
            {
                Battle.Step();
                _stage.AfterStep(false, AddFeed);
            }
            _accumulator = 0f;
            _endDelay = 0.3f;
            _stage.RefreshNetworks();
        }

        public void Tick(float deltaTime, bool pointerOverUi)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && StrikeArmed && mouse.leftButton.wasPressedThisFrame && !pointerOverUi)
            {
                if (_stage.TryGetGroundPoint(mouse.position.ReadValue(), out Vector3 p))
                {
                    Battle.Issue(Command.Cast(BoardLayout.PlayerSide, CommanderSkill.OrbitalStrike, BattleStage.ToFix(p)));
                }
                StrikeArmed = false;
            }
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                StrikeArmed = false;
            }

            if (!Battle.IsOver)
            {
                float tick = 1f / BattleSim.TicksPerSecond;
                _accumulator += deltaTime * Speeds[SpeedIndex];
                _accumulator = Mathf.Min(_accumulator, tick * 12f);
                while (_accumulator >= tick && !Battle.IsOver)
                {
                    Battle.Step();
                    _stage.AfterStep(true, AddFeed);
                    _accumulator -= tick;
                }
            }
            else if (!Finished)
            {
                StrikeArmed = false;
                _endDelay -= deltaTime;
                Finished = _endDelay <= 0f;
            }
            _stage.Interpolate(Mathf.Clamp01(_accumulator * BattleSim.TicksPerSecond));
        }

        /// <summary>"我方魔网：5 / 6 有魔网 · 网络完好" for one side.</summary>
        public string NetworkSummary(int side)
        {
            int supplied = 0, combat = 0;
            foreach (Unit u in Battle.Units)
            {
                if (u.Side == side && u.Def.HasWeapon)
                {
                    combat++;
                    if (u.Supplied)
                    {
                        supplied++;
                    }
                }
            }
            int lost = Battle.Stats[side].RelaysLost;
            return (side == BoardLayout.PlayerSide ? "我方魔网：" : "敌方魔网：") + supplied + " / " + combat + " 有魔网"
                + (lost > 0 ? " · " + lost + " 个节点被毁" : " · 网络完好");
        }

        private void AddFeed(string text)
        {
            Feed.Add(text);
            if (Feed.Count > FeedLength)
            {
                Feed.RemoveAt(0);
            }
        }
    }
}
