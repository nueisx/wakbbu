using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Interaction
{
    /// <summary>
    /// 핀치를 길게 누르는 동안 채워지는 진행 원(월드 공간). 진행이 0이면 숨는다.
    /// 이미지 파일 없이 코드로 링 모양 스프라이트를 만든다. 항상 헤드셋 쪽을 본다.
    /// anchor가 있으면 그 위치(예: 왼손) 위쪽에, 없으면 헤드셋 앞 왼쪽 위에 뜬다. 화면 문자열은 영어다.
    /// </summary>
    public class PinchProgressRing : MonoBehaviour
    {
        [Tooltip("링이 따라다닐 위치(왼손 등). 비우면 헤드셋 앞 왼쪽 위에 뜬다.")]
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset = new Vector3(0f, 0.09f, 0f);
        [SerializeField] private string label = "Reset";
        [SerializeField] private Color color = new Color(1f, 0.6f, 0.1f);

        // 월드 크기: 200px x 0.0003 = 6cm
        private const float CanvasPixels = 200f;
        private const float CanvasScale = 0.0003f;
        private const int SpriteSize = 128;

        private static Sprite ringSprite;

        private GameObject canvasObject;
        private Image fill;
        private float progress;

        private void Awake()
        {
            canvasObject = new GameObject("PinchRing_" + label, typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(CanvasPixels, CanvasPixels);
            rect.localScale = Vector3.one * CanvasScale;

            var track = CreateImage("Track", new Color(color.r, color.g, color.b, 0.25f));
            fill = CreateImage("Fill", color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            track.raycastTarget = false;
            fill.raycastTarget = false;

            var textObject = new GameObject("Label", typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 36f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            Stretch((RectTransform)textObject.transform);

            canvasObject.SetActive(false);
        }

        /// <summary>진행(0~1)을 정한다. 0이면 숨긴다.</summary>
        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            if (fill != null) fill.fillAmount = progress;
            if (canvasObject != null) canvasObject.SetActive(progress > 0.001f);
        }

        private void LateUpdate()
        {
            if (canvasObject == null || !canvasObject.activeSelf) return;
            Camera cam = Camera.main;

            Vector3 position;
            if (anchor != null) position = anchor.position + anchorOffset;
            else if (cam != null) position = cam.transform.position + cam.transform.forward * 0.4f - cam.transform.right * 0.12f + cam.transform.up * 0.08f;
            else position = transform.position;

            canvasObject.transform.position = position;
            if (cam == null) return;

            // 월드 공간 캔버스는 앞면(+z)이 보는 쪽 반대로 향하므로 카메라에서 멀어지는 방향을 바라보게 한다(FaceHeadset과 같다).
            Vector3 away = position - cam.transform.position;
            if (away.sqrMagnitude > 1e-6f) canvasObject.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private Image CreateImage(string imageName, Color imageColor)
        {
            var go = new GameObject(imageName, typeof(Image));
            go.transform.SetParent(canvasObject.transform, false);
            Stretch((RectTransform)go.transform);
            var image = go.GetComponent<Image>();
            image.sprite = GetRingSprite();
            image.color = imageColor;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // 가운데가 빈 링(바깥 반지름 1, 안쪽 0.7)을 한 번만 만들어 공유한다. 가장자리는 부드럽게(안티에일리어싱).
        private static Sprite GetRingSprite()
        {
            if (ringSprite != null) return ringSprite;

            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
            {
                name = "PinchRing",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            float half = SpriteSize * 0.5f;
            float edge = 1.5f / half;
            var pixels = new Color[SpriteSize * SpriteSize];
            for (int y = 0; y < SpriteSize; y++)
            {
                for (int x = 0; x < SpriteSize; x++)
                {
                    float distance = Mathf.Sqrt(Mathf.Pow((x + 0.5f - half) / half, 2f) + Mathf.Pow((y + 0.5f - half) / half, 2f));
                    float alpha = Mathf.Clamp01((1f - distance) / edge) * Mathf.Clamp01((distance - 0.7f) / edge);
                    pixels[y * SpriteSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false);

            ringSprite = Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f);
            ringSprite.hideFlags = HideFlags.HideAndDontSave;
            return ringSprite;
        }
    }
}
