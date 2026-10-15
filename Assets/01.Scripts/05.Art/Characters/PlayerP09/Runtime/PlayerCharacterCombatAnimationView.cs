// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Art
{
    /// <summary> 서버가 공개한 전투 상태(공격, 회피 순번, 시전 번호, 체력, 생사)를 읽어 P09 애니메이터의 행동 모션을 재생한다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterCombatAnimationView : ClientView
    {
        // Mirror는 한 메시지의 SyncVar를 선언 순서대로 적용하며 hook을 바로 부를 수 있다. hook 시점에는 같은 묶음의 뒤쪽 값이
        // 아직 갱신 전일 수 있으므로 판단은 메시지 처리(NetworkEarlyUpdate)가 끝난 뒤의 Update에서 현재 값으로만 한다.
        // 체력 감소만 hook에서 표시해 두고 Update에서 생사, 기술 단계와 함께 판정한다.
        // 처음 연결하면 지난 공격, 회피는 재생하지 않고 기준값으로만 잡는다. 진행 중인 기술은 서버 시각에 맞춘 위치부터 복원한다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int   _attackHash      = Animator.StringToHash("Attack");
        private static readonly int   _skillHash       = Animator.StringToHash("Skill");
        private static readonly int   _hitHash         = Animator.StringToHash("Hit");
        private static readonly int   _dodgeHash       = Animator.StringToHash("Dodge");
        private static readonly int   _actionIndexHash = Animator.StringToHash("ActionIndex");
        private static readonly int   _actionSpeedHash = Animator.StringToHash("ActionSpeed");
        private static readonly int   _aliveHash       = Animator.StringToHash("Alive");

        // 기술 상태 이름 해시, [행동 인덱스][분절]. 첫 분절은 Skill{i}, 이어지는 분절은 Skill{i}_{s}다
        private static readonly int[][] G_SkillStates = BuildSkillStateHashes();

        private const int BASE_LAYER = 0;

        private Animator                       _animator;
        private PlayerCombatState              _combat;
        private PlayerCombatAnimationProfileSO _profile;

        private bool  _needsBaseline  = false;
        private bool  _isAlive        = true;
        private byte  _attackBaseline = 0;
        private byte  _dodgeBaseline  = 0;
        private int   _castBaseline   = 0;
        private int   _attackIndex    = -1;
        private float _lastAttackTime = float.NegativeInfinity;
        private float _lastHitTime    = float.NegativeInfinity;
        private bool  _hasHealthDrop  = false;
        private float _lastHealth     = -1f;
        private int   _hitSide        = 0;

        // 피격이상 면역 콤보 단계를 재생하는 동안은 피격 모션으로 끊지 않는다
        private float _hitSuppressedUntil = float.NegativeInfinity;

        private bool                           _hasSpecCache = false;
        private EPlayerClass                   _specClass    = EPlayerClass.Warrior;
        private PlayerConfigSO.BasicAttackSpec _spec;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable()
        {
            ResetActionTriggers();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Update()
        {
            if ((_animator == null) || (_combat == null) || (_profile == null) || (!_animator.isActiveAndEnabled))
            {
                return;
            }

            if (_needsBaseline)
            {
                CaptureBaseline();
                return;
            }

            bool isAlive = _combat.LifeState == EParticipantLifeState.Alive;
            if (isAlive != _isAlive)
            {
                _isAlive = isAlive;
                _animator.SetBool(_aliveHash, isAlive);
                ResetActionTriggers();
                ConsumeBaselines();
                return;
            }

            // 쓰러져 있는 동안 생긴 행동은 재생하지 않고 기준값만 따라간다
            if (!isAlive)
            {
                ConsumeBaselines();
                _hasHealthDrop = false;
                return;
            }

            bool isDodgeNew  = CombatAnimationTriggerRules.IsNewSequence(_dodgeBaseline, _combat.DodgeSequence);
            bool isSkillNew  = CombatAnimationTriggerRules.IsNewCast(_castBaseline, _combat.CastInstanceId);
            bool isAttackNew = CombatAnimationTriggerRules.IsNewSequence(_attackBaseline, _combat.AttackSequence);
            CombatAnimationTriggerRules.EAction action = CombatAnimationTriggerRules.SelectAction(isDodgeNew, isSkillNew, isAttackNew);

            switch (action)
            {
                case CombatAnimationTriggerRules.EAction.Dodge:
                    PlayDodge();
                    break;
                case CombatAnimationTriggerRules.EAction.Skill:
                    if (!TryPlayNewSkill())
                    {
                        // 아직 시작 전이면 시전 기준값을 남겨 다음 Update에서 다시 본다
                        ConsumeSequences();
                        return;
                    }

                    break;
                case CombatAnimationTriggerRules.EAction.Attack:
                    PlayAttack();
                    break;
                default:
                    break;
            }

            // 고르지 않은 행동도 이번 값으로 소비해 다음 프레임에 뒤늦게 재생하지 않는다
            if (action != CombatAnimationTriggerRules.EAction.None)
            {
                ConsumeBaselines();
                _hasHealthDrop = false;
                return;
            }

            TryPlayHit();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 조립된 P09 애니메이터와 연결한다. 기준값은 다음 Update에서 메시지 처리가 끝난 값으로 잡는다 </summary>
        public void Configure(Animator animator, PlayerCombatState combat, PlayerCombatAnimationProfileSO profile)
        {
            Unbind();
            _animator      = animator;
            _combat        = combat;
            _profile       = profile;
            _needsBaseline = true;
            _attackIndex   = -1;

            _hasSpecCache       = false;
            _hitSuppressedUntil = float.NegativeInfinity;
            if (_combat != null)
            {
                _combat.HealthChanged += OnHealthChanged;
                _lastHealth = _combat.Health;
            }
        }

        public void Clear()
        {
            Unbind();
            _animator = null;
            _profile  = null;
        }

        private void Unbind()
        {
            if (_combat != null)
            {
                _combat.HealthChanged -= OnHealthChanged;
            }

            _combat = null;
        }

        private void OnHealthChanged(float health, float maxHealth)
        {
            if ((_lastHealth >= 0f) && (health < _lastHealth))
            {
                _hasHealthDrop = true;
            }

            _lastHealth = health;
        }

        private void CaptureBaseline()
        {
            _needsBaseline = false;
            ResetActionTriggers();
            _isAlive = _combat.LifeState == EParticipantLifeState.Alive;
            _animator.SetBool(_aliveHash, _isAlive);
            _attackBaseline = _combat.AttackSequence;
            _dodgeBaseline  = _combat.DodgeSequence;
            _hasHealthDrop  = false;
            _lastHealth     = _combat.Health;

            // 진행 중인 기술은 서버 시각에 맞춘 위치부터 복원한다. 시전 기준값을 비워 두어 다음 Update가 새 시전으로 다룬다
            if ((_isAlive) && (IsSkillRunning(_combat.SkillPhase)) && (_combat.CastInstanceId != 0))
            {
                _castBaseline = 0;
                return;
            }

            _castBaseline = _combat.CastInstanceId;
        }

        private void PlayDodge()
        {
            float target = Mathf.Max(GameDB.Player.DodgeDuration, _profile.MinDodgeSeconds);
            float speed  = CombatAnimationTriggerRules.ComputeSpeed(_profile.DodgeClipLength, target, _profile.MinSpeed, _profile.MaxSpeed);
            Fire(_dodgeHash, 0, speed, "Dodge");
            _hitSuppressedUntil = float.NegativeInfinity;
        }

        // 신규 직업은 프로필에 배정된 단계만 재생한다. 배정이 없으면(정식 배정 보류) 대체 클립도 속도 강제도 없이 아무것도 재생하지 않는다
        private void PlayAttack()
        {
            EPlayerClass playerClass = _combat.PlayerClass;
            int  stepCount = _profile.GetAttackStepCount(playerClass);
            bool isLegacy  = (stepCount == 0) && (PlayerCombatAnimationProfileSO.IsLegacyClass(playerClass));
            if ((stepCount == 0) && (!isLegacy))
            {
                return;
            }

            if (!TryGetAttackSpec(playerClass, out PlayerConfigSO.BasicAttackSpec spec))
            {
                return;
            }

            // 서버 콤보 직업은 서버가 고른 단계를 그대로 쓴다. 아니면 모션만 클라이언트가 돌려 쓴다
            int comboCount = isLegacy ? PlayerCombatAnimationProfileSO.ATTACK_COUNT : stepCount;
            int serverStep = spec.HasCombo ? _combat.AttackComboStep : 0;
            float now = Time.unscaledTime;
            _attackIndex    = spec.HasCombo
                ? Mathf.Min(serverStep, comboCount - 1)
                : CombatAnimationTriggerRules.NextAttackIndex(_attackIndex, now - _lastAttackTime,
                    _profile.GetComboResetSeconds(playerClass), comboCount);
            _lastAttackTime = now;

            int actionIndex = _attackIndex;
            if ((!isLegacy) && (!_profile.TryGetAttackIndex(playerClass, _attackIndex, out actionIndex)))
            {
                return;
            }

            float target = spec.GetInterval(serverStep);
            if (target <= 0f)
            {
                return;
            }

            if (spec.IsHitImmune(serverStep))
            {
                _hitSuppressedUntil = now + target;
            }

            float clipLength = _profile.GetAttackClipLength(actionIndex);
            if (_profile.TryGetAttackSegments(actionIndex, out float[] segments))
            {
                clipLength = CombatAnimationTriggerRules.SumLengths(segments);
            }

            float speed = CombatAnimationTriggerRules.ComputeSpeed(clipLength, target, _profile.MinSpeed, _profile.MaxSpeed);
            Fire(_attackHash, actionIndex, speed, "Attack");
        }

        // 서버 기본 공격 명세(PlayerConfigSO)를 직업별로 한 번만 읽는다
        private bool TryGetAttackSpec(EPlayerClass playerClass, out PlayerConfigSO.BasicAttackSpec spec)
        {
            if ((!_hasSpecCache) || (_specClass != playerClass))
            {
                _hasSpecCache = true;
                _specClass    = playerClass;
                _spec         = GameDB.Player.GetBasicAttack(playerClass);
            }

            spec = _spec;
            return spec != null;
        }

        // 새 시전을 재생한다. 아직 시작 전이면 거짓을 돌려 시전 기준값을 소비하지 않게 한다
        private bool TryPlayNewSkill()
        {
            // 정식 배정이 없는 기술(시간 예산 초과 보류 포함)은 아무것도 재생하지 않는다
            if ((!_profile.TryGetSkillIndex(_combat.PlayerClass, _combat.SkillKind, _combat.SkillSlot, out int index))
                || (index < 0) || (index >= G_SkillStates.Length) || (!TryGetSkillTimes(out float windup, out float total)))
            {
                return true;
            }

            float   clipLength = _profile.GetSkillClipLength(index);
            bool    isChained  = _profile.TryGetSkillSegments(index, out float[] segments);
            if (isChained)
            {
                clipLength = CombatAnimationTriggerRules.SumLengths(segments);
            }

            float   speed   = CombatAnimationTriggerRules.ComputeSpeed(clipLength, total, _profile.MinSpeed, _profile.MaxSpeed);
            double  elapsed = NetworkTime.time - (_combat.SkillResolveAt - windup);
            int     segment = 0;
            float   normalized;
            bool    isWaiting;
            bool    canResume;
            if (isChained)
            {
                canResume = CombatAnimationTriggerRules.TryGetSegmentPosition(elapsed, speed, segments, out segment, out normalized,
                    out isWaiting);
            }
            else
            {
                canResume = CombatAnimationTriggerRules.TryGetResumePosition(elapsed, speed, clipLength, out normalized, out isWaiting);
            }

            if (isWaiting)
            {
                return false;
            }

            // 이미 끝난 시전은 재생하지 않는다
            if ((!canResume) || (segment >= G_SkillStates[index].Length))
            {
                return true;
            }

            ResetActionTriggers();
            _animator.SetInteger(_actionIndexHash, index);
            _animator.SetFloat(_actionSpeedHash, speed);
            _animator.Play(G_SkillStates[index][segment], BASE_LAYER, normalized);
            LogAction("Skill", index);
            return true;
        }

        private void TryPlayHit()
        {
            if (!_hasHealthDrop)
            {
                return;
            }

            _hasHealthDrop = false;
            float now = Time.unscaledTime;
            if ((IsSkillCasting(_combat.SkillPhase)) || ((now - _lastHitTime) < _profile.HitRepeatSeconds) || (now < _hitSuppressedUntil))
            {
                return;
            }

            _lastHitTime = now;
            _hitSide     = 1 - _hitSide;
            float speed = CombatAnimationTriggerRules.ComputeSpeed(_profile.GetHitClipLength(_hitSide), _profile.HitSeconds,
                _profile.MinSpeed, _profile.MaxSpeed);
            Fire(_hitHash, _hitSide, speed, "Hit");
        }

        private bool TryGetSkillTimes(out float windup, out float total)
        {
            windup = 0f;
            total  = 0f;
            SkillCatalogSO skills = GameDB.Skills;
            if (skills == null)
            {
                return false;
            }

            if (_combat.SkillKind == ESkillRequestKind.Regular)
            {
                if (!skills.TryGetSkillBySlot(_combat.PlayerClass, _combat.SkillSlot, out SkillCatalogSO.SkillDefinition skill))
                {
                    return false;
                }

                windup = skill.Timeline.Windup;
                total  = skill.Timeline.Windup + skill.Timeline.Active + skill.Timeline.Recovery;
                return total > 0f;
            }

            for (int i = 0; i < skills.Ascensions.Count; ++i)
            {
                SkillCatalogSO.AscensionDefinition ascension = skills.Ascensions[i];
                if (ascension.Slot == _combat.SkillSlot)
                {
                    windup = ascension.Windup;
                    total  = ascension.Windup + ascension.Recovery;
                    return total > 0f;
                }
            }

            return false;
        }

        private void Fire(int trigger, int index, float speed, string label)
        {
            ResetActionTriggers();
            _animator.SetInteger(_actionIndexHash, index);
            _animator.SetFloat(_actionSpeedHash, speed);
            _animator.SetTrigger(trigger);
            LogAction(label, index);
        }

        private void ResetActionTriggers()
        {
            if (_animator == null)
            {
                return;
            }

            _animator.ResetTrigger(_attackHash);
            _animator.ResetTrigger(_skillHash);
            _animator.ResetTrigger(_hitHash);
            _animator.ResetTrigger(_dodgeHash);
        }

        private void ConsumeBaselines()
        {
            ConsumeSequences();
            _castBaseline = _combat.CastInstanceId;
        }

        private void ConsumeSequences()
        {
            _attackBaseline = _combat.AttackSequence;
            _dodgeBaseline  = _combat.DodgeSequence;
        }

        private static bool IsSkillRunning(ESkillPhase phase)
        {
            return (phase == ESkillPhase.Windup) || (phase == ESkillPhase.Active) || (phase == ESkillPhase.Recovery);
        }

        private static bool IsSkillCasting(ESkillPhase phase)
        {
            return (phase == ESkillPhase.Windup) || (phase == ESkillPhase.Active);
        }

        private static int[][] BuildSkillStateHashes()
        {
            var hashes = new int[PlayerCombatAnimationProfileSO.MAX_SKILL_ACTIONS][];
            for (int i = 0; i < hashes.Length; ++i)
            {
                hashes[i] = new int[PlayerCombatAnimationProfileSO.MAX_SEGMENTS];
                for (int s = 0; s < hashes[i].Length; ++s)
                {
                    hashes[i][s] = Animator.StringToHash((s == 0) ? ("Skill" + i) : ("Skill" + i + "_" + s));
                }
            }

            return hashes;
        }

        // M2 검증에서 두 클라이언트 로그를 맞대 보려고 개발 빌드에서만 남긴다
        private void LogAction(string label, int index)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[CombatAnim] {_combat.netId} {label} {index}");
#endif
        }
    }
}
