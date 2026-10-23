using System;
using Starline.Battle;

namespace Starline.Battle.Tests
{
    internal static partial class Program
    {
        private const string ReplayStage = "test_wreck_field";

        // Golden replay (see GoldenReplay). When data files change these no longer apply and the test
        // prints the new values; when only the kernel changed, a mismatch means battles play out
        // differently: bump Replay.KernelVersion, then update these.
        private const string GoldenData = "0801b7a62bed9a05";
        private const string GoldenHash = "2ac288b92450495f";
        private const int GoldenTicks = 571;

        private static void RunReplayTests()
        {
            Run("录像：关卡种子固定，不同关卡不同", StageSeedsAreStable);
            Run("录像：战力按配置表计算，节点不计", PowerRatingRules);
            Run("录像：JSON 往返逐位一致", ReplayJsonRoundTrip);
            Run("录像：回放与原战斗结果完全相同", ReplayReproducesBattle);
            Run("录像校验：真录像通过，并重算战力", VerifierAcceptsGenuineReplay);
            Run("录像校验：篡改过的录像被拒", VerifierRejectsTamperedReplays);
            Run("录像：本关最佳只保留战力更低的胜局", BestReplayRules);
            Run("录像：黄金录像哈希不变", GoldenReplay);
        }

        /// <summary>The fixed test stage: the wreck-field ambush under a stage id.</summary>
        private static Encounter ReplayStageEncounter()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            e.Id = ReplayStage;
            return e;
        }

        /// <summary>Plays the test stage with a meteor on the nearest enemy whenever it is ready, and records it.</summary>
        private static Replay RecordTestReplay()
        {
            Encounter e = ReplayStageEncounter();
            var battle = new BattleSim(e.CreateSetup(e.SuggestedPlayer.Clone(), StageSeed.For(e.Id)));
            while (!battle.IsOver)
            {
                if (battle.CanCast(0, CommanderSkill.OrbitalStrike))
                {
                    Unit target = null;
                    foreach (Unit u in battle.Units)
                    {
                        if (u.Side == 1 && u.Def.HasWeapon && (target == null || u.Position.X < target.Position.X))
                        {
                            target = u;
                        }
                    }
                    if (target != null)
                    {
                        battle.Issue(Command.Cast(0, CommanderSkill.OrbitalStrike, target.Position));
                    }
                }
                battle.Step();
            }
            return Replay.Record(e.Id, battle);
        }

        private static void StageSeedsAreStable()
        {
            Assert(StageSeed.For("patrol") == StageSeed.For("patrol"), "同一关卡种子应相同");
            Assert(StageSeed.For("patrol") != StageSeed.For("locust_raid"), "不同关卡种子应不同");
            Encounter e = Expedition.EncounterLibrary.Create("patrol");
            Assert(e.Id == "patrol", "从配置表创建的关卡应带 id");
        }

        private static void PowerRatingRules()
        {
            // 重甲骑士 ★1：(700 + 200) × 1 + 30 / 1.2 × 10 = 1150
            Assert(PowerRating.Of(new Placement("battleship", 0, 0)) == 1150, "重甲骑士 ★1 战力应为 1150，实际 " + PowerRating.Of(new Placement("battleship", 0, 0)));
            Assert(PowerRating.Of(new Placement("battleship", 0, 0, 2)) > 1150, "★2 战力应更高");
            Assert(PowerRating.Of(new Placement("relay", 1, 1)) == 0, "法力水晶不计战力");
            Assert(PowerRating.Of(new Placement("supply_ship", 4, 3)) == 0, "源晶塔不计战力");
            Info("测试阵容战力 " + PowerRating.Of(Fixtures.WreckFieldAmbush().SuggestedPlayer));
        }

        private static void ReplayJsonRoundTrip()
        {
            Replay replay = RecordTestReplay();
            Assert(replay.Commands.Count > 0, "测试录像应包含陨星术指令");
            string json = ReplayJson.Write(replay);
            Replay back = ReplayJson.Read(json);
            Assert(ReplayJson.Write(back) == json, "读回后再写出应完全相同");
            Assert(back.Hash == replay.Hash && back.Seed == replay.Seed, "哈希和种子应逐位一致");
            Info("录像 " + json.Length + " 字节，" + replay.Commands.Count + " 条指令");
        }

        private static void ReplayReproducesBattle()
        {
            Replay replay = ReplayJson.Read(ReplayJson.Write(RecordTestReplay()));
            var player = new ReplayPlayer(replay, ReplayStageEncounter());
            player.RunToEnd();
            Assert(player.Battle.Winner == replay.Winner, "胜负应相同");
            Assert(player.Battle.Tick == replay.Ticks, "帧数应相同");
            Assert(player.Battle.ComputeStateHash() == replay.Hash, "最终状态哈希应相同");
        }

        private static void VerifierAcceptsGenuineReplay()
        {
            Replay replay = RecordTestReplay();
            Assert(replay.Winner == 0, "测试录像应是我方胜局（调整测试阵容或技能）");
            int claimed = replay.Power;
            replay.Power = 1; // the client's number is ignored
            ReplayCheck check = ReplayVerifier.Verify(ReplayJson.Read(ReplayJson.Write(replay)), ReplayStageEncounter());
            Assert(check.Ok, "真录像应通过校验：" + check.Error);
            Assert(check.Power == claimed, "服务器应重算战力");
        }

        private static void VerifierRejectsTamperedReplays()
        {
            string json = ReplayJson.Write(RecordTestReplay());
            Encounter stage = ReplayStageEncounter();

            ExpectRejected("改了配置表版本", json, r => r.Data = "0000000000000000", stage);
            ExpectRejected("改了内核版本", json, r => r.Kernel = Replay.KernelVersion + 1, stage);
            ExpectRejected("换了种子", json, r => r.Seed = r.Seed + 1, stage);
            ExpectRejected("换了关卡", json, r => r.Stage = "patrol", stage);
            ExpectRejected("声称获胜但其实是另一局", json, r => r.Hash ^= 1, stage);
            ExpectRejected("改了星级", json, r => r.Player.Placements[4].Star = 3, stage);
            ExpectRejected("星级越界", json, r => r.Player.Placements[4].Star = 9, stage);
            ExpectRejected("未知角色", json, r => r.Player.Placements[4].UnitId = "dragon", stage);
            ExpectRejected("去掉一名角色", json, r => r.Player.Placements.RemoveAt(r.Player.Placements.Count - 1), stage);
            ExpectRejected("改了技能时机", json, r => r.Commands[0].Tick += 7, stage);
            ExpectRejected("删掉技能", json, r => r.Commands.Clear(), stage);
            ExpectRejected("指令帧号倒序", json, r => r.Commands.Insert(0, new ReplayCommand { Tick = 5000 }), stage);
            ExpectRejected("败局", json, r => r.Winner = 1, stage);
        }

        private static void ExpectRejected(string what, string json, Action<Replay> tamper, Encounter stage)
        {
            Replay replay = ReplayJson.Read(json);
            tamper(replay);
            ReplayCheck check = ReplayVerifier.Verify(replay, stage);
            Assert(!check.Ok, what + "：应被拒绝");
        }

        private static void BestReplayRules()
        {
            Replay win = RecordTestReplay();
            Replay lower = ReplayJson.Read(ReplayJson.Write(win));
            lower.Power = win.Power - 1;
            Replay loss = ReplayJson.Read(ReplayJson.Write(win));
            loss.Winner = BoardLayout.EnemySide;
            Replay stale = ReplayJson.Read(ReplayJson.Write(win));
            stale.Data = "0000000000000000";

            Assert(win.IsPlayable && !stale.IsPlayable, "配置表哈希不同的录像不能回放");
            Assert(win.IsBetterThan(null), "没有录像时胜局应保存");
            Assert(!loss.IsBetterThan(null), "败局不保存");
            Assert(lower.IsBetterThan(win), "战力更低的胜局应替换");
            Assert(!win.IsBetterThan(lower) && !win.IsBetterThan(win), "战力更高或相同的胜局不替换");
            Assert(win.IsBetterThan(stale) && win.IsBetterThan(loss), "旧版本或败局应被胜局替换");
        }

        private static void GoldenReplay()
        {
            Replay replay = RecordTestReplay();
            string hash = replay.Hash.ToString("x16");
            if (GameData.Fingerprint != GoldenData)
            {
                Info("配置表已改动，黄金录像需要更新：GoldenData = \"" + GameData.Fingerprint + "\"，GoldenHash = \"" + hash + "\"，GoldenTicks = " + replay.Ticks);
                return;
            }
            Assert(hash == GoldenHash && replay.Ticks == GoldenTicks,
                "配置表没变但战斗结果变了（" + hash + "，" + replay.Ticks + " 帧）：内核改动影响了战斗，请把 Replay.KernelVersion 加 1 并更新黄金录像");
        }
    }
}
