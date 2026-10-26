using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>A supply node: the source's port or a relay, with the unit that carries it.</summary>
    public sealed class SupplyNode
    {
        public FixVec2 Position;
        /// <summary>The unit whose survival keeps this node alive (supply ship or relay).</summary>
        public Unit Owner;
        public bool IsSource;
        /// <summary>Link range and coverage in battle units (terrain can extend them).</summary>
        public Fix LinkRange;
        public Fix Coverage;
        /// <summary>Set by <see cref="SupplyNetwork.Rebuild"/>: true when linked back to the source.</summary>
        public bool Connected;
        /// <summary>Indices of linked nodes (only meaningful while both are alive).</summary>
        public readonly List<int> Links = new List<int>();

        public bool IsAlive => Owner != null && Owner.IsAlive;
    }

    /// <summary>
    /// One side's supply network (GDD 16.3). Nodes sit on grid intersections; two nodes on
    /// the same grid line within <see cref="BoardLayout.LinkRangeCells"/> cells are linked.
    /// A node is connected when a chain of living links reaches the source. A position is
    /// covered when it lies within <see cref="BoardLayout.CoverageCells"/> cells of a
    /// connected node on both axes. Terrain (GDD 5.5) can extend a node's link range and
    /// coverage, and some tiles cut the units standing on them off the network.
    /// </summary>
    public sealed class SupplyNetwork
    {
        private readonly List<SupplyNode> _nodes = new List<SupplyNode>();
        private readonly Queue<int> _queue = new Queue<int>();
        private static readonly Fix LinkRange = BoardLayout.CellSize * BoardLayout.LinkRangeCells;
        private static readonly Fix Coverage = BoardLayout.CellSize * BoardLayout.CoverageCells;

        /// <summary>Terrain of the battle, or null for plain ground.</summary>
        public BattleTerrain Terrain;

        public IReadOnlyList<SupplyNode> Nodes => _nodes;

        public void AddNode(Unit owner, FixVec2 position, bool isSource)
        {
            _nodes.Add(new SupplyNode { Owner = owner, Position = position, IsSource = isSource, LinkRange = LinkRange, Coverage = Coverage });
        }

        /// <summary>Adds a node on intersection (depthLine, rowLine) with the ranges the terrain gives it.</summary>
        public void AddNode(Unit owner, int side, int depthLine, int rowLine, bool isSource)
        {
            int linkCells = BoardLayout.LinkRangeCells;
            int coverageCells = BoardLayout.CoverageCells;
            Terrain?.NodeRanges(side, depthLine, rowLine, out linkCells, out coverageCells);
            _nodes.Add(new SupplyNode
            {
                Owner = owner,
                Position = BoardLayout.Intersection(side, depthLine, rowLine),
                IsSource = isSource,
                LinkRange = BoardLayout.CellSize * linkCells,
                Coverage = BoardLayout.CellSize * coverageCells,
            });
        }

        /// <summary>Computes links once; call after all nodes are added.</summary>
        public void BuildLinks()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    if (AreLinked(_nodes[i], _nodes[j]))
                    {
                        _nodes[i].Links.Add(j);
                        _nodes[j].Links.Add(i);
                    }
                }
            }
            Rebuild();
        }

        /// <summary>Linked when on the same grid line within the longer of the two link ranges.</summary>
        public static bool AreLinked(SupplyNode a, SupplyNode b) =>
            AreLinked(a.Position, b.Position, Fix.Max(a.LinkRange, b.LinkRange));

        public static bool AreLinked(FixVec2 a, FixVec2 b) => AreLinked(a, b, LinkRange);

        private static bool AreLinked(FixVec2 a, FixVec2 b, Fix range)
        {
            if (a.X == b.X)
            {
                return Fix.Abs(a.Y - b.Y) <= range;
            }
            if (a.Y == b.Y)
            {
                return Fix.Abs(a.X - b.X) <= range;
            }
            return false;
        }

        /// <summary>Recomputes which nodes are connected; call when a node dies.</summary>
        public void Rebuild()
        {
            foreach (SupplyNode node in _nodes)
            {
                node.Connected = false;
            }
            _queue.Clear();
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].IsSource && _nodes[i].IsAlive)
                {
                    _nodes[i].Connected = true;
                    _queue.Enqueue(i);
                }
            }
            while (_queue.Count > 0)
            {
                SupplyNode node = _nodes[_queue.Dequeue()];
                foreach (int next in node.Links)
                {
                    SupplyNode other = _nodes[next];
                    if (!other.Connected && other.IsAlive)
                    {
                        other.Connected = true;
                        _queue.Enqueue(next);
                    }
                }
            }
        }

        public bool IsCovered(FixVec2 position)
        {
            foreach (SupplyNode node in _nodes)
            {
                if (node.Connected && node.IsAlive
                    && Fix.Abs(position.X - node.Position.X) <= node.Coverage
                    && Fix.Abs(position.Y - node.Position.Y) <= node.Coverage)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Covered, and not standing on a tile that cuts this unit off the network.</summary>
        public bool IsCoveredFor(FixVec2 position, UnitDef def) =>
            IsCovered(position) && (Terrain == null || !Terrain.CutsNetwork(position, def));

        /// <summary>
        /// Builds the network a formation would have before battle, for previews in the
        /// deployment screen. Owners are stand-in units that are always alive.
        /// </summary>
        public static SupplyNetwork ForFormation(Formation formation, int side, BattleTerrain terrain = null)
        {
            var network = new SupplyNetwork { Terrain = terrain };
            var standIn = new Unit { Hp = Fix.One };
            foreach (Placement p in formation.Placements)
            {
                if (p.Def.IsSupplySource)
                {
                    // The port is the middle of the source's front edge (BoardLayout.SourcePort).
                    network.AddNode(standIn, side, p.Depth, p.Row + 1, true);
                }
                else if (p.Def.IsRelay)
                {
                    network.AddNode(standIn, side, p.Depth, p.Row, false);
                }
            }
            network.BuildLinks();
            return network;
        }

        /// <summary>Whether a placed unit would start the battle connected to supply.</summary>
        public bool CoversPlacement(Placement p, int side)
        {
            int span = p.Def.Size <= 1 ? 1 : p.Def.Size;
            for (int d = p.Depth; d < p.Depth + span; d++)
            {
                for (int r = p.Row; r < p.Row + span; r++)
                {
                    if (IsCoveredFor(BoardLayout.FootprintCenter(side, d, r, 1), p.Def))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
