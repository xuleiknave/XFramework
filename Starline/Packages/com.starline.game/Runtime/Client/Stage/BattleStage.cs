using System;
using System.Collections.Generic;
using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// The 3D side of the game: board, ship models, shots and explosions, and the camera.
    /// Screens drive it; it knows nothing about menus or the expedition.
    /// </summary>
    public sealed class BattleStage
    {
        private const float ShotSeconds = 0.08f;

        private readonly Transform _root;
        private readonly BoardView _board;
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly Stack<LineRenderer> _linePool = new Stack<LineRenderer>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private Transform _unitRoot;
        private BattleSim _battle;
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

        public BattleStage(Transform parent, Camera camera)
        {
            Camera = camera;
            _root = new GameObject("Stage").transform;
            _root.SetParent(parent, false);
            _board = BoardView.Create(_root);
            // No ?? here: Unity objects override == null, which the null-coalescing operator ignores.
            CameraRig rig = camera.GetComponent<CameraRig>();
            Rig = rig != null ? rig : camera.gameObject.AddComponent<CameraRig>();
            Rig.SnapToDeploy();
            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(_root, false);
        }

        public Camera Camera { get; }
        public CameraRig Rig { get; }

        /// <summary>Ship models currently on the board, by id (preview ids during deployment, unit ids in battle).</summary>
        public IReadOnlyDictionary<int, UnitView> Views => _views;

        /// <summary>Raised whenever the set of views is rebuilt, so overlays can follow.</summary>
        public event Action ViewsRebuilt;

        public void SetVisible(bool visible) => _root.gameObject.SetActive(visible);

        public void Clear()
        {
            _battle = null;
            ClearEffects();
            ClearViews();
            _board.HideGhost();
            _board.ShowIntersections(false);
            ViewsRebuilt?.Invoke();
        }

        // ------------------------------------------------------------------
        // Deployment preview

        /// <summary>Shows both formations before battle, with coverage tint and supply lines.</summary>
        public void ShowFormations(Formation player, Formation enemy)
        {
            _battle = null;
            ClearViews();
            SupplyNetwork playerNet = SupplyNetwork.ForFormation(player, BoardLayout.PlayerSide);
            SupplyNetwork enemyNet = SupplyNetwork.ForFormation(enemy, BoardLayout.EnemySide);
            int id = 1;
            id = SpawnPreview(player, BoardLayout.PlayerSide, playerNet, id);
            SpawnPreview(enemy, BoardLayout.EnemySide, enemyNet, id);

            _board.TintCoverage(playerNet, enemyNet);
            _board.BeginNetworks();
            _board.DrawNetwork(playerNet, BoardLayout.PlayerSide);
            _board.DrawNetwork(enemyNet, BoardLayout.EnemySide);
            _board.EndNetworks();
            ViewsRebuilt?.Invoke();
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

        public void ShowGhost(UnitDef def, int depth, int row, bool valid) => _board.ShowGhost(def, depth, row, valid);

        public void HideGhost() => _board.HideGhost();

        public void ShowIntersections(bool show) => _board.ShowIntersections(show);

        // ------------------------------------------------------------------
        // Battle

        public void BeginBattle(BattleSim battle)
        {
            _battle = battle;
            ClearEffects();
            ClearViews();
            _board.HideGhost();
            _board.ShowIntersections(false);
            foreach (Unit unit in battle.Units)
            {
                UnitView view = UnitView.Create(unit.Def, unit.Side, unit.Id, ViewUtil.ToWorld(unit.Position), _unitRoot, unit.Star);
                view.PushState(unit);
                view.PushState(unit);
                _views.Add(unit.Id, view);
            }
            RefreshNetworks();
            ViewsRebuilt?.Invoke();
        }

        /// <summary>
        /// Applies the events of the tick that just ran: shots, hits, explosions. Messages
        /// worth showing in the battle feed go to <paramref name="feed"/>.
        /// </summary>
        public void AfterStep(bool withEffects, Action<string> feed)
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
                            feed(def.IsRelay ? who + " 魔网节点被摧毁！" : def.IsSupplySource ? who + " 魔网源头被摧毁！" : who + " 被击败");
                            if (withEffects)
                            {
                                SpawnFlash(dead.transform.position + Vector3.up * 0.5f, def.Size >= 2 ? 4f : 2f, 0.45f, new Color(1f, 0.8f, 0.4f));
                            }
                            _views.Remove(e.TargetId);
                            UnityEngine.Object.Destroy(dead.gameObject);
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
                            feed((unit.Side == BoardLayout.PlayerSide ? "我方 " : "敌方 ") + unit.Def.DisplayName + " 法力耗尽");
                        }
                        break;
                    }
                    case BattleEventType.SkillCast:
                        if (e.SourceId == BoardLayout.PlayerSide)
                        {
                            feed("释放【" + CommanderSkillDef.All[e.Amount.ToIntFloor()].DisplayName + "】");
                        }
                        break;
                    case BattleEventType.StrikeLanded:
                        if (withEffects)
                        {
                            SpawnFlash(ViewUtil.ToWorld(e.Point) + Vector3.up * 0.3f, CommanderSkillDef.StrikeRadius.ToFloat() * 2f, 0.6f, new Color(1f, 0.35f, 0.3f));
                        }
                        break;
                    case BattleEventType.BattleEnded:
                        feed(e.SourceId == BoardLayout.PlayerSide ? "胜利！" : e.SourceId == BoardLayout.EnemySide ? "失败" : "平局");
                        break;
                }
            }
            foreach (Unit unit in _battle.Units)
            {
                if (_views.TryGetValue(unit.Id, out UnitView view))
                {
                    view.PushState(unit);
                }
            }
        }

        /// <summary>Smooths ship movement between ticks; <paramref name="t"/> is the fraction of the next tick.</summary>
        public void Interpolate(float t)
        {
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
            if (--_tintCountdown <= 0)
            {
                RefreshNetworks();
                _tintCountdown = 10;
            }
        }

        public void RefreshNetworks()
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

        /// <summary>Per-frame effect updates (call every frame).</summary>
        public void Tick()
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
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                Flash f = _flashes[i];
                float k = (Time.time - f.Start) / f.Duration;
                if (k >= 1f)
                {
                    UnityEngine.Object.Destroy(f.Sphere);
                    _flashes.RemoveAt(i);
                    continue;
                }
                f.Sphere.transform.localScale = Vector3.one * (f.MaxScale * Mathf.Sin(k * Mathf.PI));
            }
        }

        // ------------------------------------------------------------------
        // Picking

        public bool TryGetGroundPoint(Vector2 screen, out Vector3 point)
        {
            Ray ray = Camera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        public static FixVec2 ToFix(Vector3 world) => new FixVec2(
            Fix.FromRatio(Mathf.RoundToInt(world.x * 100f), 100),
            Fix.FromRatio(Mathf.RoundToInt(world.z * 100f), 100));

        // ------------------------------------------------------------------
        // Effects

        private void SpawnShot(UnitView source, UnitView target, bool critical)
        {
            LineRenderer line = _linePool.Count > 0 ? _linePool.Pop() : ViewUtil.Line(_root, "Shot");
            line.gameObject.SetActive(true);
            Color c = critical ? Color.yellow : (source.Side == BoardLayout.PlayerSide ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.65f, 0.4f));
            float width = critical ? 0.3f : source.Def.Role == UnitRole.Artillery ? 0.18f : 0.08f;
            ViewUtil.SetLine(line, source.transform.position + Vector3.up * 0.6f, target.transform.position + Vector3.up * 0.6f, c, width, critical ? 5f : 3f);
            _shots.Add(new Shot { Line = line, Until = Time.time + ShotSeconds });
        }

        private void SpawnFlash(Vector3 position, float size, float duration, Color color)
        {
            GameObject sphere = ViewUtil.Primitive(PrimitiveType.Sphere, _root, position, Vector3.zero, color);
            sphere.GetComponent<Renderer>().material = ViewUtil.Unlit;
            sphere.GetComponent<Renderer>().material.color = color * 4f;
            _flashes.Add(new Flash { Sphere = sphere, Start = Time.time, Duration = duration, MaxScale = size });
        }

        private void ClearEffects()
        {
            foreach (Shot s in _shots)
            {
                s.Line.gameObject.SetActive(false);
                _linePool.Push(s.Line);
            }
            _shots.Clear();
            foreach (Flash f in _flashes)
            {
                UnityEngine.Object.Destroy(f.Sphere);
            }
            _flashes.Clear();
        }

        private void ClearViews()
        {
            if (_unitRoot != null)
            {
                UnityEngine.Object.Destroy(_unitRoot.gameObject);
            }
            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(_root, false);
            _views.Clear();
        }
    }
}
