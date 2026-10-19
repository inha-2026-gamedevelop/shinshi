// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 지점 기술을 확정한 클릭이 평타로 이어지지 않게 막는 입력 상태, 궁수 R만 서버가 수락해 동작이 끝나면 누른 평타를 다시 보낸다 </summary>
    public struct AttackSuppressionGate
    {
        /****************************************
        *              Constants
        ****************************************/

        // 수락 결과를 받고도 기술 동작 복제가 이 시간 안에 보이지 않으면 이미 끝난 것으로 본다
        public const double ACCEPTED_ACTION_WAIT_SECONDS = 1.0;

        /****************************************
        *                Fields
        ****************************************/

        private bool   _active;
        private bool   _resumable;
        private bool   _bound;
        private bool   _accepted;
        private bool   _actionSeen;
        private uint   _sequence;
        private double _acceptedAt;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 동작이 끝나면 누른 평타를 재개하는 기술인지, 궁수 R만이다 </summary>
        public static bool ResumesAfterSkill(EPlayerClass playerClass, ESkillRequestKind kind, byte slot)
        {
            return (playerClass == EPlayerClass.Archer) && (kind == ESkillRequestKind.Regular)
                && (slot == Constants.Combat.SKILL_SLOT_R);
        }

        /// <summary> 지점 확정 클릭 때 부른다, 재개하지 않는 기술은 손을 뗄 때까지만 막는다 </summary>
        public void Begin(bool resumable)
        {
            this       = default;
            _active    = true;
            _resumable = resumable;
        }

        /// <summary> 소유 해제, 사망, 비활성처럼 입력을 이어 갈 수 없을 때 상태를 비운다 </summary>
        public void Reset()
        {
            this = default;
        }

        /// <summary> 확정 뒤 처음 실제로 나간 기술 요청의 순번을 묶는다, 그 뒤에 또 나간 요청이 있으면 재개를 포기한다 </summary>
        public void OnSkillSent(uint sequence)
        {
            if (!_active)
            {
                return;
            }

            if (_bound)
            {
                _resumable = false;
                return;
            }

            _bound    = true;
            _sequence = sequence;
        }

        /// <summary> 묶은 순번의 결과만 받는다, 거절이면 재개를 포기하고 손을 뗄 때까지 막는다. 지난 요청의 결과는 무시한다 </summary>
        public void OnSkillResult(uint sequence, ERejectReason reason, double now)
        {
            if ((!_active) || (!_bound) || (_accepted) || (sequence != _sequence))
            {
                return;
            }

            if (reason != ERejectReason.None)
            {
                _resumable = false;
                return;
            }

            _accepted   = true;
            _acceptedAt = now;
        }

        /// <summary> 매 프레임 부른다, 이번 프레임에 공격 입력을 막아야 하면 참이다 </summary>
        public bool Update(bool isMouseHeld, ESkillPhase phase, double now)
        {
            if (!_active)
            {
                return false;
            }

            if (!isMouseHeld)
            {
                this = default;
                return false;
            }

            if ((!_resumable) || (!_accepted))
            {
                return true;
            }

            if (phase != ESkillPhase.Idle)
            {
                _actionSeen = true;
                return true;
            }

            if ((_actionSeen) || ((now - _acceptedAt) >= ACCEPTED_ACTION_WAIT_SECONDS))
            {
                this = default;
                return false;
            }

            return true;
        }
    }
}
