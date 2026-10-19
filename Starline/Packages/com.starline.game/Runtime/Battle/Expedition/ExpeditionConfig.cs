using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    public sealed class RewardPoolEntry
    {
        public string UnitId;
        public int Weight;
        public int EliteWeight;
    }

    /// <summary>Story text shown at the start and end of an act.</summary>
    public sealed class ActStory
    {
        public string IntroTitle;
        public string Intro;
        public string VictoryTitle;
        public string Victory;
        public string DefeatTitle;
        public string Defeat;
    }

    /// <summary>One act of the expedition (expedition.json → acts[]).</summary>
    public sealed class ActConfig
    {
        public string Id;
        public string Title;
        public int StartRelays;
        public Formation StartFormation = new Formation();
        /// <summary>Starting ships with their board positions (unit id, depth, row).</summary>
        public readonly List<Placement> StartShips = new List<Placement>();
        public readonly List<NodeType[]> Layers = new List<NodeType[]>();
        public readonly List<string[]> NormalTiers = new List<string[]>();
        public int[] NormalTierByLayer;
        public readonly List<string[]> EliteTiers = new List<string[]>();
        public int[] EliteTierByLayer;
        public string Boss;
        public readonly List<RewardPoolEntry> RewardPool = new List<RewardPoolEntry>();
        public ActStory Story = new ActStory();

        public string[] NormalPool(int layer) => NormalTiers[Pick(NormalTierByLayer, layer)];

        public string[] ElitePool(int layer) => EliteTiers[Pick(EliteTierByLayer, layer)];

        private static int Pick(int[] byLayer, int layer) => byLayer[layer < byLayer.Length ? layer : byLayer.Length - 1];
    }

    /// <summary>Expedition rules and acts, loaded from expedition.json.</summary>
    public sealed class ExpeditionConfig
    {
        public int MaxDurability;
        public int MaxRelays;
        public int BaseCommandLimit;
        public int CommandLimitPerBattle;
        public int MaxCommandLimit;
        public int NormalLoss;
        public int BossLoss;
        public readonly List<ActConfig> Acts = new List<ActConfig>();

        private static ExpeditionConfig _current;
        private static bool _subscribed;

        /// <summary>The loaded configuration (rebuilt whenever <see cref="GameData"/> reloads).</summary>
        public static ExpeditionConfig Current
        {
            get
            {
                if (!_subscribed)
                {
                    GameData.Loaded += () => _current = null;
                    _subscribed = true;
                }
                return _current ?? (_current = Parse(GameData.File("expedition")));
            }
        }

        public ActConfig Act(string id) => Acts.Find(a => a.Id == id) ?? throw new KeyNotFoundException("Unknown act: " + id);

        private static ExpeditionConfig Parse(JsonValue root)
        {
            JsonValue rules = root["rules"];
            var config = new ExpeditionConfig
            {
                MaxDurability = rules["maxDurability"].AsInt(),
                MaxRelays = rules["maxRelays"].AsInt(),
                BaseCommandLimit = rules["baseCommandLimit"].AsInt(),
                CommandLimitPerBattle = rules["commandLimitPerBattle"].AsInt(),
                MaxCommandLimit = rules["maxCommandLimit"].AsInt(),
                NormalLoss = rules["normalLoss"].AsInt(),
                BossLoss = rules["bossLoss"].AsInt(),
            };

            foreach (JsonValue a in root["acts"].Elements)
            {
                var act = new ActConfig
                {
                    Id = a["id"].AsString(),
                    Title = a["title"].AsString(),
                    Boss = a["boss"].AsString(),
                    NormalTierByLayer = Ints(a["normalTierByLayer"]),
                    EliteTierByLayer = Ints(a["eliteTierByLayer"]),
                };
                JsonValue start = a["start"];
                act.StartRelays = start["relays"].AsInt();
                foreach (JsonValue p in start["formation"].Elements)
                {
                    GameData.AddPlacement(act.StartFormation, p);
                }
                foreach (JsonValue p in start["ships"].Elements)
                {
                    act.StartShips.Add(new Placement(p[0].AsString(), p[1].AsInt(), p[2].AsInt()));
                }
                foreach (JsonValue layer in a["layers"].Elements)
                {
                    var types = new NodeType[layer.Count];
                    for (int i = 0; i < types.Length; i++)
                    {
                        types[i] = GameData.ParseEnum<NodeType>(layer[i]);
                    }
                    act.Layers.Add(types);
                }
                foreach (JsonValue tier in a["normalTiers"].Elements)
                {
                    act.NormalTiers.Add(Strings(tier));
                }
                foreach (JsonValue tier in a["eliteTiers"].Elements)
                {
                    act.EliteTiers.Add(Strings(tier));
                }
                foreach (JsonValue r in a["rewardPool"].Elements)
                {
                    int weight = r["weight"].AsInt();
                    act.RewardPool.Add(new RewardPoolEntry
                    {
                        UnitId = r["unit"].AsString(),
                        Weight = weight,
                        EliteWeight = r.Int("eliteWeight", weight),
                    });
                }
                JsonValue story = a["story"];
                act.Story = new ActStory
                {
                    IntroTitle = story["introTitle"].AsString(),
                    Intro = story["intro"].AsString(),
                    VictoryTitle = story["victoryTitle"].AsString(),
                    Victory = story["victory"].AsString(),
                    DefeatTitle = story["defeatTitle"].AsString(),
                    Defeat = story["defeat"].AsString(),
                };
                config.Acts.Add(act);
            }
            return config;
        }

        private static int[] Ints(JsonValue list)
        {
            var result = new int[list.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = list[i].AsInt();
            }
            return result;
        }

        private static string[] Strings(JsonValue list)
        {
            var result = new string[list.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = list[i].AsString();
            }
            return result;
        }
    }
}
