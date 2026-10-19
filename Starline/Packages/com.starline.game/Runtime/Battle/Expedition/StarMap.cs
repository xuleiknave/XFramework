using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    public enum NodeType
    {
        Battle,
        Elite,
        /// <summary>Supply station: repair one fleet durability or pick up a relay beacon.</summary>
        Supply,
        Boss,
    }

    public sealed class MapNode
    {
        public int Id;
        public int Layer;
        /// <summary>Position within the layer, 0..LayerSize-1 (top to bottom on the map).</summary>
        public int Index;
        public int LayerSize;
        public NodeType Type;
        /// <summary>Encounter for battle nodes (see <see cref="EncounterLibrary"/>).</summary>
        public string EncounterKey;
        /// <summary>Ids of nodes in the next layer this node leads to.</summary>
        public readonly List<int> Next = new List<int>();
        public bool Visited;

        public bool IsBattle => Type != NodeType.Supply;
    }

    /// <summary>
    /// One act's star map (GDD 4.2): layers of nodes from left to right ending at the boss,
    /// each node linking to one or two nodes in the next layer, so every step offers 2-3 routes.
    /// </summary>
    public sealed class StarMap
    {
        public readonly List<List<MapNode>> Layers = new List<List<MapNode>>();
        public readonly List<MapNode> Nodes = new List<MapNode>();

        public MapNode Get(int id) => Nodes[id];

        public MapNode Boss => Layers[Layers.Count - 1][0];

        /// <summary>Generates an act's map from its layer plan (expedition.json); the last layer is the boss.</summary>
        public static StarMap Generate(ActConfig act, ulong seed)
        {
            var random = new DeterministicRandom(seed);
            var map = new StarMap();
            for (int layer = 0; layer < act.Layers.Count; layer++)
            {
                NodeType[] types = (NodeType[])act.Layers[layer].Clone();
                Shuffle(types, random);
                var nodes = new List<MapNode>();
                for (int i = 0; i < types.Length; i++)
                {
                    var node = new MapNode
                    {
                        Id = map.Nodes.Count,
                        Layer = layer,
                        Index = i,
                        LayerSize = types.Length,
                        Type = types[i],
                    };
                    node.EncounterKey = PickEncounter(act, node, random);
                    nodes.Add(node);
                    map.Nodes.Add(node);
                }
                map.Layers.Add(nodes);
            }
            for (int layer = 0; layer + 1 < map.Layers.Count; layer++)
            {
                Connect(map.Layers[layer], map.Layers[layer + 1]);
            }
            return map;
        }

        public static StarMap GenerateAct1(ulong seed) => Generate(ExpeditionConfig.Current.Acts[0], seed);

        private static string PickEncounter(ActConfig act, MapNode node, DeterministicRandom random)
        {
            string[] pool;
            switch (node.Type)
            {
                case NodeType.Boss:
                    return act.Boss;
                case NodeType.Elite:
                    pool = act.ElitePool(node.Layer);
                    break;
                case NodeType.Battle:
                    pool = act.NormalPool(node.Layer);
                    break;
                default:
                    return null;
            }
            return pool[random.Range(0, pool.Length)];
        }

        /// <summary>
        /// Links each node to the nearest node(s) of the next layer by relative height, then
        /// makes sure every next-layer node can be reached.
        /// </summary>
        private static void Connect(List<MapNode> from, List<MapNode> to)
        {
            foreach (MapNode a in from)
            {
                // Relative heights in sixths so 2- and 3-node layers line up exactly.
                int ya = (2 * a.Index + 1) * 3 / from.Count;
                foreach (MapNode b in to)
                {
                    int yb = (2 * b.Index + 1) * 3 / to.Count;
                    if (Abs(ya - yb) <= 2)
                    {
                        a.Next.Add(b.Id);
                    }
                }
            }
            foreach (MapNode b in to)
            {
                bool reached = from.Exists(a => a.Next.Contains(b.Id));
                if (!reached)
                {
                    MapNode nearest = from[b.Index * from.Count / to.Count];
                    nearest.Next.Add(b.Id);
                }
            }
        }

        private static void Shuffle(NodeType[] items, DeterministicRandom random)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = random.Range(0, i + 1);
                NodeType tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        private static int Abs(int v) => v < 0 ? -v : v;
    }
}
