using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>
    /// The act's star map: fleet status on the left, nodes and routes on the right. Also
    /// hosts the act intro, the supply station choice and the abandon-run confirmation.
    /// </summary>
    public sealed class MapScreen : UiScreen
    {
        private readonly ExpeditionRun _run;
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _briefings = new Dictionary<int, string>();
        private readonly bool _showIntro;
        private VisualElement _infoBox;
        private Label _infoTitle, _infoBody, _infoReward;

        public MapScreen(GameApp app, bool showIntro) : base(app, "Map")
        {
            _run = app.Run;
            _showIntro = showIntro;
            foreach (MapNode node in _run.Map.Nodes)
            {
                if (node.IsBattle)
                {
                    Encounter e = EncounterLibrary.Create(node.EncounterKey);
                    _names[node.Id] = Loc.T(e.Name);
                    _briefings[node.Id] = Loc.T(e.Briefing);
                }
                else
                {
                    _names[node.Id] = Loc.Ui("map.supply");
                    _briefings[node.Id] = Loc.Ui("map.supply_briefing");
                }
            }

            Find<Label>("act-title").text = Loc.Ui("map.title", Loc.T(_run.Act.Title));
            _infoBox = Find<VisualElement>("node-info");
            _infoTitle = Find<Label>("node-title");
            _infoBody = Find<Label>("node-briefing");
            _infoReward = Find<Label>("node-reward");
            Button("abandon", ConfirmAbandon);

            var map = new StarMapView(_run, NodeLabel);
            map.NodeClicked += OnNodeClicked;
            map.NodeHovered += ShowNodeInfo;
            Find<VisualElement>("map-canvas").Add(map);
        }

        public override void Enter()
        {
            RefreshSidebar();
            ShowNodeInfo(null);
            if (_showIntro)
            {
                ActStory story = _run.Act.Story;
                App.Ui.ShowModal(Ui.Dialog(Loc.T(story.IntroTitle), Loc.T(story.Intro), (Loc.Ui("map.depart"), App.Ui.CloseModals)));
            }
        }

        private void RefreshSidebar()
        {
            int max = ExpeditionRun.MaxDurability;
            Label durability = Find<Label>("durability");
            durability.text = Loc.Ui("map.durability", Ui.Hearts(_run.Durability, max), _run.Durability, max);
            durability.EnableInClassList("bad", _run.Durability <= 2);
            durability.EnableInClassList("good", _run.Durability > 2);
            Find<Label>("command").text = Loc.Ui("map.command", _run.CommandLimit, ExpeditionRun.CommandLimitPerBattle);
            Find<Label>("relays").text = Loc.Ui("map.relays", _run.Fleet.Relays, ExpeditionRun.MaxRelays);
            Find<Label>("record").text = Loc.Ui("map.record", _run.BattlesWon, _run.BattlesFought);

            VisualElement fleet = Find<VisualElement>("fleet-list");
            fleet.Clear();
            var counts = new SortedDictionary<string, int>();
            foreach (OwnedShip ship in _run.Fleet.Ships)
            {
                string key = Loc.T(ship.Def.Name) + " " + Ui.Stars(ship.Star);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            foreach (KeyValuePair<string, int> kv in counts)
            {
                fleet.Add(Ui.Label("· " + kv.Key + " × " + kv.Value, "body"));
            }

            VisualElement messages = Find<VisualElement>("messages");
            messages.Clear();
            foreach (LocText message in _run.Messages)
            {
                messages.Add(Ui.Label(Loc.T(message), "warn", "small"));
            }
        }

        private void ShowNodeInfo(MapNode node)
        {
            if (node == null)
            {
                _infoTitle.text = Loc.Ui("map.legend");
                _infoBody.text = Loc.Ui("map.legend_body");
                _infoReward.text = "";
                return;
            }
            _infoTitle.text = TypeName(node.Type) + " · " + _names[node.Id];
            _infoBody.text = _briefings[node.Id];
            _infoReward.text = node.Type == NodeType.Elite ? Loc.Ui("map.reward_elite")
                : node.Type == NodeType.Boss ? Loc.Ui("map.reward_boss", ExpeditionConfig.Current.BossLoss)
                : node.Type == NodeType.Supply ? ""
                : Loc.Ui("map.reward_battle", ExpeditionConfig.Current.NormalLoss);
        }

        private void OnNodeClicked(MapNode node)
        {
            if (App.Ui.HasModal)
            {
                return;
            }
            App.EnterNode(node);
            if (_run.State == RunState.Supply)
            {
                ShowSupplyDialog();
            }
        }

        private void ShowSupplyDialog()
        {
            VisualElement dialog = Ui.Dialog(Loc.Ui("map.supply"), Loc.Ui("map.supply_dialog"));
            VisualElement buttons = dialog.Q(className: "dialog-buttons");
            Button repair = Ui.Button(Loc.Ui("map.supply_repair", _run.Durability, ExpeditionRun.MaxDurability),
                () => ChooseSupply(true), "btn-primary");
            repair.SetEnabled(_run.Durability < ExpeditionRun.MaxDurability);
            Button relay = Ui.Button(Loc.Ui("map.supply_relay", _run.Fleet.Relays), () => ChooseSupply(false), "btn-secondary");
            relay.SetEnabled(_run.Fleet.Relays < ExpeditionRun.MaxRelays);
            buttons.Add(repair);
            buttons.Add(relay);
            App.Ui.ShowModal(dialog);
        }

        private void ChooseSupply(bool repair)
        {
            _run.ChooseSupply(repair);
            App.ShowMap();
        }

        private void ConfirmAbandon()
        {
            App.Ui.ShowModal(Ui.Dialog(Loc.Ui("map.abandon_title"), Loc.Ui("map.abandon_body"),
                (Loc.Ui("map.abandon_cancel"), App.Ui.CloseModals),
                (Loc.Ui("map.abandon_confirm"), App.ShowMainMenu)));
        }

        private string NodeLabel(MapNode node)
        {
            switch (node.Type)
            {
                case NodeType.Supply:
                    return Loc.Ui("map.supply");
                default:
                    return TypeName(node.Type) + "\n" + WithoutPrefix(_names[node.Id]);
            }
        }

        /// <summary>"精英战：缝合巨怪巡游" → "缝合巨怪巡游": the node already shows its type.</summary>
        private static string WithoutPrefix(string name)
        {
            int colon = name.IndexOf('：');
            if (colon >= 0)
            {
                return name.Substring(colon + 1);
            }
            colon = name.IndexOf(": ", System.StringComparison.Ordinal);
            return colon >= 0 ? name.Substring(colon + 2) : name;
        }

        private static string TypeName(NodeType type)
        {
            switch (type)
            {
                case NodeType.Elite: return Loc.Ui("map.type_elite");
                case NodeType.Supply: return Loc.Ui("map.supply");
                case NodeType.Boss: return Loc.Ui("map.type_boss");
                default: return Loc.Ui("map.type_battle");
            }
        }
    }

    /// <summary>Draws the star map: route lines with the vector API, nodes as absolutely placed buttons.</summary>
    public sealed class StarMapView : VisualElement
    {
        private const float NodeWidth = 317f;
        private const float NodeHeight = 134f;

        // 星夜金边: unvisited roads dim, open roads gold, travelled roads lit in mana teal.
        private static readonly Color LineIdle = new Color(0.23f, 0.28f, 0.4f);
        private static readonly Color LineOpen = new Color(0.91f, 0.78f, 0.48f);
        private static readonly Color LineTravelled = new Color(0.44f, 0.82f, 0.71f);

        private readonly ExpeditionRun _run;
        private readonly Dictionary<int, Button> _buttons = new Dictionary<int, Button>();
        private readonly Dictionary<int, Vector2> _centers = new Dictionary<int, Vector2>();

        public event System.Action<MapNode> NodeClicked;
        public event System.Action<MapNode> NodeHovered;

        public StarMapView(ExpeditionRun run, System.Func<MapNode, string> label)
        {
            _run = run;
            AddToClassList("star-map");
            pickingMode = PickingMode.Ignore;
            List<MapNode> reachable = run.Reachable();
            foreach (MapNode node in run.Map.Nodes)
            {
                MapNode n = node;
                bool open = reachable.Contains(node);
                // Closed nodes stay enabled (only styled) so hovering still shows their briefing.
                Button button = Ui.Button(label(node), () =>
                {
                    if (open)
                    {
                        NodeClicked?.Invoke(n);
                    }
                }, "node", "node--" + node.Type.ToString().ToLowerInvariant());
                button.EnableInClassList("node--open", open);
                button.EnableInClassList("node--closed", !open);
                button.EnableInClassList("node--visited", node.Visited);
                button.EnableInClassList("node--current", node == run.Current);
                button.RegisterCallback<PointerEnterEvent>(_ => NodeHovered?.Invoke(n));
                button.RegisterCallback<PointerLeaveEvent>(_ => NodeHovered?.Invoke(null));
                _buttons[node.Id] = button;
                Add(button);
            }
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            generateVisualContent += DrawRoutes;
        }

        private void Layout()
        {
            Rect r = contentRect;
            int layers = _run.Map.Layers.Count;
            foreach (MapNode node in _run.Map.Nodes)
            {
                float x = NodeWidth / 2f + (r.width - NodeWidth) * node.Layer / Mathf.Max(1, layers - 1);
                float y = r.height * (node.Index + 0.5f) / node.LayerSize;
                _centers[node.Id] = new Vector2(x, y);
                Button b = _buttons[node.Id];
                b.style.left = x - NodeWidth / 2f;
                b.style.top = y - NodeHeight / 2f;
                b.style.width = NodeWidth;
                b.style.height = NodeHeight;
            }
            MarkDirtyRepaint();
        }

        private void DrawRoutes(MeshGenerationContext context)
        {
            if (_centers.Count == 0)
            {
                return;
            }
            Painter2D painter = context.painter2D;
            List<MapNode> reachable = _run.Reachable();
            foreach (MapNode node in _run.Map.Nodes)
            {
                foreach (int nextId in node.Next)
                {
                    MapNode next = _run.Map.Get(nextId);
                    bool travelled = node.Visited && next.Visited;
                    bool open = node == _run.Current && reachable.Contains(next);
                    painter.strokeColor = travelled ? LineTravelled : open ? LineOpen : LineIdle;
                    painter.lineWidth = travelled || open ? 7f : 3.6f;
                    painter.BeginPath();
                    painter.MoveTo(_centers[node.Id]);
                    painter.LineTo(_centers[nextId]);
                    painter.Stroke();
                }
            }
        }
    }
}
