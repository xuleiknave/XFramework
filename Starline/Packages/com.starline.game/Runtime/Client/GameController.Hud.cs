using System.Collections.Generic;
using UnityEngine;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>HUD for every phase (IMGUI keeps the prototype free of scene and UI assets).</summary>
    public sealed partial class GameController
    {
        private const float GuiHeight = 900f;
        private const float LeftPanelWidth = 230f;
        private const float RightPanelWidth = 330f;
        private const float TopBarHeight = 56f;
        private const float BottomBarHeight = 112f;
        private const float MapSidebarWidth = 300f;
        private const float NodeWidth = 128f;
        private const float NodeHeight = 52f;

        private const string ActTitle = "第一幕 · 边境星区";

        private const string IntroText =
            "雷恩舰长：断线日之后，灯塔站是星线最末端还亮着的信标。从这里往外，每一座信标都被蜂群啃断了。\n\n"
            + "指挥官，晨星舰队现在只有三艘船。但每打下一座信标，就能把一个殖民地重新连回星线，也能收编那里的舰船。\n\n"
            + "残骸要塞在这片星区的尽头。拿下它，边境星区的星线就通了。";

        private const string VictoryText =
            "诺娃：残骸要塞的信号消失了。边境星区的星线……重新亮了起来。\n\n"
            + "雷恩舰长：干得漂亮，指挥官。三个边境殖民地已经连回灯塔站。下一站是星团腹地。\n\n"
            + "（第二幕“星团腹地”正在开发中）";

        private const string DefeatText =
            "雷恩舰长：舰队撤回灯塔站。星线还在那里，蜂群也还在那里——我们还会回去的。";

        private static readonly Color Backdrop = new Color(0.03f, 0.045f, 0.08f, 0.97f);

        private float _guiScale = 1f;
        private GUIStyle _wrap, _title, _small, _big, _bad, _good, _warn, _center, _node;
        private Vector2 _rosterScroll;
        private bool _showIntro;
        private System.Action _pending;
        private MapNode _hoverNode;
        private readonly Dictionary<int, string> _nodeNames = new Dictionary<int, string>();

        /// <summary>
        /// Runs a button's action on the next Update. Changing state in the middle of OnGUI
        /// would make GUILayout's layout and event passes disagree.
        /// </summary>
        private void Defer(System.Action action) => _pending = action;

        private void OnGUI()
        {
            EnsureStyles();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_guiScale, _guiScale, 1f));
            float w = Screen.width / _guiScale;

            DrawUnitBars();
            switch (_phase)
            {
                case Phase.Map:
                    DrawMap(w);
                    break;
                case Phase.Supply:
                    DrawMap(w);
                    DrawSupply(w);
                    break;
                case Phase.Deploy:
                    DrawDeployTop(w);
                    DrawRoster();
                    DrawForecastPanel(w);
                    break;
                case Phase.Battle:
                    DrawBattleTop(w);
                    DrawBattleBottom(w);
                    break;
                case Phase.Result:
                    DrawBattleTop(w);
                    DrawResult(w);
                    break;
                case Phase.Reward:
                    DrawReward(w);
                    break;
                case Phase.RunEnd:
                    DrawRunEnd(w);
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (_wrap != null)
            {
                return;
            }
            _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            _small = new GUIStyle(_wrap) { fontSize = 12 };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
            _center = new GUIStyle(_wrap) { alignment = TextAnchor.MiddleCenter };
            _node = new GUIStyle(GUI.skin.button) { fontSize = 13, wordWrap = true };
            _bad = new GUIStyle(_small);
            _bad.normal.textColor = new Color(1f, 0.45f, 0.45f);
            _good = new GUIStyle(_small);
            _good.normal.textColor = new Color(0.5f, 0.9f, 0.6f);
            _warn = new GUIStyle(_small);
            _warn.normal.textColor = new Color(0.95f, 0.8f, 0.4f);
        }

        // ------------------------------------------------------------------
        // Star map

        private void DrawMap(float w)
        {
            FillScreen(w);
            DrawMapSidebar();

            StarMap map = _run.Map;
            float left = MapSidebarWidth + 60f;
            float right = w - 40f - NodeWidth;
            float top = 110f;
            float height = GuiHeight - 200f;
            GUI.Label(new Rect(left, 30, 700, 30), ActTitle + " · 星图", _title);
            GUI.Label(new Rect(left, 58, 760, 22), "从左往右推进，每次在相连的节点中选一个。悬停查看情报，点击亮起的节点出发。", _small);

            Vector2 Center(MapNode n)
            {
                float x = left + (right - left) * n.Layer / (map.Layers.Count - 1) + NodeWidth / 2f;
                float y = top + height * (n.Index + 0.5f) / n.LayerSize;
                return new Vector2(x, y);
            }

            List<MapNode> reachable = _run.Reachable();
            foreach (MapNode n in map.Nodes)
            {
                foreach (int nextId in n.Next)
                {
                    MapNode next = map.Get(nextId);
                    bool onPath = n.Visited && next.Visited;
                    bool open = n == _run.Current && reachable.Contains(next);
                    Color c = onPath ? new Color(0.45f, 0.9f, 0.6f) : open ? new Color(0.55f, 0.8f, 1f) : new Color(0.3f, 0.36f, 0.45f);
                    DrawLine(Center(n), Center(next), c, onPath || open ? 3f : 1.5f);
                }
            }

            MapNode hovered = null;
            Vector2 mouse = Event.current.mousePosition;
            foreach (MapNode n in map.Nodes)
            {
                Vector2 c = Center(n);
                var rect = new Rect(c.x - NodeWidth / 2f, c.y - NodeHeight / 2f, NodeWidth, NodeHeight);
                if (rect.Contains(mouse))
                {
                    hovered = n;
                }
                bool canGo = _phase == Phase.Map && !_showIntro && reachable.Contains(n);
                GUI.backgroundColor = n == _run.Current ? new Color(0.5f, 1f, 0.6f)
                    : n.Visited ? new Color(0.4f, 0.6f, 0.45f)
                    : canGo ? NodeColor(n.Type)
                    : new Color(0.45f, 0.48f, 0.55f);
                GUI.enabled = canGo;
                if (GUI.Button(rect, NodeLabel(n), _node) && canGo)
                {
                    Defer(() => OnNodeChosen(n));
                }
                GUI.enabled = true;
                GUI.backgroundColor = Color.white;
            }

            // Only change what the sidebar shows between frames, so Layout and Repaint stay in sync.
            if (Event.current.type == EventType.Repaint)
            {
                _hoverNode = hovered;
            }

            if (_showIntro)
            {
                DrawDialog(w, "启程", IntroText, "出发", () => _showIntro = false);
            }
        }

        private void DrawMapSidebar()
        {
            GUILayout.BeginArea(new Rect(16, 16, MapSidebarWidth, GuiHeight - 32), GUI.skin.box);
            GUILayout.Label("晨星舰队", _title);
            GUILayout.Label("舰队耐久  " + Hearts(_run.Durability, ExpeditionRun.MaxDurability) + "  " + _run.Durability + " / " + ExpeditionRun.MaxDurability,
                _run.Durability <= 2 ? _bad : _good);
            GUILayout.Label("指挥点上限 " + _run.CommandLimit + "（每过一个战斗节点 +" + ExpeditionRun.CommandLimitPerBattle + "）    中继信标 " + _run.Fleet.Relays, _small);
            GUILayout.Label("战斗 " + _run.BattlesWon + " 胜 / " + _run.BattlesFought + " 场", _small);
            GUILayout.Space(8);

            GUILayout.Label("舰船", _wrap);
            foreach (string line in FleetSummary())
            {
                GUILayout.Label(line, _small);
            }
            GUILayout.Label("3 艘同名同星的舰船会自动合成高一星。", _small);

            if (_run.Messages.Count > 0)
            {
                GUILayout.Space(8);
                foreach (string message in _run.Messages)
                {
                    GUILayout.Label(message, _warn);
                }
            }

            GUILayout.FlexibleSpace();
            MapNode info = _hoverNode ?? (_run.State == RunState.Supply ? _run.Current : null);
            if (info != null)
            {
                GUILayout.Label(NodeTypeName(info.Type) + " · " + _nodeNames[info.Id], _wrap);
                if (info.IsBattle)
                {
                    Encounter e = EncounterLibrary.Create(info.EncounterKey);
                    GUILayout.Label(e.Briefing, _small);
                    GUILayout.Label(info.Type == NodeType.Elite ? "胜利：三选一，必有一个 ★★ 舰船"
                        : info.Type == NodeType.Boss ? "胜利：边境星区通关    失败：耐久 −2，可以重打"
                        : "胜利：三选一    失败：耐久 −1", _good);
                }
                else
                {
                    GUILayout.Label("修理舰队（耐久 +1），或者领取 1 个中继信标。", _small);
                }
            }
            else
            {
                GUILayout.Label("图例：战斗 · 精英（更强，奖励更好）· 补给站（修理或领信标）· BOSS", _small);
            }
            GUILayout.EndArea();
        }

        private IEnumerable<string> FleetSummary()
        {
            var counts = new SortedDictionary<string, int>();
            foreach (OwnedShip ship in _run.Fleet.Ships)
            {
                string key = ship.Def.DisplayName + " " + RewardOption.Stars(ship.Star);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            foreach (KeyValuePair<string, int> kv in counts)
            {
                yield return "· " + kv.Key + " × " + kv.Value;
            }
        }

        private string NodeLabel(MapNode n)
        {
            switch (n.Type)
            {
                case NodeType.Supply:
                    return "补给站";
                case NodeType.Boss:
                    return "BOSS\n残骸要塞";
                default:
                    string name = _nodeNames[n.Id];
                    int colon = name.IndexOf('：');
                    return NodeTypeName(n.Type) + "\n" + (colon >= 0 ? name.Substring(colon + 1) : name);
            }
        }

        private static string NodeTypeName(NodeType type)
        {
            switch (type)
            {
                case NodeType.Elite: return "精英";
                case NodeType.Supply: return "补给站";
                case NodeType.Boss: return "BOSS";
                default: return "战斗";
            }
        }

        private static Color NodeColor(NodeType type)
        {
            switch (type)
            {
                case NodeType.Elite: return new Color(1f, 0.8f, 0.35f);
                case NodeType.Supply: return new Color(0.45f, 1f, 0.75f);
                case NodeType.Boss: return new Color(1f, 0.45f, 0.35f);
                default: return new Color(0.55f, 0.8f, 1f);
            }
        }

        private void DrawSupply(float w)
        {
            var box = new Rect(w / 2 - 200 + MapSidebarWidth / 2, 300, 400, 230);
            GUILayout.BeginArea(box, GUI.skin.box);
            GUILayout.Label("补给站", _title);
            GUILayout.Label("一座刚刚连回星线的殖民地前哨，可以帮舰队做一次整备。二选一：", _wrap);
            GUILayout.Space(10);
            GUI.enabled = _run.Durability < ExpeditionRun.MaxDurability;
            if (GUILayout.Button("修理舰队：耐久 +1（当前 " + _run.Durability + " / " + ExpeditionRun.MaxDurability + "）", GUILayout.Height(44)))
            {
                Defer(() => { _run.ChooseSupply(true); EnterMap(); });
            }
            GUI.enabled = _run.Fleet.Relays < ExpeditionRun.MaxRelays;
            if (GUILayout.Button("领取中继信标：+1（当前 " + _run.Fleet.Relays + " 个）", GUILayout.Height(44)))
            {
                Defer(() => { _run.ChooseSupply(false); EnterMap(); });
            }
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------
        // Rewards and run end

        private void DrawReward(float w)
        {
            FillScreen(w);
            GUI.Label(new Rect(w / 2 - 300, 120, 600, 40), "战利品：三选一", _title);
            GUI.Label(new Rect(w / 2 - 300, 150, 600, 24), "收编一艘舰船，或者带走一个中继信标。新舰船会自动上阵，可以在布阵时调整。", _small);

            float cardW = 260f, gap = 30f;
            float x = w / 2 - (cardW * 3 + gap * 2) / 2;
            for (int i = 0; i < _run.Rewards.Count; i++)
            {
                RewardOption option = _run.Rewards[i];
                var rect = new Rect(x + i * (cardW + gap), 200, cardW, 360);
                GUILayout.BeginArea(rect, GUI.skin.box);
                GUILayout.Label(option.Title, _title);
                GUILayout.Space(6);
                if (option.Kind == RewardKind.Relay)
                {
                    GUILayout.Label("补给节点 · 放在格子交叉点 · 指挥 1", _small);
                    GUILayout.Space(6);
                    GUILayout.Label("覆盖周围 2 格，同一条格线上 3 格内的节点自动连成补给线。多一个信标，阵型就能铺得更开。", _wrap);
                    GUILayout.Label("当前 " + _run.Fleet.Relays + " 个", _small);
                }
                else
                {
                    UnitDef def = UnitCatalog.Get(option.UnitId);
                    GUILayout.Label(RoleName(def.Role) + (def.Size >= 2 ? " · 2×2" : "") + " · 指挥 " + def.CommandCost, _small);
                    GUILayout.Space(6);
                    GUILayout.Label(ShipStats(def, option.Star), _wrap);
                    GUILayout.Space(6);
                    int owned = _run.Fleet.CopiesOwned(option.UnitId, option.Star);
                    if (option.Star < UnitDef.MaxStar && owned > 0)
                    {
                        GUILayout.Label(owned >= 2 ? "已有 2 艘同星 → 选它立即合成 " + RewardOption.Stars(option.Star + 1) + "！"
                            : "已有 1 艘同星（3 艘合成 " + RewardOption.Stars(option.Star + 1) + "）", owned >= 2 ? _good : _small);
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("选择", GUILayout.Height(40)))
                {
                    int index = i;
                    Defer(() => { _run.ChooseReward(index); EnterMap(); });
                }
                GUILayout.EndArea();
            }
            if (GUI.Button(new Rect(w / 2 - 80, 590, 160, 34), "跳过"))
            {
                Defer(() => { _run.ChooseReward(-1); EnterMap(); });
            }
        }

        private static string ShipStats(UnitDef def, int star)
        {
            float hp = (def.MaxHp * UnitDef.HpScale(star)).ToFloat();
            float shield = (def.MaxShield * UnitDef.HpScale(star)).ToFloat();
            float dps = (def.Damage * UnitDef.DamageScale(star)).ToFloat() / def.FireIntervalSeconds.ToFloat();
            string text = "结构 " + Mathf.RoundToInt(hp) + "  护盾 " + Mathf.RoundToInt(shield)
                + "\n每秒伤害 " + dps.ToString("0") + "  射程 " + (def.Range.ToFloat() / BoardLayout.CellSize.ToFloat()).ToString("0.#") + " 格";
            if (def.MaxAmmo > 0)
            {
                text += "\n弹药 " + def.MaxAmmo + "（断补给后打完就停火）";
            }
            if (def.OffGridGraceSeconds > Fix.Zero)
            {
                text += "\n可离网 " + def.OffGridGraceSeconds.ToIntFloor() + " 秒";
            }
            return text;
        }

        private void DrawRunEnd(float w)
        {
            FillScreen(w);
            bool won = _run.State == RunState.Victory;
            DrawDialog(w, won ? "边境星区 · 通关" : "远征失败", (won ? VictoryText : DefeatText)
                + "\n\n本次远征：战斗 " + _run.BattlesWon + " 胜 / " + _run.BattlesFought + " 场，剩余耐久 " + _run.Durability
                + "，舰队 " + _run.Fleet.Ships.Count + " 艘舰船。",
                "开始新的远征", StartNewRun);
        }

        private void DrawDialog(float w, string title, string text, string button, System.Action onClick)
        {
            var box = new Rect(w / 2 - 280 + (_phase == Phase.Map ? MapSidebarWidth / 2 : 0), 220, 560, 380);
            GUILayout.BeginArea(box, GUI.skin.box);
            GUILayout.Label(title, _title);
            GUILayout.Space(8);
            GUILayout.Label(text, _wrap);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(button, GUILayout.Height(44)))
            {
                Defer(onClick);
            }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------
        // Deployment

        private void DrawDeployTop(float w)
        {
            GUI.Box(new Rect(0, 0, w, TopBarHeight - 4), GUIContent.none);
            GUI.Label(new Rect(16, 12, 640, 30), ActTitle + " · 第 " + (_run.Current.Layer + 1) + " 跳    " + _encounter.Name, _title);
            GUI.Label(new Rect(w - 760, 16, 200, 24), "耐久 " + Hearts(_run.Durability, ExpeditionRun.MaxDurability), _run.Durability <= 2 ? _bad : _good);
            GUI.Label(new Rect(w - 560, 16, 548, 24),
                "左键：放置 / 拿起移动    右键：撤下 / 取消    选中中继信标后点交叉点", _small);
        }

        private void DrawRoster()
        {
            float h = GuiHeight - TopBarHeight - 12;
            GUILayout.BeginArea(new Rect(8, TopBarHeight, LeftPanelWidth, h), GUI.skin.box);
            GUILayout.Label("编队    指挥点 " + PlayerFormation.CommandPointsUsed() + " / " + _run.CommandLimit, _title);
            GUILayout.Space(4);

            _rosterScroll = GUILayout.BeginScrollView(_rosterScroll, GUILayout.Height(h - 300));
            int relaysLeft = _run.Fleet.RelaysLeft;
            bool relaySelected = _selection == Selection.Relay;
            GUI.enabled = relaysLeft > 0 || relaySelected;
            if (GUILayout.Toggle(relaySelected, "中继信标   剩 " + relaysLeft + " / " + _run.Fleet.Relays + "   指挥 1\n补给节点 · 放在交叉点", GUI.skin.button, GUILayout.Height(42)) != relaySelected)
            {
                Defer(relaySelected ? (System.Action)CancelSelection : () => Select(Selection.Relay));
            }
            GUI.enabled = true;

            var ships = new List<OwnedShip>(_run.Fleet.Ships);
            ships.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.UnitId, b.UnitId);
                return byName != 0 ? byName : b.Star - a.Star;
            });
            foreach (OwnedShip ship in ships)
            {
                UnitDef def = ship.Def;
                bool deployed = _run.Fleet.IsDeployed(ship.Id);
                bool selected = _selection == Selection.Ship && _selectedShipId == ship.Id;
                int copies = _run.Fleet.CopiesOwned(ship.UnitId, ship.Star);
                string label = def.DisplayName + " " + RewardOption.Stars(ship.Star) + "   指挥 " + def.CommandCost
                    + (deployed ? "   已上阵" : selected ? "   放置中" : "   后备")
                    + "\n" + RoleName(def.Role) + (def.Size >= 2 ? " · 2×2" : "")
                    + (ship.Star < UnitDef.MaxStar ? " · 合成 " + copies + "/3" : "");
                GUI.backgroundColor = deployed ? new Color(0.6f, 0.75f, 0.65f) : Color.white;
                if (GUILayout.Toggle(selected, label, GUI.skin.button, GUILayout.Height(42)) != selected)
                {
                    int shipId = ship.Id;
                    Defer(selected ? (System.Action)CancelSelection
                        : deployed ? () => PickUp(PlayerFormation.FindShip(shipId))
                        : () => Select(Selection.Ship, shipId));
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button("自动补满（后备舰船和信标）", GUILayout.Height(30)))
            {
                Defer(AutoFill);
            }
            if (GUILayout.Button("全部撤下（保留补给母舰）", GUILayout.Height(30)))
            {
                Defer(ClearFormation);
            }
            GUILayout.Space(6);
            int connected = 0, combat = 0;
            foreach (UnitView v in _views.Values)
            {
                if (v.Side == BoardLayout.PlayerSide && v.Def.HasWeapon)
                {
                    combat++;
                    if (v.Supplied)
                    {
                        connected++;
                    }
                }
            }
            GUILayout.Label("补给网络：" + connected + " / " + combat + " 艘舰船已接入", connected < combat ? _warn : _good);
            GUILayout.Label("绿色格子 = 补给覆盖（节点周围 2 格）\n节点相距 ≤ 3 格且在同一条格线上会自动连线", _small);
            if (_notice != null)
            {
                GUILayout.Label(_notice, _bad);
            }
            GUILayout.EndArea();
        }

        private void DrawForecastPanel(float w)
        {
            float h = GuiHeight - TopBarHeight - 12;
            GUILayout.BeginArea(new Rect(w - RightPanelWidth - 8, TopBarHeight, RightPanelWidth, h), GUI.skin.box);
            GUILayout.Label(_encounter.Name, _title);
            GUILayout.Label(_encounter.Briefing, _small);
            GUILayout.Space(8);
            GUILayout.Label("战前推演    情报点 " + _intel + " / " + _encounter.IntelPoints, _title);
            GUILayout.Space(4);
            if (_forecast == null)
            {
                GUILayout.Label("消耗 1 点情报点，模拟 100 场战斗，预测胜率和常见败因。\n（推演不使用指挥官技能）", _wrap);
            }
            else
            {
                GUILayout.Label(Mathf.RoundToInt((float)_forecast.WinRate * 100f) + "%", _big);
                GUILayout.Label("胜率（±" + Mathf.RoundToInt((float)_forecast.Margin * 100f) + "%）    平均 "
                    + Mathf.RoundToInt((float)_forecast.AverageSeconds) + " 秒    平均损失 " + _forecast.AverageLosses.ToString("0.0") + " 艘", _small);
                GUILayout.Space(6);
                if (_forecast.LossReasons.Count > 0)
                {
                    GUILayout.Label("常见败因", _small);
                    foreach (string reason in _forecast.LossReasons)
                    {
                        GUILayout.Label("· " + reason, _warn);
                    }
                }
                if (!string.IsNullOrEmpty(_forecast.Advice))
                {
                    GUILayout.Space(4);
                    GUILayout.Label("参谋建议：" + _forecast.Advice, _wrap);
                }
                if (_forecastHistory.Count > 1)
                {
                    GUILayout.Space(4);
                    GUILayout.Label("推演记录：" + string.Join(" → ", _forecastHistory.ConvertAll(v => v + "%")), _small);
                }
            }
            GUILayout.FlexibleSpace();

            foreach (string error in _errors)
            {
                GUILayout.Label("✕ " + error, _bad);
            }
            GUI.enabled = _intel > 0 && _errors.Count == 0;
            if (GUILayout.Button(_intel > 0 ? "战前推演（情报 −1）" : "情报点已用完", GUILayout.Height(36)))
            {
                Defer(RunForecast);
            }
            GUI.enabled = _errors.Count == 0;
            if (GUILayout.Button("开战", GUILayout.Height(56)))
            {
                Defer(StartBattle);
            }
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------
        // Battle

        private void DrawBattleTop(float w)
        {
            GUI.Box(new Rect(0, 0, w, TopBarHeight - 4), GUIContent.none);
            int player = _battle.CountCombatants(BoardLayout.PlayerSide);
            int enemy = _battle.CountCombatants(BoardLayout.EnemySide);
            GUI.Label(new Rect(16, 14, 300, 26), "我方 " + player + " / " + _battle.CountInitialCombatants(BoardLayout.PlayerSide), _title);
            GUI.Label(new Rect(w / 2 - 60, 12, 140, 30), Mathf.FloorToInt(_battle.ElapsedSeconds.ToFloat()) + " 秒 / " + _encounter.TimeLimitSeconds, _title);
            GUI.Label(new Rect(w / 2 + 160, 14, 300, 26), "敌方 " + enemy + " / " + _battle.CountInitialCombatants(BoardLayout.EnemySide), _title);

            if (_phase != Phase.Battle)
            {
                return;
            }
            for (int i = 0; i < Speeds.Length; i++)
            {
                bool on = _speedIndex == i;
                if (GUI.Toggle(new Rect(w - 250 + i * 54, 10, 48, 32), on, Speeds[i] + "x", GUI.skin.button) && !on)
                {
                    _speedIndex = i;
                }
            }
            if (GUI.Button(new Rect(w - 86, 10, 74, 32), "跳过 »"))
            {
                SkipBattle();
            }

            // Event feed under the top bar on the right.
            float y = TopBarHeight + 4;
            foreach (string line in _feed)
            {
                GUI.Label(new Rect(w - 300, y, 290, 20), line, _small);
                y += 18;
            }
        }

        private void DrawBattleBottom(float w)
        {
            float top = GuiHeight - BottomBarHeight;
            GUI.Box(new Rect(0, top, w, BottomBarHeight), GUIContent.none);

            float points = _battle.SupplyPoints(BoardLayout.PlayerSide).ToFloat();
            GUI.Label(new Rect(16, top + 10, 320, 22), "补给点数  " + points.ToString("0.0") + " / 10   （每秒 +1）", _wrap);
            for (int i = 0; i < 10; i++)
            {
                GUI.color = i < Mathf.FloorToInt(points) ? new Color(0.45f, 0.85f, 0.55f) : new Color(0.2f, 0.25f, 0.32f);
                GUI.DrawTexture(new Rect(16 + i * 30, top + 38, 26, 12), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(16, top + 58, 360, 40), "舰船自动战斗，你只需决定何时释放指挥官技能。", _small);

            GUI.Label(new Rect(400, top + 12, 260, 40), NetworkSummary(BoardLayout.PlayerSide, "我方补给"), _wrap);
            GUI.Label(new Rect(400, top + 56, 260, 40), NetworkSummary(BoardLayout.EnemySide, "敌方补给"), _wrap);

            float x = w - 16 - CommanderSkillDef.All.Length * 170;
            foreach (CommanderSkillDef skill in CommanderSkillDef.All)
            {
                int cd = _battle.SkillCooldownTicks(BoardLayout.PlayerSide, skill.Skill);
                bool can = _battle.CanCast(BoardLayout.PlayerSide, skill.Skill);
                string state = cd > 0 ? "冷却 " + Mathf.CeilToInt(cd / (float)BattleSim.TicksPerSecond) + " 秒"
                    : can ? (skill.NeedsTarget ? "点击后选择落点" : "可释放") : "补给点数不足";
                if (skill.Skill == CommanderSkill.OrbitalStrike && _armedStrike)
                {
                    state = "在战场上点击落点（右键取消）";
                }
                GUI.enabled = can;
                if (GUI.Button(new Rect(x, top + 14, 160, 84), skill.DisplayName + "\n补给 " + skill.Cost + "\n" + state))
                {
                    if (skill.NeedsTarget)
                    {
                        _armedStrike = !_armedStrike;
                    }
                    else
                    {
                        _battle.Issue(Command.Cast(BoardLayout.PlayerSide, skill.Skill));
                    }
                }
                GUI.enabled = true;
                x += 170;
            }
        }

        private string NetworkSummary(int side, string label)
        {
            int supplied = 0, combat = 0;
            foreach (Unit u in _battle.Units)
            {
                if (u.Side == side && u.Def.HasWeapon)
                {
                    combat++;
                    if (u.Supplied)
                    {
                        supplied++;
                    }
                }
            }
            int lost = _battle.Stats[side].RelaysLost;
            return label + "：" + supplied + " / " + combat + " 有补给" + (lost > 0 ? " · " + lost + " 个节点被毁" : " · 网络完好");
        }

        private void DrawResult(float w)
        {
            var box = new Rect(w / 2 - 210, 240, 420, 330);
            GUILayout.BeginArea(box, GUI.skin.box);
            string result = _lastBattleWon ? "胜利" : _battle.Winner == BoardLayout.EnemySide ? "失败" : "平局（算作失败）";
            GUILayout.Label(result, _big);
            GUILayout.Label("用时 " + Mathf.FloorToInt(_battle.ElapsedSeconds.ToFloat()) + " 秒", _wrap);
            int myLost = _battle.CountInitialCombatants(BoardLayout.PlayerSide) - _battle.CountCombatants(BoardLayout.PlayerSide);
            int enemyLost = _battle.CountInitialCombatants(BoardLayout.EnemySide) - _battle.CountCombatants(BoardLayout.EnemySide);
            GUILayout.Label("我方损失 " + myLost + " 艘，击毁敌方 " + enemyLost + " 个" + (_lastBattleWon ? "（舰船战后全部修复）" : ""), _wrap);
            SideStats stats = _battle.Stats[BoardLayout.PlayerSide];
            if (stats.RelaysLost > 0)
            {
                GUILayout.Label("我方有 " + stats.RelaysLost + " 个补给节点被摧毁", _warn);
            }
            if (stats.RanDry.Count > 0)
            {
                var names = new List<string>();
                foreach (string id in stats.RanDry)
                {
                    names.Add(UnitCatalog.Get(id).DisplayName);
                }
                GUILayout.Label("弹药耗尽过：" + string.Join("、", names), _warn);
            }
            foreach (string message in _run.Messages)
            {
                GUILayout.Label(message, _bad);
            }
            GUILayout.FlexibleSpace();

            string next;
            switch (_run.State)
            {
                case RunState.Reward: next = "领取战利品"; break;
                case RunState.Victory: next = "要塞陷落！"; break;
                case RunState.Defeat: next = "撤回灯塔站"; break;
                case RunState.Battle: next = "调整布阵，再次挑战 Boss"; break;
                default: next = "返回星图"; break;
            }
            if (GUILayout.Button(next, GUILayout.Height(40)))
            {
                Defer(LeaveResult);
            }
            GUILayout.EndArea();
        }

        /// <summary>Health, shield, ammo and supply state above every unit.</summary>
        private void DrawUnitBars()
        {
            if (_phase != Phase.Deploy && _phase != Phase.Battle && _phase != Phase.Result)
            {
                return;
            }
            foreach (UnitView view in _views.Values)
            {
                Vector3 sp = _camera.WorldToScreenPoint(view.HeadPosition);
                if (sp.z <= 0f)
                {
                    continue;
                }
                float x = sp.x / _guiScale;
                float y = (Screen.height - sp.y) / _guiScale;
                float width = view.Def.Size >= 2 ? 52f : 34f;

                if (_phase != Phase.Deploy)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.6f);
                    GUI.DrawTexture(new Rect(x - width / 2 - 1, y - 1, width + 2, 7), Texture2D.whiteTexture);
                    GUI.color = view.Side == BoardLayout.PlayerSide ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.5f, 0.35f);
                    GUI.DrawTexture(new Rect(x - width / 2, y, width * view.HpFraction, 3), Texture2D.whiteTexture);
                    GUI.color = new Color(0.45f, 0.8f, 1f);
                    GUI.DrawTexture(new Rect(x - width / 2, y + 3, width * view.ShieldFraction, 2), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                string tag = null;
                GUIStyle style = _small;
                if (view.Def.HasWeapon && !view.Supplied)
                {
                    tag = _phase == Phase.Deploy ? "✕ 未接入" : "断补给";
                    style = _bad;
                }
                else if (view.InGrace)
                {
                    tag = "离网 " + Mathf.CeilToInt(view.GraceSecondsLeft) + " 秒";
                    style = _warn;
                }
                if (_phase != Phase.Deploy && view.Def.MaxAmmo > 0 && (view.Ammo < view.Def.MaxAmmo || !view.Supplied))
                {
                    tag = (tag != null ? tag + " " : "") + "弹 " + view.Ammo + "/" + view.Def.MaxAmmo;
                    if (view.Ammo == 0)
                    {
                        style = _bad;
                    }
                }
                if (_phase == Phase.Deploy && (view.Side == BoardLayout.PlayerSide || view.Star > 1 || view.Def.Size >= 2))
                {
                    string stars = view.Star > 1 ? " " + RewardOption.Stars(view.Star) : "";
                    GUI.Label(new Rect(x - 40, y - 4, 90, 18), view.Def.DisplayName + stars, _small);
                }
                if (tag != null)
                {
                    GUI.Label(new Rect(x - 45, y - 20, 120, 18), tag, style);
                }
            }
        }

        // ------------------------------------------------------------------
        // Helpers

        private static void FillScreen(float w)
        {
            GUI.color = Backdrop;
            GUI.DrawTexture(new Rect(0, 0, w, GuiHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>A straight line in GUI space (rotated texture; works with the scaled GUI matrix).</summary>
        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            Matrix4x4 saved = GUI.matrix;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            GUI.matrix = saved
                * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f), Quaternion.Euler(0f, 0f, angle), Vector3.one)
                * Matrix4x4.TRS(new Vector3(-a.x, -a.y, 0f), Quaternion.identity, Vector3.one);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, (b - a).magnitude, width), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        private static string Hearts(int value, int max) => new string('■', value) + new string('□', max - value);

        private static string RoleName(UnitRole role)
        {
            switch (role)
            {
                case UnitRole.Light: return "轻型舰 · 克远程";
                case UnitRole.Heavy: return "重型舰 · 克轻型";
                case UnitRole.Craft: return "舰载机 · 克重型、远程";
                case UnitRole.PointDefense: return "点防御 · 克舰载机";
                case UnitRole.Artillery: return "远程舰 · 克重型";
                default: return "补给";
            }
        }
    }
}
