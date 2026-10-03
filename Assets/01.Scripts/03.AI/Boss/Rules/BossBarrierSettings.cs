// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기획이 확정하지 않은 값이라 전부 임시값이다. 위험 구역 배치, 예고 시간, 전개 시간, 피해량, 피해 간격은 테스트로 정한다
    // 안전 구역 크기는 확정하지 않았다. 아래 제한 비율은 원안의 전장 제한 20~30% 참고값 안의 임시 중간값일 뿐이다
    // 확정되면 BossPatternCatalogSO로 옮긴다. 그 전에는 서버 연결이 이 값을 주입한다
    /// <summary> 월백 결계 전개의 설정값 묶음, 모든 값이 임시값이다 </summary>
    [Serializable]
    public sealed class BossBarrierSettings
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("임시값, 기획 미확정")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 12f;                // m/s, 보스가 전개 위치로 이동하는 속도
        [SerializeField, Min(0f)]   private float _moveTimeoutSeconds = 3f;        // 이동이 막혀도 이 시간 뒤에는 예고를 시작한다
        [SerializeField, Min(0.1f)] private float _telegraphSeconds = 2.5f;        // 위험 구역 예고 시간
        [SerializeField, Min(0f)]   private float _deploySeconds = 1.5f;           // 활성 뒤 전개 시퀀스가 끝날 때까지의 시간
        [SerializeField, Range(0.01f, 0.95f)] private float _restrictedAreaRatio = 0.25f; // 전장 면적 중 위험 구역 비율
        [SerializeField, Min(0.1f)] private float _tickInterval = 1f;              // 지속 피해 간격
        [SerializeField, Min(0f)]   private float _damageRef = 0.02f;              // 회당 피해, 기준 체력 대비 비율

        /****************************************
        *              Properties
        ****************************************/

        public float MoveSpeed           => _moveSpeed;
        public float MoveTimeoutSeconds  => _moveTimeoutSeconds;
        public float TelegraphSeconds    => _telegraphSeconds;
        public float DeploySeconds       => _deploySeconds;
        public float RestrictedAreaRatio => _restrictedAreaRatio;
        public float TickInterval        => _tickInterval;
        public float DamageRef           => _damageRef;

        /****************************************
        *                Methods
        ****************************************/

        public BossBarrierSettings() { }

        public BossBarrierSettings(float moveSpeed, float moveTimeoutSeconds, float telegraphSeconds, float deploySeconds,
            float restrictedAreaRatio, float tickInterval, float damageRef)
        {
            _moveSpeed           = moveSpeed;
            _moveTimeoutSeconds  = moveTimeoutSeconds;
            _telegraphSeconds    = telegraphSeconds;
            _deploySeconds       = deploySeconds;
            _restrictedAreaRatio = restrictedAreaRatio;
            _tickInterval        = tickInterval;
            _damageRef           = damageRef;
        }

        /// <summary> 전장 바깥 반경에서 위험 구역이 면적 비율만큼 되도록 안쪽 반경을 구한다, 이 안쪽이 안전 구역이다 </summary>
        public float GetInnerRadius(float outerRadius)
        {
            return outerRadius * Mathf.Sqrt(1f - Mathf.Clamp(_restrictedAreaRatio, 0f, 1f));
        }
    }
}
