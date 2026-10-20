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
                    _names[node.Id] = e.Name;
                    _briefings[node.Id] = e.Briefing;
                }
                else
                {
                    _names[node.Id] = "补给站";
                    _briefings[node.Id] = "一座刚刚连回星线的城邦驿站。休整远征军（耐久 +1），或者领取 1 个法力水晶。";
                }
            }

            Find<Label>("act-title").text = _run.Act.Title + " · 地图";
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
                App.Ui.ShowModal(Ui.Dialog(story.IntroTitle, story.Intro, ("出发", App.Ui.CloseModals)));
            }
        }

        private void RefreshSidebar()
        {
            int max = ExpeditionRun.MaxDurability;
            Label durability = Find<Label>("durability");
            durability.text = "远征军耐久  " + Ui.Hearts(_run.Durability, max) + "  " + _run.Durability + " / " + max;
            durability.EnableInClassList("bad", _run.Durability <= 2);
            durability.EnableInClassList("good", _run.Durability > 2);
            Find<Label>("command").text = "指挥点上限 " + _run.CommandLimit + "（每过一个战斗节点 +" + ExpeditionRun.CommandLimitPerBattle + "）";
            Find<Label>("relays").text = "法力水晶 " + _run.Fleet.Relays + " / " + ExpeditionRun.MaxRelays;
            Find<Label>("record").text = "战斗 " + _run.BattlesWon + " 胜 / " + _run.BattlesFought + " 场";

            VisualElement fleet = Find<VisualElement>("fleet-list");
            fleet.Clear();
            var counts = new SortedDictionary<string, int>();
            foreach (OwnedShip ship in _run.Fleet.Ships)
            {
                string key = ship.Def.DisplayName + " " + Ui.Stars(ship.Star);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            foreach (KeyValuePair<string, int> kv in counts)
            {
                fleet.Add(Ui.Label("· " + kv.Key + " × " + kv.Value, "body"));
            }

            VisualElement messages = Find<VisualElement>("messages");
            messages.Clear();
            foreach (string message in _run.Messages)
            {
                messages.Add(Ui.Label(message, "warn", "small"));
            }
        }

        private void ShowNodeInfo(MapNode node)
        {
            if (node == null)
            {
                _infoTitle.text = "图例";
                _infoBody.text = "战斗 · 精英（更强，奖励更好）· 补给站（休整或领水晶）· BOSS。悬停节点查看情报，点击亮起的节点出发。";
                _infoReward.text = "";
                return;
            }
            _infoTitle.text = TypeName(node.Type) + " · " + _names[node.Id];
            _infoBody.text = _briefings[node.Id];
            _infoReward.text = node.Type == NodeType.Elite ? "胜利：三选一，必有一个 ★★ 部队"
                : node.Type == NodeType.Boss ? "胜利：通关本幕    失败：耐久 −" + ExpeditionConfig.Current.BossLoss + "，可以重打"
                : node.Type == NodeType.Supply ? ""
                : "胜利：三选一    失败：耐久 −" + ExpeditionConfig.Current.NormalLoss;
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
            VisualElement dialog = Ui.Dialog("补给站", "一座刚刚连回星线的城邦驿站，可以帮远征军做一次整备。二选一：");
            VisualElement buttons = dialog.Q(className: "dialog-buttons");
            Button repair = Ui.Button("休整远征军：耐久 +1（当前 " + _run.Durability + " / " + ExpeditionRun.MaxDurability + "）",
                () => ChooseSupply(true), "btn-primary");
            repair.SetEnabled(_run.Durability < ExpeditionRun.MaxDurability);
            Button relay = Ui.Button("领取法力水晶：+1（当前 " + _run.Fleet.Relays + " 个）", () => ChooseSupply(false), "btn-secondary");
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
            App.Ui.ShowModal(Ui.Dialog("放弃远征", "当前远征的进度会丢失（存档功能在下一阶段加入）。确定返回主菜单吗？",
                ("继续远征", App.Ui.CloseModals),
                ("放弃并返回主菜单", App.ShowMainMenu)));
        }

        private string NodeLabel(MapNode node)
        {
            switch (node.Type)
            {
                case NodeType.Supply:
                    return "补给站";
                case NodeType.Boss:
                    string boss = _names[node.Id];
                    int c = boss.IndexOf('：');
                    return "BOSS\n" + (c >= 0 ? boss.Substring(c + 1) : boss);
                default:
                    string name = _names[node.Id];
                    int colon = name.IndexOf('：');
                    return TypeName(node.Type) + "\n" + (colon >= 0 ? name.Substring(colon + 1) : name);
            }
        }

        private static string TypeName(NodeType type)
        {
            switch (type)
            {
                case NodeType.Elite: return "精英";
                case NodeType.Supply: return "补给站";
                case NodeType.Boss: return "BOSS";
                default: return "战斗";
            }
        }
    }

    /// <summary>Draws the star map: route lines with the vector API, nodes as absolutely placed buttons.</summary>
    public sealed class StarMapView : VisualElement
    {
        private const float NodeWidth = 317f;
        private const float NodeHeight = 134f;

        private static readonly Color LineIdle = new Color(0.3f, 0.36f, 0.45f);
        private static readonly Color LineOpen = new Color(0.55f, 0.8f, 1f);
        private static readonly Color LineTravelled = new Color(0.45f, 0.9f, 0.6f);

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
