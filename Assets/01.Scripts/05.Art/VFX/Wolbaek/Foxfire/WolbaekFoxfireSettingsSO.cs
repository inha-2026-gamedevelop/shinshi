// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 여우불 표현 시간과 흐름 설정, 피해 및 서버 판정 수치는 담지 않는다 </summary>
    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Foxfire", fileName = "SO_WolbaekFoxfire")]
    public sealed class WolbaekFoxfireSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("등장과 흐름")]
        [SerializeField, Min(0.001f)] private float _appearSeconds    = 0.18f;
        [SerializeField, Min(0.001f)] private float _flowCycleSeconds = 1.8f;
        [SerializeField] private float _uvFlowSpeed = 0.11f;
        [SerializeField, Min(0f)] private float _petalDriftDistance = 0.12f;
        [SerializeField, Min(0f)] private float _impactPetalRiseSpeed = 0.45f;
        [SerializeField, Min(0.001f)] private float _tailLagSeconds = 1.05f;
        [SerializeField, Min(0f)] private float _tailGrowSeconds = 0f;
        [SerializeField, Min(0.001f)] private float _teleportResetDistance = 4f;
        [SerializeField, Min(0f)] private float _flowBendDistance = 0.14f;
        [SerializeField, Min(0f)] private float _impactFragmentSpread = 0.45f;

        [Header("외부 확정 대기, 자동 폭발하지 않음")]
        [SerializeField, Min(0.001f)] private float _flightTimeoutSeconds = 8f;
        [SerializeField, Min(0f)] private float _confirmationGraceSeconds = 1f;

        [Header("확정한 타격의 표현")]
        [SerializeField, Min(0.001f)] private float _expansionSeconds = 0.24f;
        [SerializeField, Min(0f)] private float _holdSeconds = 0.42f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds = 0.65f;

        [Header("로컬 미리보기 전용")]
        [SerializeField, Min(0.001f)] private float _previewFlightSeconds = 1.4f;
        [SerializeField, Min(0.001f)] private float _previewLockedSeconds = 1.1f;
        [SerializeField, Min(0f)] private float _previewFlightDistance = 2.2f;

        /****************************************
        *              Properties
        ****************************************/

        public float AppearSeconds            => _appearSeconds;
        public float FlowCycleSeconds         => _flowCycleSeconds;
        public float UvFlowSpeed               => _uvFlowSpeed;
        public float PetalDriftDistance        => _petalDriftDistance;
        public float ImpactPetalRiseSpeed      => _impactPetalRiseSpeed;
        public float TailLagSeconds            => _tailLagSeconds;
        public float TailGrowSeconds           => _tailGrowSeconds;
        public float TeleportResetDistance     => _teleportResetDistance;
        public float FlowBendDistance          => _flowBendDistance;
        public float ImpactFragmentSpread     => _impactFragmentSpread;
        public float FlightTimeoutSeconds     => _flightTimeoutSeconds;
        public float ConfirmationGraceSeconds => _confirmationGraceSeconds;
        public float ExpansionSeconds         => _expansionSeconds;
        public float HoldSeconds              => _holdSeconds;
        public float FadeSeconds              => _fadeSeconds;
        public float PreviewFlightSeconds     => _previewFlightSeconds;
        public float PreviewLockedSeconds     => _previewLockedSeconds;
        public float PreviewFlightDistance    => _previewFlightDistance;
    }
}
