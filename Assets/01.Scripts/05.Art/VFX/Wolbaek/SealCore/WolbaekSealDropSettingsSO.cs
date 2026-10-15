// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 봉인핵과 독립 좌표에서 재생하는 낙하 여우불의 표현 값. 타격은 외부 Resolved 입력을 사용한다 </summary>
    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Seal Drop", fileName = "SO_WolbaekSealDrop")]
    public sealed class WolbaekSealDropSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/
        [Header("발광 색")]
        [SerializeField, ColorUsage(true, true)] private Color _defaultColor = new Color(0.62f, 1.05f, 1.65f, 1f);

        [Header("연출 시간, 초")]
        [SerializeField, Min(0.001f)] private float _appearSeconds   = 0.45f;
        [SerializeField, Min(0.001f)] private float _resolvedSeconds = 0.6f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds     = 0.65f;

        [Header("낙하와 유동, 미터와 초")]
        [SerializeField, Min(0f)] private float _dropHeight     = 6f;
        [SerializeField, Min(0f)] private float _hoverAmplitude = 0.14f;
        [SerializeField, Min(0.001f)] private float _flowCycle  = 1.9f;

        [Header("표현 대비")]
        [SerializeField, Range(0f, 1f)] private float _flameOpacity  = 0.88f;
        [SerializeField, Range(0f, 1f)] private float _groundOpacity = 0.5f;
        [SerializeField, Min(0f)] private float _flashStrength       = 2.5f;

        /****************************************
        *              Properties
        ****************************************/
        public Color DefaultColor => _defaultColor;
        public float AppearSeconds => _appearSeconds;
        public float ResolvedSeconds => _resolvedSeconds;
        public float FadeSeconds => _fadeSeconds;
        public float DropHeight => _dropHeight;
        public float HoverAmplitude => _hoverAmplitude;
        public float FlowCycle => _flowCycle;
        public float FlameOpacity => _flameOpacity;
        public float GroundOpacity => _groundOpacity;
        public float FlashStrength => _flashStrength;
    }
}
