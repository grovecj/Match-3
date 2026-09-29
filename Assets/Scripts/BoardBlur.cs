using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// Covers the screen with a blurred still image of what the camera sees, so the player can't
    /// study the board while the game is paused. Put it on a full-screen <see cref="RawImage"/>
    /// that sits behind the rest of the UI, with the RawImage component turned off.
    ///
    /// How the blur works:
    ///   1. Render the camera once, off-screen, into a RenderTexture: an image on the GPU that a
    ///      camera can draw into instead of the screen.
    ///   2. Copy it into a texture half as wide and half as tall, again and again. With bilinear
    ///      filtering each copy averages every 2x2 block of pixels into one, smearing detail away.
    ///   3. Show the tiny result stretched back up to full screen. Bilinear filtering blends
    ///      between its few pixels, so the board becomes soft blobs of color.
    /// The snapshot is a still picture, which is exactly what we want: the board is frozen anyway.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class BoardBlur : MonoBehaviour
    {
        [Tooltip("The camera to take the snapshot from. Defaults to the main camera.")]
        [SerializeField] private Camera sourceCamera;
        [Tooltip("How many times the snapshot is halved. Each halving averages 2x2 pixels, so more steps = blurrier.")]
        [SerializeField, Range(1, 8)] private int downsampleSteps = 5;

        private RawImage image;
        private RenderTexture blurred;

        private void Awake()
        {
            image = GetComponent<RawImage>();
            if (sourceCamera == null)
            {
                sourceCamera = Camera.main;
            }
            image.enabled = false;
        }

        private void OnDestroy()
        {
            ReleaseBlurred();
        }

        /// <summary>Takes a snapshot of the camera, blurs it, and shows it.</summary>
        public void Show()
        {
            ReleaseBlurred();

            int width = Screen.width;
            int height = Screen.height;

            // GetTemporary borrows a RenderTexture from a pool Unity keeps, which is cheaper than
            // creating a new one each time. Every GetTemporary needs a matching ReleaseTemporary.
            // The 24 asks for a depth buffer, which a camera needs to render into the texture.
            RenderTexture current = RenderTexture.GetTemporary(width, height, 24);

            // A render request asks the render pipeline (URP here) to render this camera into
            // our texture right now, instead of waiting for it to draw to the screen as usual.
            // Our UI is a Screen Space - Overlay canvas, which the camera doesn't draw, so the
            // snapshot holds only the board and background.
            var request = new RenderPipeline.StandardRequest { destination = current };
            if (!RenderPipeline.SupportsRenderRequest(sourceCamera, request))
            {
                // Can't take a snapshot: hide the board behind a plain black image instead.
                Debug.LogWarning("BoardBlur: this render pipeline can't render to a texture; hiding the board instead.", this);
                RenderTexture.ReleaseTemporary(current);
                image.texture = Texture2D.blackTexture;
                image.enabled = true;
                return;
            }
            RenderPipeline.SubmitRenderRequest(sourceCamera, request);

            for (int i = 0; i < downsampleSteps; i++)
            {
                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
                RenderTexture smaller = RenderTexture.GetTemporary(width, height, 0);

                // Bilinear filtering blends neighboring pixels when a texture is sampled between
                // them, which is what does the averaging. (Point filtering would just pick one.)
                current.filterMode = FilterMode.Bilinear;
                // Graphics.Blit copies one texture into another, scaling it to fit.
                Graphics.Blit(current, smaller);

                RenderTexture.ReleaseTemporary(current);
                current = smaller;
            }

            current.filterMode = FilterMode.Bilinear;
            blurred = current;
            image.texture = blurred;
            image.enabled = true;
        }

        /// <summary>Hides the blurred snapshot and frees its texture.</summary>
        public void Hide()
        {
            image.enabled = false;
            image.texture = null;
            ReleaseBlurred();
        }

        private void ReleaseBlurred()
        {
            if (blurred != null)
            {
                RenderTexture.ReleaseTemporary(blurred);
                blurred = null;
            }
        }
    }
}
