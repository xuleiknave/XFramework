using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// A unit model standing in the menu scene. Move, turn and scale this object freely; the
    /// model (from <see cref="ModelLibrary"/>) is rebuilt from <see cref="unitId"/> and
    /// <see cref="star"/> in the editor and in play mode, so the prefab only stores the settings
    /// below. Cartoon characters play their idle loop in play mode and stand on a glow ring like
    /// on the board.
    /// </summary>
    [ExecuteAlways]
    [SelectionBase]
    public sealed class MenuProp : MonoBehaviour
    {
        [Tooltip("单位 id，对应 Resources/StarlineModels/<id>.fbx，例如 supply_ship、relay、hive、shield_guard、priest")]
        public string unitId = "relay";

        [Tooltip("星级 1~5：卡通角色每档换一套外观（建筑只有一档）")]
        [Range(1, 5)] public int star = 1;

        [Tooltip("脚下发光环的半径（米），0 不画；和棋盘一致：1 格角色 0.7，2×2 角色 1.5")]
        [Min(0f)] public float groundRing;

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
        private Material _ringMaterial;
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
                float strength = glowStrength + glowPulse * Mathf.Sin(t * 1.7f);
                _glow.SetColor("_EmissionColor", glowColor * strength);
                if (_ringMaterial != null)
                {
                    _ringMaterial.color = glowColor * strength * 0.6f;
                }
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
            string modelKey = ModelLibrary.ModelKey(unitId, star);
            _hull = ModelLibrary.CreateHullMaterial(modelKey);
            _hull.hideFlags = HideFlags.DontSave;
            _glow = ModelLibrary.CreateGlowMaterial();
            _glow.hideFlags = HideFlags.DontSave;
            _glow.SetColor("_EmissionColor", glowColor * glowStrength);
            _model = ModelLibrary.Instantiate(modelKey, transform);
            if (_model != null)
            {
                ModelLibrary.AssignMaterials(_model, _hull, _glow);
                if (Application.isPlaying)
                {
                    // In edit mode the character stays in its bind pose; the idle loop starts in play mode.
                    UnitAnimator.Attach(_model, ModelLibrary.Clips(modelKey));
                }
                if (groundRing > 0f)
                {
                    BuildGroundRing();
                }
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

        /// <summary>Flat glow ring on the ground, a child of the model so it bobs along with fliers.</summary>
        private void BuildGroundRing()
        {
            var go = new GameObject("Ground ring");
            go.transform.SetParent(_model.transform, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ring = go.AddComponent<LineRenderer>();
            const int segments = 40;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = segments;
            ring.startWidth = ring.endWidth = 0.05f;
            ring.startColor = ring.endColor = Color.white;
            ring.alignment = LineAlignment.TransformZ;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            _ringMaterial = MenuLook.Unlit(glowColor * glowStrength * 0.6f);
            ring.sharedMaterial = _ringMaterial;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * groundRing, Mathf.Sin(a) * groundRing, -0.02f));
            }
        }

        private void Clear()
        {
            ViewUtil.DestroySafe(_model);
            ViewUtil.DestroySafe(_hull);
            ViewUtil.DestroySafe(_glow);
            ViewUtil.DestroySafe(_ringMaterial);
            _model = null;
            _ringMaterial = null;
        }
    }
}
