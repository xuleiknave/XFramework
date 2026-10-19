namespace XFramework.Battle
{
    /// <summary>
    /// Side-view board geometry. The contact line is x = 0; the player (side 0) deploys on
    /// x &lt; 0 and the enemy (side 1) on x &gt; 0. Each half is <see cref="Depth"/> cells deep
    /// (along x) and <see cref="Width"/> cells along the front line (along y).
    ///
    /// Cell coordinates are (depth, row): depth 0 is the front row next to the contact line,
    /// depth 5 the back; row 0..7 runs along the front. Grid lines are numbered the same way:
    /// depth lines 0..6 and row lines 0..8, so a 2x2 unit anchored at (d, r) spans lines d..d+2.
    /// </summary>
    public static class BoardLayout
    {
        public const int Depth = 6;
        public const int Width = 8;
        public const int PlayerSide = 0;
        public const int EnemySide = 1;

        public static readonly Fix CellSize = 2;

        /// <summary>Supply nodes connect along a grid line when at most this many cells apart.</summary>
        public const int LinkRangeCells = 3;
        /// <summary>Supply nodes cover cells within this many cells of their intersection.</summary>
        public const int CoverageCells = 2;

        /// <summary>Movement bounds: the board plus one cell of margin.</summary>
        public static readonly FixVec2 HalfExtents = new FixVec2(CellSize * (Depth + 1), CellSize * (Width / 2 + 1));

        /// <summary>Sign of the x axis pointing from the contact line into a side's half.</summary>
        public static int BackwardSign(int side) => side == PlayerSide ? -1 : 1;

        /// <summary>World position of a depth grid line (0 = contact line).</summary>
        public static Fix DepthLineX(int side, int depthLine) => CellSize * (BackwardSign(side) * depthLine);

        /// <summary>World position of a row grid line (0..Width).</summary>
        public static Fix RowLineY(int rowLine) => CellSize * (rowLine - Width / 2);

        public static FixVec2 Intersection(int side, int depthLine, int rowLine) =>
            new FixVec2(DepthLineX(side, depthLine), RowLineY(rowLine));

        /// <summary>Centre of a unit footprint anchored at cell (depth, row).</summary>
        public static FixVec2 FootprintCenter(int side, int depth, int row, int size)
        {
            int span = size <= 1 ? 1 : size;
            // Centre in "half cells" so 1x1 and 2x2 footprints both land exactly.
            int depthHalves = depth * 2 + span;
            int rowHalves = row * 2 + span;
            Fix x = CellSize * Fix.FromRatio(BackwardSign(side) * depthHalves, 2);
            Fix y = CellSize * Fix.FromRatio(rowHalves - Width, 2);
            return new FixVec2(x, y);
        }

        /// <summary>The supply port of a 2x2 source anchored at (depth, row): middle of its front edge.</summary>
        public static FixVec2 SourcePort(int side, int depth, int row) => Intersection(side, depth, row + 1);

        public static bool IsCellInside(int depth, int row) => depth >= 0 && depth < Depth && row >= 0 && row < Width;

        public static bool IsIntersectionInside(int depthLine, int rowLine) =>
            depthLine >= 0 && depthLine <= Depth && rowLine >= 0 && rowLine <= Width;
    }
}
