using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Visual for one unit: the generated model from <see cref="ModelLibrary"/> when there is one,
    /// otherwise a placeholder built from primitives. In battle it interpolates between the last
    /// two simulation positions so movement looks smooth.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float CraftHeight = 2.2f;

        private Vector3 _previous;
        private Vector3 _current;
        private Renderer[] _renderers;
        private Color _baseColor;
        private float _flashUntil;
        private Material _hull;
        private Material _glow;
        private Color _glowColor;

        public int UnitId { get; private set; }
        public int Side { get; private set; }
        public UnitDef Def { get; private set; }
        public int Star { get; private set; } = 1;

        // Latest state for the HUD.
        public float HpFraction { get; private set; } = 1f;
        public float ShieldFraction { get; private set; } = 1f;
        public int Ammo { get; private set; }
        public bool Supplied { get; private set; } = true;
        public bool InGrace { get; private set; }
        public float GraceSecondsLeft { get; private set; }

        /// <summary>Top of the model, used to place HUD bars.</summary>
        public Vector3 HeadPosition => transform.position + Vector3.up * (Height + 0.4f);

        private float Height { get; set; } = 1f;

        public static UnitView Create(UnitDef def, int side, int unitId, Vector3 position, Transform parent, int star = 1)
        {
            var root = new GameObject(def.DisplayName + " #" + unitId);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<UnitView>();
            view.UnitId = unitId;
            view.Side = side;
            view.Def = def;
            view.Star = star;
            view.Ammo = def.MaxAmmo;
            view._baseColor = def.Id == "brood_mother" ? ViewUtil.HiddenColor
                : def.Id == "wreck_fortress" ? new Color(0.62f, 0.55f, 0.48f)
                : def.IsStructure ? (side == BoardLayout.PlayerSide ? ViewUtil.SupplyColor : ViewUtil.EnemyColor * 0.85f)
                : ViewUtil.SideColor(side);
            string modelKey = ModelLibrary.ModelKey(def.Id, star);
            GameObject model = ModelLibrary.Instantiate(modelKey, root.transform);
            if (model != null)
            {
                view.UseModel(model, modelKey);
            }
            else
            {
                view.BuildBody();
            }
            if (def.IsCraft)
            {
                position += Vector3.up * CraftHeight;
            }
            view._previous = view._current = position;
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(side == BoardLayout.PlayerSide ? Vector3.right : Vector3.left);
            // Higher stars read as slightly bigger (★5 = 1.24×); costumes change at ★3 and ★5 (see ModelKey).
            root.transform.localScale = Vector3.one * (1f + 0.06f * (star - 1));
            view._renderers = root.GetComponentsInChildren<Renderer>();
            if (view._hull != null)
            {
                view.Height = view.MeasureHeight();
                return view;
            }
            foreach (Renderer r in view._renderers)
            {
                SceneLook.SetGlow(r.material, view._baseColor, def.IsStructure ? 0.35f : 0.2f);
            }
            return view;
        }

        /// <summary>Called after every simulation tick with the new state.</summary>
        public void PushState(Unit unit)
        {
            _previous = _current;
            _current = ViewUtil.ToWorld(unit.Position) + (Def.IsCraft ? Vector3.up * CraftHeight : Vector3.zero);
            HpFraction = unit.Hp.ToFloat() / unit.MaxHp.ToFloat();
            ShieldFraction = unit.MaxShield > Fix.Zero ? unit.Shield.ToFloat() / unit.MaxShield.ToFloat() : 0f;
            Ammo = unit.Ammo;
            Supplied = unit.Supplied;
            InGrace = unit.Supplied && !unit.InCoverage && !Def.IsStructure;
            float grace = Def.OffGridGraceSeconds.ToFloat() - unit.OffGridTicks / (float)BattleSim.TicksPerSecond;
            GraceSecondsLeft = Mathf.Max(0f, grace);
        }

        /// <summary>Deployment preview: marks whether the unit would start connected.</summary>
        public void SetPreviewSupplied(bool supplied)
        {
            Supplied = supplied;
            InGrace = false;
        }

        public void Interpolate(float t, Vector3 lookTarget, bool hasLookTarget)
        {
            Vector3 pos = Vector3.Lerp(_previous, _current, t);
            Vector3 dir = hasLookTarget ? lookTarget - pos : _current - _previous;
            dir.y = 0f;
            if (!Def.IsStatic && dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 0.2f);
            }
            transform.position = pos;
            if (_hull != null)
            {
                return; // model colours are updated in LateUpdate
            }

            Color c = Time.time < _flashUntil ? Color.white : _baseColor;
            foreach (Renderer r in _renderers)
            {
                r.material.color = c;
            }
        }

        public void FlashHit() => _flashUntil = Time.time + 0.06f;

        private void UseModel(GameObject model, string modelKey)
        {
            _hull = ModelLibrary.CreateHullMaterial(modelKey);
            _glow = ModelLibrary.CreateGlowMaterial();
            ModelLibrary.AssignMaterials(model, _hull, _glow);
            _glowColor = Def.Id == "brood_mother" ? ViewUtil.HiddenColor
                : Side == BoardLayout.EnemySide ? ViewUtil.UndeadGlowColor
                : Def.IsStructure ? ViewUtil.SupplyColor
                : ViewUtil.PlayerColor;
        }

        private float MeasureHeight()
        {
            float top = transform.position.y;
            foreach (Renderer r in _renderers)
            {
                top = Mathf.Max(top, r.bounds.max.y);
            }
            return top - transform.position.y;
        }

        /// <summary>
        /// Model look (art direction: the base ring and magic glow show mana network state): steady side colour when supplied,
        /// dim pulsing red when cut off, blinking yellow while a griffin squad is off-grid.
        /// </summary>
        private void LateUpdate()
        {
            if (_hull == null)
            {
                return;
            }
            _hull.SetColor("_EmissionColor", Time.time < _flashUntil ? Color.white * 0.6f : Color.black);
            Color glow = _glowColor;
            float strength = 2.5f;
            if (!Def.IsStructure && !Supplied)
            {
                glow = ViewUtil.CutColor;
                strength = 0.5f + 0.6f * Mathf.PingPong(Time.time * 2f, 1f);
            }
            else if (InGrace)
            {
                glow = ViewUtil.GraceColor;
                strength = Mathf.Repeat(Time.time * 2f, 1f) < 0.5f ? 2.5f : 0.8f;
            }
            _glow.SetColor("_EmissionColor", glow * strength);
        }

        private void OnDestroy()
        {
            if (_hull != null)
            {
                Destroy(_hull);
                Destroy(_glow);
            }
        }

        /// <summary>
        /// Placeholder models: angular hulls for the Morningstar fleet, rounded organic
        /// shapes for the Swarm, so the two sides read differently at a glance.
        /// </summary>
        private void BuildBody()
        {
            Transform t = transform;
            Color c = _baseColor;
            bool swarm = Side == BoardLayout.EnemySide;
            const float hover = 0.5f;

            if (Def.IsSupplySource)
            {
                Height = 1.6f;
                if (swarm)
                {
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0f, 1f, 0f), new Vector3(3.2f, 1.8f, 3.2f), c);
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0.9f, 1.6f, 0.6f), new Vector3(1.2f, 1.2f, 1.2f), c);
                }
                else
                {
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.4f, 0f), new Vector3(1.6f, 0.8f, 3.6f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0.95f, hover + 0.3f, -0.4f), new Vector3(0.6f, 0.6f, 2.2f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(-0.95f, hover + 0.3f, -0.4f), new Vector3(0.6f, 0.6f, 2.2f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 1f, -1f), new Vector3(0.6f, 0.5f, 0.8f), c);
                }
                return;
            }
            if (Def.IsRelay)
            {
                Height = 1.8f;
                ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, 0.9f, 0f), new Vector3(0.18f, 0.9f, 0.18f), c);
                ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, 1.5f, 0f), new Vector3(0.9f, 0.04f, 0.9f), c);
                GameObject core = ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0f, 1.9f, 0f), Vector3.one * 0.35f, c);
                core.name = "Beacon core";
                return;
            }

            if (swarm)
            {
                BuildSwarmBody(t, c);
                return;
            }

            switch (Def.Role)
            {
                case UnitRole.Heavy: // battleship
                    Height = 1.9f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.5f, 0f), new Vector3(1.6f, 0.9f, 3.6f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.4f, 2f), new Vector3(1f, 0.6f, 0.8f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 1.2f, -0.8f), new Vector3(0.7f, 0.7f, 0.9f), c);
                    ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, hover + 1.05f, 0.7f), new Vector3(0.6f, 0.12f, 0.6f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 1.15f, 1.2f), new Vector3(0.18f, 0.18f, 1f), c);
                    break;
                case UnitRole.Light: // frigate
                    Height = 1f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.25f, 0f), new Vector3(0.7f, 0.4f, 1.8f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.25f, 1f), new Vector3(0.4f, 0.3f, 0.5f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.55f, -0.3f), new Vector3(0.35f, 0.3f, 0.5f), c);
                    break;
                case UnitRole.PointDefense: // point-defence ship
                    Height = 1.1f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.25f, 0f), new Vector3(0.9f, 0.4f, 1.6f), c);
                    ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0.25f, hover + 0.6f, 0.4f), new Vector3(0.3f, 0.15f, 0.3f), c);
                    ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(-0.25f, hover + 0.6f, 0.4f), new Vector3(0.3f, 0.15f, 0.3f), c);
                    ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, hover + 0.6f, -0.4f), new Vector3(0.3f, 0.15f, 0.3f), c);
                    break;
                case UnitRole.Artillery: // missile ship
                    Height = 1.1f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, hover + 0.25f, 0f), new Vector3(0.8f, 0.4f, 2f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0.45f, hover + 0.45f, 0.1f), new Vector3(0.35f, 0.35f, 1.2f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(-0.45f, hover + 0.45f, 0.1f), new Vector3(0.35f, 0.35f, 1.2f), c);
                    break;
                case UnitRole.Craft: // strike craft wing: three small fighters
                    Height = 0.4f;
                    foreach (Vector3 offset in new[] { new Vector3(0f, 0f, 0.4f), new Vector3(0.5f, 0f, -0.2f), new Vector3(-0.5f, 0f, -0.2f) })
                    {
                        ViewUtil.Primitive(PrimitiveType.Cube, t, offset, new Vector3(0.18f, 0.1f, 0.55f), c);
                        ViewUtil.Primitive(PrimitiveType.Cube, t, offset + new Vector3(0f, 0f, -0.1f), new Vector3(0.6f, 0.04f, 0.2f), c);
                    }
                    break;
            }
        }

        private void BuildSwarmBody(Transform t, Color c)
        {
            switch (Def.Role)
            {
                case UnitRole.Heavy:
                    float s = Def.Size >= 2 ? 1f : 0.6f;
                    Height = 2.2f * s;
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0f, 1.1f * s, 0f), new Vector3(2.4f, 1.8f, 3f) * s, c);
                    ViewUtil.Primitive(PrimitiveType.Capsule, t, new Vector3(0.8f * s, 0.8f * s, 1.2f * s), new Vector3(0.5f, 0.6f, 0.5f) * s, c);
                    ViewUtil.Primitive(PrimitiveType.Capsule, t, new Vector3(-0.8f * s, 0.8f * s, 1.2f * s), new Vector3(0.5f, 0.6f, 0.5f) * s, c);
                    break;
                case UnitRole.Light:
                    Height = 0.9f;
                    ViewUtil.Primitive(PrimitiveType.Capsule, t, new Vector3(0f, 0.6f, 0f), new Vector3(0.6f, 0.6f, 0.6f), c).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0f, 0.6f, 0.7f), Vector3.one * 0.4f, c);
                    break;
                case UnitRole.Artillery:
                    Height = 1.4f;
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1f, 1.6f), c);
                    ViewUtil.Primitive(PrimitiveType.Capsule, t, new Vector3(0f, 1.2f, 0.3f), new Vector3(0.4f, 0.5f, 0.4f), c);
                    break;
                case UnitRole.Craft:
                    Height = 0.4f;
                    foreach (Vector3 offset in new[] { new Vector3(0f, 0f, 0.3f), new Vector3(0.4f, 0.1f, -0.2f), new Vector3(-0.4f, -0.1f, -0.2f) })
                    {
                        ViewUtil.Primitive(PrimitiveType.Sphere, t, offset, new Vector3(0.3f, 0.2f, 0.45f), c);
                    }
                    break;
                default:
                    Height = 1f;
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, new Vector3(0f, 0.5f, 0f), Vector3.one, c);
                    break;
            }
        }
    }
}
