using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    public enum DeploySelection
    {
        None,
        Ship,
        Relay,
        Source,
    }

    /// <summary>
    /// Deployment logic: picking ships from the fleet, placing them on cells and relays on
    /// intersections, moving and removing them, and the pre-battle forecast. The deploy
    /// screen shows its state; mouse input on the board comes in through <see cref="Tick"/>.
    /// </summary>
    public sealed class DeployController
    {
        private readonly ExpeditionRun _run;
        private readonly Encounter _encounter;
        private readonly BattleStage _stage;
        /// <summary>Where a unit picked up from the board came from, so cancelling puts it back.</summary>
        private Placement _pickedOrigin;
        private int _hoverDepth = -1;
        private int _hoverRow = -1;
        private bool _hoverValid;

        public DeployController(ExpeditionRun run, Encounter encounter, BattleStage stage)
        {
            _run = run;
            _encounter = encounter;
            _stage = stage;
            Intel = encounter.IntelPoints;
        }

        public DeploySelection Selection { get; private set; }
        public int SelectedShipId { get; private set; }
        public int Intel { get; private set; }
        public ForecastResult Forecast { get; private set; }
        public List<int> ForecastHistory { get; } = new List<int>();
        public List<string> Errors { get; private set; } = new List<string>();
        public string Notice { get; private set; }
        public int SuppliedShips { get; private set; }
        public int DeployedShips { get; private set; }

        public Formation Formation => _run.Fleet.Formation;
        public Fleet Fleet => _run.Fleet;
        public int CommandLimit => _run.CommandLimit;
        public bool CanFight => Errors.Count == 0;

        /// <summary>Raised after anything the screen shows has changed.</summary>
        public event Action Changed;

        public UnitDef SelectedDef
        {
            get
            {
                switch (Selection)
                {
                    case DeploySelection.Ship:
                        return Fleet.Get(SelectedShipId)?.Def;
                    case DeploySelection.Relay:
                        return UnitCatalog.Relay;
                    case DeploySelection.Source:
                        return UnitCatalog.SupplyShip;
                    default:
                        return null;
                }
            }
        }

        public void Begin() => Refresh();

        // ------------------------------------------------------------------
        // Selection (called by the roster buttons)

        public void SelectShip(int shipId)
        {
            Placement placed = Formation.FindShip(shipId);
            if (placed != null)
            {
                PickUp(placed);
                return;
            }
            Select(DeploySelection.Ship, shipId);
        }

        public void SelectRelay() => Select(DeploySelection.Relay);

        public void Select(DeploySelection selection, int shipId = 0)
        {
            CancelSelection();
            Selection = selection;
            SelectedShipId = shipId;
            Changed?.Invoke();
        }

        /// <summary>Drops the current selection; a unit picked up from the board goes back to where it was.</summary>
        public void CancelSelection()
        {
            bool restored = false;
            if (_pickedOrigin != null)
            {
                Formation.Placements.Add(_pickedOrigin);
                _pickedOrigin = null;
                restored = true;
            }
            bool had = Selection != DeploySelection.None;
            Selection = DeploySelection.None;
            SelectedShipId = 0;
            if (restored)
            {
                Refresh();
            }
            else if (had)
            {
                Changed?.Invoke();
            }
        }

        public void AutoFill()
        {
            CancelSelection();
            AutoDeploy.FillFormation(Fleet, CommandLimit);
            Refresh();
        }

        public void ClearFormation()
        {
            CancelSelection();
            Formation.Placements.RemoveAll(p => !p.Def.IsSupplySource);
            Refresh();
        }

        public void RunForecast()
        {
            if (Intel <= 0 || Errors.Count > 0)
            {
                return;
            }
            Intel--;
            Forecast = BattleForecast.Run(_encounter, Formation);
            ForecastHistory.Add(Mathf.RoundToInt((float)Forecast.WinRate * 100f));
            if (ForecastHistory.Count > 5)
            {
                ForecastHistory.RemoveAt(0);
            }
            Changed?.Invoke();
        }

        /// <summary>Puts back anything still held and returns the formation to fight with.</summary>
        public Formation Finish()
        {
            CancelSelection();
            _stage.HideGhost();
            _stage.ShowIntersections(false);
            return Formation.Clone();
        }

        // ------------------------------------------------------------------
        // Board input

        /// <summary>Per-frame board input. <paramref name="pointerOverUi"/> blocks world clicks.</summary>
        public void Tick(bool pointerOverUi)
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
            UnitDef selected = SelectedDef;
            if (selected != null && _hoverDepth >= 0 && !pointerOverUi)
            {
                _stage.ShowGhost(selected, _hoverDepth, _hoverRow, _hoverValid);
            }
            else
            {
                _stage.HideGhost();
            }
            _stage.ShowIntersections(Selection == DeploySelection.Relay);

            if (pointerOverUi)
            {
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                OnLeftClick();
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                OnRightClick();
            }
        }

        /// <summary>Finds the cell (or intersection, when placing a relay) under the mouse.</summary>
        private void UpdateHover(Vector2 screen)
        {
            _hoverDepth = _hoverRow = -1;
            _hoverValid = false;
            if (!_stage.TryGetGroundPoint(screen, out Vector3 p))
            {
                return;
            }

            float cell = BoardLayout.CellSize.ToFloat();
            if (Selection == DeploySelection.Relay)
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
                bool available = Selection != DeploySelection.Relay || Fleet.RelaysLeft > 0;
                _hoverValid = available
                    && FormationRules.CanPlace(Formation, def, _hoverDepth, _hoverRow)
                    && Formation.CommandPointsUsed() + def.CommandCost <= CommandLimit;
            }
        }

        private void OnLeftClick()
        {
            if (Selection != DeploySelection.None)
            {
                if (_hoverDepth < 0)
                {
                    return;
                }
                if (!_hoverValid)
                {
                    Notice = Selection == DeploySelection.Relay && Fleet.RelaysLeft <= 0 ? "法力水晶已经全部放下"
                        : "这里放不下，或者指挥点不够";
                    Changed?.Invoke();
                    return;
                }
                switch (Selection)
                {
                    case DeploySelection.Ship:
                        Fleet.Deploy(Fleet.Get(SelectedShipId), _hoverDepth, _hoverRow);
                        break;
                    case DeploySelection.Relay:
                        Formation.Add(UnitCatalog.Relay.Id, _hoverDepth, _hoverRow);
                        break;
                    case DeploySelection.Source:
                        Formation.Add(UnitCatalog.SupplyShip.Id, _hoverDepth, _hoverRow);
                        break;
                }
                _pickedOrigin = null;
                if (Selection != DeploySelection.Relay || Fleet.RelaysLeft <= 0)
                {
                    Selection = DeploySelection.None;
                    SelectedShipId = 0;
                }
                Notice = null;
                Refresh();
                return;
            }

            // No selection: pick up the unit (or relay) under the mouse to move it.
            Placement picked = PlacementUnderMouse();
            if (picked != null)
            {
                PickUp(picked);
            }
        }

        private void OnRightClick()
        {
            if (Selection != DeploySelection.None)
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
                Notice = "源晶塔不能撤下，可以左键拿起来换位置";
                Changed?.Invoke();
                return;
            }
            Formation.Placements.Remove(picked);
            Refresh();
        }

        private void PickUp(Placement picked)
        {
            Select(picked.IsRelay ? DeploySelection.Relay : picked.Def.IsSupplySource ? DeploySelection.Source : DeploySelection.Ship, picked.ShipId);
            Formation.Placements.Remove(picked);
            _pickedOrigin = picked;
            Refresh();
        }

        private Placement PlacementUnderMouse()
        {
            if (Mouse.current == null || !_stage.TryGetGroundPoint(Mouse.current.position.ReadValue(), out Vector3 p))
            {
                return null;
            }
            float cell = BoardLayout.CellSize.ToFloat();

            // Relays first: they sit on intersections, so check a small radius around the nearest one.
            int dl = Mathf.RoundToInt(-p.x / cell);
            int rl = Mathf.RoundToInt(p.z / cell + BoardLayout.Width / 2f);
            Placement relay = Formation.RelayAt(dl, rl);
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
            return Formation.UnitAt(Mathf.FloorToInt(-p.x / cell), Mathf.FloorToInt(p.z / cell + BoardLayout.Width / 2f));
        }

        /// <summary>Rebuilds the board preview and the derived numbers after a formation change.</summary>
        private void Refresh()
        {
            _stage.ShowFormations(Formation, _encounter.Enemy);
            Errors = FormationRules.Validate(Formation, CommandLimit);
            SupplyNetwork network = SupplyNetwork.ForFormation(Formation, BoardLayout.PlayerSide);
            SuppliedShips = DeployedShips = 0;
            foreach (Placement p in Formation.Placements)
            {
                if (p.Def.HasWeapon)
                {
                    DeployedShips++;
                    if (network.CoversPlacement(p, BoardLayout.PlayerSide))
                    {
                        SuppliedShips++;
                    }
                }
            }
            Changed?.Invoke();
        }
    }
}
