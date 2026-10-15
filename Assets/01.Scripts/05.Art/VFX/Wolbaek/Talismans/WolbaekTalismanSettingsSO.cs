// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부적 프리팹별 로컬 표현 설정. 유지 시간이나 성공 판정을 포함하지 않는다 </summary>
    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Talisman", fileName = "SO_WolbaekTalisman")]
    public sealed class WolbaekTalismanSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/
        [Header("프리팹 표현 기본값")]
        [SerializeField] private EWolbaekTalismanSign _defaultSign = EWolbaekTalismanSign.Moon;
        [SerializeField, ColorUsage(true, true)] private Color _defaultColor = Color.white;

        [Header("연출 시간, 초")]
        [SerializeField, Min(0.001f)] private float _appearSeconds  = 0.55f;
        [SerializeField, Min(0.001f)] private float _resetSeconds   = 0.3f;
        [SerializeField, Min(0.001f)] private float _successSeconds = 0.65f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds    = 0.75f;

        [Header("유기적인 움직임")]
        [SerializeField, Min(0.001f)] private float _flowCycle   = 1.8f;
        [SerializeField, Min(0.001f)] private float _petalCycle  = 3.2f;
        [SerializeField, Min(0f)] private float _petalDrift       = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _mistOpacity = 0.28f;

        [Header("종이, 룬, 꽃잎 대비")]
        [SerializeField, Range(0f, 1f)] private float _paperOpacity = 0.62f;
        [SerializeField, Min(0f)] private float _runeStrength       = 1.4f;
        [SerializeField, ColorUsage(true, true)] private Color _petalTint = new Color(1.5f, 0.85f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float _assignedPetalTintShare = 0.25f;
        [SerializeField, Min(0f)] private float _groundLightStrength = 0.7f;

        [Header("발밑 표현 광원")]
        [SerializeField, Min(0f)] private float _groundLightIntensity = 0.6f;
        [SerializeField, Min(0.01f)] private float _groundLightRange  = 2.3f;

        [Header("공중 잔상, 성공 상승")]
        [SerializeField, Range(0f, 1f)] private float _airMistOpacity     = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _successMistOpacity = 0.45f;
        [SerializeField, Min(0f)] private float _successRise              = 2.4f;
        [SerializeField, Min(0f)] private float _paperSuccessRise         = 1.4f;

        /****************************************
        *              Properties
        ****************************************/
        public EWolbaekTalismanSign DefaultSign => _defaultSign;
        public Color DefaultColor => _defaultColor;
        public float AppearSeconds => _appearSeconds;
        public float ResetSeconds => _resetSeconds;
        public float SuccessSeconds => _successSeconds;
        public float FadeSeconds => _fadeSeconds;
        public float FlowCycle => _flowCycle;
        public float PetalCycle => _petalCycle;
        public float PetalDrift => _petalDrift;
        public float MistOpacity => _mistOpacity;
        public float PaperOpacity => _paperOpacity;
        public float RuneStrength => _runeStrength;
        public Color PetalTint => _petalTint;
        public float AssignedPetalTintShare => _assignedPetalTintShare;
        public float GroundLightStrength => _groundLightStrength;
        public float GroundLightIntensity => _groundLightIntensity;
        public float GroundLightRange => _groundLightRange;
        public float AirMistOpacity => _airMistOpacity;
        public float SuccessMistOpacity => _successMistOpacity;
        public float SuccessRise => _successRise;
        public float PaperSuccessRise => _paperSuccessRise;
    }
}
