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
    /// connected node on both axes.
    /// </summary>
    public sealed class SupplyNetwork
    {
        private readonly List<SupplyNode> _nodes = new List<SupplyNode>();
        private readonly Queue<int> _queue = new Queue<int>();
        private static readonly Fix LinkRange = BoardLayout.CellSize * BoardLayout.LinkRangeCells;
        private static readonly Fix Coverage = BoardLayout.CellSize * BoardLayout.CoverageCells;

        public IReadOnlyList<SupplyNode> Nodes => _nodes;

        public void AddNode(Unit owner, FixVec2 position, bool isSource)
        {
            _nodes.Add(new SupplyNode { Owner = owner, Position = position, IsSource = isSource });
        }

        /// <summary>Computes links once; call after all nodes are added.</summary>
        public void BuildLinks()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    if (AreLinked(_nodes[i].Position, _nodes[j].Position))
                    {
                        _nodes[i].Links.Add(j);
                        _nodes[j].Links.Add(i);
                    }
                }
            }
            Rebuild();
        }

        public static bool AreLinked(FixVec2 a, FixVec2 b)
        {
            if (a.X == b.X)
            {
                return Fix.Abs(a.Y - b.Y) <= LinkRange;
            }
            if (a.Y == b.Y)
            {
                return Fix.Abs(a.X - b.X) <= LinkRange;
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
                    && Fix.Abs(position.X - node.Position.X) <= Coverage
                    && Fix.Abs(position.Y - node.Position.Y) <= Coverage)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Builds the network a formation would have before battle, for previews in the
        /// deployment screen. Owners are stand-in units that are always alive.
        /// </summary>
        public static SupplyNetwork ForFormation(Formation formation, int side)
        {
            var network = new SupplyNetwork();
            var standIn = new Unit { Hp = Fix.One };
            foreach (Placement p in formation.Placements)
            {
                if (p.Def.IsSupplySource)
                {
                    network.AddNode(standIn, BoardLayout.SourcePort(side, p.Depth, p.Row), true);
                }
                else if (p.Def.IsRelay)
                {
                    network.AddNode(standIn, BoardLayout.Intersection(side, p.Depth, p.Row), false);
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
                    if (IsCovered(BoardLayout.FootprintCenter(side, d, r, 1)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
