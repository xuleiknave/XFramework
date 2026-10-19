using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Playable prototype: deploy on the side-view board (units on cells, relays on
    /// intersections), run the pre-battle forecast, fight the auto-battle with commander
    /// skills, then retry with a tweaked formation.
    ///
    /// Add this component to an empty GameObject in an empty scene and press Play
    /// (or use the menu Starline &gt; 创建可玩原型场景).
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private enum Phase
        {
            Deploy,
            Battle,
            Result,
        }

        private static readonly float[] Speeds = { 1f, 2f, 4f };
        private const float GuiHeight = 900f;
        private const float LeftPanelWidth = 230f;
        private const float RightPanelWidth = 330f;
        private const float TopBarHeight = 56f;
        private const float BottomBarHeight = 112f;
        private const float ShotSeconds = 0.08f;

        private Phase _phase;
        private Encounter _encounter;
        private Formation _formation;
        private Camera _camera;
        private CameraRig _rig;
        private BoardView _board;
        private Transform _unitRoot;
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();

        // Deployment
        private string _selectedUnitId;
        private int _hoverDepth = -1;
        private int _hoverRow = -1;
        private bool _hoverValid;
        private int _intel;
        private ForecastResult _forecast;
        private readonly List<int> _forecastHistory = new List<int>();
        private List<string> _errors = new List<string>();
        private string _notice;

        // Battle
        private BattleSim _battle;
        private float _accumulator;
        private int _speedIndex;
        private bool _armedStrike;
        private float _resultDelay;
        private readonly List<string> _feed = new List<string>();
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly Stack<LineRenderer> _linePool = new Stack<LineRenderer>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private int _tintCountdown;

        // GUI
        private float _guiScale = 1f;
        private GUIStyle _wrap, _title, _small, _big, _bad, _good, _warn;

        private struct Shot
        {
            public LineRenderer Line;
            public float Until;
        }

        private struct Flash
        {
            public GameObject Sphere;
            public float Start;
            public float Duration;
            public float MaxScale;
        }

        // ------------------------------------------------------------------
        // Lifecycle

        private void Start()
        {
            SetUpScene();
            _encounter = Encounter.CreateSandstormAmbush();
            _formation = _encounter.SuggestedPlayer.Clone();
            EnterDeploy(resetIntel: true);
            _rig.SnapToDeploy();
        }

        private void Update()
        {
            _guiScale = Screen.height / GuiHeight;
            switch (_phase)
            {
                case Phase.Deploy:
                    UpdateDeploy();
                    break;
                case Phase.Battle:
                    UpdateBattle();
                    break;
                case Phase.Result:
                    InterpolateViews();
                    break;
            }
            UpdateShots();
            UpdateFlashes();
        }

        private void SetUpScene()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = camGo.AddComponent<Camera>();
            }
            _camera.fieldOfView = 50f;
            SceneLook.Apply(_camera);
            _rig = _camera.GetComponent<CameraRig>();
            if (_rig == null)
            {
                _rig = _camera.gameObject.AddComponent<CameraRig>();
            }

            _board = BoardView.Create(transform);
        }

        // ------------------------------------------------------------------
        // Deployment

        private void EnterDeploy(bool resetIntel)
        {
            _phase = Phase.Deploy;
            _battle = null;
            _armedStrike = false;
            if (resetIntel)
            {
                _intel = _encounter.IntelPoints;
            }
            _selectedUnitId = null;
            _forecast = null;
            _feed.Clear();
            _rig.GoToDeploy();
            RefreshDeploy();
        }

        /// <summary>Rebuilds the deployment visuals after any formation change.</summary>
        private void RefreshDeploy()
        {
            ClearViews();
            SupplyNetwork playerNet = SupplyNetwork.ForFormation(_formation, BoardLayout.PlayerSide);
            SupplyNetwork enemyNet = SupplyNetwork.ForFormation(_encounter.Enemy, BoardLayout.EnemySide);
            int id = 1;
            id = SpawnPreview(_formation, BoardLayout.PlayerSide, playerNet, id);
            SpawnPreview(_encounter.Enemy, BoardLayout.EnemySide, enemyNet, id);

            _board.TintCoverage(playerNet, enemyNet);
            _board.BeginNetworks();
            _board.DrawNetwork(playerNet, BoardLayout.PlayerSide);
            _board.DrawNetwork(enemyNet, BoardLayout.EnemySide);
            _board.EndNetworks();
            _errors = FormationRules.Validate(_formation, _encounter.CommandLimit);
        }

        private int SpawnPreview(Formation formation, int side, SupplyNetwork network, int nextId)
        {
            foreach (Placement p in formation.Placements)
            {
                UnitDef def = p.Def;
                Vector3 pos = ViewUtil.ToWorld(def.IsRelay
                    ? BoardLayout.Intersection(side, p.Depth, p.Row)
                    : BoardLayout.FootprintCenter(side, p.Depth, p.Row, def.Size));
                UnitView view = UnitView.Create(def, side, nextId, pos, _unitRoot);
                view.SetPreviewSupplied(def.IsStructure || network.CoversPlacement(p, side));
                _views.Add(nextId, view);
                nextId++;
            }
            return nextId;
        }

        private void UpdateDeploy()
        {
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            if (mouse == null)
            {
                return;
            }
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                _selectedUnitId = null;
            }

            UpdateHover(mouse.position.ReadValue());
            bool overUi = IsOverUi(mouse.position.ReadValue());

            if (_selectedUnitId != null && _hoverDepth >= 0 && !overUi)
            {
                _board.ShowGhost(UnitCatalog.Get(_selectedUnitId), _hoverDepth, _hoverRow, _hoverValid);
            }
            else
            {
                _board.HideGhost();
            }
            _board.ShowIntersections(_selectedUnitId != null && UnitCatalog.Get(_selectedUnitId).IsRelay);

            if (overUi)
            {
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                OnDeployLeftClick();
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                OnDeployRightClick();
            }
        }

        /// <summary>Finds the cell (or intersection, when placing a relay) under the mouse.</summary>
        private void UpdateHover(Vector2 screen)
        {
            _hoverDepth = _hoverRow = -1;
            _hoverValid = false;
            if (!TryGetGroundPoint(screen, out Vector3 p))
            {
                return;
            }

            float cell = BoardLayout.CellSize.ToFloat();
            bool relayMode = _selectedUnitId != null && UnitCatalog.Get(_selectedUnitId).IsRelay;
            if (relayMode)
            {
                int dl = Mathf.RoundToInt(-p.x / cell);
                int rl = Mathf.RoundToInt(p.z / cell + BoardLayout.Width / 2f);
                Vector3 snap = ViewUtil.ToWorld(BoardLayout.Intersection(BoardLayout.PlayerSide, dl, rl));
                if (BoardLayout.IsIntersectionInside(dl, rl) && (snap - p).magnitude < cell * 0.45f)
                {
                    _hoverDepth = dl;
                    _hoverRow = rl;
                }
            }
            else if (p.x < 0f)
            {
                int d = Mathf.FloorToInt(-p.x / cell);
                int r = Mathf.FloorToInt(p.z / cell + BoardLayout.Width / 2f);
                if (BoardLayout.IsCellInside(d, r))
                {
                    _hoverDepth = d;
                    _hoverRow = r;
                }
            }

            if (_hoverDepth >= 0 && _selectedUnitId != null)
            {
                UnitDef def = UnitCatalog.Get(_selectedUnitId);
                _hoverValid = FormationRules.CanPlace(_formation, def, _hoverDepth, _hoverRow)
                    && Remaining(_selectedUnitId) > 0
                    && _formation.CommandPointsUsed() + def.CommandCost <= _encounter.CommandLimit;
            }
        }

        private void OnDeployLeftClick()
        {
            if (_selectedUnitId != null)
            {
                if (_hoverDepth < 0)
                {
                    return;
                }
                if (!_hoverValid)
                {
                    _notice = Remaining(_selectedUnitId) <= 0 ? "这个兵器已经全部上阵"
                        : "这里放不下，或者指挥点不够";
                    return;
                }
                _formation.Add(_selectedUnitId, _hoverDepth, _hoverRow);
                if (Remaining(_selectedUnitId) <= 0)
                {
                    _selectedUnitId = null;
                }
                _notice = null;
                RefreshDeploy();
                return;
            }

            // No selection: pick up the unit (or relay) under the mouse to move it.
            Placement picked = PlacementUnderMouse();
            if (picked != null)
            {
                _formation.Placements.Remove(picked);
                _selectedUnitId = picked.UnitId;
                RefreshDeploy();
            }
        }

        private void OnDeployRightClick()
        {
            if (_selectedUnitId != null)
            {
                _selectedUnitId = null;
                return;
            }
            Placement picked = PlacementUnderMouse();
            if (picked == null)
            {
                return;
            }
            if (picked.Def.IsSupplySource)
            {
                _notice = "补给母舰不能撤下，可以左键拿起来换位置";
                return;
            }
            _formation.Placements.Remove(picked);
            RefreshDeploy();
        }

        private Placement PlacementUnderMouse()
        {
            if (Mouse.current == null || !TryGetGroundPoint(Mouse.current.position.ReadValue(), out Vector3 p))
            {
                return null;
            }
            float cell = BoardLayout.CellSize.ToFloat();

            // Relays first: they sit on intersections, so check a small radius around the nearest one.
            int dl = Mathf.RoundToInt(-p.x / cell);
            int rl = Mathf.RoundToInt(p.z / cell + BoardLayout.Width / 2f);
            Placement relay = _formation.RelayAt(dl, rl);
            if (relay != null)
            {
                Vector3 snap = ViewUtil.ToWorld(BoardLayout.Intersection(BoardLayout.PlayerSide, dl, rl));
                if ((snap - p).magnitude < cell * 0.3f)
                {
                    return relay;
                }
            }
            if (p.x >= 0f)
            {
                return null;
            }
            return _formation.UnitAt(Mathf.FloorToInt(-p.x / cell), Mathf.FloorToInt(p.z / cell + BoardLayout.Width / 2f));
        }

        private int Remaining(string unitId) => _encounter.OwnedCount(unitId) - _formation.Count(unitId);

        private void RunForecast()
        {
            if (_intel <= 0 || _errors.Count > 0)
            {
                return;
            }
            _intel--;
            _forecast = BattleForecast.Run(_encounter, _formation);
            _forecastHistory.Add(Mathf.RoundToInt((float)_forecast.WinRate * 100f));
            if (_forecastHistory.Count > 5)
            {
                _forecastHistory.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------
        // Battle

        private void StartBattle()
        {
            if (_errors.Count > 0)
            {
                return;
            }
            // Any seed outside the forecast range, so the real fight is not one the player previewed.
            ulong seed = (ulong)System.DateTime.Now.Ticks % 900000UL + 1UL;
            _battle = new BattleSim(_encounter.CreateSetup(_formation.Clone(), seed));
            _phase = Phase.Battle;
            _accumulator = 0f;
            _speedIndex = 0;
            _armedStrike = false;
            _resultDelay = 1.5f;
            _feed.Clear();
            _selectedUnitId = null;
            _board.HideGhost();
            _board.ShowIntersections(false);

            ClearViews();
            foreach (Unit unit in _battle.Units)
            {
                UnitView view = UnitView.Create(unit.Def, unit.Side, unit.Id, ViewUtil.ToWorld(unit.Position), _unitRoot);
                view.PushState(unit);
                view.PushState(unit);
                _views.Add(unit.Id, view);
            }
            RefreshBattleBoard();
            _rig.GoToBattle();
            AddFeed("开战！");
        }

        private void UpdateBattle()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && _armedStrike && mouse.leftButton.wasPressedThisFrame && !IsOverUi(mouse.position.ReadValue()))
            {
                if (TryGetGroundPoint(mouse.position.ReadValue(), out Vector3 p))
                {
                    _battle.Issue(Command.Cast(BoardLayout.PlayerSide, CommanderSkill.OrbitalStrike, ToFix(p)));
                }
                _armedStrike = false;
            }
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                _armedStrike = false;
            }

            if (!_battle.IsOver)
            {
                float tick = 1f / BattleSim.TicksPerSecond;
                _accumulator += Time.deltaTime * Speeds[_speedIndex];
                _accumulator = Mathf.Min(_accumulator, tick * 12f);
                while (_accumulator >= tick && !_battle.IsOver)
                {
                    StepBattle(withEffects: true);
                    _accumulator -= tick;
                }
            }
            else
            {
                _resultDelay -= Time.deltaTime;
                if (_resultDelay <= 0f)
                {
                    _phase = Phase.Result;
                    _armedStrike = false;
                }
            }

            InterpolateViews();
            if (--_tintCountdown <= 0)
            {
                RefreshBattleBoard();
                _tintCountdown = 10;
            }
        }

        private void StepBattle(bool withEffects)
        {
            _battle.Step();
            ConsumeEvents(withEffects);
            foreach (Unit unit in _battle.Units)
            {
                if (_views.TryGetValue(unit.Id, out UnitView view))
                {
                    view.PushState(unit);
                }
            }
        }

        private void SkipBattle()
        {
            while (!_battle.IsOver)
            {
                StepBattle(withEffects: false);
            }
            _accumulator = 0f;
            _resultDelay = 0.3f;
            RefreshBattleBoard();
        }

        private void RefreshBattleBoard()
        {
            if (_battle == null)
            {
                return;
            }
            _board.TintCoverage(_battle.Network(BoardLayout.PlayerSide), _battle.Network(BoardLayout.EnemySide));
            _board.BeginNetworks();
            _board.DrawNetwork(_battle.Network(BoardLayout.PlayerSide), BoardLayout.PlayerSide);
            _board.DrawNetwork(_battle.Network(BoardLayout.EnemySide), BoardLayout.EnemySide);
            _board.EndNetworks();
        }

        private void ConsumeEvents(bool withEffects)
        {
            foreach (BattleEvent e in _battle.Events)
            {
                switch (e.Type)
                {
                    case BattleEventType.Fired:
                        if (withEffects && _views.TryGetValue(e.SourceId, out UnitView src) && _views.TryGetValue(e.TargetId, out UnitView dst))
                        {
                            SpawnShot(src, dst, e.Critical);
                        }
                        break;
                    case BattleEventType.Damaged:
                        if (withEffects && _views.TryGetValue(e.TargetId, out UnitView hit))
                        {
                            hit.FlashHit();
                        }
                        break;
                    case BattleEventType.Destroyed:
                        if (_views.TryGetValue(e.TargetId, out UnitView dead))
                        {
                            UnitDef def = dead.Def;
                            string who = (dead.Side == BoardLayout.PlayerSide ? "我方 " : "敌方 ") + def.DisplayName;
                            AddFeed(def.IsRelay ? who + " 补给节点被摧毁！" : def.IsSupplySource ? who + " 补给源被摧毁！" : who + " 被击毁");
                            if (withEffects)
                            {
                                SpawnFlash(dead.transform.position + Vector3.up * 0.5f, def.Size >= 2 ? 4f : 2f, 0.45f, new Color(1f, 0.8f, 0.4f));
                            }
                            _views.Remove(e.TargetId);
                            Destroy(dead.gameObject);
                            if (def.IsRelay || def.IsSupplySource)
                            {
                                _tintCountdown = 0;
                            }
                        }
                        break;
                    case BattleEventType.OutOfAmmo:
                    {
                        Unit unit = _battle.GetUnit(e.TargetId);
                        if (unit != null)
                        {
                            AddFeed((unit.Side == BoardLayout.PlayerSide ? "我方 " : "敌方 ") + unit.Def.DisplayName + " 弹药耗尽");
                        }
                        break;
                    }
                    case BattleEventType.SkillCast:
                        if (e.SourceId == BoardLayout.PlayerSide)
                        {
                            AddFeed("释放【" + CommanderSkillDef.All[e.Amount.ToIntFloor()].DisplayName + "】");
                        }
                        break;
                    case BattleEventType.StrikeLanded:
                        if (withEffects)
                        {
                            SpawnFlash(ViewUtil.ToWorld(e.Point) + Vector3.up * 0.3f, CommanderSkillDef.StrikeRadius.ToFloat() * 2f, 0.6f, new Color(1f, 0.35f, 0.3f));
                        }
                        break;
                    case BattleEventType.BattleEnded:
                        AddFeed(e.SourceId == BoardLayout.PlayerSide ? "胜利！" : e.SourceId == BoardLayout.EnemySide ? "失败" : "平局");
                        break;
                }
            }
        }

        private void InterpolateViews()
        {
            float t = Mathf.Clamp01(_accumulator * BattleSim.TicksPerSecond);
            if (_battle == null)
            {
                return;
            }
            foreach (Unit unit in _battle.Units)
            {
                if (!_views.TryGetValue(unit.Id, out UnitView view))
                {
                    continue;
                }
                Unit target = _battle.GetUnit(unit.TargetId);
                bool hasTarget = target != null && _views.ContainsKey(target.Id);
                view.Interpolate(t, hasTarget ? _views[target.Id].transform.position : Vector3.zero, hasTarget);
            }
        }

        private void AddFeed(string text)
        {
            _feed.Add(text);
            if (_feed.Count > 6)
            {
                _feed.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------
        // Effects

        private void SpawnShot(UnitView source, UnitView target, bool critical)
        {
            LineRenderer line = _linePool.Count > 0 ? _linePool.Pop() : ViewUtil.Line(transform, "Shot");
            line.gameObject.SetActive(true);
            Color c = critical ? Color.yellow : (source.Side == BoardLayout.PlayerSide ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.65f, 0.4f));
            float width = critical ? 0.3f : source.Def.Role == UnitRole.Artillery ? 0.18f : 0.08f;
            ViewUtil.SetLine(line, source.transform.position + Vector3.up * 0.6f, target.transform.position + Vector3.up * 0.6f, c, width, critical ? 5f : 3f);
            _shots.Add(new Shot { Line = line, Until = Time.time + ShotSeconds });
        }

        private void UpdateShots()
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _shots[i].Until)
                {
                    _shots[i].Line.gameObject.SetActive(false);
                    _linePool.Push(_shots[i].Line);
                    _shots.RemoveAt(i);
                }
            }
        }

        private void SpawnFlash(Vector3 position, float size, float duration, Color color)
        {
            GameObject sphere = ViewUtil.Primitive(PrimitiveType.Sphere, transform, position, Vector3.zero, color);
            sphere.GetComponent<Renderer>().material = ViewUtil.Unlit;
            sphere.GetComponent<Renderer>().material.color = color * 4f;
            _flashes.Add(new Flash { Sphere = sphere, Start = Time.time, Duration = duration, MaxScale = size });
        }

        private void UpdateFlashes()
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                Flash f = _flashes[i];
                float k = (Time.time - f.Start) / f.Duration;
                if (k >= 1f)
                {
                    Destroy(f.Sphere);
                    _flashes.RemoveAt(i);
                    continue;
                }
                f.Sphere.transform.localScale = Vector3.one * (f.MaxScale * Mathf.Sin(k * Mathf.PI));
            }
        }

        private void ClearViews()
        {
            if (_unitRoot != null)
            {
                Destroy(_unitRoot.gameObject);
            }
            _unitRoot = new GameObject("Units").transform;
            _views.Clear();
        }

        // ------------------------------------------------------------------
        // Input helpers

        private bool TryGetGroundPoint(Vector2 screen, out Vector3 point)
        {
            Ray ray = _camera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        private static FixVec2 ToFix(Vector3 world) => new FixVec2(
            Fix.FromRatio(Mathf.RoundToInt(world.x * 100f), 100),
            Fix.FromRatio(Mathf.RoundToInt(world.z * 100f), 100));

        /// <summary>True when the mouse is over a HUD panel, so world clicks are ignored.</summary>
        private bool IsOverUi(Vector2 screen)
        {
            float x = screen.x / _guiScale;
            float y = (Screen.height - screen.y) / _guiScale;
            float w = Screen.width / _guiScale;
            if (y < TopBarHeight)
            {
                return true;
            }
            if (_phase == Phase.Deploy)
            {
                return x < LeftPanelWidth + 12f || x > w - RightPanelWidth - 12f;
            }
            return y > GuiHeight - BottomBarHeight;
        }

        // ------------------------------------------------------------------
        // HUD (IMGUI keeps the prototype free of scene and UI assets)

        private void OnGUI()
        {
            EnsureStyles();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_guiScale, _guiScale, 1f));
            float w = Screen.width / _guiScale;

            DrawUnitBars();
            switch (_phase)
            {
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
            _bad = new GUIStyle(_small);
            _bad.normal.textColor = new Color(1f, 0.45f, 0.45f);
            _good = new GUIStyle(_small);
            _good.normal.textColor = new Color(0.5f, 0.9f, 0.6f);
            _warn = new GUIStyle(_small);
            _warn.normal.textColor = new Color(0.95f, 0.8f, 0.4f);
        }

        private void DrawDeployTop(float w)
        {
            GUI.Box(new Rect(0, 0, w, TopBarHeight - 4), GUIContent.none);
            GUI.Label(new Rect(16, 12, 520, 30), "第一幕 · 地球级    " + _encounter.Name, _title);
            GUI.Label(new Rect(w - 560, 16, 548, 24),
                "左键：放置 / 拿起移动    右键：撤下 / 取消选择    选中中继站后点交叉点", _small);
        }

        private void DrawRoster()
        {
            float h = GuiHeight - TopBarHeight - 12;
            GUILayout.BeginArea(new Rect(8, TopBarHeight, LeftPanelWidth, h), GUI.skin.box);
            GUILayout.Label("编队    指挥点 " + _formation.CommandPointsUsed() + " / " + _encounter.CommandLimit, _title);
            GUILayout.Space(6);
            foreach (RosterEntry entry in _encounter.Roster)
            {
                UnitDef def = entry.Def;
                int left = Remaining(entry.UnitId);
                string label = def.DisplayName + "   剩 " + left + "   指挥 " + def.CommandCost
                    + (def.IsRelay ? "\n补给节点 · 放在交叉点" : "\n" + RoleName(def.Role) + (def.Size >= 2 ? " · 2×2" : ""));
                bool selected = _selectedUnitId == entry.UnitId;
                GUI.enabled = left > 0 || selected;
                if (GUILayout.Toggle(selected, label, GUI.skin.button, GUILayout.Height(46)) != selected)
                {
                    _selectedUnitId = selected ? null : entry.UnitId;
                }
                GUI.enabled = true;
            }
            GUILayout.Space(10);
            if (GUILayout.Button("恢复推荐阵型", GUILayout.Height(30)))
            {
                _formation = _encounter.SuggestedPlayer.Clone();
                _selectedUnitId = null;
                RefreshDeploy();
            }
            if (GUILayout.Button("清空（保留补给母舰）", GUILayout.Height(30)))
            {
                _formation.Placements.RemoveAll(p => !p.Def.IsSupplySource);
                _selectedUnitId = null;
                RefreshDeploy();
            }
            GUILayout.Space(10);
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
            GUILayout.Label("补给网络：" + connected + " / " + combat + " 个兵器已接入", connected < combat ? _warn : _good);
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
                    + Mathf.RoundToInt((float)_forecast.AverageSeconds) + " 秒    平均损失 " + _forecast.AverageLosses.ToString("0.0") + " 个", _small);
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
                RunForecast();
            }
            GUI.enabled = _errors.Count == 0;
            if (GUILayout.Button("开战", GUILayout.Height(56)))
            {
                StartBattle();
            }
            GUI.enabled = true;
            GUILayout.EndArea();
        }

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
            GUI.Label(new Rect(16, top + 58, 360, 40), "兵器自动战斗，你只需决定何时释放指挥官技能。", _small);

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
            var box = new Rect(w / 2 - 210, 250, 420, 300);
            GUILayout.BeginArea(box, GUI.skin.box);
            string result = _battle.Winner == BoardLayout.PlayerSide ? "胜利" : _battle.Winner == BoardLayout.EnemySide ? "失败" : "平局";
            GUILayout.Label(result, _big);
            GUILayout.Label("用时 " + Mathf.FloorToInt(_battle.ElapsedSeconds.ToFloat()) + " 秒", _wrap);
            int myLost = _battle.CountInitialCombatants(BoardLayout.PlayerSide) - _battle.CountCombatants(BoardLayout.PlayerSide);
            int enemyLost = _battle.CountInitialCombatants(BoardLayout.EnemySide) - _battle.CountCombatants(BoardLayout.EnemySide);
            GUILayout.Label("我方损失 " + myLost + " 个兵器，击毁敌方 " + enemyLost + " 个", _wrap);
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
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("调整布阵再打", GUILayout.Height(40)))
            {
                ClearBattleEffects();
                EnterDeploy(resetIntel: true);
            }
            GUILayout.EndArea();
        }

        private void ClearBattleEffects()
        {
            foreach (Shot s in _shots)
            {
                s.Line.gameObject.SetActive(false);
                _linePool.Push(s.Line);
            }
            _shots.Clear();
        }

        /// <summary>Health, shield, ammo and supply state above every unit.</summary>
        private void DrawUnitBars()
        {
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
                if (_phase == Phase.Deploy && view.Side == BoardLayout.PlayerSide)
                {
                    GUI.Label(new Rect(x - 40, y - 4, 80, 18), view.Def.DisplayName, _small);
                }
                if (tag != null)
                {
                    GUI.Label(new Rect(x - 45, y - 20, 120, 18), tag, style);
                }
            }
        }

        private static string RoleName(UnitRole role)
        {
            switch (role)
            {
                case UnitRole.Light: return "轻型 · 克远程";
                case UnitRole.Heavy: return "重型 · 克轻型";
                case UnitRole.Air: return "空中 · 克重型";
                case UnitRole.AntiAir: return "防空 · 克空中";
                case UnitRole.Artillery: return "远程 · 克重型";
                default: return "补给";
            }
        }
    }
}
