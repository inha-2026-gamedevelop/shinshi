// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 직업과 기술 슬롯을 애니메이터 행동 상태에 잇는 표현 데이터. 클립 길이는 컨트롤러 생성 도구가 채운다 </summary>
    [CreateAssetMenu(fileName = "PlayerCombatAnimationProfile", menuName = "Shinshi/PlayerCombatAnimationProfileSO")]
    public sealed class PlayerCombatAnimationProfileSO : ScriptableObject
    {
        // 행동 인덱스는 컨트롤러의 ActionIndex다. 기존 AC_PlayerP09는 기본 공격 0 ~ 2, 기술 0 ~ 6(Sp_Skill1 ~ 3, Attack3, QuickShift_F,
        // Attack1, Attack2), 피격 0 ~ 1이다. 도술사, 신령사는 시전 모션이 없어 검 모션을 임시로 배정했다. 짧은 기술은 긴 클립을
        // 3배속으로도 못 맞춰 생성 도구가 지연 기준(0.5초)을 넘는 배정을 짧은 클립으로 바꾼다.
        // 검사, 마검사, 궁수는 별도 생성물(AC_PlayerP09_Classes)을 쓴다. 이 프로필에서 배정 항목이 없는 기술과 기본 공격은
        // 시간 예산을 넘는 후보의 정식 배정 보류이며, 다른 클립으로 대체하거나 속도를 억지로 맞추지 않고 아무 모션도 재생하지 않는다.
        // 분절 기술은 같은 속도로 이어 재생하는 상태 사슬이고, 분절 길이로 서버 시각에 맞춘 복원 위치를 정한다

        /****************************************
        *                Fields
        ****************************************/

        public const int ATTACK_COUNT = 3;
        public const int SKILL_COUNT  = 7;
        public const int HIT_COUNT    = 2;

        public const int MAX_ATTACK_ACTIONS = 8;
        public const int MAX_SKILL_ACTIONS  = 16;
        public const int MAX_SEGMENTS       = 6;

        [Serializable]
        public sealed class SkillAnimation
        {
            [SerializeField] private EPlayerClass      _playerClass = EPlayerClass.Warrior;
            [SerializeField] private ESkillRequestKind _kind        = ESkillRequestKind.Regular;
            [SerializeField] private int               _slot        = 0;
            [SerializeField] private int               _actionIndex = 0;

            public EPlayerClass      PlayerClass => _playerClass;
            public ESkillRequestKind Kind        => _kind;
            public int               Slot        => _slot;
            public int               ActionIndex => _actionIndex;

            public SkillAnimation(EPlayerClass playerClass, ESkillRequestKind kind, int slot, int actionIndex)
            {
                _playerClass = playerClass;
                _kind        = kind;
                _slot        = slot;
                _actionIndex = actionIndex;
            }
        }

        /// <summary> 직업의 기본 공격 콤보 단계를 행동 인덱스에 잇는다 </summary>
        [Serializable]
        public sealed class AttackAnimation
        {
            [SerializeField] private EPlayerClass _playerClass = EPlayerClass.Warrior;
            [SerializeField] private int          _step        = 0;
            [SerializeField] private int          _actionIndex = 0;

            public EPlayerClass PlayerClass => _playerClass;
            public int          Step        => _step;
            public int          ActionIndex => _actionIndex;

            public AttackAnimation(EPlayerClass playerClass, int step, int actionIndex)
            {
                _playerClass = playerClass;
                _step        = step;
                _actionIndex = actionIndex;
            }
        }

        /// <summary> 서버 기본 공격 명세가 없는 직업이 쓸 시각 목표 간격과 콤보 초기화 시간 </summary>
        [Serializable]
        public sealed class ClassAttackInterval
        {
            [SerializeField] private EPlayerClass _playerClass        = EPlayerClass.Warrior;
            [SerializeField] private float        _seconds            = 1f;
            [SerializeField] private float        _comboResetSeconds  = 0f;

            public EPlayerClass PlayerClass       => _playerClass;
            public float        Seconds           => _seconds;
            public float        ComboResetSeconds => _comboResetSeconds;

            public ClassAttackInterval(EPlayerClass playerClass, float seconds, float comboResetSeconds)
            {
                _playerClass       = playerClass;
                _seconds           = seconds;
                _comboResetSeconds = comboResetSeconds;
            }
        }

        /// <summary> 이어 붙인 행동 상태 사슬의 분절별 클립 길이(초) </summary>
        [Serializable]
        public sealed class SegmentSet
        {
            [SerializeField] private int     _actionIndex = 0;
            [SerializeField] private float[] _lengths     = Array.Empty<float>();

            public int     ActionIndex => _actionIndex;
            public float[] Lengths     => _lengths;

            public SegmentSet(int actionIndex, float[] lengths)
            {
                _actionIndex = actionIndex;
                _lengths     = lengths;
            }
        }

        [SerializeField] private SkillAnimation[] _skills = Array.Empty<SkillAnimation>();

        [Header("신규 직업 기본 공격과 분절")]
        [SerializeField] private AttackAnimation[]     _attacks         = Array.Empty<AttackAnimation>();
        [SerializeField] private ClassAttackInterval[] _attackIntervals = Array.Empty<ClassAttackInterval>();
        [SerializeField] private SegmentSet[]          _attackSegments  = Array.Empty<SegmentSet>();
        [SerializeField] private SegmentSet[]          _skillSegments   = Array.Empty<SegmentSet>();

        [Header("클립 길이(초), 생성 도구가 채운다")]
        [SerializeField] private float[] _attackClipLengths = new float[ATTACK_COUNT];
        [SerializeField] private float[] _skillClipLengths  = new float[SKILL_COUNT];
        [SerializeField] private float[] _hitClipLengths    = new float[HIT_COUNT];
        [SerializeField] private float   _dodgeClipLength   = 1f;

        [Header("목표 시간과 속도")]
        [SerializeField] private float _hitSeconds        = 0.45f;
        [SerializeField] private float _minDodgeSeconds   = 0.35f;
        [SerializeField] private float _minSpeed          = 0.5f;
        [SerializeField] private float _maxSpeed          = 3f;
        [SerializeField] private float _comboResetSeconds = 1.5f;
        [SerializeField] private float _hitRepeatSeconds  = 0.3f;

        /****************************************
        *              Properties
        ****************************************/

        public float HitSeconds        => _hitSeconds;
        public float MinDodgeSeconds   => _minDodgeSeconds;
        public float MinSpeed          => _minSpeed;
        public float MaxSpeed          => _maxSpeed;
        public float ComboResetSeconds => _comboResetSeconds;
        public float HitRepeatSeconds  => _hitRepeatSeconds;
        public float DodgeClipLength   => _dodgeClipLength;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구 직업(무사, 도술사, 신령사)은 배정 항목이 없으면 기존 순환 콤보를 쓴다. 신규 직업은 항목이 없으면 재생하지 않는다 </summary>
        public static bool IsLegacyClass(EPlayerClass playerClass)
        {
            return (int)playerClass <= (int)EPlayerClass.Summoner;
        }

        public bool TryGetSkillIndex(EPlayerClass playerClass, ESkillRequestKind kind, int slot, out int actionIndex)
        {
            if (_skills != null)
            {
                for (int i = 0; i < _skills.Length; ++i)
                {
                    SkillAnimation entry = _skills[i];
                    if ((entry != null) && (entry.PlayerClass == playerClass) && (entry.Kind == kind) && (entry.Slot == slot))
                    {
                        actionIndex = entry.ActionIndex;
                        return true;
                    }
                }
            }

            actionIndex = 0;
            return false;
        }

        /// <summary> 직업의 기본 공격 콤보 단계 수, 0이면 배정된 단계가 없다 </summary>
        public int GetAttackStepCount(EPlayerClass playerClass)
        {
            int count = 0;
            if (_attacks != null)
            {
                for (int i = 0; i < _attacks.Length; ++i)
                {
                    if ((_attacks[i] != null) && (_attacks[i].PlayerClass == playerClass))
                    {
                        ++count;
                    }
                }
            }

            return count;
        }

        public bool TryGetAttackIndex(EPlayerClass playerClass, int step, out int actionIndex)
        {
            if (_attacks != null)
            {
                for (int i = 0; i < _attacks.Length; ++i)
                {
                    AttackAnimation entry = _attacks[i];
                    if ((entry != null) && (entry.PlayerClass == playerClass) && (entry.Step == step))
                    {
                        actionIndex = entry.ActionIndex;
                        return true;
                    }
                }
            }

            actionIndex = 0;
            return false;
        }

        public bool TryGetFallbackAttackInterval(EPlayerClass playerClass, out float seconds)
        {
            if (_attackIntervals != null)
            {
                for (int i = 0; i < _attackIntervals.Length; ++i)
                {
                    ClassAttackInterval entry = _attackIntervals[i];
                    if ((entry != null) && (entry.PlayerClass == playerClass) && (entry.Seconds > 0f))
                    {
                        seconds = entry.Seconds;
                        return true;
                    }
                }
            }

            seconds = 0f;
            return false;
        }

        /// <summary> 직업별 콤보 초기화 시간, 지정이 없으면 공통 값 </summary>
        public float GetComboResetSeconds(EPlayerClass playerClass)
        {
            if (_attackIntervals != null)
            {
                for (int i = 0; i < _attackIntervals.Length; ++i)
                {
                    ClassAttackInterval entry = _attackIntervals[i];
                    if ((entry != null) && (entry.PlayerClass == playerClass) && (entry.ComboResetSeconds > 0f))
                    {
                        return entry.ComboResetSeconds;
                    }
                }
            }

            return _comboResetSeconds;
        }

        public bool TryGetAttackSegments(int actionIndex, out float[] lengths) { return TryGetSegments(_attackSegments, actionIndex, out lengths); }
        public bool TryGetSkillSegments(int actionIndex, out float[] lengths)  { return TryGetSegments(_skillSegments, actionIndex, out lengths); }

        public float GetAttackClipLength(int index) { return GetLength(_attackClipLengths, index); }
        public float GetSkillClipLength(int index)  { return GetLength(_skillClipLengths, index); }
        public float GetHitClipLength(int index)    { return GetLength(_hitClipLengths, index); }

#if UNITY_EDITOR
        /// <summary> 컨트롤러 생성 도구 전용, 배정과 클립 길이를 채운다 </summary>
        public void EditorSetup(SkillAnimation[] skills, float[] attackLengths, float[] skillLengths, float[] hitLengths, float dodgeLength)
        {
            _skills            = skills;
            _attackClipLengths = attackLengths;
            _skillClipLengths  = skillLengths;
            _hitClipLengths    = hitLengths;
            _dodgeClipLength   = dodgeLength;
        }

        public SkillAnimation[] EditorSkills        => _skills;
        public float[]          EditorAttackLengths => _attackClipLengths;
        public float[]          EditorSkillLengths  => _skillClipLengths;
        public float[]          EditorHitLengths    => _hitClipLengths;

        /// <summary> 컨트롤러 생성 도구 전용, 신규 직업의 기본 공격 배정, 시각 목표 간격, 분절 길이를 채운다 </summary>
        public void EditorSetupClasses(AttackAnimation[] attacks, ClassAttackInterval[] intervals, SegmentSet[] attackSegments,
            SegmentSet[] skillSegments)
        {
            _attacks         = attacks;
            _attackIntervals = intervals;
            _attackSegments  = attackSegments;
            _skillSegments   = skillSegments;
        }
#endif

        private static bool TryGetSegments(SegmentSet[] sets, int actionIndex, out float[] lengths)
        {
            if (sets != null)
            {
                for (int i = 0; i < sets.Length; ++i)
                {
                    SegmentSet entry = sets[i];
                    if ((entry != null) && (entry.ActionIndex == actionIndex) && (entry.Lengths != null) && (entry.Lengths.Length > 1))
                    {
                        lengths = entry.Lengths;
                        return true;
                    }
                }
            }

            lengths = null;
            return false;
        }

        private static float GetLength(float[] lengths, int index)
        {
            return ((lengths != null) && (index >= 0) && (index < lengths.Length) && (lengths[index] > 0f)) ? lengths[index] : 1f;
        }
    }
}
