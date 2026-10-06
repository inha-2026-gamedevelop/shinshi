// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 참가자의 출정 고정 부적을 기술 4칸마다 미리 합성해 들고 있다가 시전 때 할당 없이 돌려준다 </summary>
    public sealed class TalismanModifierResolver : ITalismanModifierResolver
    {
        // 합성(TalismanModifierRules.TryCompile)은 참가자 등록 때 한 번만 한다. Resolve는 시전마다 불리므로 배열 조회만 한다.
        // 칸은 참가자 번호로 찾는다. 같은 참가자가 재접속하면 같은 칸을 덮어쓴다. 등록되지 않은 참가자와 다른 기술 정의는
        // 원래 수치(Identity)다. 마을처럼 출정 준비가 없는 연결이 그 경우다

        /****************************************
        *                Fields
        ****************************************/

        private const int NO_PARTICIPANT = 0;

        private readonly TalismanCatalogSO                _talismans;
        private readonly SkillCatalogSO                   _skills;
        private readonly int[]                            _participantIds;
        private readonly SkillCatalogSO.SkillDefinition[] _slotSkills;
        private readonly SkillModifiers[]                 _slotModifiers;

        /****************************************
        *              Properties
        ****************************************/

        public int Capacity => _participantIds.Length;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanModifierResolver(TalismanCatalogSO talismans, SkillCatalogSO skills, int capacity)
        {
            if ((talismans == null) || (skills == null))
            {
                throw new ArgumentNullException(nameof(talismans), "부적과 기술 카탈로그가 필요하다");
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "참가자 상한은 1 이상이다");
            }

            _talismans      = talismans;
            _skills         = skills;
            _participantIds = new int[capacity];
            _slotSkills     = new SkillCatalogSO.SkillDefinition[capacity * Constants.Player.SKILL_SLOT_COUNT];
            _slotModifiers  = new SkillModifiers[capacity * Constants.Player.SKILL_SLOT_COUNT];
        }

        /// <summary> 참가자의 네 기술을 합성해 둔다. 직업 기술이 아닌 부적, 같은 단계 중복, 모르는 효과면 아무것도 바꾸지 않고 거짓 </summary>
        public bool TryRegister(int participantId, EPlayerClass playerClass, IReadOnlyList<CombatTalismanEntry> talismans,
            out string error)
        {
            if (participantId == NO_PARTICIPANT)
            {
                error = "참가자 번호가 없다";
                return false;
            }

            int row = FindRow(participantId);
            if (row < 0)
            {
                row = FindRow(NO_PARTICIPANT);
            }

            if (row < 0)
            {
                error = "부적 합성 칸이 가득 찼다";
                return false;
            }

            if (!TryCountOwnSkills(playerClass, talismans, out error))
            {
                return false;
            }

            // 네 칸을 모두 합성한 뒤에만 쓴다. 중간에 실패하면 이전 등록이 반쯤 바뀐 채 남는다
            SkillModifiers q = default, w = default, e = default, r = default;
            if ((!TryCompileSlot(playerClass, Constants.Combat.SKILL_SLOT_Q, talismans, ref q, out error))
                || (!TryCompileSlot(playerClass, Constants.Combat.SKILL_SLOT_W, talismans, ref w, out error))
                || (!TryCompileSlot(playerClass, Constants.Combat.SKILL_SLOT_E, talismans, ref e, out error))
                || (!TryCompileSlot(playerClass, Constants.Combat.SKILL_SLOT_R, talismans, ref r, out error)))
            {
                return false;
            }

            int start = row * Constants.Player.SKILL_SLOT_COUNT;
            _participantIds[row] = participantId;
            Store(start, playerClass, Constants.Combat.SKILL_SLOT_Q, in q);
            Store(start, playerClass, Constants.Combat.SKILL_SLOT_W, in w);
            Store(start, playerClass, Constants.Combat.SKILL_SLOT_E, in e);
            Store(start, playerClass, Constants.Combat.SKILL_SLOT_R, in r);
            error = null;
            return true;
        }

        /// <summary> 등록을 지운다, 이후 Resolve는 원래 수치다 </summary>
        public void Unregister(int participantId)
        {
            int row = FindRow(participantId);
            if (row < 0)
            {
                return;
            }

            _participantIds[row] = NO_PARTICIPANT;
            int start = row * Constants.Player.SKILL_SLOT_COUNT;
            for (int i = 0; i < Constants.Player.SKILL_SLOT_COUNT; ++i)
            {
                _slotSkills[start + i]    = null;
                _slotModifiers[start + i] = SkillModifiers.Identity;
            }
        }

        // 시전마다 부른다. 할당 없이 배열만 본다
        public SkillModifiers Resolve(int participantId, SkillCatalogSO.SkillDefinition skill)
        {
            if (skill == null)
            {
                return SkillModifiers.Identity;
            }

            int row = FindRow(participantId);
            int slot = skill.Slot;
            if ((row < 0) || (slot < 0) || (slot >= Constants.Player.SKILL_SLOT_COUNT))
            {
                return SkillModifiers.Identity;
            }

            int index = (row * Constants.Player.SKILL_SLOT_COUNT) + slot;
            return ReferenceEquals(_slotSkills[index], skill) ? _slotModifiers[index] : SkillModifiers.Identity;
        }

        private bool TryCompileSlot(EPlayerClass playerClass, int slot, IReadOnlyList<CombatTalismanEntry> talismans,
            ref SkillModifiers modifiers, out string error)
        {
            if (!_skills.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
            {
                error = $"직업 기술 칸이 비어 있다, class={playerClass}, slot={slot}";
                return false;
            }

            return TalismanModifierRules.TryCompile(_talismans, skill, talismans, out modifiers, out error);
        }

        // 다른 직업 기술이나 모르는 기술에 걸린 부적은 어느 칸에도 걸리지 않아 조용히 사라진다. 등록 때 거절해 드러낸다
        private bool TryCountOwnSkills(EPlayerClass playerClass, IReadOnlyList<CombatTalismanEntry> talismans, out string error)
        {
            int count = (talismans == null) ? 0 : talismans.Count;
            if (count > Constants.Combat.MAX_TALISMANS_PER_PARTICIPANT)
            {
                error = $"부적 칸 수가 상한을 넘는다, count={count}";
                return false;
            }

            for (int i = 0; i < count; ++i)
            {
                CombatTalismanEntry entry = talismans[i];
                if (entry == null)
                {
                    error = "비어 있는 부적 칸이 있다";
                    return false;
                }

                if ((!_skills.TryGetSkill(entry.SkillId, out SkillCatalogSO.SkillDefinition skill))
                    || (skill.PlayerClass != playerClass))
                {
                    error = $"이 직업의 기술이 아닌 부적이다, class={playerClass}, skill={entry.SkillId}, effectId={entry.EffectId}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private void Store(int start, EPlayerClass playerClass, int slot, in SkillModifiers modifiers)
        {
            _skills.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill);
            _slotSkills[start + slot]    = skill;
            _slotModifiers[start + slot] = modifiers;
        }

        private int FindRow(int participantId)
        {
            for (int i = 0; i < _participantIds.Length; ++i)
            {
                if (_participantIds[i] == participantId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
