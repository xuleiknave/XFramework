using System.Collections.Generic;
using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Draws both halves of the board: cell tiles tinted by supply coverage, intersection
    /// markers for relay placement, supply links and nodes, and a placement ghost.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private static readonly Color PlayerCell = new Color(0.08f, 0.13f, 0.2f);
        private static readonly Color PlayerCovered = new Color(0.1f, 0.24f, 0.17f);
        private static readonly Color EnemyCell = new Color(0.18f, 0.11f, 0.07f);
        private static readonly Color EnemyCovered = new Color(0.24f, 0.15f, 0.08f);
        private static readonly Color GhostOk = new Color(0.4f, 1f, 0.5f);
        private static readonly Color GhostBad = new Color(1f, 0.3f, 0.3f);

        private readonly Renderer[,,] _cells = new Renderer[2, BoardLayout.Depth, BoardLayout.Width];
        private readonly List<GameObject> _intersections = new List<GameObject>();
        private readonly List<LineRenderer> _links = new List<LineRenderer>();
        private readonly List<GameObject> _nodeMarkers = new List<GameObject>();
        private int _linksUsed;
        private int _nodesUsed;
        private GameObject _ghost;
        private Renderer _ghostRenderer;

        public static BoardView Create(Transform parent)
        {
            var go = new GameObject("Board");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<BoardView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            float size = BoardLayout.CellSize.ToFloat() * 0.92f;
            for (int side = 0; side < 2; side++)
            {
                for (int d = 0; d < BoardLayout.Depth; d++)
                {
                    for (int r = 0; r < BoardLayout.Width; r++)
                    {
                        Vector3 pos = ViewUtil.ToWorld(BoardLayout.FootprintCenter(side, d, r, 1));
                        GameObject tile = ViewUtil.Primitive(PrimitiveType.Quad, transform, pos, new Vector3(size, size, 1f), PlayerCell);
                        tile.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        _cells[side, d, r] = tile.GetComponent<Renderer>();
                    }
                }
            }

            // Contact line.
            LineRenderer contact = ViewUtil.Line(transform, "Contact line");
            float half = BoardLayout.CellSize.ToFloat() * BoardLayout.Width / 2f;
            ViewUtil.SetLine(contact, new Vector3(0f, 0.02f, -half - 1f), new Vector3(0f, 0.02f, half + 1f), new Color(0.5f, 0.55f, 0.65f), 0.06f);

            // Player intersections, shown while placing relays.
            for (int d = 0; d <= BoardLayout.Depth; d++)
            {
                for (int r = 0; r <= BoardLayout.Width; r++)
                {
                    Vector3 pos = ViewUtil.ToWorld(BoardLayout.Intersection(BoardLayout.PlayerSide, d, r)) + Vector3.up * 0.05f;
                    GameObject dot = ViewUtil.Primitive(PrimitiveType.Sphere, transform, pos, Vector3.one * 0.22f, new Color(0.45f, 0.75f, 0.55f));
                    dot.SetActive(false);
                    _intersections.Add(dot);
                }
            }

            _ghost = ViewUtil.Primitive(PrimitiveType.Cube, transform, Vector3.zero, Vector3.one, GhostOk);
            _ghostRenderer = _ghost.GetComponent<Renderer>();
            _ghost.SetActive(false);
        }

        public void ShowIntersections(bool show)
        {
            foreach (GameObject dot in _intersections)
            {
                dot.SetActive(show);
            }
        }

        /// <summary>Tints both halves by which cells a network currently covers.</summary>
        public void TintCoverage(SupplyNetwork player, SupplyNetwork enemy)
        {
            for (int side = 0; side < 2; side++)
            {
                SupplyNetwork net = side == BoardLayout.PlayerSide ? player : enemy;
                for (int d = 0; d < BoardLayout.Depth; d++)
                {
                    for (int r = 0; r < BoardLayout.Width; r++)
                    {
                        bool covered = net != null && net.IsCovered(BoardLayout.FootprintCenter(side, d, r, 1));
                        Color c = side == BoardLayout.PlayerSide
                            ? (covered ? PlayerCovered : PlayerCell)
                            : (covered ? EnemyCovered : EnemyCell);
                        _cells[side, d, r].material.color = c;
                    }
                }
            }
        }

        /// <summary>Starts a new frame of supply drawing.</summary>
        public void BeginNetworks()
        {
            _linksUsed = 0;
            _nodesUsed = 0;
        }

        public void DrawNetwork(SupplyNetwork network, int side)
        {
            Color live = side == BoardLayout.PlayerSide ? ViewUtil.SupplyColor : ViewUtil.EnemyColor;
            IReadOnlyList<SupplyNode> nodes = network.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                SupplyNode a = nodes[i];
                foreach (int j in a.Links)
                {
                    if (j < i)
                    {
                        continue;
                    }
                    SupplyNode b = nodes[j];
                    bool ok = a.IsAlive && b.IsAlive && a.Connected && b.Connected;
                    LineRenderer line = NextLink();
                    ViewUtil.SetLine(line,
                        ViewUtil.ToWorld(a.Position) + Vector3.up * 0.08f,
                        ViewUtil.ToWorld(b.Position) + Vector3.up * 0.08f,
                        ok ? live : ViewUtil.CutColor, ok ? 0.22f : 0.12f);
                }

                GameObject marker = NextNode();
                marker.transform.position = ViewUtil.ToWorld(a.Position) + Vector3.up * 0.15f;
                marker.GetComponent<Renderer>().material.color = !a.IsAlive ? new Color(0.25f, 0.25f, 0.25f)
                    : a.Connected ? live : ViewUtil.CutColor;
            }
        }

        public void EndNetworks()
        {
            for (int i = _linksUsed; i < _links.Count; i++)
            {
                _links[i].gameObject.SetActive(false);
            }
            for (int i = _nodesUsed; i < _nodeMarkers.Count; i++)
            {
                _nodeMarkers[i].SetActive(false);
            }
        }

        /// <summary>Shows a box over the cells (or intersection) a placement would use.</summary>
        public void ShowGhost(UnitDef def, int depth, int row, bool valid)
        {
            _ghost.SetActive(true);
            _ghostRenderer.material.color = valid ? GhostOk : GhostBad;
            float cell = BoardLayout.CellSize.ToFloat();
            if (def.IsRelay)
            {
                _ghost.transform.position = ViewUtil.ToWorld(BoardLayout.Intersection(BoardLayout.PlayerSide, depth, row)) + Vector3.up * 0.3f;
                _ghost.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                _ghost.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            }
            else
            {
                int span = def.Size <= 1 ? 1 : def.Size;
                _ghost.transform.position = ViewUtil.ToWorld(BoardLayout.FootprintCenter(BoardLayout.PlayerSide, depth, row, def.Size)) + Vector3.up * 0.1f;
                _ghost.transform.localScale = new Vector3(cell * span * 0.9f, 0.15f, cell * span * 0.9f);
                _ghost.transform.rotation = Quaternion.identity;
            }
        }

        public void HideGhost() => _ghost.SetActive(false);

        private LineRenderer NextLink()
        {
            if (_linksUsed == _links.Count)
            {
                _links.Add(ViewUtil.Line(transform, "Supply link"));
            }
            LineRenderer line = _links[_linksUsed++];
            line.gameObject.SetActive(true);
            return line;
        }

        private GameObject NextNode()
        {
            if (_nodesUsed == _nodeMarkers.Count)
            {
                GameObject marker = ViewUtil.Primitive(PrimitiveType.Cube, transform, Vector3.zero, Vector3.one * 0.45f, ViewUtil.SupplyColor);
                marker.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                _nodeMarkers.Add(marker);
            }
            GameObject node = _nodeMarkers[_nodesUsed++];
            node.SetActive(true);
            return node;
        }
    }
}
