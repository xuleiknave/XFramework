using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using XFramework.Battle;

namespace XFramework.Client
{
    /// <summary>
    /// P0 prototype entry point. Add this component to an empty GameObject in an
    /// empty scene and press Play: it builds the ground, camera and light, runs the
    /// deterministic battle at a fixed tick rate and handles RTS-style input.
    ///
    /// Controls:
    ///   Left click / drag     select own units (Shift adds to selection)
    ///   Right click ground    move (fires on the way)
    ///   Right click enemy     focus fire
    ///   A + right click       attack-move (stops to fight)
    ///   S                     stop
    ///   Ctrl + 1..9 / 1..9    assign / select control group
    ///   Space                 pause / resume (orders can be given while paused)
    ///   Tab                   cycle speed 0.5x / 1x / 2x
    ///   Arrow keys, middle drag, wheel   camera
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        private static readonly float[] Speeds = { 0.5f, 1f, 2f };
        private const float ClickPickRadiusPixels = 28f;
        private const float DragThresholdPixels = 6f;
        private const float ShotEffectSeconds = 0.08f;

        public ulong Seed = 1;

        private Battle _battle;
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();
        private readonly List<UnitView> _selection = new List<UnitView>();
        private readonly Dictionary<int, List<int>> _groups = new Dictionary<int, List<int>>();
        private readonly List<ShotEffect> _shots = new List<ShotEffect>();
        private readonly Stack<LineRenderer> _linePool = new Stack<LineRenderer>();

        private Camera _camera;
        private Transform _unitRoot;
        private Material _lineMaterial;
        private float _accumulator;
        private int _speedIndex = 1;
        private bool _paused;

        private bool _dragging;
        private Vector2 _dragStart;

        private struct ShotEffect
        {
            public LineRenderer Line;
            public float Until;
        }

        private void Start()
        {
            SetUpScene();
            StartBattle();
        }

        private void StartBattle()
        {
            if (_unitRoot != null)
            {
                Destroy(_unitRoot.gameObject);
            }
            _views.Clear();
            _selection.Clear();
            _groups.Clear();
            _accumulator = 0f;
            _paused = false;

            _battle = new Battle(Scenario.CreateP0Skirmish(Seed));
            _battle.AddAgent(new SimpleCommanderAI(Scenario.EnemySide));

            _unitRoot = new GameObject("Units").transform;
            foreach (Unit unit in _battle.Units)
            {
                _views.Add(unit.Id, UnitView.Create(unit, _unitRoot));
            }
        }

        private void Update()
        {
            HandleInput();
            AdvanceSimulation();
            UpdateViews();
            UpdateShotEffects();
        }

        // ------------------------------------------------------------------
        // Simulation

        private void AdvanceSimulation()
        {
            if (_paused || _battle.IsOver)
            {
                return;
            }

            float tickSeconds = 1f / Battle.TicksPerSecond;
            _accumulator += Time.deltaTime * Speeds[_speedIndex];
            // Cap catch-up work after a long frame (e.g. a hitch or breakpoint).
            _accumulator = Mathf.Min(_accumulator, tickSeconds * 10f);

            while (_accumulator >= tickSeconds && !_battle.IsOver)
            {
                _battle.Step();
                _accumulator -= tickSeconds;
                ConsumeEvents();
                foreach (Unit unit in _battle.Units)
                {
                    _views[unit.Id].PushState(unit);
                }
            }
        }

        private void ConsumeEvents()
        {
            foreach (BattleEvent e in _battle.Events)
            {
                switch (e.Type)
                {
                    case BattleEventType.Fired:
                        if (_views.TryGetValue(e.SourceId, out UnitView src) && _views.TryGetValue(e.TargetId, out UnitView dst))
                        {
                            SpawnShot(src, dst, e.Critical);
                        }
                        break;
                    case BattleEventType.Damaged:
                        if (_views.TryGetValue(e.TargetId, out UnitView hit))
                        {
                            hit.FlashHit();
                        }
                        break;
                    case BattleEventType.Destroyed:
                        if (_views.TryGetValue(e.TargetId, out UnitView dead))
                        {
                            _selection.Remove(dead);
                            _views.Remove(e.TargetId);
                            Destroy(dead.gameObject);
                        }
                        break;
                }
            }
        }

        private void UpdateViews()
        {
            float t = Mathf.Clamp01(_accumulator * Battle.TicksPerSecond);
            foreach (Unit unit in _battle.Units)
            {
                if (!_views.TryGetValue(unit.Id, out UnitView view))
                {
                    continue;
                }
                Unit target = _battle.GetUnit(unit.CurrentTargetId);
                bool hasTarget = target != null && _views.ContainsKey(target.Id);
                view.Interpolate(t, hasTarget ? _views[target.Id].transform.position : Vector3.zero, hasTarget);
            }
        }

        // ------------------------------------------------------------------
        // Input

        private void HandleInput()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
            {
                return;
            }

            if (kb.spaceKey.wasPressedThisFrame)
            {
                _paused = !_paused;
            }
            if (kb.tabKey.wasPressedThisFrame)
            {
                _speedIndex = (_speedIndex + 1) % Speeds.Length;
            }
            if (_battle.IsOver)
            {
                return;
            }

            HandleControlGroups(kb);

            if (kb.sKey.wasPressedThisFrame && _selection.Count > 0)
            {
                _battle.Issue(Command.Stop(Scenario.PlayerSide, SelectedIds()));
            }

            Vector2 mousePos = mouse.position.ReadValue();
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _dragging = true;
                _dragStart = mousePos;
            }
            if (mouse.leftButton.wasReleasedThisFrame && _dragging)
            {
                _dragging = false;
                if ((mousePos - _dragStart).magnitude < DragThresholdPixels)
                {
                    ClickSelect(mousePos, shift);
                }
                else
                {
                    BoxSelect(_dragStart, mousePos, shift);
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && _selection.Count > 0)
            {
                UnitView enemy = PickUnit(mousePos, Scenario.EnemySide);
                if (enemy != null)
                {
                    _battle.Issue(Command.Attack(Scenario.PlayerSide, SelectedIds(), enemy.UnitId));
                }
                else if (TryGetGroundPoint(mousePos, out Vector3 ground))
                {
                    FixVec2 point = ToFix(ground);
                    _battle.Issue(kb.aKey.isPressed
                        ? Command.AttackMove(Scenario.PlayerSide, SelectedIds(), point)
                        : Command.Move(Scenario.PlayerSide, SelectedIds(), point));
                }
            }
        }

        private void HandleControlGroups(Keyboard kb)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            for (int n = 1; n <= 9; n++)
            {
                if (!kb[Key.Digit1 + (n - 1)].wasPressedThisFrame)
                {
                    continue;
                }
                if (ctrl)
                {
                    _groups[n] = SelectedIds();
                }
                else if (_groups.TryGetValue(n, out List<int> ids))
                {
                    ClearSelection();
                    foreach (int id in ids)
                    {
                        if (_views.TryGetValue(id, out UnitView view))
                        {
                            AddToSelection(view);
                        }
                    }
                }
            }
        }

        private void ClickSelect(Vector2 screen, bool additive)
        {
            if (!additive)
            {
                ClearSelection();
            }
            UnitView view = PickUnit(screen, Scenario.PlayerSide);
            if (view != null)
            {
                AddToSelection(view);
            }
        }

        private void BoxSelect(Vector2 a, Vector2 b, bool additive)
        {
            if (!additive)
            {
                ClearSelection();
            }
            Rect rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            foreach (UnitView view in _views.Values)
            {
                if (view.Side != Scenario.PlayerSide)
                {
                    continue;
                }
                Vector3 sp = _camera.WorldToScreenPoint(view.transform.position);
                if (sp.z > 0f && rect.Contains(sp))
                {
                    AddToSelection(view);
                }
            }
        }

        private UnitView PickUnit(Vector2 screen, int side)
        {
            UnitView best = null;
            float bestDist = ClickPickRadiusPixels;
            foreach (UnitView view in _views.Values)
            {
                if (view.Side != side)
                {
                    continue;
                }
                Vector3 sp = _camera.WorldToScreenPoint(view.transform.position);
                float d = Vector2.Distance(screen, sp);
                if (sp.z > 0f && d < bestDist)
                {
                    best = view;
                    bestDist = d;
                }
            }
            return best;
        }

        private void AddToSelection(UnitView view)
        {
            if (!_selection.Contains(view))
            {
                _selection.Add(view);
                view.SetSelected(true);
            }
        }

        private void ClearSelection()
        {
            foreach (UnitView view in _selection)
            {
                if (view != null)
                {
                    view.SetSelected(false);
                }
            }
            _selection.Clear();
        }

        private List<int> SelectedIds()
        {
            var ids = new List<int>(_selection.Count);
            foreach (UnitView view in _selection)
            {
                ids.Add(view.UnitId);
            }
            return ids;
        }

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

        private static FixVec2 ToFix(Vector3 world)
        {
            // Quantise to 1/100 so the command is a clean fixed-point value.
            return new FixVec2(
                Fix.FromRatio(Mathf.RoundToInt(world.x * 100f), 100),
                Fix.FromRatio(Mathf.RoundToInt(world.z * 100f), 100));
        }

        // ------------------------------------------------------------------
        // Effects

        private void SpawnShot(UnitView source, UnitView target, bool critical)
        {
            LineRenderer line = _linePool.Count > 0 ? _linePool.Pop() : CreateLine();
            line.gameObject.SetActive(true);
            line.SetPosition(0, source.transform.position + Vector3.up * 0.5f);
            line.SetPosition(1, target.transform.position + Vector3.up * 0.5f);
            Color c = critical ? Color.yellow : (source.Side == Scenario.PlayerSide ? UnitView.PlayerColor : UnitView.EnemyColor);
            line.startColor = line.endColor = c;
            float width = critical ? 0.5f : source.Def.Role == UnitRole.Heavy ? 0.3f : 0.1f;
            line.startWidth = line.endWidth = width;
            _shots.Add(new ShotEffect { Line = line, Until = Time.time + ShotEffectSeconds });
        }

        private LineRenderer CreateLine()
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.material = _lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private void UpdateShotEffects()
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

        // ------------------------------------------------------------------
        // Scene setup

        private void SetUpScene()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = camGo.AddComponent<Camera>();
            }
            if (_camera.GetComponent<RtsCamera>() == null)
            {
                _camera.gameObject.AddComponent<RtsCamera>();
            }
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.02f, 0.03f, 0.07f);

            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Sun");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            Vector2 half = new Vector2(40f, 25f);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Battlefield";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(half.x * 2f / 10f, 1f, half.y * 2f / 10f);
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.GetComponent<Renderer>().material.color = new Color(0.07f, 0.09f, 0.14f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _lineMaterial = new Material(shader);
        }

        // ------------------------------------------------------------------
        // HUD (IMGUI keeps the prototype free of scene assets)

        private void OnGUI()
        {
            foreach (UnitView view in _views.Values)
            {
                DrawHealthBar(view);
            }

            if (_dragging && Mouse.current != null)
            {
                Vector2 now = Mouse.current.position.ReadValue();
                Rect r = ScreenRectToGui(_dragStart, now);
                GUI.color = new Color(0.4f, 1f, 0.4f, 0.15f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            int player = _battle.CountUnits(Scenario.PlayerSide);
            int enemy = _battle.CountUnits(Scenario.EnemySide);
            string status = "时间 " + _battle.ElapsedSeconds.ToIntFloor() + " 秒    我方 " + player + "    敌方 " + enemy
                + "    速度 " + Speeds[_speedIndex] + "x" + (_paused ? "    【已暂停】" : "");
            GUI.Label(new Rect(12, 10, 700, 24), status);
            GUI.Label(new Rect(12, 30, 1100, 24),
                "左键选择/框选  右键移动或集火  A+右键攻击移动  S 停止  Ctrl+数字编组  空格暂停  Tab 变速  方向键/中键/滚轮 镜头");

            if (_battle.IsOver)
            {
                string result = _battle.Winner == Scenario.PlayerSide ? "胜利" : _battle.Winner == Scenario.EnemySide ? "失败" : "平局";
                var box = new Rect(Screen.width / 2f - 120, Screen.height / 2f - 50, 240, 100);
                GUI.Box(box, result + "\n用时 " + _battle.ElapsedSeconds.ToIntFloor() + " 秒");
                if (GUI.Button(new Rect(box.x + 60, box.y + 55, 120, 30), "再来一局"))
                {
                    Seed++;
                    StartBattle();
                }
            }
        }

        private void DrawHealthBar(UnitView view)
        {
            Vector3 sp = _camera.WorldToScreenPoint(view.transform.position + Vector3.up * (view.Def.Radius.ToFloat() + 0.8f));
            if (sp.z <= 0f)
            {
                return;
            }
            float width = 14f + view.Def.Radius.ToFloat() * 14f;
            float x = sp.x - width / 2f;
            float y = Screen.height - sp.y;

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 1, y - 1, width + 2, 7), Texture2D.whiteTexture);
            GUI.color = view.Side == Scenario.PlayerSide ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.95f, 0.3f, 0.25f);
            GUI.DrawTexture(new Rect(x, y, width * view.HpFraction, 3), Texture2D.whiteTexture);
            GUI.color = new Color(0.4f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(x, y + 3, width * view.ShieldFraction, 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static Rect ScreenRectToGui(Vector2 a, Vector2 b)
        {
            float xMin = Mathf.Min(a.x, b.x);
            float xMax = Mathf.Max(a.x, b.x);
            float yMin = Screen.height - Mathf.Max(a.y, b.y);
            float yMax = Screen.height - Mathf.Min(a.y, b.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
