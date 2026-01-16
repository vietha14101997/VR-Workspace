using UnityEngine;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Hover effect that moves the target on the Z-axis for depth/pop effect.
    /// Useful for World Space Canvas with perspective camera.
    /// Ported from VRButtonAnimation Z-pop logic.
    /// </summary>
    [System.Serializable]
    public class ZPopHoverEffect : HoverEffectBase
    {
        public override string EffectId => "z_pop";

        [Header("Z-Pop Settings")]
        [Tooltip("Amount to move on Z-axis when hovered")]
        [SerializeField] private float popAmount = 0.1f;

        [Tooltip("Direction of pop (true = toward camera/negative Z, false = away)")]
        [SerializeField] private bool popForward = true;

        // Runtime
        private Vector3 _originalPosition;
        private Transform _target;

        public override void Initialize(HoverEffectController controller)
        {
            base.Initialize(controller);

            _target = controller.TargetVisuals;
            if (_target != null)
            {
                _originalPosition = _target.localPosition;
            }
        }

        protected override void ApplyEffect(float progress)
        {
            if (_target == null) return;

            float zOffset = popForward ? -popAmount : popAmount;
            float currentOffset = Mathf.Lerp(0f, zOffset, progress);

            _target.localPosition = new Vector3(
                _originalPosition.x,
                _originalPosition.y,
                _originalPosition.z + currentOffset
            );
        }

        public override void SetStateImmediate(bool hovered)
        {
            base.SetStateImmediate(hovered);

            if (_target != null)
            {
                if (hovered)
                {
                    float zOffset = popForward ? -popAmount : popAmount;
                    _target.localPosition = new Vector3(
                        _originalPosition.x,
                        _originalPosition.y,
                        _originalPosition.z + zOffset
                    );
                }
                else
                {
                    _target.localPosition = _originalPosition;
                }
            }
        }

        #region Fluent API

        public ZPopHoverEffect WithPopAmount(float amount)
        {
            popAmount = amount;
            return this;
        }

        public ZPopHoverEffect WithPopForward(bool forward)
        {
            popForward = forward;
            return this;
        }

        #endregion
    }
}
