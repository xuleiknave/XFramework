using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>What a terrain or tile effect changes (GDD 5.5).</summary>
    public enum TerrainStat
    {
        /// <summary>Movement speed multiplier.</summary>
        Speed,
        /// <summary>Weapon range change in whole cells.</summary>
        Range,
        /// <summary>Outgoing weapon damage multiplier.</summary>
        Damage,
        /// <summary>Incoming weapon damage multiplier.</summary>
        DamageTaken,
        /// <summary>Incoming damage multiplier for hits from ranged roles (Artillery, PointDefense).</summary>
        RangedDamageTaken,
        /// <summary>Time between attacks multiplier (above 1 attacks slower).</summary>
        AttackInterval,
        /// <summary>Fraction of maximum HP restored per second.</summary>
        HpRegen,
        /// <summary>Shield regeneration multiplier.</summary>
        ShieldRegen,
        /// <summary>Mana (ammunition) refill speed multiplier while connected.</summary>
        ManaRegen,
    }

    /// <summary>One modifier with the units it applies to. Empty filters match everyone.</summary>
    public sealed class TerrainEffect
    {
        public TerrainStat Stat;
        /// <summary>A multiplier, a number of cells (<see cref="TerrainStat.Range"/>) or a fraction per second (<see cref="TerrainStat.HpRegen"/>).</summary>
        public Fix Value;
        public Faction[] Factions = new Faction[0];
        public Faction[] ExceptFactions = new Faction[0];
        public UnitRole[] Roles = new UnitRole[0];
        /// <summary>Skips flying units (role Craft).</summary>
        public bool GroundOnly;
        /// <summary>Short description for the deployment screen (Game table: tile.&lt;id&gt;.effect&lt;n&gt; or terrain.&lt;id&gt;.effect&lt;n&gt;).</summary>
        public LocText Text;

        /// <summary>Structures are only affected when the effect names the Structure role.</summary>
        public bool AppliesTo(UnitDef def)
        {
            if (def.IsStructure && System.Array.IndexOf(Roles, UnitRole.Structure) < 0)
            {
                return false;
            }
            if (GroundOnly && def.IsCraft)
            {
                return false;
            }
            if (Roles.Length > 0 && System.Array.IndexOf(Roles, def.Role) < 0)
            {
                return false;
            }
            if (Factions.Length > 0 && System.Array.IndexOf(Factions, def.Faction) < 0)
            {
                return false;
            }
            return System.Array.IndexOf(ExceptFactions, def.Faction) < 0;
        }
    }

    /// <summary>A special board cell: high ground, shallows, quicksand, ...</summary>
    public sealed class TileDef
    {
        /// <summary>Position in <see cref="TerrainCatalog.Tiles"/>; layouts store this.</summary>
        public int Index;
        public string Id;
        /// <summary>Name and description (Game table: tile.&lt;id&gt;.name / .description).</summary>
        public LocText Name => new LocText("tile." + Id + ".name");
        public LocText Description => new LocText("tile." + Id + ".description");
        /// <summary>Character used in hand-written layouts.</summary>
        public char Symbol;
        public readonly List<TerrainEffect> Effects = new List<TerrainEffect>();
        /// <summary>Factions that ignore this tile completely. Flying units always ignore tiles.</summary>
        public Faction[] ExemptFactions = new Faction[0];
        /// <summary>Units cannot be deployed on it (deep water, crevasses).</summary>
        public bool BlocksDeploy;
        /// <summary>Units standing on it are never connected; no crystal may sit on its corners.</summary>
        public bool NoNetwork;
        /// <summary>Extra coverage cells for a crystal on one of its corners.</summary>
        public int RelayCoverageBonus;
        /// <summary>Extra link cells for a crystal on one of its corners.</summary>
        public int RelayLinkBonus;

        public bool AffectsUnit(UnitDef def) =>
            !def.IsCraft && !def.IsStructure && System.Array.IndexOf(ExemptFactions, def.Faction) < 0;
    }

    /// <summary>How many tiles of one kind the generator places on each half.</summary>
    public sealed class TilePaletteEntry
    {
        public TileDef Tile;
        public int Min;
        public int Max;
    }

    /// <summary>A battlefield environment: one per chapter (GDD 5.5).</summary>
    public sealed class TerrainDef
    {
        public string Id;
        /// <summary>Name and description (Game table: terrain.&lt;id&gt;.name / .description).</summary>
        public LocText Name => new LocText("terrain." + Id + ".name");
        public LocText Description => new LocText("terrain." + Id + ".description");
        /// <summary>Applies to both sides for the whole battle.</summary>
        public readonly List<TerrainEffect> Effects = new List<TerrainEffect>();
        /// <summary>Added to every node's link range (cells).</summary>
        public int LinkRangeBonus;
        /// <summary>Added to every node's coverage (cells).</summary>
        public int CoverageBonus;
        /// <summary>Tiles the generator may place, in placement order.</summary>
        public readonly List<TilePaletteEntry> Palette = new List<TilePaletteEntry>();
    }

    /// <summary>Terrains and tiles from terrains.json (see <see cref="GameData"/>).</summary>
    public static class TerrainCatalog
    {
        private static readonly List<TileDef> _tiles = new List<TileDef>();
        private static readonly List<TerrainDef> _terrains = new List<TerrainDef>();

        public static IReadOnlyList<TileDef> Tiles => _tiles;
        public static IReadOnlyList<TerrainDef> Terrains => _terrains;

        public static TerrainDef Get(string id)
        {
            foreach (TerrainDef t in _terrains)
            {
                if (t.Id == id)
                {
                    return t;
                }
            }
            throw new KeyNotFoundException("Unknown terrain: " + id);
        }

        public static TileDef GetTile(string id)
        {
            foreach (TileDef t in _tiles)
            {
                if (t.Id == id)
                {
                    return t;
                }
            }
            throw new KeyNotFoundException("Unknown tile: " + id);
        }

        public static TileDef TileBySymbol(char symbol)
        {
            foreach (TileDef t in _tiles)
            {
                if (t.Symbol == symbol)
                {
                    return t;
                }
            }
            return null;
        }

        internal static void Load(JsonValue root)
        {
            _tiles.Clear();
            _terrains.Clear();
            foreach (JsonValue t in root["tiles"].Elements)
            {
                string symbol = t["symbol"].AsString();
                if (symbol.Length != 1 || symbol == ".")
                {
                    throw new System.FormatException(t.Path + "：symbol 必须是一个字符且不能是 .");
                }
                var tile = new TileDef
                {
                    Index = _tiles.Count,
                    Id = t["id"].AsString(),
                    Symbol = symbol[0],
                    ExemptFactions = ReadEnums<Faction>(t.Get("exempt")),
                    BlocksDeploy = t.Bool("blocksDeploy"),
                    NoNetwork = t.Bool("noNetwork"),
                    RelayCoverageBonus = t.Int("relayCoverage"),
                    RelayLinkBonus = t.Int("relayLink"),
                };
                ReadEffects(t.Get("effects"), tile.Effects, "tile." + tile.Id);
                if (TileBySymbol(tile.Symbol) != null)
                {
                    throw new System.FormatException(t.Path + "：重复的地块符号 " + symbol);
                }
                _tiles.Add(tile);
            }

            foreach (JsonValue t in root["terrains"].Elements)
            {
                var terrain = new TerrainDef
                {
                    Id = t["id"].AsString(),
                    LinkRangeBonus = t.Int("linkRange"),
                    CoverageBonus = t.Int("coverage"),
                };
                ReadEffects(t.Get("effects"), terrain.Effects, "terrain." + terrain.Id);
                if (t.Get("tiles") is JsonValue palette)
                {
                    foreach (JsonValue entry in palette.Elements)
                    {
                        terrain.Palette.Add(new TilePaletteEntry
                        {
                            Tile = GetTile(entry[0].AsString()),
                            Min = entry[1].AsInt(),
                            Max = entry[2].AsInt(),
                        });
                    }
                }
                _terrains.Add(terrain);
            }
        }

        /// <summary>Effect n (from 1) gets the text key &lt;textPrefix&gt;.effect&lt;n&gt;.</summary>
        private static void ReadEffects(JsonValue list, List<TerrainEffect> into, string textPrefix)
        {
            if (list == null)
            {
                return;
            }
            foreach (JsonValue e in list.Elements)
            {
                into.Add(new TerrainEffect
                {
                    Stat = GameData.ParseEnum<TerrainStat>(e["stat"]),
                    Value = e["value"].AsFix(),
                    Factions = ReadEnums<Faction>(e.Get("factions")),
                    ExceptFactions = ReadEnums<Faction>(e.Get("exceptFactions")),
                    Roles = ReadEnums<UnitRole>(e.Get("roles")),
                    GroundOnly = e.Bool("ground"),
                    Text = new LocText(textPrefix + ".effect" + (into.Count + 1)),
                });
            }
        }

        private static T[] ReadEnums<T>(JsonValue list) where T : struct
        {
            if (list == null)
            {
                return new T[0];
            }
            var result = new T[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                result[i] = GameData.ParseEnum<T>(list[i]);
            }
            return result;
        }
    }
}
