// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Shinshi.Art
{
    /// <summary> 화면 공간 툰 조명과 경계선 후처리 값. ToonLightingRendererFeature가 읽는다 </summary>
    // 켜고 끄는 값도 bool이 아니라 강도로 둔다. 볼륨 블렌딩에서 bool과 텍스처 값은 섞이지 않고 즉시 바뀌기 때문이다
    [Serializable, VolumeComponentMenu("Shinshi/Toon Look")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class ToonLookVolume : VolumeComponent
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("조명")]
        [Tooltip("0이면 툰 조명을 끈다")]
        [SerializeField] private ClampedFloatParameter _lightingIntensity = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("명암 단계 수. 소수면 위아래 정수 단계를 섞는다")]
        [SerializeField] private ClampedFloatParameter _rampSteps         = new ClampedFloatParameter(2f, 2f, 4f);
        [Tooltip("단계 경계의 부드러운 폭")]
        [SerializeField] private ClampedFloatParameter _rampEdgeWidth     = new ClampedFloatParameter(0.05f, 0.005f, 0.3f);
        [Tooltip("그늘 쪽에 곱하는 색")]
        [SerializeField] private ColorParameter        _shadowColor       = new ColorParameter(new Color(0.75f, 0.8f, 1f, 1f));
        [Tooltip("계단 조명에서 환경광을 얼마나 남길지")]
        [SerializeField] private ClampedFloatParameter _ambientKeep       = new ClampedFloatParameter(1f, 0f, 1.5f);
        [Tooltip("원래 색에 곱하는 비율의 하한")]
        [SerializeField] private ClampedFloatParameter _ratioMin          = new ClampedFloatParameter(0.5f, 0.2f, 1f);
        [Tooltip("원래 색에 곱하는 비율의 상한")]
        [SerializeField] private ClampedFloatParameter _ratioMax          = new ClampedFloatParameter(1.6f, 1f, 3f);

        [Header("표면 디테일")]
        [Tooltip("같은 면 안의 잔무늬를 부드럽게 해 넓은 색 면으로 만든다. 0이면 끈다")]
        [SerializeField] private ClampedFloatParameter _detailSoften     = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("부드럽게 할 때 보는 이웃 거리(1080p 기준 픽셀)")]
        [SerializeField] private ClampedFloatParameter _detailRadius     = new ClampedFloatParameter(2f, 1f, 4f);
        [Tooltip("이 값보다 색 차이가 큰 이웃은 섞지 않는다(무늬 경계 보존)")]
        [SerializeField] private ClampedFloatParameter _detailColorSigma = new ClampedFloatParameter(0.15f, 0.03f, 0.5f);

        [Header("보존")]
        [Tooltip("이 밝기부터 툰 효과를 줄인다(발광, 강한 반사)")]
        [SerializeField] private ClampedFloatParameter _highlightKeepStart = new ClampedFloatParameter(1.2f, 0.5f, 4f);
        [Tooltip("시작 밝기에서 이만큼 더 밝으면 효과 0")]
        [SerializeField] private ClampedFloatParameter _highlightKeepRange = new ClampedFloatParameter(0.8f, 0.1f, 4f);
        [Tooltip("이 거리(m)부터 툰 효과를 줄인다")]
        [SerializeField] private ClampedFloatParameter _fadeStart          = new ClampedFloatParameter(40f, 0f, 200f);
        [Tooltip("시작 거리에서 이만큼 더 멀면 효과 0(m)")]
        [SerializeField] private ClampedFloatParameter _fadeRange          = new ClampedFloatParameter(40f, 1f, 200f);

        [Header("경계선")]
        [Tooltip("0이면 경계선을 끈다")]
        [SerializeField] private ClampedFloatParameter _edgeIntensity       = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("이웃 노멀 내적이 이 값보다 작으면 선")]
        [SerializeField] private ClampedFloatParameter _normalEdge          = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("깊이 2차 차분을 깊이로 나눈 값이 이보다 크면 선")]
        [SerializeField] private ClampedFloatParameter _depthEdgeThreshold  = new ClampedFloatParameter(0.06f, 0.005f, 0.5f);
        [Tooltip("노멀 경계를 끄는 거리(m)")]
        [SerializeField] private ClampedFloatParameter _normalEdgeFadeRange = new ClampedFloatParameter(15f, 1f, 100f);
        [Tooltip("선 위치의 원래 색에 곱하는 밝기")]
        [SerializeField] private ClampedFloatParameter _lineDarken          = new ClampedFloatParameter(0.45f, 0f, 1f);
        [Tooltip("선 색조")]
        [SerializeField] private ColorParameter        _lineTint            = new ColorParameter(Color.white);

        /****************************************
        *              Properties
        ****************************************/

        public float LightingIntensity   => _lightingIntensity.value;
        public float RampSteps           => _rampSteps.value;
        public float RampEdgeWidth       => _rampEdgeWidth.value;
        public Color ShadowColor         => _shadowColor.value;
        public float AmbientKeep         => _ambientKeep.value;
        public float RatioMin            => _ratioMin.value;
        public float RatioMax            => _ratioMax.value;
        public float DetailSoften        => _detailSoften.value;
        public float DetailRadius        => _detailRadius.value;
        public float DetailColorSigma    => _detailColorSigma.value;
        public float HighlightKeepStart  => _highlightKeepStart.value;
        public float HighlightKeepRange  => _highlightKeepRange.value;
        public float FadeStart           => _fadeStart.value;
        public float FadeRange           => _fadeRange.value;
        public float EdgeIntensity       => _edgeIntensity.value;
        public float NormalEdge          => _normalEdge.value;
        public float DepthEdgeThreshold  => _depthEdgeThreshold.value;
        public float NormalEdgeFadeRange => _normalEdgeFadeRange.value;
        public float LineDarken          => _lineDarken.value;
        public Color LineTint            => _lineTint.value;

        /****************************************
        *            Public Methods
        ****************************************/

        public bool IsActive()
        {
            return active && (_lightingIntensity.value > 0f || _edgeIntensity.value > 0f || _detailSoften.value > 0f);
        }
    }
}
