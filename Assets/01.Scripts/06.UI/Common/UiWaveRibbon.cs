// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary>물결처럼 옆으로 흐르는 빛 띠를 그린다. 마루에 가는 밝은 선이 있고 위로는 짧게, 아래로는 길게 옅어진다</summary>
    // 모양이 계속 바뀌는 물결은 스프라이트 한 장으로 만들 수 없어 매 프레임 띠 메시를 다시 만든다.
    // 정점 수가 적어 다시 만드는 비용이 작고, 메시는 VertexHelper가 재사용하므로 할당이 없다
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiWaveRibbon : MaskableGraphic
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TWO_PI          = Mathf.PI * 2f;
        private const int   MIN_SEGMENTS    = 2;
        private const int   VERTS_PER_STEP  = 6;
        private const float EDGE_SOFTNESS   = 1f;
        private const float HALF            = 0.5f;
        private const float GLOW_ALPHA      = 0.6f;
        private const float HARMONIC_FREQ   = 2.3f;
        private const float HARMONIC_SIZE   = 0.35f;
        private const float HARMONIC_SPEED  = 1.7f;

        [Header("물결 모양 (픽셀)")]
        [SerializeField] private int   _segments   = 48;
        [SerializeField] private float _baseline   = 0f;
        [SerializeField] private float _amplitude  = 10f;
        [SerializeField] private float _wavelength = 400f;
        [SerializeField] private float _swell      = 0.3f;

        [Header("두께 (픽셀)")]
        [SerializeField] private float _lineWidth = 1.5f;
        [SerializeField] private float _glowAbove = 10f;
        [SerializeField] private float _bandBelow = 30f;

        [Header("밝기")]
        [SerializeField, Range(0f, 1f)] private float _bandAlpha = 0.5f;

        [Header("흐름 (라디안/초)")]
        [SerializeField] private float _flowSpeed  = 0.3f;
        [SerializeField] private float _swellSpeed = 0.4f;

        private float _phase      = 0f;
        private float _swellPhase = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void Update()
        {
            // 결과 화면처럼 게임 시간이 멈춘 동안에도 흘러야 하므로 시간 배율을 무시한다
            float deltaTime = Time.unscaledDeltaTime;
            _phase      = Mathf.Repeat(_phase + (_flowSpeed * deltaTime), TWO_PI);
            _swellPhase = Mathf.Repeat(_swellPhase + (_swellSpeed * deltaTime), TWO_PI);
            SetVerticesDirty();
        }

        /****************************************
        *                Methods
        ****************************************/

        // 가로로 나눈 칸마다 위에서 아래로 정점 여섯 개를 쌓고 이웃한 두 정점끼리 사각형을 잇는다.
        // 마루 선 위아래에 1픽셀씩 번지는 칸을 두어 가는 선이 계단처럼 보이지 않게 한다
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_wavelength <= 0f)
            {
                return;
            }

            Rect  rect      = rectTransform.rect;
            int   segments  = Mathf.Max(_segments, MIN_SEGMENTS);
            float amplitude = _amplitude * (1f + (_swell * Mathf.Sin(_swellPhase)));
            float frequency = TWO_PI / _wavelength;
            float halfLine  = _lineWidth * HALF;

            Color32 line  = color;
            Color32 glow  = WithAlpha(color, color.a * _bandAlpha * GLOW_ALPHA);
            Color32 band  = WithAlpha(color, color.a * _bandAlpha);
            Color32 clear = WithAlpha(color, 0f);

            for (int i = 0; i <= segments; ++i)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, (float)i / segments);
                float y = rect.center.y + _baseline
                    + (amplitude * Mathf.Sin((frequency * x) + _phase))
                    + (amplitude * HARMONIC_SIZE * Mathf.Sin((frequency * HARMONIC_FREQ * x) - (_phase * HARMONIC_SPEED)));

                vh.AddVert(new Vector3(x, y + halfLine + _glowAbove), clear, Vector4.zero);
                vh.AddVert(new Vector3(x, y + halfLine + EDGE_SOFTNESS), glow, Vector4.zero);
                vh.AddVert(new Vector3(x, y + halfLine), line, Vector4.zero);
                vh.AddVert(new Vector3(x, y - halfLine), line, Vector4.zero);
                vh.AddVert(new Vector3(x, y - halfLine - EDGE_SOFTNESS), band, Vector4.zero);
                vh.AddVert(new Vector3(x, y - halfLine - _bandBelow), clear, Vector4.zero);
            }

            for (int i = 0; i < segments; ++i)
            {
                int current = i * VERTS_PER_STEP;
                int next    = current + VERTS_PER_STEP;

                for (int row = 0; row < (VERTS_PER_STEP - 1); ++row)
                {
                    AddStrip(vh, current + row, next + row);
                }
            }
        }

        private static void AddStrip(VertexHelper vh, int top, int nextTop)
        {
            vh.AddTriangle(top, nextTop, nextTop + 1);
            vh.AddTriangle(top, nextTop + 1, top + 1);
        }

        private static Color32 WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
