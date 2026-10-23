using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Live 3D background of the main menu and save slots (art canvas, 主菜单): night over
    /// 灯塔城. The scene itself is the prefab Resources/StarlineMenuScene, editable in Unity
    /// (Starline &gt; 生成主菜单夜景预制体 creates it); without the prefab the default layout from
    /// <see cref="MenuSceneDefaults"/> is built. Placed far from the board and shown only while a
    /// screen asks for it (<see cref="UiScreen.UsesMenuScene"/>); meanwhile it owns the camera.
    /// </summary>
    public sealed class MenuScene
    {
        public const string PrefabResource = "StarlineMenuScene";

        /// <summary>Far from the battle board so the two never overlap.</summary>
        private static readonly Vector3 Origin = new Vector3(0f, 0f, 400f);

        private readonly MenuSceneRoot _scene;
        private readonly Camera _camera;
        private readonly CameraRig _rig;
        private float _time;
        private float _boardFieldOfView;

        public MenuScene(Transform parent, Camera camera, CameraRig rig)
        {
            _camera = camera;
            _rig = rig;
            GameObject prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab != null)
            {
                // Instantiate under an inactive holder so nothing wakes up before it is shown
                // (never toggle the prefab asset itself: in the editor that would change the file).
                var holder = new GameObject("Menu Scene Holder");
                holder.SetActive(false);
                GameObject instance = Object.Instantiate(prefab, holder.transform);
                instance.SetActive(false);
                instance.transform.SetParent(null, false);
                Object.Destroy(holder);
                _scene = instance.GetComponent<MenuSceneRoot>();
                if (_scene == null)
                {
                    Object.Destroy(instance);
                }
            }
            if (_scene == null)
            {
                if (prefab != null)
                {
                    Debug.LogWarning("Starline：Resources/" + PrefabResource + " 的根物体上没有 MenuSceneRoot，改用默认夜景。");
                }
                _scene = MenuSceneDefaults.Build();
            }
            _scene.name = "Menu Scene";
            _scene.transform.SetParent(parent, false);
            _scene.transform.position = Origin;
            if (_scene.cameraAnchor != null)
            {
                // The anchor's camera is only for previewing the shot in the editor.
                Camera preview = _scene.cameraAnchor.GetComponent<Camera>();
                if (preview != null)
                {
                    preview.enabled = false;
                }
            }
        }

        public bool Visible => _scene.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (visible == Visible)
            {
                return;
            }
            _scene.gameObject.SetActive(visible);
            SceneLook.SetNight(visible, _scene.ambient, _scene.sky);
            // The rig only moves the camera during its own transitions; snapping stops any
            // transition, then the menu (while visible) or the deploy pose (after) owns the camera.
            _rig.SnapToDeploy();
            if (visible)
            {
                _boardFieldOfView = _camera.fieldOfView;
                _time = 0f;
                PlaceCamera();
            }
            else
            {
                _camera.fieldOfView = _boardFieldOfView;
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
        }

        /// <summary>The anchor's pose plus a slow drift so the scene feels alive behind the menu.</summary>
        private void PlaceCamera()
        {
            Transform anchor = _scene.cameraAnchor != null ? _scene.cameraAnchor : _scene.transform;
            float t = _time * _scene.driftSpeed;
            Vector3 drift = anchor.right * (_scene.drift.x * Mathf.Sin(t)) + anchor.up * (_scene.drift.y * Mathf.Sin(t * 1.6f));
            _camera.transform.SetPositionAndRotation(anchor.position + drift, anchor.rotation);
            _camera.fieldOfView = _scene.fieldOfView;
        }
    }
}
