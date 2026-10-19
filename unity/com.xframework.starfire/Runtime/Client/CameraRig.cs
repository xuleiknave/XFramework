using UnityEngine;

namespace XFramework.Client
{
    /// <summary>
    /// Fixed camera with two poses: near top-down for deployment, and a low side angle for
    /// battle (player on the left, enemy on the right). Moves smoothly between them.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        private static readonly Vector3 DeployPosition = new Vector3(0f, 21f, -6f);
        private static readonly Vector3 DeployLookAt = new Vector3(0f, 0f, -0.8f);
        private static readonly Vector3 BattlePosition = new Vector3(0f, 11f, -21f);
        private static readonly Vector3 BattleLookAt = new Vector3(0f, 0f, 1.8f);
        private const float TransitionSeconds = 1.2f;

        private Vector3 _fromPos, _toPos, _fromLook, _toLook;
        private float _t = 1f;

        public void SnapToDeploy()
        {
            _fromPos = _toPos = DeployPosition;
            _fromLook = _toLook = DeployLookAt;
            _t = 1f;
            Apply(DeployPosition, DeployLookAt);
        }

        public void GoToDeploy() => Begin(DeployPosition, DeployLookAt);

        public void GoToBattle() => Begin(BattlePosition, BattleLookAt);

        private void Begin(Vector3 pos, Vector3 look)
        {
            _fromPos = transform.position;
            _fromLook = transform.position + transform.forward * Vector3.Distance(transform.position, look);
            _toPos = pos;
            _toLook = look;
            _t = 0f;
        }

        private void LateUpdate()
        {
            if (_t >= 1f)
            {
                return;
            }
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / TransitionSeconds);
            float e = Mathf.SmoothStep(0f, 1f, _t);
            Apply(Vector3.Lerp(_fromPos, _toPos, e), Vector3.Lerp(_fromLook, _toLook, e));
        }

        private void Apply(Vector3 pos, Vector3 look)
        {
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        }
    }
}
