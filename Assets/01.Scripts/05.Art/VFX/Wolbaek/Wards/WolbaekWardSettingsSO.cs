// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 결계 프리팹별 표현 설정. 지점과 반경, 상태 전환은 외부 확정 입력을 사용한다 </summary>
    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Ward", fileName = "SO_WolbaekWard")]
    public sealed class WolbaekWardSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/
        [Header("프리팹 표현 기본값")]
        [SerializeField] private EWolbaekWardSign _defaultSign = EWolbaekWardSign.Diamond;
        [SerializeField, ColorUsage(true, true)] private Color _defaultColor = Color.white;

        [Header("연출 시간, 초")]
        [SerializeField, Min(0.001f)] private float _appearSeconds = 0.65f;
        [SerializeField, Min(0.001f)] private float _switchSeconds = 0.45f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds   = 0.8f;

        [Header("돔과 움직임, 미터와 초")]
        [SerializeField, Min(0.001f)] private float _domeHeight = 2.1f;
        [SerializeField, Min(0.001f)] private float _flowCycle  = 2.4f;
        [SerializeField, Min(0f)] private float _waveAmplitude  = 0.055f;
        [SerializeField, Min(0f)] private float _petalDrift     = 0.045f;

        [Header("표현 대비")]
        [SerializeField, Range(0f, 1f)] private float _shellOpacity  = 0.3f;
        [SerializeField, Range(0f, 1f)] private float _ribbonOpacity = 0.6f;
        [SerializeField, Range(0f, 1f)] private float _petalOpacity  = 0.75f;
        [SerializeField, Range(0f, 1f)] private float _moteOpacity   = 0.45f;
        [SerializeField, Range(0f, 1f)] private float _glyphOpacity  = 0.9f;
        [SerializeField, Min(0f)] private float _glyphStrength      = 1.5f;
        [SerializeField, Range(0f, 1f)] private float _switchWhiteShare = 0.35f;
        [SerializeField, ColorUsage(true, true)] private Color _petalTint = new Color(1.2f, 0.65f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float _assignedPetalTintShare = 0.3f;

        /****************************************
        *              Properties
        ****************************************/
        public EWolbaekWardSign DefaultSign => _defaultSign;
        public Color DefaultColor => _defaultColor;
        public float AppearSeconds => _appearSeconds;
        public float SwitchSeconds => _switchSeconds;
        public float FadeSeconds => _fadeSeconds;
        public float DomeHeight => _domeHeight;
        public float FlowCycle => _flowCycle;
        public float WaveAmplitude => _waveAmplitude;
        public float PetalDrift => _petalDrift;
        public float ShellOpacity => _shellOpacity;
        public float RibbonOpacity => _ribbonOpacity;
        public float PetalOpacity => _petalOpacity;
        public float MoteOpacity => _moteOpacity;
        public float GlyphOpacity => _glyphOpacity;
        public float GlyphStrength => _glyphStrength;
        public float SwitchWhiteShare => _switchWhiteShare;
        public Color PetalTint => _petalTint;
        public float AssignedPetalTintShare => _assignedPetalTintShare;
    }
}
