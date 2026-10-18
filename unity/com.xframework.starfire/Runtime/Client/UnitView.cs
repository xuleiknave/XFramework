using UnityEngine;
using XFramework.Battle;

namespace XFramework.Client
{
    /// <summary>
    /// Placeholder visual for one battle unit. Interpolates between the last two
    /// simulation positions so movement looks smooth at any frame rate.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        public static readonly Color PlayerColor = new Color(0.25f, 0.55f, 1f);
        public static readonly Color EnemyColor = new Color(1f, 0.45f, 0.15f);

        private Vector3 _previous;
        private Vector3 _current;
        private Renderer _body;
        private GameObject _selectionRing;
        private Color _baseColor;
        private float _flashUntil;

        public int UnitId { get; private set; }
        public int Side { get; private set; }
        public UnitDef Def { get; private set; }
        public float HpFraction { get; private set; } = 1f;
        public float ShieldFraction { get; private set; } = 1f;
        public bool Selected { get; private set; }

        public static UnitView Create(Unit unit, Transform parent)
        {
            var root = new GameObject(unit.Def.DisplayName + " #" + unit.Id);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<UnitView>();
            view.UnitId = unit.Id;
            view.Side = unit.Side;
            view.Def = unit.Def;
            view._baseColor = unit.Side == Scenario.PlayerSide ? PlayerColor : EnemyColor;
            view.BuildBody();
            view._current = view._previous = ToWorld(unit.Position);
            root.transform.position = view._current;
            root.transform.rotation = Quaternion.LookRotation(unit.Side == Scenario.PlayerSide ? Vector3.right : Vector3.left);
            return view;
        }

        public static Vector3 ToWorld(FixVec2 p) => new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());

        public void SetSelected(bool selected)
        {
            Selected = selected;
            _selectionRing.SetActive(selected);
        }

        /// <summary>Called after every simulation tick with the new state.</summary>
        public void PushState(Unit unit)
        {
            _previous = _current;
            _current = ToWorld(unit.Position);
            HpFraction = unit.Hp.ToFloat() / unit.Def.MaxHp.ToFloat();
            ShieldFraction = unit.Def.MaxShield > Fix.Zero ? unit.Shield.ToFloat() / unit.Def.MaxShield.ToFloat() : 0f;
        }

        public void Interpolate(float t, Vector3 lookTarget, bool hasLookTarget)
        {
            Vector3 pos = Vector3.Lerp(_previous, _current, t);
            Vector3 dir = hasLookTarget ? lookTarget - pos : _current - _previous;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 0.2f);
            }
            transform.position = pos;

            _body.material.color = Time.time < _flashUntil ? Color.white : _baseColor;
        }

        public void FlashHit() => _flashUntil = Time.time + 0.06f;

        private void BuildBody()
        {
            float r = Def.Radius.ToFloat();
            GameObject body;
            switch (Def.Role)
            {
                case UnitRole.Fighter:
                    body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    body.transform.localScale = new Vector3(r * 1.4f, r * 0.7f, r * 2f);
                    break;
                case UnitRole.Escort:
                    body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.transform.localScale = new Vector3(r * 1.2f, r * 0.8f, r * 2f);
                    break;
                default:
                    body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.transform.localScale = new Vector3(r * 0.9f, r * 0.6f, r * 2.2f);
                    break;
            }
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, r * 0.5f, 0f);
            _body = body.GetComponent<Renderer>();
            _body.material.color = _baseColor;

            _selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_selectionRing.GetComponent<Collider>());
            _selectionRing.transform.SetParent(transform, false);
            _selectionRing.transform.localScale = new Vector3(r * 2.6f, 0.02f, r * 2.6f);
            _selectionRing.GetComponent<Renderer>().material.color = new Color(0.4f, 1f, 0.4f);
            _selectionRing.SetActive(false);
        }
    }
}
