// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;

namespace Shinshi.Art
{
    /// <summary> 소유 클라이언트의 유효한 예측 이동만 읽고 원격이나 서버 추종 구간에는 샘플을 제공하지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleCharacterPredictionMotionSource : ClientView, IPlayerFemaleCharacterMotionSource
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerPrediction  _prediction;
        [SerializeField] private PlayerCombatState _combat;

        private readonly PlayerFemaleCharacterMotionSampler _sampler = new PlayerFemaleCharacterMotionSampler();

        private bool   _isBound;
        private string _validationError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public Transform MovementRoot   => (_combat != null) ? _combat.transform : null;
        public bool      IsBound        => _isBound;
        public string    ValidationError => _validationError;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryBind(out _validationError);
        }

        private void OnEnable()
        {
            ResetMotionSample();
        }

        private void OnDisable()
        {
            ResetMotionSample();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 플레이어 루트의 예측과 전관찰자 전투 상태를 읽기 전용으로 연결한다 </summary>
        public bool Configure(PlayerPrediction prediction, PlayerCombatState combat)
        {
            _prediction = prediction;
            _combat     = combat;
            return TryBind(out _validationError);
        }

        /// <summary> 다른 플레이어의 누적 이동량을 잘못 읽지 않도록 루트 참조를 확인한다 </summary>
        public bool TryBind(out string error)
        {
            _isBound = false;
            ResetMotionSample();
            if ((IsServerBuild) || (_prediction == null) || (_combat == null)
                || (_prediction.transform != _combat.transform))
            {
                error = "같은 플레이어 루트의 PlayerPrediction과 PlayerCombatState가 필요하다";
                _validationError = error;
                return false;
            }

            _isBound = true;
            error = string.Empty;
            _validationError = error;
            return true;
        }

        public bool TryRead(double unscaledTime, out float speed)
        {
            bool available = (_isBound) && (!IsServerBuild) && (isActiveAndEnabled)
                && (_prediction != null) && (_combat != null) && (_prediction.isActiveAndEnabled)
                && (_combat.isActiveAndEnabled) && (_prediction.transform == _combat.transform)
                && (_combat.isClient) && (_combat.isOwned) && (!_combat.isServer)
                && (_combat.LifeState == EParticipantLifeState.Alive) && (_prediction.IsPredicting);

            // 위치와 CorrectedDistance는 서버 보정을 포함한다. 예측 Advance가 쌓은 거리만 사용한다.
            float distance = (available) ? _prediction.TravelledDistance : 0f;
            return _sampler.TryRead(available, distance, unscaledTime, out speed);
        }

        public void ResetMotionSample()
        {
            _sampler.ResetMotionSample();
        }
    }
}
