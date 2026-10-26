using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Root of the menu scene prefab (Resources/StarlineMenuScene). Holds what the scene needs
    /// from the rest of the game: where the camera stands, how it drifts, and the night ambient.
    /// The moonlight is an ordinary directional light inside the prefab, so it only lights the
    /// world while the menu scene is shown.
    /// </summary>
    public sealed class MenuSceneRoot : MonoBehaviour
    {
        [Tooltip("镜头位置和朝向。选中它，用 GameObject > Align With View 可以把镜头对准当前场景视图")]
        public Transform cameraAnchor;

        [Tooltip("镜头视角（度）")]
        [Range(20f, 90f)] public float fieldOfView = 50f;

        [Tooltip("镜头左右、上下缓慢漂移的幅度（米）")]
        public Vector2 drift = new Vector2(0.6f, 0.15f);

        [Tooltip("漂移速度")]
        [Min(0f)] public float driftSpeed = 0.07f;

        [Tooltip("夜里的环境光颜色（越亮，暗处的模型越清楚）")]
        public Color ambient = new Color(0.07f, 0.09f, 0.16f);

        [Tooltip("天空背景色")]
        public Color sky = new Color(0.02f, 0.03f, 0.06f);
    }
}
