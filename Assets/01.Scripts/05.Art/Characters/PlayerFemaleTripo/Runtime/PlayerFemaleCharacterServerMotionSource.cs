// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;

namespace Shinshi.Art
{
    /// <summary> 원격 관찰자용 속도 소스, Transform 차분이 아니라 서버가 공개한 LocomotionSpeed SyncVar를 그대로 읽는다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleCharacterServerMotionSource : ClientView, IPlayerFemaleCharacterMotionSource
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerCombatState _combat;

        private bool   _isBound;
        private string _validationError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public Transform MovementRoot    => (_combat != null) ? _combat.transform : null;
        public bool      IsBound         => _isBound;
        public string    ValidationError => _validationError;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryBind(out _validationError);
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool Configure(PlayerCombatState combat)
        {
            _combat = combat;
            return TryBind(out _validationError);
        }

        public bool TryBind(out string error)
        {
            _isBound = false;
            if ((IsServerBuild) || (_combat == null))
            {
                error = "서버가 공개하는 PlayerCombatState가 필요하다";
                _validationError = error;
                return false;
            }

            _isBound = true;
            error = string.Empty;
            _validationError = error;
            return true;
        }

        // 소유자, 서버, 사망 상태에서는 항상 거짓이다. 바인딩되고 원격이며 살아 있을 때만 서버가 공개한 값을 그대로 돌려준다
        public bool TryRead(double unscaledTime, out float speed)
        {
            bool available = (_isBound) && (!IsServerBuild) && (isActiveAndEnabled) && (_combat != null)
                && (_combat.isActiveAndEnabled) && (_combat.isClient) && (!_combat.isOwned) && (!_combat.isServer)
                && (_combat.LifeState == EParticipantLifeState.Alive);

            speed = available ? _combat.LocomotionSpeed : 0f;
            return available;
        }

        public void ResetMotionSample() { }
    }
}
