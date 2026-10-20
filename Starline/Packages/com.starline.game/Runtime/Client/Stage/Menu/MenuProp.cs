using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// A unit model standing in the menu scene. Move, turn and scale this object freely; the
    /// model (from <see cref="ModelLibrary"/>) is rebuilt from <see cref="unitId"/> in the editor
    /// and in play mode, so the prefab only stores the settings below.
    /// </summary>
    [ExecuteAlways]
    [SelectionBase]
    public sealed class MenuProp : MonoBehaviour
    {
        [Tooltip("单位 id，对应 Resources/StarlineModels/<id>.fbx，例如 supply_ship、battleship、relay、hive")]
        public string unitId = "relay";

        [Tooltip("发光部位（底座环、水晶、火焰）的颜色")]
        public Color glowColor = new Color(0.44f, 0.82f, 0.71f);

        [Tooltip("发光强度：0 不亮，2~3 正常，越大泛光越强")]
        [Min(0f)] public float glowStrength = 2.5f;

        [Tooltip("运行时发光的呼吸幅度（0 不闪）")]
        [Min(0f)] public float glowPulse;

        [Tooltip("运行时上下浮动的幅度（单位：米，狮鹫骑士之类）")]
        [Min(0f)] public float bobHeight;

        private GameObject _model;
        private Material _hull;
        private Material _glow;
        private bool _dirty;
        private float _phase;

        private void OnEnable()
        {
            Rebuild();
            _phase = (transform.position.x * 0.37f + transform.position.z * 0.21f) % 6.28f;
        }

        private void OnDisable() => Clear();

        // Unity does not allow creating objects inside OnValidate, so the rebuild waits for Update.
        private void OnValidate() => _dirty = true;

        private void Update()
        {
            if (_dirty)
            {
                Rebuild();
            }
            if (!Application.isPlaying || _model == null)
            {
                return;
            }
            float t = Time.time + _phase;
            if (glowPulse > 0f)
            {
                _glow.SetColor("_EmissionColor", glowColor * (glowStrength + glowPulse * Mathf.Sin(t * 1.7f)));
            }
            if (bobHeight > 0f)
            {
                _model.transform.localPosition = Vector3.up * (bobHeight * Mathf.Sin(t * 1.1f));
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            Clear();
            _hull = ModelLibrary.CreateHullMaterial();
            _hull.hideFlags = HideFlags.DontSave;
            _glow = ModelLibrary.CreateGlowMaterial();
            _glow.hideFlags = HideFlags.DontSave;
            _glow.SetColor("_EmissionColor", glowColor * glowStrength);
            _model = ModelLibrary.Instantiate(unitId, transform);
            if (_model != null)
            {
                ModelLibrary.AssignMaterials(_model, _hull, _glow);
            }
            else
            {
                // Placeholder block so a mistyped id is easy to spot.
                _model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ViewUtil.DestroySafe(_model.GetComponent<Collider>());
                _model.name = "Model (缺少 " + unitId + ")";
                _model.transform.SetParent(transform, false);
                _model.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                _model.transform.localScale = new Vector3(1f, 1.2f, 1f);
                _model.GetComponent<Renderer>().sharedMaterial = _glow;
            }
            MenuLook.MarkGenerated(_model);
        }

        private void Clear()
        {
            ViewUtil.DestroySafe(_model);
            ViewUtil.DestroySafe(_hull);
            ViewUtil.DestroySafe(_glow);
            _model = null;
        }
    }
}
