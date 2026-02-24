using UnityEngine;
using System;
using Random = UnityEngine.Random;

namespace VRWorkspace.UI.Effects
{
    /// <summary>
    /// Animates floating data particles for VR UI effects.
    /// Used by VRMenuFrame's FX_DataStream and RTTMenuFrame.
    /// </summary>
    public class FloatingDataAnim : MonoBehaviour
    {
        /// <summary>
        /// Global toggle to enable/disable all FloatingDataAnim effects.
        /// Set to false to temporarily disable all floating data animations.
        /// </summary>
        public static bool IsEnabled = true; // Temporarily disabled

        public float speed;
        public Vector2 range;
        private RectTransform _rt;
        private Vector2 _dir;

        // Throttle dirty notifications to reduce render overhead
        private float _lastNotifyTime;
        private const float NOTIFY_INTERVAL = 0.1f; // 100ms = 10 FPS dirty updates max

        /// <summary>
        /// Event called when animation updates (for RTT dirty flag optimization)
        /// </summary>
        public event Action OnAnimationUpdate;

        void Start()
        {
            _rt = GetComponent<RectTransform>();
            RandomizeDirection();
        }

        void RandomizeDirection()
        {
            _dir = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.2f, 0.8f)).normalized;
            if (Random.value > 0.5f) _dir.y *= -1;
        }

        /// <summary>
        /// Force re-initialization when loading from prefab
        /// </summary>
        public void ForceReinitialize()
        {
            _rt = GetComponent<RectTransform>();

            // Re-randomize speed if not set
            if (speed <= 0)
            {
                speed = Random.Range(10f, 40f);
            }

            // Randomize direction
            RandomizeDirection();

            // Randomize position
            if (range.x > 0 && range.y > 0)
            {
                float halfW = range.x / 2f;
                float halfH = range.y / 2f;
                _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
            }
        }

        void Update()
        {
            if (!IsEnabled || _rt == null) return;
            _rt.anchoredPosition += _dir * speed * Time.deltaTime;

            float halfW = range.x / 2f + 50f;
            float halfH = range.y / 2f + 50f;

            if (_rt.anchoredPosition.y > halfH) _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), -halfH);
            else if (_rt.anchoredPosition.y < -halfH) _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), halfH);

            if (_rt.anchoredPosition.x > halfW) _rt.anchoredPosition = new Vector2(-halfW, Random.Range(-halfH, halfH));
            else if (_rt.anchoredPosition.x < -halfW) _rt.anchoredPosition = new Vector2(halfW, Random.Range(-halfH, halfH));

            // Throttle dirty notifications to reduce render overhead
            // Only notify at NOTIFY_INTERVAL (10 FPS) instead of every frame
            if (Time.time - _lastNotifyTime >= NOTIFY_INTERVAL)
            {
                _lastNotifyTime = Time.time;
                OnAnimationUpdate?.Invoke();
            }
        }
    }

}
