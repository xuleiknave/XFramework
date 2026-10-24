using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Orbit camera around the board centre. The board is shown upright: the camera sits behind
    /// the player half (world -x) so the player is at the bottom of the screen and the enemy at
    /// the top. Each frame the distance is fitted so the whole board fills the free part of the
    /// screen (<see cref="SetViewport"/>), using an off-centre projection to centre it there.
    /// Deployment uses a fixed near top-down pose; battle allows free look and view presets.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>A named camera angle: yaw 90 looks from the player side towards the enemy.</summary>
        public readonly struct ViewPreset
        {
            public ViewPreset(string name, float yaw, float pitch)
            {
                Name = name;
                Yaw = yaw;
                Pitch = pitch;
            }

            public string Name { get; }
            public float Yaw { get; }
            public float Pitch { get; }
        }

        public static readonly ViewPreset[] BattleViews =
        {
            new ViewPreset("默认", 90f, 55f),
            new ViewPreset("俯视", 90f, 86f),
            new ViewPreset("侧面", 0f, 32f),
            new ViewPreset("敌方视角", 270f, 45f),
        };

        private static readonly ViewPreset DeployView = new ViewPreset("布阵", 90f, 78f);

        private const float MinPitch = 12f;
        private const float MaxPitch = 88f;
        private const float MinZoom = 0.35f;
        private const float MaxZoom = 1.6f;
        private const float DragYawPerPixel = 0.25f;
        private const float DragPitchPerPixel = 0.18f;
        private const float DragThresholdPixels = 6f;
        private const float Smoothing = 7f;
        private const float FitMargin = 1.04f;
        private const float UnitHeight = 1.6f;

        private Camera _camera;
        private float _yaw, _pitch, _zoom = 1f;
        private float _goalYaw, _goalPitch, _goalZoom = 1f;
        private Rect _viewport = new Rect(0f, 0f, 1f, 1f);
        private Rect _goalViewport = new Rect(0f, 0f, 1f, 1f);
        private bool _freeLook;
        private int _presetIndex;
        private bool _dragging;
        private bool _dragMoved;
        private Vector2 _dragStart;
        private float _pinchDistance;

        /// <summary>Name of the current battle preset, or "自由" once the player has moved the camera.</summary>
        public string ViewName { get; private set; } = BattleViews[0].Name;

        /// <summary>
        /// Free part of the screen for the board, in normalized screen coordinates
        /// (origin bottom-left). Screens pass the rect of their board-area element.
        /// </summary>
        public void SetViewport(Rect normalized)
        {
            if (normalized.width < 0.05f || normalized.height < 0.05f)
            {
                normalized = new Rect(0f, 0f, 1f, 1f);
            }
            _goalViewport = normalized;
        }

        public void SnapToDeploy()
        {
            GoToDeploy();
            _yaw = _goalYaw;
            _pitch = _goalPitch;
            _zoom = _goalZoom;
            _viewport = _goalViewport;
            Apply();
        }

        public void GoToDeploy()
        {
            _freeLook = false;
            _dragging = false;
            SetGoal(DeployView, 1f);
        }

        public void GoToBattle()
        {
            _freeLook = true;
            _presetIndex = 0;
            SetGoal(BattleViews[0], 1f);
        }

        /// <summary>Moves to the next battle preset and returns its name.</summary>
        public string CycleView()
        {
            _presetIndex = (_presetIndex + 1) % BattleViews.Length;
            SetGoal(BattleViews[_presetIndex], 1f);
            return ViewName;
        }

        /// <summary>Back to the default battle angle and zoom.</summary>
        public void ResetView()
        {
            _presetIndex = 0;
            SetGoal(BattleViews[0], 1f);
        }

        private void SetGoal(ViewPreset view, float zoom)
        {
            // Turn the short way round from wherever the camera is now.
            _goalYaw = _yaw + Mathf.DeltaAngle(_yaw, view.Yaw);
            _goalPitch = view.Pitch;
            _goalZoom = zoom;
            ViewName = view.Name;
        }

        /// <summary>
        /// Free-look input during battle: drag to orbit, wheel or pinch to zoom, V for the next
        /// preset. Drags that start over the UI, or while <paramref name="blockDrag"/> is set
        /// (aiming a skill), are ignored.
        /// </summary>
        public void TickFreeLook(Func<Vector2, bool> isOverUi, bool blockDrag)
        {
            if (!_freeLook)
            {
                return;
            }
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                CycleView();
            }

            Touchscreen touch = Touchscreen.current;
            if (touch != null && TickTouch(touch, isOverUi, blockDrag))
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }
            Vector2 pos = mouse.position.ReadValue();
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && !isOverUi(pos))
            {
                Zoom(scroll > 0f ? 0.9f : 1f / 0.9f);
            }
            bool pressed = mouse.leftButton.isPressed || mouse.middleButton.isPressed;
            bool pressedNow = mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            if (pressedNow && !blockDrag && !isOverUi(pos))
            {
                _dragging = true;
                _dragMoved = false;
                _dragStart = pos;
            }
            if (!pressed)
            {
                _dragging = false;
            }
            if (_dragging)
            {
                Orbit(pos, mouse.delta.ReadValue());
            }
        }

        /// <summary>One finger orbits, two fingers pinch to zoom. Returns true while touching.</summary>
        private bool TickTouch(Touchscreen touch, Func<Vector2, bool> isOverUi, bool blockDrag)
        {
            int count = 0;
            Vector2 a = default, b = default, delta = default;
            foreach (var t in touch.touches)
            {
                if (!t.press.isPressed)
                {
                    continue;
                }
                if (count == 0)
                {
                    a = t.position.ReadValue();
                    delta = t.delta.ReadValue();
                }
                else if (count == 1)
                {
                    b = t.position.ReadValue();
                }
                count++;
            }

            if (count == 0)
            {
                _dragging = false;
                _pinchDistance = 0f;
                return false;
            }
            if (count >= 2)
            {
                _dragging = false;
                float distance = Vector2.Distance(a, b);
                if (_pinchDistance > 0f && distance > 0f)
                {
                    Zoom(_pinchDistance / distance);
                    ViewName = "自由";
                }
                _pinchDistance = distance;
                return true;
            }

            _pinchDistance = 0f;
            if (!_dragging && touch.primaryTouch.press.wasPressedThisFrame && !blockDrag && !isOverUi(a))
            {
                _dragging = true;
                _dragMoved = false;
                _dragStart = a;
            }
            if (_dragging)
            {
                Orbit(a, delta);
            }
            return true;
        }

        private void Orbit(Vector2 position, Vector2 delta)
        {
            if (!_dragMoved && (position - _dragStart).magnitude < DragThresholdPixels)
            {
                return;
            }
            _dragMoved = true;
            // Dragging right swings the camera to the left around the board, like grabbing it.
            _goalYaw += delta.x * DragYawPerPixel;
            _goalPitch = Mathf.Clamp(_goalPitch - delta.y * DragPitchPerPixel, MinPitch, MaxPitch);
            ViewName = "自由";
        }

        private void Zoom(float factor)
        {
            _goalZoom = Mathf.Clamp(_goalZoom * factor, MinZoom, MaxZoom);
        }

        private void Awake() => _camera = GetComponent<Camera>();

        private void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);
            _yaw = Mathf.Lerp(_yaw, _goalYaw, k);
            _pitch = Mathf.Lerp(_pitch, _goalPitch, k);
            _zoom = Mathf.Lerp(_zoom, _goalZoom, k);
            _viewport = new Rect(
                Mathf.Lerp(_viewport.x, _goalViewport.x, k),
                Mathf.Lerp(_viewport.y, _goalViewport.y, k),
                Mathf.Lerp(_viewport.width, _goalViewport.width, k),
                Mathf.Lerp(_viewport.height, _goalViewport.height, k));
            Apply();
        }

        private void Apply()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float tanV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;

            float distance = FitDistance(rotation, tanH * _viewport.width, tanV * _viewport.height) * _zoom;
            transform.rotation = rotation;
            transform.position = -(rotation * Vector3.forward) * distance;

            // Off-centre projection: the optical axis (and so the board centre) lands in the
            // middle of the viewport rect instead of the middle of the screen.
            Matrix4x4 projection = Matrix4x4.Perspective(_camera.fieldOfView, aspect, _camera.nearClipPlane, _camera.farClipPlane);
            projection[0, 2] = -(2f * _viewport.center.x - 1f);
            projection[1, 2] = -(2f * _viewport.center.y - 1f);
            _camera.projectionMatrix = projection;
        }

        /// <summary>
        /// Smallest distance from the board centre at which every board corner (both halves,
        /// up to unit height) fits inside the given half-angle tangents.
        /// </summary>
        private static float FitDistance(Quaternion rotation, float tanH, float tanV)
        {
            FixVec2 half = BoardLayout.HalfExtents;
            float hx = half.X.ToFloat();
            float hz = half.Y.ToFloat();
            Quaternion inverse = Quaternion.Inverse(rotation);
            float best = 1f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -hx : hx, (i & 2) == 0 ? 0f : UnitHeight, (i & 4) == 0 ? -hz : hz);
                Vector3 v = inverse * corner;
                // Camera at -forward * d, so this corner's depth in front of it is v.z + d.
                best = Mathf.Max(best, Mathf.Abs(v.x) * FitMargin / tanH - v.z);
                best = Mathf.Max(best, Mathf.Abs(v.y) * FitMargin / tanV - v.z);
            }
            return best;
        }
    }
}
