namespace Shinshi.AI
{
    /// <summary> 숲 튜토리얼 한 번의 진행 기억, 재개하면 새로 만든다. 부적 확인 여부만 바깥에서 받는다 </summary>
    public sealed class ForestProgress
    {
        /****************************************
        *                Fields
        ****************************************/

        // 첫 전투의 일반 산귀 수, 실제 플레이 뒤 조정할 수 있는 기획 확정안이다
        public const int FIRST_FIGHT_ENEMY_COUNT = 2;

        private EForestPhase _phase             = EForestPhase.Talisman;
        private int          _firstFightKills   = 0;
        private bool         _usedAttack        = false;
        private bool         _usedSkillQ        = false;
        private bool         _telegraphSeen     = false;
        private bool         _telegraphActive   = false;

        /****************************************
        *              Properties
        ****************************************/

        public EForestPhase Phase           => _phase;
        public int          FirstFightKills => _firstFightKills;
        public bool         IsDone          => _phase == EForestPhase.Done;

        // 예고 공격을 한 번이라도 봤다, 피격 여부는 상관없다
        public bool         TelegraphSeen   => _telegraphSeen;

        // 입력을 막아야 하는 구간인가
        public bool         BlocksInput     => _phase == EForestPhase.Talisman;

        public EForestHint  CurrentHint     => ResolveHint();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이미 부적을 확인한 캐릭터는 조사 화면 없이 첫 전투로 시작한다 </summary>
        public ForestProgress(bool talismanChecked)
        {
            _phase = talismanChecked ? EForestPhase.FirstFight : EForestPhase.Talisman;
        }

        public void OnTalismanConfirmed()
        {
            if (_phase != EForestPhase.Talisman)
            {
                return;
            }

            _phase = EForestPhase.FirstFight;
        }

        // 명중은 필요 없고 사용 한 번이면 된다. 적이 없어도 인정한다
        public void OnAttackUsed()
        {
            if (_phase != EForestPhase.FirstFight)
            {
                return;
            }

            _usedAttack = true;
            TryFinishFirstFight();
        }

        public void OnSkillQUsed()
        {
            if (_phase != EForestPhase.FirstFight)
            {
                return;
            }

            _usedSkillQ = true;
            TryFinishFirstFight();
        }

        public void OnEnemyKilled()
        {
            switch (_phase)
            {
                case EForestPhase.FirstFight:
                    ++_firstFightKills;
                    TryFinishFirstFight();
                    break;
                case EForestPhase.SecondFight:
                    // 예고 공격 전에 처치했다면 예고 학습은 생략하고 그대로 끝난다
                    _telegraphActive = false;
                    _phase           = EForestPhase.Done;
                    break;
                default:
                    break;
            }
        }

        public void OnTelegraphStarted()
        {
            if (_phase != EForestPhase.SecondFight)
            {
                return;
            }

            _telegraphSeen   = true;
            _telegraphActive = true;
        }

        public void OnTelegraphEnded()
        {
            _telegraphActive = false;
        }

        // 적을 다 잡았어도 쓰지 않은 기본 공격과 Q가 남았으면 첫 전투는 끝나지 않는다
        private void TryFinishFirstFight()
        {
            if ((_firstFightKills < FIRST_FIGHT_ENEMY_COUNT) || (!_usedAttack) || (!_usedSkillQ))
            {
                return;
            }

            _phase = EForestPhase.SecondFight;
        }

        private EForestHint ResolveHint()
        {
            switch (_phase)
            {
                case EForestPhase.FirstFight:
                    if (!_usedAttack)
                    {
                        return EForestHint.BasicAttack;
                    }

                    return _usedSkillQ ? EForestHint.None : EForestHint.SkillQ;
                case EForestPhase.SecondFight:
                    return _telegraphActive ? EForestHint.LeaveDangerZone : EForestHint.None;
                default:
                    return EForestHint.None;
            }
        }
    }
}
