using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    public enum RunState
    {
        /// <summary>Choosing the next node on the star map.</summary>
        Map,
        /// <summary>At a battle node: deploy and fight.</summary>
        Battle,
        /// <summary>Won a battle: pick one of three rewards.</summary>
        Reward,
        /// <summary>At a supply station: choose repair or a relay beacon.</summary>
        Supply,
        /// <summary>Act boss defeated.</summary>
        Victory,
        /// <summary>Fleet durability reached zero.</summary>
        Defeat,
    }

    public enum RewardKind
    {
        Ship,
        Relay,
    }

    public sealed class RewardOption
    {
        public RewardKind Kind;
        public string UnitId;
        public int Star = 1;

        public string Title => Kind == RewardKind.Relay ? "法力水晶 +1" : UnitCatalog.Get(UnitId).DisplayName + " " + Stars(Star);

        public static string Stars(int star) => new string('★', star);
    }

    /// <summary>
    /// One expedition run through Act 1 (GDD 4): star map, battles, three-choice rewards,
    /// star merging and fleet durability. Pure game state; the client drives it and runs the
    /// battles, then reports the outcome with <see cref="ReportBattle"/>.
    /// </summary>
    public sealed class ExpeditionRun
    {
        private static ExpeditionConfig Rules => ExpeditionConfig.Current;

        public static int MaxDurability => Rules.MaxDurability;
        public static int MaxRelays => Rules.MaxRelays;
        public static int BaseCommandLimit => Rules.BaseCommandLimit;
        /// <summary>Command limit gained per battle node passed, won or lost, so one defeat does not snowball.</summary>
        public static int CommandLimitPerBattle => Rules.CommandLimitPerBattle;
        public static int MaxCommandLimit => Rules.MaxCommandLimit;

        private readonly DeterministicRandom _random;

        public ExpeditionRun(ulong seed) : this(ExpeditionConfig.Current.Acts[0], seed)
        {
        }

        public ExpeditionRun(ActConfig act, ulong seed)
        {
            Act = act;
            Seed = seed;
            _random = new DeterministicRandom(seed * 2654435761UL + 1);
            Map = StarMap.Generate(act, seed);
            Durability = MaxDurability;
            State = RunState.Map;

            // Starting fleet (GDD 4.1): supply ship, relays and three ships.
            Fleet.Relays = act.StartRelays;
            Fleet.Formation = act.StartFormation.Clone();
            foreach (Placement p in act.StartShips)
            {
                Fleet.Deploy(Fleet.Add(p.UnitId).Ship, p.Depth, p.Row);
            }
        }

        public ActConfig Act { get; }
        public ulong Seed { get; }
        public StarMap Map { get; }
        public Fleet Fleet { get; } = new Fleet();
        public RunState State { get; private set; }
        public int Durability { get; private set; }
        public int BattlesWon { get; private set; }
        public int BattlesFought { get; private set; }
        /// <summary>Battle nodes left behind (won, or lost and passed); boss retries do not count.</summary>
        public int BattleNodesPassed { get; private set; }
        /// <summary>The node being played, or the last one finished (null before the first jump).</summary>
        public MapNode Current { get; private set; }
        public Encounter CurrentEncounter { get; private set; }
        public List<RewardOption> Rewards { get; } = new List<RewardOption>();
        /// <summary>Short messages for the UI about what just happened (merges, durability, ...).</summary>
        public List<string> Messages { get; } = new List<string>();

        public int CommandLimit
        {
            get
            {
                int limit = BaseCommandLimit + CommandLimitPerBattle * BattleNodesPassed;
                return limit > MaxCommandLimit ? MaxCommandLimit : limit;
            }
        }

        public bool IsOver => State == RunState.Victory || State == RunState.Defeat;

        /// <summary>Nodes the fleet can jump to now.</summary>
        public List<MapNode> Reachable()
        {
            var result = new List<MapNode>();
            if (State != RunState.Map)
            {
                return result;
            }
            if (Current == null)
            {
                result.AddRange(Map.Layers[0]);
            }
            else
            {
                foreach (int id in Current.Next)
                {
                    result.Add(Map.Get(id));
                }
            }
            return result;
        }

        public void Enter(MapNode node)
        {
            if (!Reachable().Contains(node))
            {
                return;
            }
            Messages.Clear();
            Current = node;
            node.Visited = true;
            if (node.IsBattle)
            {
                CurrentEncounter = EncounterLibrary.Create(node.EncounterKey);
                State = RunState.Battle;
            }
            else
            {
                CurrentEncounter = null;
                State = RunState.Supply;
            }
        }

        /// <summary>Applies the result of the current battle node.</summary>
        public void ReportBattle(bool won)
        {
            if (State != RunState.Battle)
            {
                return;
            }
            Messages.Clear();
            BattlesFought++;
            bool boss = Current.Type == NodeType.Boss;
            if (won || !boss)
            {
                BattleNodesPassed++;
            }
            if (won)
            {
                BattlesWon++;
                if (boss)
                {
                    State = RunState.Victory;
                    return;
                }
                RollRewards(Current.Type == NodeType.Elite);
                State = RunState.Reward;
                return;
            }

            int loss = boss ? Rules.BossLoss : Rules.NormalLoss;
            Durability -= loss;
            Messages.Add("战斗失败，远征军耐久 −" + loss);
            if (Durability <= 0)
            {
                Durability = 0;
                State = RunState.Defeat;
            }
            else if (!boss)
            {
                // A lost node is passed without rewards; the boss must be beaten to finish the act.
                State = RunState.Map;
            }
        }

        /// <summary>Takes a reward (or skips with -1) and returns to the map.</summary>
        public void ChooseReward(int index)
        {
            if (State != RunState.Reward)
            {
                return;
            }
            Messages.Clear();
            if (index >= 0 && index < Rewards.Count)
            {
                RewardOption option = Rewards[index];
                if (option.Kind == RewardKind.Relay)
                {
                    Fleet.Relays++;
                    AutoDeploy.PlaceRelay(Fleet, CommandLimit);
                    Messages.Add("获得 法力水晶（共 " + Fleet.Relays + " 个）");
                }
                else
                {
                    GainShip(option.UnitId, option.Star);
                }
            }
            Rewards.Clear();
            State = RunState.Map;
        }

        /// <summary>Supply station: repair one durability, or take a relay beacon.</summary>
        public void ChooseSupply(bool repair)
        {
            if (State != RunState.Supply)
            {
                return;
            }
            Messages.Clear();
            if (repair)
            {
                Durability = Durability + 1 > MaxDurability ? MaxDurability : Durability + 1;
                Messages.Add("远征军休整完毕，耐久 " + Durability + " / " + MaxDurability);
            }
            else
            {
                Fleet.Relays++;
                AutoDeploy.PlaceRelay(Fleet, CommandLimit);
                Messages.Add("获得 法力水晶（共 " + Fleet.Relays + " 个）");
            }
            State = RunState.Map;
        }

        private void GainShip(string unitId, int star)
        {
            AddShipResult added = Fleet.Add(unitId, star);
            string name = added.Ship.Def.DisplayName;
            Messages.Add("获得 " + UnitCatalog.Get(unitId).DisplayName + " " + RewardOption.Stars(star));
            foreach (int merged in added.MergedTo)
            {
                Messages.Add("3 名合成 → " + name + " " + RewardOption.Stars(merged));
            }
            if (!Fleet.IsDeployed(added.Ship.Id))
            {
                Messages.Add(AutoDeploy.PlaceShip(Fleet, added.Ship, CommandLimit)
                    ? name + " 已自动上阵，可在布阵时调整"
                    : name + " 进入后备（指挥点不够或没有空位）");
            }
        }

        private void RollRewards(bool elite)
        {
            Rewards.Clear();
            int guard = 0;
            while (Rewards.Count < 3 && guard++ < 50)
            {
                RewardOption option;
                if (Rewards.Count == 2 && Fleet.Relays < MaxRelays && _random.Range(0, 4) == 0)
                {
                    option = new RewardOption { Kind = RewardKind.Relay };
                }
                else
                {
                    // Half of the offers repeat a ship the player owns, so merges come up regularly.
                    string unitId = _random.Range(0, 2) == 0 ? OwnedCombatUnit() : null;
                    unitId = unitId ?? WeightedShip(elite);
                    int star = elite && Rewards.Count == 0 ? 2 : 1;
                    option = new RewardOption { Kind = RewardKind.Ship, UnitId = unitId, Star = star };
                }
                if (!Rewards.Exists(r => r.Kind == option.Kind && r.UnitId == option.UnitId && r.Star == option.Star))
                {
                    Rewards.Add(option);
                }
            }
        }

        private string OwnedCombatUnit()
        {
            var owned = Fleet.Ships.FindAll(s => s.Def.HasWeapon && s.Star < UnitDef.MaxMergeStar);
            return owned.Count == 0 ? null : owned[_random.Range(0, owned.Count)].UnitId;
        }

        private string WeightedShip(bool elite)
        {
            List<RewardPoolEntry> pool = Act.RewardPool;
            int total = 0;
            foreach (RewardPoolEntry entry in pool)
            {
                total += elite ? entry.EliteWeight : entry.Weight;
            }
            int roll = _random.Range(0, total);
            foreach (RewardPoolEntry entry in pool)
            {
                roll -= elite ? entry.EliteWeight : entry.Weight;
                if (roll < 0)
                {
                    return entry.UnitId;
                }
            }
            return pool[0].UnitId;
        }
    }
}
