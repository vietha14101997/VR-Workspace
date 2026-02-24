using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.Components
{
    /// <summary>
    /// BaseMeshEffect that enables rounded corner clipping on UI Images
    /// via a custom SDF shader (UI/RoundedImage). Encodes rect size and
    /// vertex local position into UV channels for the shader to consume.
    /// This avoids relying on v.vertex which is in Canvas space after batching.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class RoundedCorners : BaseMeshEffect
    {
        [SerializeField] private float _radius = 16f;
        [SerializeField] private bool _useParentRect = false;

        private static Material _sharedMaterial;

        /// <summary>
        /// Shared material using UI/RoundedImage shader. One instance for all thumbnails.
        /// </summary>
        public static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                {
                    var shader = Shader.Find("UI/RoundedImage");
                    if (shader != null)
                        _sharedMaterial = new Material(shader);
                }
                return _sharedMaterial;
            }
        }

        public float Radius
        {
            get => _radius;
            set
            {
                if (!Mathf.Approximately(_radius, value))
                {
                    _radius = value;
                    if (graphic != null) graphic.SetVerticesDirty();
                }
            }
        }

        public bool UseParentRect
        {
            get => _useParentRect;
            set
            {
                if (_useParentRect != value)
                {
                    _useParentRect = value;
                    if (graphic != null) graphic.SetVerticesDirty();
                }
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasShaderChannels();
        }

        /// <summary>
        /// Ensure the parent Canvas has additionalShaderChannels for TexCoord1 + TexCoord2.
        /// Without this, UV1/UV2 data from ModifyMesh won't reach the shader.
        /// </summary>
        private void EnsureCanvasShaderChannels()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            const AdditionalCanvasShaderChannels required =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2;

            if ((canvas.additionalShaderChannels & required) != required)
            {
                canvas.additionalShaderChannels |= required;
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0)
                return;

            if (_radius <= 0) return;

            UIVertex vert = new UIVertex();
            float width, height;
            float centerX = 0f, centerY = 0f;

            if (_useParentRect && transform.parent != null)
            {
                // UseParentRect: clip at parent boundary (for EnvelopeParent overflow)
                var parentRT = transform.parent as RectTransform;
                if (parentRT == null) return;
                width = parentRT.rect.width;
                height = parentRT.rect.height;
                // Image is center-anchored in parent, so parent center = (0,0) in Image-local space
            }
            else
            {
                // Self rect: compute from actual vertex bounding box
                // This is more reliable than RectTransform.rect because it reflects
                // the true rendered size after preserveAspect and AspectRatioFitter
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;

                for (int i = 0; i < vh.currentVertCount; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.x < minX) minX = vert.position.x;
                    if (vert.position.x > maxX) maxX = vert.position.x;
                    if (vert.position.y < minY) minY = vert.position.y;
                    if (vert.position.y > maxY) maxY = vert.position.y;
                }

                width = maxX - minX;
                height = maxY - minY;
                centerX = (minX + maxX) * 0.5f;
                centerY = (minY + maxY) * 0.5f;
            }

            if (width <= 0 || height <= 0)
                return;

            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vert, i);
                // UV1: xy = rect size, zw = vertex position relative to rect center
                // This preserves Image-local coordinates that get lost after Canvas batching
                vert.uv1 = new Vector4(width, height, vert.position.x - centerX, vert.position.y - centerY);
                vert.uv2 = new Vector4(_radius, 0, 0, 0);
                vh.SetUIVertex(vert, i);
            }
        }
    }

}
