using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>
    /// Generates a tile layout from a terrain's palette (GDD 5.5). Deterministic: the same
    /// terrain and seed always give the same layout, so a level plays the same every visit
    /// and replays need only the seed. Rules:
    /// - both halves get the same number of each tile (fair), placed independently;
    /// - tiles grow in clusters (rivers, slopes) rather than scattered cells;
    /// - the source area at the back centre stays clear;
    /// - at most <see cref="MaxBlockedPerSide"/> undeployable cells per half, the front row keeps
    ///   at least <see cref="MinOpenFrontCells"/> open cells and a 2x2 space stays open in the front four rows.
    /// </summary>
    public static class TerrainGenerator
    {
        public const int MaxBlockedPerSide = 6;
        public const int MinOpenFrontCells = 4;
        private const ulong SeedSalt = 0x5445525241494E00UL; // "TERRAIN"
        private const int MaxAttempts = 200;
        private static readonly int[] StepDepth = { 1, -1, 0, 0 };
        private static readonly int[] StepRow = { 0, 0, 1, -1 };

        /// <summary>Back centre where the source tower and its port usually stand.</summary>
        public static bool IsReserved(int depth, int row) => depth >= 4 && row >= 2 && row <= 5;

        public static TerrainLayout Generate(TerrainDef def, ulong seed)
        {
            var random = new DeterministicRandom(seed ^ SeedSalt);
            var layout = new TerrainLayout();
            foreach (TilePaletteEntry entry in def.Palette)
            {
                int count = random.Range(entry.Min, entry.Max + 1);
                List<int> player = PlaceCluster(layout, BoardLayout.PlayerSide, entry.Tile, count, random);
                List<int> enemy = PlaceCluster(layout, BoardLayout.EnemySide, entry.Tile, count, random);
                // If one half ran out of room, trim the other so both stay equal.
                Trim(layout, BoardLayout.PlayerSide, player, enemy.Count);
                Trim(layout, BoardLayout.EnemySide, enemy, player.Count);
            }
            return layout;
        }

        private static void Trim(TerrainLayout layout, int side, List<int> placed, int count)
        {
            while (placed.Count > count)
            {
                int cell = placed[placed.Count - 1];
                layout.Set(side, cell / BoardLayout.Width, cell % BoardLayout.Width, null);
                placed.RemoveAt(placed.Count - 1);
            }
        }

        private static List<int> PlaceCluster(TerrainLayout layout, int side, TileDef tile, int count, DeterministicRandom random)
        {
            var placed = new List<int>();
            int attempts = 0;
            while (placed.Count < count && attempts < MaxAttempts)
            {
                attempts++;
                int depth, row;
                if (placed.Count == 0 || random.Range(0, 4) == 0)
                {
                    // Start a new cluster (or occasionally a second one).
                    depth = random.Range(0, BoardLayout.Depth);
                    row = random.Range(0, BoardLayout.Width);
                }
                else
                {
                    int from = placed[random.Range(0, placed.Count)];
                    int dir = random.Range(0, 4);
                    depth = from / BoardLayout.Width + StepDepth[dir];
                    row = from % BoardLayout.Width + StepRow[dir];
                }
                if (CanUse(layout, side, depth, row, tile))
                {
                    layout.Set(side, depth, row, tile);
                    placed.Add(depth * BoardLayout.Width + row);
                }
            }
            return placed;
        }

        private static bool CanUse(TerrainLayout layout, int side, int depth, int row, TileDef tile)
        {
            if (!BoardLayout.IsCellInside(depth, row) || IsReserved(depth, row) || layout.TileAt(side, depth, row) != null)
            {
                return false;
            }
            if (!tile.BlocksDeploy)
            {
                return true;
            }
            layout.Set(side, depth, row, tile);
            bool ok = BlockedCount(layout, side) <= MaxBlockedPerSide
                && OpenFrontCells(layout, side) >= MinOpenFrontCells
                && HasOpenSquare(layout, side);
            layout.Set(side, depth, row, null);
            return ok;
        }

        private static bool Blocked(TerrainLayout layout, int side, int depth, int row)
        {
            TileDef tile = layout.TileAt(side, depth, row);
            return tile != null && tile.BlocksDeploy;
        }

        public static int BlockedCount(TerrainLayout layout, int side)
        {
            int count = 0;
            for (int d = 0; d < BoardLayout.Depth; d++)
            {
                for (int r = 0; r < BoardLayout.Width; r++)
                {
                    if (Blocked(layout, side, d, r))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public static int OpenFrontCells(TerrainLayout layout, int side)
        {
            int count = 0;
            for (int r = 0; r < BoardLayout.Width; r++)
            {
                if (!Blocked(layout, side, 0, r))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Is there an open 2x2 space for a large unit in the front four rows?</summary>
        public static bool HasOpenSquare(TerrainLayout layout, int side)
        {
            for (int d = 0; d < 3; d++)
            {
                for (int r = 0; r < BoardLayout.Width - 1; r++)
                {
                    if (!Blocked(layout, side, d, r) && !Blocked(layout, side, d + 1, r)
                        && !Blocked(layout, side, d, r + 1) && !Blocked(layout, side, d + 1, r + 1))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
