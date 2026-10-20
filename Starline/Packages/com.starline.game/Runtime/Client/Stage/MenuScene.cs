using System.Collections.Generic;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Live 3D background of the main menu and save slots (art canvas, 主菜单): night over
    /// 灯塔城. The source tower still glows at the end of the mana network with the expedition
    /// gathered around it; the crystals beyond it stand dark one after another, and far away an
    /// undead altar burns green. Built from the unit models at runtime, far from the board, and
    /// shown only while a screen asks for it (<see cref="UiScreen.UsesMenuScene"/>).
    /// </summary>
    public sealed class MenuScene
    {
        /// <summary>Far from the battle board so the two never overlap.</summary>
        private static readonly Vector3 Origin = new Vector3(0f, 0f, 400f);
        private static readonly Vector3 CameraPosition = new Vector3(0f, 3.2f, -12f);
        private static readonly Vector3 CameraLookAt = new Vector3(3.5f, 2.2f, 6f);

        private static readonly Color GroundColor = new Color(0.05f, 0.07f, 0.11f);
        private static readonly Color MountainColor = new Color(0.035f, 0.05f, 0.09f);
        private static readonly Color DarkCrystal = ViewUtil.SupplyColor * 0.04f;

        private readonly Transform _root;
        private readonly Camera _camera;
        private readonly CameraRig _rig;
        private readonly Material _hull;
        private Light _towerLight;
        private Material _litCrystalGlow;
        private Transform _griffin;
        private float _griffinHeight;
        private float _time;

        public MenuScene(Transform parent, Camera camera, CameraRig rig)
        {
            _camera = camera;
            _rig = rig;
            _root = new GameObject("Menu Scene").transform;
            _root.SetParent(parent, false);
            _root.position = Origin;
            _hull = ModelLibrary.CreateHullMaterial();
            Build();
            _root.gameObject.SetActive(false);
        }

        public bool Visible => _root.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (visible == Visible)
            {
                return;
            }
            _root.gameObject.SetActive(visible);
            SceneLook.SetNight(visible);
            // The rig only moves the camera during its own transitions; snapping stops any
            // transition, then the menu (while visible) or the deploy pose (after) owns the camera.
            _rig.SnapToDeploy();
            if (visible)
            {
                _time = 0f;
                PlaceCamera();
            }
        }

        public void Tick(float deltaTime)
        {
            if (!Visible)
            {
                return;
            }
            _time += deltaTime;
            PlaceCamera();
            _towerLight.intensity = 6f + 0.8f * Mathf.Sin(_time * 1.3f);
            _litCrystalGlow.SetColor("_EmissionColor", ViewUtil.SupplyColor * (2.4f + 0.6f * Mathf.Sin(_time * 1.7f)));
            Vector3 p = _griffin.localPosition;
            p.y = _griffinHeight + 0.18f * Mathf.Sin(_time * 1.1f);
            _griffin.localPosition = p;
        }

        /// <summary>Slow drift so the scene feels alive behind the menu.</summary>
        private void PlaceCamera()
        {
            Vector3 drift = new Vector3(0.6f * Mathf.Sin(_time * 0.07f), 0.15f * Mathf.Sin(_time * 0.11f), 0f);
            Vector3 pos = _root.position + CameraPosition + drift;
            _camera.transform.position = pos;
            _camera.transform.rotation = Quaternion.LookRotation(_root.position + CameraLookAt - pos, Vector3.up);
        }

        // ------------------------------------------------------------------
        // Building

        private void Build()
        {
            GameObject ground = ViewUtil.Primitive(PrimitiveType.Cylinder, _root, new Vector3(0f, -0.05f, 20f), new Vector3(240f, 0.05f, 240f), GroundColor);
            ground.name = "Ground";

            // The source tower of 灯塔城 and the light it throws on the expedition.
            Vector3 tower = new Vector3(5f, 0f, 4f);
            Place("supply_ship", tower, 200f, 1.5f, ViewUtil.SupplyColor, 3f);
            _towerLight = new GameObject("Tower Light").AddComponent<Light>();
            _towerLight.transform.SetParent(_root, false);
            _towerLight.transform.localPosition = tower + new Vector3(0f, 4.5f, -1f);
            _towerLight.type = LightType.Point;
            _towerLight.color = ViewUtil.SupplyColor;
            _towerLight.range = 16f;
            _towerLight.shadows = LightShadows.None;

            // The expedition, facing the way out along the dark crystals.
            Color blue = ViewUtil.PlayerColor;
            Place("battleship", new Vector3(2.2f, 0f, 1.4f), 30f, 1f, blue, 2.5f);
            Place("frigate", new Vector3(3.8f, 0f, 0.1f), 15f, 1f, blue, 2.5f);
            Place("missile_ship", new Vector3(6.9f, 0f, 0.5f), 40f, 1f, blue, 2.5f);
            Place("pd_ship", new Vector3(8.3f, 0f, 2.3f), 50f, 1f, blue, 2.5f);
            _griffinHeight = 2.6f;
            _griffin = Place("strike_craft", new Vector3(1.0f, _griffinHeight, 4.5f), 35f, 1f, blue, 2.5f);

            // The mana network: lit up to the first crystal, dark beyond it.
            Vector3[] crystals =
            {
                new Vector3(8.5f, 0f, 10f), new Vector3(11f, 0f, 17f), new Vector3(13f, 0f, 25f),
                new Vector3(14.2f, 0f, 34f), new Vector3(15f, 0f, 44f), new Vector3(15.4f, 0f, 55f),
            };
            Transform first = Place("relay", crystals[0], 0f, 1f, ViewUtil.SupplyColor, 2.4f);
            _litCrystalGlow = GlowOf(first);
            for (int i = 1; i < crystals.Length; i++)
            {
                Place("relay", crystals[i], 0f, 1f, DarkCrystal, 1f);
            }
            ManaLine(tower + new Vector3(0f, 3.2f, 0f), crystals[0] + new Vector3(0f, 1.3f, 0f));

            // Far away, the undead are waiting.
            Place("hive", new Vector3(19f, 0f, 70f), 200f, 1.6f, ViewUtil.UndeadGlowColor, 3f);

            Mountains();
            Sky();
        }

        /// <summary>Puts a unit model on the ground; returns its root. Falls back to a block when the model is missing.</summary>
        private Transform Place(string unitId, Vector3 localPos, float yaw, float scale, Color glow, float glowStrength)
        {
            var root = new GameObject(unitId).transform;
            root.SetParent(_root, false);
            root.localPosition = localPos;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            root.localScale = Vector3.one * scale;
            Material glowMaterial = ModelLibrary.CreateGlowMaterial();
            glowMaterial.SetColor("_EmissionColor", glow * glowStrength);
            GameObject model = ModelLibrary.Instantiate(unitId, root);
            if (model != null)
            {
                ModelLibrary.AssignMaterials(model, _hull, glowMaterial);
            }
            else
            {
                GameObject block = ViewUtil.Primitive(PrimitiveType.Cube, root, new Vector3(0f, 0.6f, 0f), new Vector3(1f, 1.2f, 1f), Color.gray);
                block.GetComponent<Renderer>().material = glowMaterial;
            }
            return root;
        }

        private static Material GlowOf(Transform unit)
        {
            foreach (Renderer r in unit.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.name.StartsWith("Glow"))
                    {
                        return m;
                    }
                }
            }
            return unit.GetComponentInChildren<Renderer>().sharedMaterial;
        }

        private void ManaLine(Vector3 from, Vector3 to)
        {
            LineRenderer line = ViewUtil.Line(_root, "Mana Line");
            line.useWorldSpace = false;
            line.material = new Material(ViewUtil.Unlit);
            ViewUtil.SetLine(line, from, to, ViewUtil.SupplyColor, 0.08f, 3f);
        }

        /// <summary>A ring of dark low pyramids on the horizon.</summary>
        private void Mountains()
        {
            var random = new System.Random(11);
            Mesh pyramid = PyramidMesh();
            Material material = new Material(_hull) { name = "Mountain", mainTexture = null, color = MountainColor };
            material.SetColor("_EmissionColor", Color.black);
            for (int i = 0; i < 28; i++)
            {
                float angle = Mathf.Lerp(-80f, 80f, i / 27f) + (float)random.NextDouble() * 4f;
                float distance = 110f + (float)random.NextDouble() * 40f;
                float height = 8f + (float)random.NextDouble() * 22f;
                float width = 18f + (float)random.NextDouble() * 22f;
                var go = new GameObject("Mountain");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 90f, 0f);
                go.transform.localScale = new Vector3(width, height, width);
                go.AddComponent<MeshFilter>().sharedMesh = pyramid;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static Mesh PyramidMesh()
        {
            Vector3 top = new Vector3(0f, 1f, 0f);
            Vector3[] b = { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                // Separate vertices per face for flat shading.
                vertices.Add(b[i]);
                vertices.Add(top);
                vertices.Add(b[(i + 1) % 4]);
                triangles.Add(i * 3);
                triangles.Add(i * 3 + 1);
                triangles.Add(i * 3 + 2);
            }
            var mesh = new Mesh { name = "Pyramid" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Stars as one mesh of small quads on a dome, plus a pale moon.</summary>
        private void Sky()
        {
            var random = new System.Random(7);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const float radius = 260f;
            for (int i = 0; i < 500; i++)
            {
                float yaw = (float)random.NextDouble() * 200f - 100f;
                float pitch = 4f + Mathf.Pow((float)random.NextDouble(), 0.7f) * 70f;
                Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                float size = 0.35f + (float)random.NextDouble() * (random.NextDouble() < 0.08 ? 1.2f : 0.5f);
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
            var mesh = new Mesh { name = "Stars" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var stars = new GameObject("Stars");
            stars.transform.SetParent(_root, false);
            stars.transform.localPosition = CameraPosition;
            stars.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = stars.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(ViewUtil.Unlit) { name = "Stars", color = new Color(0.85f, 0.9f, 1f) * 1.6f };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GameObject moon = ViewUtil.Primitive(PrimitiveType.Sphere, _root, new Vector3(-60f, 75f, 200f), Vector3.one * 14f, Color.white);
            moon.name = "Moon";
            Renderer moonRenderer = moon.GetComponent<Renderer>();
            moonRenderer.material = new Material(ViewUtil.Unlit) { name = "Moon", color = new Color(0.9f, 0.93f, 1f) * 1.8f };
            moonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
