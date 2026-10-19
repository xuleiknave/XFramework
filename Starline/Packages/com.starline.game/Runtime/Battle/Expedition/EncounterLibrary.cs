using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    /// <summary>
    /// Hand-made Act 1 (Frontier) enemy formations, grouped by difficulty tier. The star map
    /// picks one per node; values move to data files later.
    /// </summary>
    public static class EncounterLibrary
    {
        public const string Boss = "wreck_fortress";

        /// <summary>Normal battles by tier: tier 1 for the first layer, 3 just before the boss.</summary>
        public static readonly string[][] NormalTiers =
        {
            new[] { "patrol", "locust_raid" },
            new[] { "turret_line", "crawler_wall" },
            new[] { "hive_outpost", "evolved_pack" },
        };

        /// <summary>Elite battles: early elites, then late elites.</summary>
        public static readonly string[][] EliteTiers =
        {
            new[] { "brood_patrol" },
            new[] { "wreck_ambush" },
        };

        public static Encounter Create(string key)
        {
            switch (key)
            {
                case "patrol":
                    return Make("巡逻虫群", "灯塔站外的第一座信标被一小群蜂群占着。把它们赶走。",
                        SmallHive().Add("ripper", 0, 2).Add("ripper", 0, 5).Add("crawler", 0, 3));
                case "locust_raid":
                    return Make("飞蝗骚扰", "飞蝗群专咬补给线。看好你的中继信标。",
                        SmallHive().Add("ripper", 0, 3).Add("crawler", 0, 4).Add("locust", 1, 1).Add("locust", 1, 6));
                case "turret_line":
                    return Make("炮巢阵地", "蜂群在信标残骸上长出了酸液炮巢。它的弹药靠后方的巢链节点补充。",
                        SmallHive().Add("crawler", 0, 3).Add("ripper", 0, 1)
                            .Add("acid_turret", 2, 3).Add("locust", 1, 0).Add("locust", 1, 7));
                case "crawler_wall":
                    return Make("甲壳兽墙", "一排甲壳兽挡住了航道。重甲怕导弹舰和舰载机群。",
                        SmallHive().Add("crawler", 0, 1).Add("crawler", 0, 3).Add("crawler", 0, 5)
                            .Add("ripper", 1, 3));
                case "hive_outpost":
                    return Make("蜂巢前哨", "要塞外围的前哨站，三条巢链连着两座炮巢。",
                        FullHive().Add("crawler", 0, 2).Add("crawler", 0, 5).Add("ripper", 0, 1)
                            .Add("acid_turret", 2, 3).Add("locust", 1, 0).Add("locust", 1, 7));
                case "evolved_pack":
                    return Make("进化虫群", "这群蜂群吃过信标能量，比普通个体强得多（★2）。",
                        FullHive().Add("crawler", 0, 3, 2).Add("ripper", 0, 1).Add("ripper", 0, 6)
                            .Add("acid_turret", 2, 4).Add("locust", 1, 0));
                case "brood_patrol":
                    return Make("精英战：母体巡游", "一头母体带着护卫在信标间游荡。它每第 4 次攻击是重炮齐射。",
                        SmallHive().Add("brood_mother", 0, 3).Add("ripper", 0, 1).Add("ripper", 0, 6).Add("locust", 1, 0));
                case "wreck_ambush":
                    return Make("精英战：残骸带伏击", "残骸带里埋伏着一支蜂群，后方有母体压阵。",
                        FullHive().Add("crawler", 0, 3).Add("ripper", 0, 1).Add("ripper", 0, 6)
                            .Add("acid_turret", 2, 4).Add("locust", 1, 0)
                            .Add("brood_mother", 3, 0));
                case Boss:
                    return Make("Boss：残骸要塞", "蜂群把断线日炸毁的星线残骸堆成了一座要塞。要塞不会移动，但炮火覆盖我方前两排，每第 3 次攻击是重炮齐射。",
                        new Formation()
                            .Add("hive", 4, 3).Add("hive_node", 4, 1).Add("hive_node", 4, 7)
                            .Add(Boss, 1, 3)
                            .Add("crawler", 0, 3).Add("ripper", 0, 1).Add("ripper", 0, 6)
                            .Add("acid_turret", 2, 1).Add("locust", 1, 7));
                default:
                    throw new KeyNotFoundException("Unknown encounter: " + key);
            }
        }

        private static Formation SmallHive() => new Formation()
            .Add("hive", 4, 3).Add("hive_node", 2, 4).Add("hive_node", 2, 1);

        private static Formation FullHive() => SmallHive().Add("hive_node", 2, 7);

        private static Encounter Make(string name, string briefing, Formation enemy) => new Encounter
        {
            Name = name,
            Briefing = briefing,
            Enemy = enemy,
        };
    }
}
