using System.Collections.Generic;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Stars on a dome around this object (put it at the camera): one generated mesh of small
    /// quads facing the centre. Change the numbers and the sky regenerates.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MenuSky : MonoBehaviour
    {
        [Min(0)] public int starCount = 500;
        public int seed = 7;
        [Min(10f)] public float radius = 260f;
        [Tooltip("星星分布的水平角度范围（度，0 是正前方）")]
        public float spread = 100f;
        [Tooltip("最低和最高仰角（度）")]
        public float minPitch = 4f;
        public float maxPitch = 74f;
        public Color color = new Color(0.85f, 0.9f, 1f);
        [Min(0f)] public float brightness = 1.6f;
        [Tooltip("约有多少比例的星星更大更亮")]
        [Range(0f, 1f)] public float brightShare = 0.08f;

        private Mesh _mesh;
        private Material _material;
        private bool _dirty;

        private void OnEnable() => Rebuild();

        private void OnValidate() => _dirty = true;

        private void OnDisable()
        {
            ViewUtil.DestroySafe(_mesh);
            ViewUtil.DestroySafe(_material);
            _mesh = null;
            _material = null;
        }

        private void Update()
        {
            if (_dirty)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            OnDisable();
            var random = new System.Random(seed);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < starCount; i++)
            {
                float yaw = ((float)random.NextDouble() * 2f - 1f) * spread;
                float pitch = minPitch + Mathf.Pow((float)random.NextDouble(), 0.7f) * (maxPitch - minPitch);
                Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                float size = 0.35f + (float)random.NextDouble() * (random.NextDouble() < brightShare ? 1.2f : 0.5f);
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized * size;
                Vector3 up = Vector3.Cross(dir, right).normalized * size;
                Vector3 c = dir * radius;
                int start = vertices.Count;
                vertices.Add(c - right - up);
                vertices.Add(c - right + up);
                vertices.Add(c + right + up);
                vertices.Add(c + right - up);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
            _mesh = new Mesh { name = "Stars", hideFlags = HideFlags.DontSave };
            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _material = MenuLook.Unlit(color * brightness);
            MeshRenderer r = GetComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
