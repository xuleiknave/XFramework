using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>
    /// Every ship and enemy unit type, loaded from units.json (see <see cref="GameData"/>).
    /// Counters: strike craft beat heavy ships and artillery, point defence beats strike craft,
    /// heavy beats light, light beats artillery, artillery beats heavy.
    /// </summary>
    public static class UnitCatalog
    {
        private static readonly List<UnitDef> _all = new List<UnitDef>();
        private static readonly Dictionary<string, UnitDef> _byId = new Dictionary<string, UnitDef>();

        public static IReadOnlyList<UnitDef> All => _all;

        // Units the code refers to directly.
        public static UnitDef SupplyShip => Get("supply_ship");
        public static UnitDef Relay => Get("relay");
        public static UnitDef Frigate => Get("frigate");

        public static UnitDef Get(string id)
        {
            if (_byId.Count == 0)
            {
                throw new System.InvalidOperationException("游戏数据还没有加载：先调用 GameData.Load");
            }
            if (!_byId.TryGetValue(id, out UnitDef def))
            {
                throw new KeyNotFoundException("Unknown unit id: " + id);
            }
            return def;
        }

        public static bool Exists(string id) => _byId.ContainsKey(id);

        internal static void Load(JsonValue root)
        {
            _all.Clear();
            _byId.Clear();
            foreach (JsonValue u in root["units"].Elements)
            {
                var prefers = new List<UnitRole>();
                if (u.Get("prefers") is JsonValue list)
                {
                    foreach (JsonValue role in list.Elements)
                    {
                        prefers.Add(GameData.ParseEnum<UnitRole>(role));
                    }
                }
                var def = new UnitDef
                {
                    Id = u["id"].AsString(),
                    DisplayName = u["name"].AsString(),
                    Description = u.String("description", ""),
                    Role = GameData.ParseEnum<UnitRole>(u["role"]),
                    Size = u.Int("size", 1),
                    CommandCost = u.Int("commandCost"),
                    MaxHp = u.FixValue("hp"),
                    MaxShield = u.FixValue("shield"),
                    ShieldRegenPerSecond = u.FixValue("shieldRegen"),
                    Damage = u.FixValue("damage"),
                    FireIntervalSeconds = u.FixValue("fireInterval", Fix.One),
                    Range = u.FixValue("range"),
                    HeavySalvoEvery = u.Int("heavySalvoEvery"),
                    Speed = u.FixValue("speed"),
                    Radius = u.FixValue("radius", Fix.One),
                    MaxAmmo = u.Int("ammo"),
                    AmmoRefillSeconds = u.FixValue("ammoRefill", Fix.One),
                    OffGridGraceSeconds = u.FixValue("offGridGrace"),
                    PreferredTargets = prefers.ToArray(),
                    IsSupplySource = u.Bool("supplySource"),
                    IsRelay = u.Bool("relay"),
                };
                if (_byId.ContainsKey(def.Id))
                {
                    throw new System.FormatException(u.Path + "：重复的单位 id " + def.Id);
                }
                _all.Add(def);
                _byId.Add(def.Id, def);
            }
        }
    }
}
