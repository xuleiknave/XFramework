using System.Text;

namespace Starline.Battle
{
    /// <summary>
    /// Which tile sits on each board cell, for both halves. Cells are addressed like
    /// formations: (side, depth, row). Empty cells hold no tile.
    /// </summary>
    public sealed class TerrainLayout
    {
        private const int Empty = -1;
        private readonly int[] _tiles = new int[2 * BoardLayout.Depth * BoardLayout.Width];

        public TerrainLayout()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i] = Empty;
            }
        }

        private static int IndexOf(int side, int depth, int row) => (side * BoardLayout.Depth + depth) * BoardLayout.Width + row;

        public TileDef TileAt(int side, int depth, int row)
        {
            if (!BoardLayout.IsCellInside(depth, row))
            {
                return null;
            }
            int index = _tiles[IndexOf(side, depth, row)];
            return index == Empty ? null : TerrainCatalog.Tiles[index];
        }

        public void Set(int side, int depth, int row, TileDef tile)
        {
            _tiles[IndexOf(side, depth, row)] = tile == null ? Empty : tile.Index;
        }

        /// <summary>The tile under a world position (null off the board or on an empty cell).</summary>
        public TileDef TileAtPosition(FixVec2 p)
        {
            int side = p.X < Fix.Zero ? BoardLayout.PlayerSide : BoardLayout.EnemySide;
            long cell = BoardLayout.CellSize.Raw;
            int depth = (int)(Fix.Abs(p.X).Raw / cell);
            Fix shifted = p.Y + BoardLayout.CellSize * (BoardLayout.Width / 2);
            if (shifted < Fix.Zero)
            {
                return null;
            }
            int row = (int)(shifted.Raw / cell);
            return TileAt(side, depth, row);
        }

        /// <summary>Tiles touching a grid intersection (up to four cells).</summary>
        public void ForEachTileAround(int side, int depthLine, int rowLine, System.Action<TileDef> action)
        {
            for (int d = depthLine - 1; d <= depthLine; d++)
            {
                for (int r = rowLine - 1; r <= rowLine; r++)
                {
                    TileDef tile = TileAt(side, d, r);
                    if (tile != null)
                    {
                        action(tile);
                    }
                }
            }
        }

        public int Count(int side, TileDef tile)
        {
            int count = 0;
            for (int d = 0; d < BoardLayout.Depth; d++)
            {
                for (int r = 0; r < BoardLayout.Width; r++)
                {
                    if (TileAt(side, d, r) == tile)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Reads one half from <see cref="BoardLayout.Depth"/> strings (depth 0 first), one
        /// character per row; "." is an empty cell, other characters are tile symbols.
        /// </summary>
        public void ParseHalf(int side, JsonValue lines)
        {
            if (lines.Count != BoardLayout.Depth)
            {
                throw new System.FormatException(lines.Path + "：需要 " + BoardLayout.Depth + " 行");
            }
            for (int d = 0; d < BoardLayout.Depth; d++)
            {
                string line = lines[d].AsString();
                if (line.Length != BoardLayout.Width)
                {
                    throw new System.FormatException(lines[d].Path + "：每行需要 " + BoardLayout.Width + " 个字符");
                }
                for (int r = 0; r < BoardLayout.Width; r++)
                {
                    char c = line[r];
                    TileDef tile = c == '.' ? null : TerrainCatalog.TileBySymbol(c)
                        ?? throw new System.FormatException(lines[d].Path + "：未知的地块符号 " + c);
                    Set(side, d, r, tile);
                }
            }
        }

        /// <summary>One half in the same text form <see cref="ParseHalf"/> reads, depth 0 first.</summary>
        public string Describe(int side)
        {
            var sb = new StringBuilder();
            for (int d = 0; d < BoardLayout.Depth; d++)
            {
                for (int r = 0; r < BoardLayout.Width; r++)
                {
                    TileDef tile = TileAt(side, d, r);
                    sb.Append(tile == null ? '.' : tile.Symbol);
                }
                if (d < BoardLayout.Depth - 1)
                {
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>Stable fingerprint, for replays and tests.</summary>
        public ulong ComputeHash()
        {
            ulong hash = 14695981039346656037UL;
            foreach (int t in _tiles)
            {
                hash ^= (ulong)(t + 1);
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    /// <summary>The terrain of one battle: the environment plus the tile layout.</summary>
    public sealed class BattleTerrain
    {
        public TerrainDef Def;
        public TerrainLayout Layout;

        /// <summary>The environment with a layout generated from <paramref name="layoutSeed"/>.</summary>
        public static BattleTerrain Generate(string terrainId, ulong layoutSeed)
        {
            TerrainDef def = TerrainCatalog.Get(terrainId);
            return new BattleTerrain { Def = def, Layout = TerrainGenerator.Generate(def, layoutSeed) };
        }

        public TileDef TileAt(int side, int depth, int row) => Layout?.TileAt(side, depth, row);

        public TileDef TileAtPosition(FixVec2 p) => Layout?.TileAtPosition(p);

        /// <summary>Can a unit's footprint use this cell?</summary>
        public bool IsDeployable(int side, int depth, int row)
        {
            TileDef tile = TileAt(side, depth, row);
            return tile == null || !tile.BlocksDeploy;
        }

        /// <summary>Can a crystal sit on this intersection?</summary>
        public bool AllowsRelay(int side, int depthLine, int rowLine)
        {
            bool allowed = true;
            Layout?.ForEachTileAround(side, depthLine, rowLine, tile =>
            {
                if (tile.NoNetwork)
                {
                    allowed = false;
                }
            });
            return allowed;
        }

        /// <summary>Link range and coverage (in cells) for a node on this intersection.</summary>
        public void NodeRanges(int side, int depthLine, int rowLine, out int linkCells, out int coverageCells)
        {
            int link = BoardLayout.LinkRangeCells + (Def?.LinkRangeBonus ?? 0);
            int coverage = BoardLayout.CoverageCells + (Def?.CoverageBonus ?? 0);
            int linkBonus = 0;
            int coverageBonus = 0;
            Layout?.ForEachTileAround(side, depthLine, rowLine, tile =>
            {
                linkBonus = System.Math.Max(linkBonus, tile.RelayLinkBonus);
                coverageBonus = System.Math.Max(coverageBonus, tile.RelayCoverageBonus);
            });
            linkCells = link + linkBonus;
            coverageCells = coverage + coverageBonus;
        }

        /// <summary>Does standing at this position cut a unit off the network?</summary>
        public bool CutsNetwork(FixVec2 position, UnitDef def)
        {
            TileDef tile = TileAtPosition(position);
            return tile != null && tile.NoNetwork && tile.AffectsUnit(def);
        }
    }

    /// <summary>
    /// The combined terrain modifiers for one unit on one tile. Multipliers are clamped so
    /// terrain seasons a battle without deciding it (GDD 5.5).
    /// </summary>
    public struct TerrainModifiers
    {
        public Fix Speed;
        /// <summary>Range change in battle units (whole cells).</summary>
        public Fix RangeBonus;
        public Fix Damage;
        public Fix DamageTaken;
        public Fix RangedDamageTaken;
        public Fix AttackInterval;
        public Fix HpRegen;
        public Fix ShieldRegen;
        public Fix ManaRegen;

        private static readonly Fix MinMultiplier = Fix.FromRatio(1, 2);
        private static readonly Fix MaxMultiplier = Fix.FromRatio(7, 5);
        private static readonly Fix MinSpeed = Fix.FromRatio(2, 5);
        private const int MaxRangeCells = 2;

        public static TerrainModifiers Identity => new TerrainModifiers
        {
            Speed = Fix.One,
            Damage = Fix.One,
            DamageTaken = Fix.One,
            RangedDamageTaken = Fix.One,
            AttackInterval = Fix.One,
            ShieldRegen = Fix.One,
            ManaRegen = Fix.One,
        };

        public static TerrainModifiers For(UnitDef def, BattleTerrain terrain, TileDef tile)
        {
            TerrainModifiers m = Identity;
            if (terrain?.Def != null)
            {
                foreach (TerrainEffect e in terrain.Def.Effects)
                {
                    if (e.AppliesTo(def))
                    {
                        m.Apply(e);
                    }
                }
            }
            if (tile != null && tile.AffectsUnit(def))
            {
                foreach (TerrainEffect e in tile.Effects)
                {
                    if (e.AppliesTo(def))
                    {
                        m.Apply(e);
                    }
                }
            }
            m.Clamp();
            return m;
        }

        private void Apply(TerrainEffect e)
        {
            switch (e.Stat)
            {
                case TerrainStat.Speed: Speed = Speed * e.Value; break;
                case TerrainStat.Range: RangeBonus = RangeBonus + e.Value * BoardLayout.CellSize; break;
                case TerrainStat.Damage: Damage = Damage * e.Value; break;
                case TerrainStat.DamageTaken: DamageTaken = DamageTaken * e.Value; break;
                case TerrainStat.RangedDamageTaken: RangedDamageTaken = RangedDamageTaken * e.Value; break;
                case TerrainStat.AttackInterval: AttackInterval = AttackInterval * e.Value; break;
                case TerrainStat.HpRegen: HpRegen = HpRegen + e.Value; break;
                case TerrainStat.ShieldRegen: ShieldRegen = ShieldRegen * e.Value; break;
                case TerrainStat.ManaRegen: ManaRegen = ManaRegen * e.Value; break;
            }
        }

        private void Clamp()
        {
            Speed = Fix.Clamp(Speed, MinSpeed, MaxMultiplier);
            Fix maxRange = BoardLayout.CellSize * MaxRangeCells;
            RangeBonus = Fix.Clamp(RangeBonus, -maxRange, maxRange);
            Damage = Fix.Clamp(Damage, MinMultiplier, MaxMultiplier);
            DamageTaken = Fix.Clamp(DamageTaken, MinMultiplier, MaxMultiplier);
            RangedDamageTaken = Fix.Clamp(RangedDamageTaken, MinMultiplier, MaxMultiplier);
            AttackInterval = Fix.Clamp(AttackInterval, MinMultiplier, MaxMultiplier);
            ShieldRegen = Fix.Clamp(ShieldRegen, Fix.Zero, MaxMultiplier);
            ManaRegen = Fix.Clamp(ManaRegen, MinMultiplier, MaxMultiplier);
        }

        /// <summary>Weapon range after terrain; never shorter than one cell (or the base range if that is shorter).</summary>
        public Fix ApplyRange(Fix baseRange) =>
            Fix.Max(baseRange + RangeBonus, Fix.Min(baseRange, BoardLayout.CellSize));
    }
}
