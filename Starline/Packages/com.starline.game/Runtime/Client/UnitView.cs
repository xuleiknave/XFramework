using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Placeholder visual for one unit, built from primitives. In battle it interpolates
    /// between the last two simulation positions so movement looks smooth.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float AirHeight = 2.2f;

        private Vector3 _previous;
        private Vector3 _current;
        private Renderer[] _renderers;
        private Color _baseColor;
        private float _flashUntil;

        public int UnitId { get; private set; }
        public int Side { get; private set; }
        public UnitDef Def { get; private set; }

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

        public static UnitView Create(UnitDef def, int side, int unitId, Vector3 position, Transform parent)
        {
            var root = new GameObject(def.DisplayName + " #" + unitId);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<UnitView>();
            view.UnitId = unitId;
            view.Side = side;
            view.Def = def;
            view.Ammo = def.MaxAmmo;
            view._baseColor = def.Id == "brood_mother" ? ViewUtil.HiddenColor
                : def.IsStructure ? (side == BoardLayout.PlayerSide ? ViewUtil.SupplyColor : ViewUtil.EnemyColor * 0.85f)
                : ViewUtil.SideColor(side);
            view.BuildBody();
            if (def.IsAir)
            {
                position += Vector3.up * AirHeight;
            }
            view._previous = view._current = position;
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(side == BoardLayout.PlayerSide ? Vector3.right : Vector3.left);
            view._renderers = root.GetComponentsInChildren<Renderer>();
            return view;
        }

        /// <summary>Called after every simulation tick with the new state.</summary>
        public void PushState(Unit unit)
        {
            _previous = _current;
            _current = ViewUtil.ToWorld(unit.Position) + (Def.IsAir ? Vector3.up * AirHeight : Vector3.zero);
            HpFraction = unit.Hp.ToFloat() / Def.MaxHp.ToFloat();
            ShieldFraction = Def.MaxShield > Fix.Zero ? unit.Shield.ToFloat() / Def.MaxShield.ToFloat() : 0f;
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

            Color c = Time.time < _flashUntil ? Color.white : _baseColor;
            foreach (Renderer r in _renderers)
            {
                r.material.color = c;
            }
        }

        public void FlashHit() => _flashUntil = Time.time + 0.06f;

        private void BuildBody()
        {
            Transform t = transform;
            Color c = _baseColor;
            switch (Def.Role)
            {
                case UnitRole.Structure:
                    if (Def.IsSupplySource)
                    {
                        Height = 1.4f;
                        ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0.6f, 0f), new Vector3(3.4f, 1.2f, 3.4f), c);
                        ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, 1.4f, 0f), new Vector3(1.2f, 0.4f, 1.2f), c);
                    }
                    else
                    {
                        Height = 1.6f;
                        ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, 0.8f, 0f), new Vector3(0.35f, 0.8f, 0.35f), c);
                        GameObject top = ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 1.6f, 0f), new Vector3(0.45f, 0.45f, 0.45f), c);
                        top.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                    }
                    break;
                case UnitRole.Heavy:
                    Height = 2.2f;
                    float s = Def.Size >= 2 ? 1f : 0.62f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 1.1f * s, 0f), new Vector3(2.6f, 2.2f, 2.8f) * s, c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 2.0f * s, 1.3f * s), new Vector3(0.4f, 0.4f, 1.6f) * s, c);
                    Height *= s;
                    break;
                case UnitRole.Light:
                    Height = 0.8f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.6f, 1.5f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0.8f, 0.4f), new Vector3(0.25f, 0.25f, 0.9f), c);
                    break;
                case UnitRole.AntiAir:
                    Height = 1.2f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0.35f, 0f), new Vector3(1.1f, 0.5f, 1.4f), c);
                    GameObject barrel = ViewUtil.Primitive(PrimitiveType.Cylinder, t, new Vector3(0f, 0.9f, 0.2f), new Vector3(0.3f, 0.45f, 0.3f), c);
                    barrel.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                    break;
                case UnitRole.Artillery:
                    Height = 1f;
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.6f, 1.8f), c);
                    GameObject rack = ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0.95f, -0.1f), new Vector3(0.8f, 0.45f, 1.1f), c);
                    rack.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                    break;
                case UnitRole.Air:
                    Height = 0.4f;
                    ViewUtil.Primitive(PrimitiveType.Sphere, t, Vector3.zero, new Vector3(0.7f, 0.3f, 1.1f), c);
                    ViewUtil.Primitive(PrimitiveType.Cube, t, new Vector3(0f, 0f, -0.1f), new Vector3(1.6f, 0.08f, 0.4f), c);
                    break;
            }
        }
    }
}
