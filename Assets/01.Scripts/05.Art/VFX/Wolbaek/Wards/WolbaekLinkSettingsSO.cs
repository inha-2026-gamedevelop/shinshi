// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 연결선 프리팹별 표현 설정. 연결 대상과 위험 단계는 외부에서 공급한다 </summary>
    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Link", fileName = "SO_WolbaekLink")]
    public sealed class WolbaekLinkSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/
        [Header("프리팹 표현 기본값")]
        [SerializeField] private EWolbaekWardSign _defaultSign = EWolbaekWardSign.Diamond;
        [SerializeField, ColorUsage(true, true)] private Color _defaultColor = Color.white;

        [Header("연출 시간, 초")]
        [SerializeField, Min(0.001f)] private float _appearSeconds = 0.45f;
        [SerializeField, Min(0.001f)] private float _switchSeconds = 0.25f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds   = 0.65f;
        [SerializeField, Min(0.001f)] private float _flowCycle     = 2.6f;
        [SerializeField, Min(0.001f)] private float _warningCycle  = 0.65f;

        [Header("움직임과 끝점, 미터")]
        [SerializeField, Min(0f)] private float _waveAmplitude = 0.09f;
        [SerializeField, Min(0f)] private float _moteDrift     = 0.035f;
        [SerializeField, Min(0f)] private float _linkHeight    = 1f;
        [SerializeField, Min(0.001f)] private float _endpointRadius = 0.35f;

        [Header("표현 대비")]
        [SerializeField, Range(0f, 1f)] private float _ribbonOpacity = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _moteOpacity   = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _glyphOpacity  = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _endpointOpacity = 0.65f;
        [SerializeField, Min(0f)] private float _glyphStrength = 1.5f;
        [SerializeField, Min(0f)] private float _warningStrength = 0.6f;
        [SerializeField, Min(0f)] private float _dangerStrength  = 1.1f;
        [SerializeField, Range(0f, 1f)] private float _warningWhiteShare = 0.25f;

        /****************************************
        *              Properties
        ****************************************/
        public EWolbaekWardSign DefaultSign => _defaultSign;
        public Color DefaultColor => _defaultColor;
        public float AppearSeconds => _appearSeconds;
        public float SwitchSeconds => _switchSeconds;
        public float FadeSeconds => _fadeSeconds;
        public float FlowCycle => _flowCycle;
        public float WarningCycle => _warningCycle;
        public float WaveAmplitude => _waveAmplitude;
        public float MoteDrift => _moteDrift;
        public float LinkHeight => _linkHeight;
        public float EndpointRadius => _endpointRadius;
        public float RibbonOpacity => _ribbonOpacity;
        public float MoteOpacity => _moteOpacity;
        public float GlyphOpacity => _glyphOpacity;
        public float EndpointOpacity => _endpointOpacity;
        public float GlyphStrength => _glyphStrength;
        public float WarningStrength => _warningStrength;
        public float DangerStrength => _dangerStrength;
        public float WarningWhiteShare => _warningWhiteShare;
    }
}
