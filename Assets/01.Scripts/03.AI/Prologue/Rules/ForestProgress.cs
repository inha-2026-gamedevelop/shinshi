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
        private bool         _talismanOpen      = false;

        /****************************************
        *              Properties
        ****************************************/

        public EForestPhase Phase           => _phase;
        public int          FirstFightKills => _firstFightKills;
        public bool         IsDone          => _phase == EForestPhase.Done;

        // 예고 공격을 한 번이라도 봤다, 피격 여부는 상관없다
        public bool         TelegraphSeen   => _telegraphSeen;

        // 입력을 막아야 하는가, 부적 조사 화면이 떠 있는 동안만이고 부적까지 걸어가는 동안은 막지 않는다
        public bool         BlocksInput     => (_phase == EForestPhase.Talisman) && (_talismanOpen);

        public EForestHint  CurrentHint     => ResolveHint();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이미 부적을 확인한 캐릭터는 조사 화면 없이 첫 전투로 시작한다 </summary>
        public ForestProgress(bool talismanChecked)
        {
            _phase = talismanChecked ? EForestPhase.FirstFight : EForestPhase.Talisman;
        }

        public void OnTalismanApproached()
        {
            if (_phase != EForestPhase.Talisman)
            {
                return;
            }

            _talismanOpen = true;
        }

        public void OnTalismanConfirmed()
        {
            if (_phase != EForestPhase.Talisman)
            {
                return;
            }

            _talismanOpen = false;
            _phase        = EForestPhase.FirstFight;
        }

        // 안내가 그 행동을 요구하는 동안 쓴 것만 인정한다. 명중은 필요 없고 사용 한 번이면 되며 적이 없어도 인정한다
        public void OnAttackUsed()
        {
            if (!AcceptsAttack())
            {
                return;
            }

            _usedAttack = true;
            TryFinishFirstFight();
        }

        public void OnSkillQUsed()
        {
            if (!AcceptsSkillQ())
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

        private bool AcceptsAttack()
        {
            if (_phase != EForestPhase.FirstFight)
            {
                return false;
            }

            EForestHint hint = ResolveFirstFightHint();
            return (hint == EForestHint.BasicAttack) || (hint == EForestHint.UseAttackOnce) || (hint == EForestHint.UseBothOnce);
        }

        private bool AcceptsSkillQ()
        {
            if (_phase != EForestPhase.FirstFight)
            {
                return false;
            }

            EForestHint hint = ResolveFirstFightHint();
            return (hint == EForestHint.SkillQ) || (hint == EForestHint.UseSkillOnce) || (hint == EForestHint.UseBothOnce);
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
                case EForestPhase.Talisman:
                    return _talismanOpen ? EForestHint.None : EForestHint.Move;
                case EForestPhase.FirstFight:
                    return ResolveFirstFightHint();
                case EForestPhase.SecondFight:
                    return _telegraphActive ? EForestHint.LeaveDangerZone : EForestHint.FightTelegraphEnemy;
                default:
                    return EForestHint.None;
            }
        }

        // 일반 산귀를 다 잡았는데 쓰지 않은 행동이 남았으면 그것만 남겨 안내한다. 아직 적이 남았으면 기본 공격,
        // 두 번째 산귀가 나온 뒤의 Q, 그 밖에는 남은 산귀 처치 순이다
        private EForestHint ResolveFirstFightHint()
        {
            if (_firstFightKills >= FIRST_FIGHT_ENEMY_COUNT)
            {
                if ((!_usedAttack) && (!_usedSkillQ))
                {
                    return EForestHint.UseBothOnce;
                }

                return (!_usedAttack) ? EForestHint.UseAttackOnce : EForestHint.UseSkillOnce;
            }

            if (!_usedAttack)
            {
                return EForestHint.BasicAttack;
            }

            if ((!_usedSkillQ) && (_firstFightKills >= 1))
            {
                return EForestHint.SkillQ;
            }

            return EForestHint.KillRemaining;
        }
    }
}
