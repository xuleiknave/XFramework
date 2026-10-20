using UnityEngine;

namespace Starline.Client
{
    /// <summary>Makes a light in the menu scene breathe slowly in play mode (the source tower's glow).</summary>
    [RequireComponent(typeof(Light))]
    public sealed class MenuLightPulse : MonoBehaviour
    {
        [Min(0f)] public float amplitude = 0.8f;
        [Min(0f)] public float speed = 1.3f;

        private Light _light;
        private float _base;

        private void OnEnable()
        {
            _light = GetComponent<Light>();
            _base = _light.intensity;
        }

        private void OnDisable() => _light.intensity = _base;

        private void Update() => _light.intensity = _base + amplitude * Mathf.Sin(Time.time * speed);
    }
}
