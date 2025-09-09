using UnityEngine;

public class GazeDwellToggle : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera vrCamera;               // Main Camera
    [SerializeField] private ModeController modeController; // Toggle Virtual/Real

    [Header("Gaze")]
    [Min(0.05f)] public float dwellTime = 2.0f;     // hold to toggle
    public float maxDistance = 10f;                 // ray length
    public LayerMask hitMask;                       // target layer(s)
    public bool requireExitToRetrigger = true;      // must look away to retrigger

    private float _timer;
    private bool _armed = true;                     // ready to trigger again
    private Renderer _rend;
    private Color _baseColor;
    private MaterialPropertyBlock _mpb;

    private static readonly int _ColorID = Shader.PropertyToID("_Color");

    private void Awake()
    {
        _rend = GetComponentInChildren<Renderer>();
        if (_rend)
        {
            _mpb = new MaterialPropertyBlock();
            // avoid touching sharedMaterial each frame; read once
            var mat = _rend.sharedMaterial;
            _baseColor = (mat && mat.HasProperty(_ColorID)) ? mat.color : Color.white;
        }
        // default mask once if user left it empty
        if (hitMask.value == 0) hitMask = LayerMask.GetMask("VirtualObjects");
        if (dwellTime < 0.05f) dwellTime = 0.05f;
    }

    private void Update()
    {
        if (!vrCamera || !modeController) return;

        // Ray straight from camera forward
        var origin = vrCamera.transform.position;
        var dir = vrCamera.transform.forward;

        bool hitSelf =
            Physics.Raycast(origin, dir, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore)
            && hit.collider && hit.collider.transform.IsChildOf(transform);

        if (hitSelf && _armed)
        {
            _timer += Time.deltaTime;
            SetProgressVisual(_timer / dwellTime);

            if (_timer >= dwellTime)
            {
                modeController.ToggleMode();
                _timer = 0f;
                if (requireExitToRetrigger) _armed = false; // need gaze exit to re-arm
            }
        }
        else
        {
            // Reset when gaze leaves, and re-arm if we’re no longer hitting
            if (!hitSelf) _armed = true;
            if (_timer != 0f) { _timer = 0f; SetProgressVisual(0f); }
        }
    }

    private void SetProgressVisual(float t01)
    {
        if (!_rend || _mpb == null) return;

        Color c = Color.Lerp(_baseColor, Color.green, Mathf.Clamp01(t01));
        _mpb.SetColor(_ColorID, c);
        _rend.SetPropertyBlock(_mpb);
    }
}
