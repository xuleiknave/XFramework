using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>
    /// Playable Act 1 expedition: star map → deploy on the side-view board (ships on cells,
    /// relays on intersections) → pre-battle forecast → auto-battle with commander skills →
    /// three-choice reward, merging three copies of a ship into a higher star, until the
    /// boss falls or fleet durability runs out.
    ///
    /// Add this component to an empty GameObject in an empty scene and press Play
    /// (or use the menu Starline &gt; 创建可玩原型场景). The HUD lives in GameController.Hud.cs.
    /// </summary>
    public sealed partial class GameController : MonoBehaviour
    {
        private enum Phase
        {
            Map,
            Supply,
            Deploy,
            Battle,
            Result,
            Reward,
            RunEnd,
        }

        private enum Selection
        {
            None,
            Ship,
            Relay,
            Source,
        }

        private static readonly float[] Speeds = { 1f, 2f, 4f };
        private const float ShotSeconds = 0.08f;

        private Phase _phase;
        private ExpeditionRun _run;
        private Encounter _encounter;
        private Camera _camera;
        private CameraRig _rig;
        private BoardView _board;
        private Transform _unitRoot;
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();

        // Deployment
        private Selection _selection;
        private int _selectedShipId;
        /// <summary>Where a unit picked up from the board came from, so cancelling puts it back.</summary>
        private Placement _pickedOrigin;
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
        private bool _lastBattleWon;
        private bool _lastBattleWasBoss;
        private readonly List<string> _feed = new List<string>();
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly Stack<LineRenderer> _linePool = new Stack<LineRenderer>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private int _tintCountdown;

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

        private Formation PlayerFormation => _run.Fleet.Formation;

        // ------------------------------------------------------------------
        // Lifecycle

        private void Start()
        {
            SetUpScene();
            StartNewRun();
            _rig.SnapToDeploy();
        }

        private void Update()
        {
            _guiScale = Screen.height / GuiHeight;
            if (_pending != null)
            {
                System.Action action = _pending;
                _pending = null;
                action();
            }
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
        // Expedition flow

        private void StartNewRun()
        {
            _run = new ExpeditionRun((ulong)System.DateTime.Now.Ticks % 1000000007UL + 1UL);
            _nodeNames.Clear();
            foreach (MapNode node in _run.Map.Nodes)
            {
                _nodeNames[node.Id] = node.IsBattle ? EncounterLibrary.Create(node.EncounterKey).Name : "补给站";
            }
            _showIntro = true;
            EnterMap();
        }

        private void EnterMap()
        {
            _phase = Phase.Map;
            _battle = null;
            _encounter = null;
            ClearBattleEffects();
            ClearViews();
            _board.HideGhost();
            _board.ShowIntersections(false);
        }

        private void OnNodeChosen(MapNode node)
        {
            _run.Enter(node);
            if (_run.State == RunState.Battle)
            {
                _encounter = _run.CurrentEncounter;
                EnterDeploy();
            }
            else if (_run.State == RunState.Supply)
            {
                _phase = Phase.Supply;
            }
        }

        /// <summary>Called once when a battle finishes: reports it to the run.</summary>
        private void FinishBattle()
        {
            _phase = Phase.Result;
            _armedStrike = false;
            _lastBattleWon = _battle.Winner == BoardLayout.PlayerSide;
            _lastBattleWasBoss = _run.Current.Type == NodeType.Boss;
            _run.ReportBattle(_lastBattleWon);
        }

        /// <summary>The single button on the result screen: go wherever the run state says.</summary>
        private void LeaveResult()
        {
            ClearBattleEffects();
            switch (_run.State)
            {
                case RunState.Reward:
                    ClearViews();
                    _phase = Phase.Reward;
                    break;
                case RunState.Battle:
                    // Lost against the boss with durability left: try again.
                    EnterDeploy();
                    break;
                case RunState.Victory:
                case RunState.Defeat:
                    ClearViews();
                    _phase = Phase.RunEnd;
                    break;
                default:
                    EnterMap();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Deployment

        private void EnterDeploy()
        {
            _phase = Phase.Deploy;
            _battle = null;
            _armedStrike = false;
            _intel = _encounter.IntelPoints;
            _selection = Selection.None;
            _pickedOrigin = null;
            _forecast = null;
            _forecastHistory.Clear();
            _notice = null;
            _feed.Clear();
            _rig.GoToDeploy();
            RefreshDeploy();
        }

        /// <summary>Rebuilds the deployment visuals after any formation change.</summary>
        private void RefreshDeploy()
        {
            ClearViews();
            SupplyNetwork playerNet = SupplyNetwork.ForFormation(PlayerFormation, BoardLayout.PlayerSide);
            SupplyNetwork enemyNet = SupplyNetwork.ForFormation(_encounter.Enemy, BoardLayout.EnemySide);
            int id = 1;
            id = SpawnPreview(PlayerFormation, BoardLayout.PlayerSide, playerNet, id);
            SpawnPreview(_encounter.Enemy, BoardLayout.EnemySide, enemyNet, id);

            _board.TintCoverage(playerNet, enemyNet);
            _board.BeginNetworks();
            _board.DrawNetwork(playerNet, BoardLayout.PlayerSide);
            _board.DrawNetwork(enemyNet, BoardLayout.EnemySide);
            _board.EndNetworks();
            _errors = FormationRules.Validate(PlayerFormation, _run.CommandLimit);
        }

        private int SpawnPreview(Formation formation, int side, SupplyNetwork network, int nextId)
        {
            foreach (Placement p in formation.Placements)
            {
                UnitDef def = p.Def;
                Vector3 pos = ViewUtil.ToWorld(def.IsRelay
                    ? BoardLayout.Intersection(side, p.Depth, p.Row)
                    : BoardLayout.FootprintCenter(side, p.Depth, p.Row, def.Size));
                UnitView view = UnitView.Create(def, side, nextId, pos, _unitRoot, p.Star);
                view.SetPreviewSupplied(def.IsStructure || network.CoversPlacement(p, side));
                _views.Add(nextId, view);
                nextId++;
            }
            return nextId;
        }

        private UnitDef SelectedDef
        {
            get
            {
                switch (_selection)
                {
                    case Selection.Ship:
                        return _run.Fleet.Get(_selectedShipId)?.Def;
                    case Selection.Relay:
                        return UnitCatalog.Relay;
                    case Selection.Source:
                        return UnitCatalog.SupplyShip;
                    default:
                        return null;
                }
            }
        }

        private void Select(Selection selection, int shipId = 0)
        {
            CancelSelection();
            _selection = selection;
            _selectedShipId = shipId;
        }

        /// <summary>Drops the current selection; a unit picked up from the board goes back to where it was.</summary>
        private void CancelSelection()
        {
            if (_pickedOrigin != null)
            {
                PlayerFormation.Placements.Add(_pickedOrigin);
                _pickedOrigin = null;
                RefreshDeploy();
            }
            _selection = Selection.None;
            _selectedShipId = 0;
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
                CancelSelection();
            }

            UpdateHover(mouse.position.ReadValue());
            bool overUi = IsOverUi(mouse.position.ReadValue());
            UnitDef selected = SelectedDef;

            if (selected != null && _hoverDepth >= 0 && !overUi)
            {
                _board.ShowGhost(selected, _hoverDepth, _hoverRow, _hoverValid);
            }
            else
            {
                _board.HideGhost();
            }
            _board.ShowIntersections(_selection == Selection.Relay);

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
            if (_selection == Selection.Relay)
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

            UnitDef def = SelectedDef;
            if (_hoverDepth >= 0 && def != null)
            {
                bool available = _selection != Selection.Relay || _run.Fleet.RelaysLeft > 0;
                _hoverValid = available
                    && FormationRules.CanPlace(PlayerFormation, def, _hoverDepth, _hoverRow)
                    && PlayerFormation.CommandPointsUsed() + def.CommandCost <= _run.CommandLimit;
            }
        }

        private void OnDeployLeftClick()
        {
            if (_selection != Selection.None)
            {
                if (_hoverDepth < 0)
                {
                    return;
                }
                if (!_hoverValid)
                {
                    _notice = _selection == Selection.Relay && _run.Fleet.RelaysLeft <= 0 ? "中继信标已经全部放下"
                        : "这里放不下，或者指挥点不够";
                    return;
                }
                switch (_selection)
                {
                    case Selection.Ship:
                        _run.Fleet.Deploy(_run.Fleet.Get(_selectedShipId), _hoverDepth, _hoverRow);
                        break;
                    case Selection.Relay:
                        PlayerFormation.Add(UnitCatalog.Relay.Id, _hoverDepth, _hoverRow);
                        break;
                    case Selection.Source:
                        PlayerFormation.Add(UnitCatalog.SupplyShip.Id, _hoverDepth, _hoverRow);
                        break;
                }
                _pickedOrigin = null;
                if (_selection != Selection.Relay || _run.Fleet.RelaysLeft <= 0)
                {
                    _selection = Selection.None;
                }
                _notice = null;
                RefreshDeploy();
                return;
            }

            // No selection: pick up the unit (or relay) under the mouse to move it.
            Placement picked = PlacementUnderMouse();
            if (picked != null)
            {
                PickUp(picked);
            }
        }

        private void PickUp(Placement picked)
        {
            Select(picked.IsRelay ? Selection.Relay : picked.Def.IsSupplySource ? Selection.Source : Selection.Ship, picked.ShipId);
            PlayerFormation.Placements.Remove(picked);
            _pickedOrigin = picked;
            RefreshDeploy();
        }

        private void OnDeployRightClick()
        {
            if (_selection != Selection.None)
            {
                CancelSelection();
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
            PlayerFormation.Placements.Remove(picked);
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
            Placement relay = PlayerFormation.RelayAt(dl, rl);
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
            return PlayerFormation.UnitAt(Mathf.FloorToInt(-p.x / cell), Mathf.FloorToInt(p.z / cell + BoardLayout.Width / 2f));
        }

        private void AutoFill()
        {
            CancelSelection();
            AutoDeploy.FillFormation(_run.Fleet, _run.CommandLimit);
            RefreshDeploy();
        }

        private void ClearFormation()
        {
            CancelSelection();
            PlayerFormation.Placements.RemoveAll(p => !p.Def.IsSupplySource);
            RefreshDeploy();
        }

        private void RunForecast()
        {
            if (_intel <= 0 || _errors.Count > 0)
            {
                return;
            }
            _intel--;
            _forecast = BattleForecast.Run(_encounter, PlayerFormation);
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
            CancelSelection();
            if (_errors.Count > 0)
            {
                return;
            }
            // Any seed outside the forecast range, so the real fight is not one the player previewed.
            ulong seed = (ulong)System.DateTime.Now.Ticks % 900000UL + 1UL;
            _battle = new BattleSim(_encounter.CreateSetup(PlayerFormation.Clone(), seed));
            _phase = Phase.Battle;
            _accumulator = 0f;
            _speedIndex = 0;
            _armedStrike = false;
            _resultDelay = 1.5f;
            _feed.Clear();
            _board.HideGhost();
            _board.ShowIntersections(false);

            ClearViews();
            foreach (Unit unit in _battle.Units)
            {
                UnitView view = UnitView.Create(unit.Def, unit.Side, unit.Id, ViewUtil.ToWorld(unit.Position), _unitRoot, unit.Star);
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
                    FinishBattle();
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

        private void ClearBattleEffects()
        {
            foreach (Shot s in _shots)
            {
                s.Line.gameObject.SetActive(false);
                _linePool.Push(s.Line);
            }
            _shots.Clear();
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
            switch (_phase)
            {
                case Phase.Deploy:
                    return x < LeftPanelWidth + 12f || x > w - RightPanelWidth - 12f;
                case Phase.Battle:
                    return y > GuiHeight - BottomBarHeight;
                default:
                    return true;
            }
        }
    }
}
