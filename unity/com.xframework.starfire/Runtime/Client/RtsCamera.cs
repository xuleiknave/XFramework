using UnityEngine;
using UnityEngine.InputSystem;

namespace XFramework.Client
{
    /// <summary>Top-down tilted camera: WASD / arrow keys to pan, mouse wheel to zoom.</summary>
    public sealed class RtsCamera : MonoBehaviour
    {
        public float PanSpeed = 30f;
        public float ZoomSpeed = 0.05f;
        public float MinHeight = 15f;
        public float MaxHeight = 80f;
        public float PitchDegrees = 60f;
        public Vector2 Bounds = new Vector2(45f, 30f);

        private Vector3 _focus;
        private float _height = 45f;

        private void Start()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
            {
                return;
            }

            // Letter keys are reserved for unit commands (A attack-move, S stop).
            Vector2 pan = Vector2.zero;
            if (kb.upArrowKey.isPressed) pan.y += 1f;
            if (kb.downArrowKey.isPressed) pan.y -= 1f;
            if (kb.rightArrowKey.isPressed) pan.x += 1f;
            if (kb.leftArrowKey.isPressed) pan.x -= 1f;

            // Pan with unscaled time so the camera still moves while the battle is paused.
            float speed = PanSpeed * (_height / 45f) * Time.unscaledDeltaTime;
            _focus += new Vector3(pan.x, 0f, pan.y) * speed;

            // Middle mouse drag pans as well.
            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _focus -= new Vector3(delta.x, 0f, delta.y) * (_height * 0.002f);
            }
            _focus.x = Mathf.Clamp(_focus.x, -Bounds.x, Bounds.x);
            _focus.z = Mathf.Clamp(_focus.z, -Bounds.y, Bounds.y);

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _height = Mathf.Clamp(_height - Mathf.Sign(scroll) * _height * ZoomSpeed * 2f, MinHeight, MaxHeight);
            }

            Apply();
        }

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(PitchDegrees, 0f, 0f);
            float back = _height / Mathf.Tan(PitchDegrees * Mathf.Deg2Rad);
            transform.position = _focus + new Vector3(0f, _height, -back);
            transform.rotation = rot;
        }
    }
}
