// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 독립 봉인핵의 표현 값. Opened는 시각 상태이며 공격 가능 여부나 체력을 뜻하지 않는다 </summary>
    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Seal Core", fileName = "SO_WolbaekSealCore")]
    public sealed class WolbaekSealCoreSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/
        [Header("발광 색")]
        [SerializeField, ColorUsage(true, true)] private Color _defaultColor = new Color(0.62f, 1.05f, 1.65f, 1f);

        [Header("연출 시간, 초")]
        [SerializeField, Min(0.001f)] private float _appearSeconds  = 0.5f;
        [SerializeField, Min(0.001f)] private float _openSeconds    = 0.55f;
        [SerializeField, Min(0.001f)] private float _shatterSeconds = 0.85f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds    = 0.65f;

        [Header("핵 높이와 파편, 미터와 회전 수")]
        [SerializeField, Min(0f)] private float _coreHeight                  = 1.65f;
        [SerializeField, Min(0f)] private float _openDistance                = 0.34f;
        [SerializeField, Min(0f)] private float _openTiltDegrees             = 18f;
        [SerializeField, Min(0f)] private float _openLift                    = 0.12f;
        [SerializeField, Range(0.1f, 1f)] private float _openPieceScale      = 0.78f;
        [SerializeField, Min(0f)] private float _shatterDistance             = 1.7f;
        [SerializeField, Min(0f)] private float _shatterLift                 = 0.65f;
        [SerializeField, Min(0f)] private float _shatterTurns                = 0.8f;
        [SerializeField, Range(0f, 0.99f)] private float _shatterShrinkStart = 0.35f;

        [Header("유동과 꽃잎, 미터와 초")]
        [SerializeField, Min(0.001f)] private float _flowCycle    = 2.6f;
        [SerializeField, Min(0f)] private float _flowAmplitude    = 0.075f;
        [SerializeField, Min(0f)] private float _shellDrift       = 0.035f;
        [SerializeField, Min(0f)] private float _shellTiltDegrees = 3.2f;
        [SerializeField, Min(0f)] private float _petalDrift       = 0.1f;
        [SerializeField, Min(0f)] private float _petalTurns       = 0.12f;

        [Header("표현 대비")]
        [SerializeField, Min(0f)] private float _dormantGlow         = 0.55f;
        [SerializeField, Min(0f)] private float _openedGlow          = 1.25f;
        [SerializeField, Range(0f, 1f)] private float _flowOpacity   = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _petalOpacity  = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _groundOpacity = 0.42f;
        [SerializeField, Range(0f, 1f)] private float _coreOpacity   = 0.86f;
        [SerializeField, Min(0f)] private float _flashStrength       = 2.2f;

        [Header("핵의 주변 조명")]
        [SerializeField, Min(0f)] private float _lightRange            = 2.6f;
        [SerializeField, Min(0f)] private float _dormantLightIntensity = 0.35f;
        [SerializeField, Min(0f)] private float _openedLightIntensity  = 2.4f;

        /****************************************
        *              Properties
        ****************************************/
        public Color DefaultColor => _defaultColor;
        public float AppearSeconds => _appearSeconds;
        public float OpenSeconds => _openSeconds;
        public float ShatterSeconds => _shatterSeconds;
        public float FadeSeconds => _fadeSeconds;
        public float CoreHeight => _coreHeight;
        public float OpenDistance => _openDistance;
        public float OpenTiltDegrees => _openTiltDegrees;
        public float OpenLift => _openLift;
        public float OpenPieceScale => _openPieceScale;
        public float ShatterDistance => _shatterDistance;
        public float ShatterLift => _shatterLift;
        public float ShatterTurns => _shatterTurns;
        public float ShatterShrinkStart => _shatterShrinkStart;
        public float FlowCycle => _flowCycle;
        public float FlowAmplitude => _flowAmplitude;
        public float ShellDrift => _shellDrift;
        public float ShellTiltDegrees => _shellTiltDegrees;
        public float PetalDrift => _petalDrift;
        public float PetalTurns => _petalTurns;
        public float DormantGlow => _dormantGlow;
        public float OpenedGlow => _openedGlow;
        public float FlowOpacity => _flowOpacity;
        public float PetalOpacity => _petalOpacity;
        public float GroundOpacity => _groundOpacity;
        public float CoreOpacity => _coreOpacity;
        public float FlashStrength => _flashStrength;
        public float LightRange => _lightRange;
        public float DormantLightIntensity => _dormantLightIntensity;
        public float OpenedLightIntensity => _openedLightIntensity;
    }
}
