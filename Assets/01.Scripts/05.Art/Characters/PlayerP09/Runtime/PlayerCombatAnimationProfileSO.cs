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
        // 행동 인덱스는 AC_PlayerP09의 ActionIndex다. 기본 공격 0 ~ 2, 기술 0 ~ 6(Sp_Skill1 ~ 3, Attack3, QuickShift_F, Attack1, Attack2),
        // 피격 0 ~ 1. 도술사, 신령사는 시전 모션이 없어 검 모션을 임시로 배정했다. 짧은 기술은 긴 클립을 3배속으로도 못 맞춰
        // 생성 도구가 지연 기준(0.5초)을 넘는 배정을 짧은 클립으로 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        public const int ATTACK_COUNT = 3;
        public const int SKILL_COUNT  = 7;
        public const int HIT_COUNT    = 2;

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

        [SerializeField] private SkillAnimation[] _skills = Array.Empty<SkillAnimation>();

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
#endif

        private static float GetLength(float[] lengths, int index)
        {
            return ((lengths != null) && (index >= 0) && (index < lengths.Length) && (lengths[index] > 0f)) ? lengths[index] : 1f;
        }
    }
}
