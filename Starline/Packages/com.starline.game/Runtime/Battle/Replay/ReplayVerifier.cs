using System;
using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>Outcome of <see cref="ReplayVerifier.Verify"/>.</summary>
    public sealed class ReplayCheck
    {
        public bool Ok;
        /// <summary>Why the replay was rejected (shown in server logs), or null.</summary>
        public string Error;
        /// <summary>Power recomputed from the formation; only set when <see cref="Ok"/>.</summary>
        public int Power;

        public static ReplayCheck Fail(string error) => new ReplayCheck { Error = error };
    }

    /// <summary>
    /// Server-side check of an uploaded replay (replay-server.md section 5): versions, seed and
    /// formation rules first, then a full re-run that must end in a player win with the same tick
    /// count and state hash. Power is recomputed here, never trusted from the client.
    /// </summary>
    public static class ReplayVerifier
    {
        public const int MaxCommands = 200;

        public static ReplayCheck Verify(Replay replay, Encounter stage)
        {
            try
            {
                return VerifyUnchecked(replay, stage);
            }
            catch (Exception e)
            {
                return ReplayCheck.Fail("录像无法重跑：" + e.Message);
            }
        }

        private static ReplayCheck VerifyUnchecked(Replay replay, Encounter stage)
        {
            if (replay.Format != Replay.FormatVersion)
            {
                return ReplayCheck.Fail("录像格式版本不支持：" + replay.Format);
            }
            if (replay.Kernel != Replay.KernelVersion)
            {
                return ReplayCheck.Fail("内核版本不一致：" + replay.Kernel);
            }
            if (replay.Data != GameData.Fingerprint)
            {
                return ReplayCheck.Fail("配置表版本不一致：" + replay.Data);
            }
            if (stage.Id != replay.Stage)
            {
                return ReplayCheck.Fail("关卡不一致：" + replay.Stage);
            }
            if (replay.Seed != StageSeed.For(replay.Stage))
            {
                return ReplayCheck.Fail("种子与关卡不符");
            }
            if (replay.Winner != BoardLayout.PlayerSide)
            {
                return ReplayCheck.Fail("录像不是胜局");
            }

            foreach (Placement p in replay.Player.Placements)
            {
                if (!UnitCatalog.Exists(p.UnitId))
                {
                    return ReplayCheck.Fail("未知角色：" + p.UnitId);
                }
                if (p.Star < 1 || p.Star > UnitDef.MaxStar)
                {
                    return ReplayCheck.Fail("星级无效：" + p.UnitId + " ★" + p.Star);
                }
            }
            List<string> errors = FormationRules.Validate(replay.Player, stage.CommandLimit);
            if (errors.Count > 0)
            {
                return ReplayCheck.Fail("布阵不合法：" + errors[0]);
            }

            if (replay.Commands.Count > MaxCommands)
            {
                return ReplayCheck.Fail("指令过多");
            }
            int lastTick = 0;
            foreach (ReplayCommand c in replay.Commands)
            {
                if (c.Tick < lastTick || c.Tick > replay.Ticks)
                {
                    return ReplayCheck.Fail("指令帧号无效：" + c.Tick);
                }
                if ((int)c.Skill < 0 || (int)c.Skill >= CommanderSkillDef.All.Length)
                {
                    return ReplayCheck.Fail("未知技能");
                }
                lastTick = c.Tick;
            }

            var player = new ReplayPlayer(replay, stage);
            player.RunToEnd();
            BattleSim battle = player.Battle;
            if (battle.Winner != BoardLayout.PlayerSide)
            {
                return ReplayCheck.Fail("重跑结果不是胜局");
            }
            if (battle.Tick != replay.Ticks || battle.ComputeStateHash() != replay.Hash)
            {
                return ReplayCheck.Fail("重跑结果与录像不一致");
            }
            return new ReplayCheck { Ok = true, Power = PowerRating.Of(replay.Player) };
        }
    }
}
