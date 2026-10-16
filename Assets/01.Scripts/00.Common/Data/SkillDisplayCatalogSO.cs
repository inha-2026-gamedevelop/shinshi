// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    // 기술과 부적 효과의 판정 값은 SkillCatalogSO와 TalismanCatalogSO가 가진다. 여기는 화면에 쓰는 한국어 이름과 설명만 둔다.
    // 지역화 패키지를 쓰는 코드가 아직 없어 한국어를 그대로 담는다. GameDB 콘텐츠 해시에 들어가므로 바꾸면 서버와 클라이언트를 함께 빌드한다
    /// <summary> 스킬창이 보여 줄 기술, 부적 효과, 직업 기본 공격의 이름과 설명 </summary>
    [CreateAssetMenu(fileName = "SkillDisplayCatalog", menuName = "Shinshi/SkillDisplayCatalogSO")]
    public class SkillDisplayCatalogSO : GameDataSO
    {
        /// <summary> 기술 또는 부적 효과 하나의 표시 글자 </summary>
        [Serializable]
        public class DisplayEntry : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _definitionId;       // SkillCatalogSO 기술 ID 또는 TalismanCatalogSO 효과 ID
            [SerializeField] private string _displayName;
            [SerializeField] private string _description;        // 한두 줄 설명
            [SerializeField] private string _upgradeDescription; // 부적 효과의 강화 항목, 기술은 비운다
            [SerializeField] private bool   _noEffect;           // 기술 개편으로 서버가 무시하는 부적 효과, 장착은 막지 않고 경고만 한다

            public string DefinitionId       => _definitionId;
            public string DisplayKey         => _definitionId;
            public string DisplayName        => _displayName;
            public string Description        => _description;
            public string UpgradeDescription => _upgradeDescription;
            public bool   NoEffect           => _noEffect;

            /****************************************
            *                Methods
            ****************************************/

            public DisplayEntry(string definitionId, string displayName, string description, string upgradeDescription = "",
                bool noEffect = false)
            {
                _definitionId       = definitionId;
                _displayName        = displayName;
                _description        = description;
                _upgradeDescription = upgradeDescription;
                _noEffect           = noEffect;
            }
        }

        /// <summary> 직업 하나의 기본 공격 설명과 스택 규칙 한 줄 </summary>
        [Serializable]
        public class ClassDisplay
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EPlayerClass _playerClass;
            [SerializeField] private string       _basicAttackDescription;
            [SerializeField] private string       _stackRule; // 스택이 없는 직업은 비운다. 상한 숫자는 SkillCatalogSO 값을 붙여 보인다

            public EPlayerClass PlayerClass            => _playerClass;
            public string       BasicAttackDescription => _basicAttackDescription;
            public string       StackRule              => _stackRule;

            /****************************************
            *                Methods
            ****************************************/

            public ClassDisplay(EPlayerClass playerClass, string basicAttackDescription, string stackRule)
            {
                _playerClass            = playerClass;
                _basicAttackDescription = basicAttackDescription;
                _stackRule              = stackRule;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string COOLDOWN_NAME     = "빠른 준비";
        private const string COOLDOWN_TEXT     = "쿨다운을 줄인다.";
        private const string COOLDOWN_UPGRADE  = "쿨다운 감소율";
        private const string ENERGY_NAME       = "기력 절약";
        private const string ENERGY_TEXT       = "기력 비용을 줄인다.";
        private const string ENERGY_UPGRADE    = "기력 감소율";
        private const string ARCHER_ENERGY     = "가벼운 시위";
        private const string ARCHER_COOLDOWN   = "빠른 재정비";

        /****************************************
        *                Fields
        ****************************************/

        [Header("기술 (검사, 마검사, 궁수 Q, W, E, R)")]
        [SerializeField] private DisplayEntry[] _skills =
        {
            new DisplayEntry("skill_swordsman_slash", "넓게 베기", "전방 넓은 부채꼴을 베어 앞의 적을 모두 벤다."),
            new DisplayEntry("skill_swordsman_dash", "돌진 베기", "커서 방향으로 돌진하며 경로의 적을 벤다. 첫 적 몸체 앞에서 멈춘다."),
            new DisplayEntry("skill_swordsman_focus_slash", "집중 베기", "힘을 모아 좁은 전방을 강하게 벤다."),
            new DisplayEntry("skill_swordsman_chain_slash", "연환참", "전방을 두 번 연달아 벤다."),
            new DisplayEntry("skill_mageknight_arcane_slash", "수류 방벽", "3초 동안 기절, 속박, 밀림을 받지 않는다. 피해는 그대로 받는다."),
            new DisplayEntry("skill_mageknight_thunder_wave", "검파 해방", "모은 스택을 모두 써서 관통하는 검파를 날린다. 스택이 많을수록 강하다."),
            new DisplayEntry("skill_mageknight_flame_field", "공간 절단진", "지정한 지점에 4초 동안 베는 진을 펼친다. 생성 때 스택 +5."),
            new DisplayEntry("skill_mageknight_seal_slash", "섬광 일섬", "직선 경로를 베고 끝으로 순간 이동한다. 도착 때 스택 +10."),
            new DisplayEntry("skill_archer_rapid_shot", "얼음 부여", "이후 기본 공격이 얼음 속성이 된다. 맞힌 일반 적을 잠시 느리게 한다."),
            new DisplayEntry("skill_archer_retreat_shot", "바람 부여", "이후 기본 공격이 바람 속성이 된다. 공격 주기가 짧아지고 화살이 빨라진다."),
            new DisplayEntry("skill_archer_scatter_shot", "불 부여", "이후 기본 공격이 불 속성이 된다. 맞힌 적에게 화상을 건다."),
            new DisplayEntry("skill_archer_piercing_shot", "속성 폭발", "스택을 모두 써서 지정한 지점에 큰 원형 폭발을 일으킨다.")
        };

        [Header("부적 효과 (검사, 마검사, 궁수 45종)")]
        [SerializeField] private DisplayEntry[] _effects =
        {
            new DisplayEntry("T13-1A", COOLDOWN_NAME, COOLDOWN_TEXT, COOLDOWN_UPGRADE),
            new DisplayEntry("T13-1B", ENERGY_NAME, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T13-2A", "넓은 검세", "부채꼴 각도를 넓힌다. 원형 교체에는 반경을 늘린다.", "각도, 반경"),
            new DisplayEntry("T13-2B", "집중 검세", "부채꼴을 80도로 좁히고 피해를 높인다. 원형은 반경을 0.5m 줄인다.", "피해 증가율"),
            new DisplayEntry("T13-3A", "연속 참격", "첫 타격 0.15초 뒤에 두 번째 타격을 더한다.", "후속 타격 피해"),
            new DisplayEntry("T13-3B", "회전 참격", "전방 1타를 제자리 원형 1타로 바꾼다.", "추가 피해"),
            new DisplayEntry("T14-1A", "가벼운 발걸음", ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T14-2A", "긴 보폭", "같은 0.3초 동안 더 멀리 진입한다.", "이동 거리"),
            new DisplayEntry("T14-3A", "관통 돌진", "적 몸체를 지나며 대상마다 한 번 경로 피해를 준다. 벽과 지형 앞에서는 멈춘다.", "추가 경로 피해"),
            new DisplayEntry("T15-1A", COOLDOWN_NAME, COOLDOWN_TEXT, COOLDOWN_UPGRADE),
            new DisplayEntry("T15-2A", "강한 검격", "본타격과 잔향 검풍의 피해를 높인다.", "피해 증가율"),
            new DisplayEntry("T15-3A", "잔향 검풍", "본타격 0.2초 뒤에 짧은 검풍을 더한다. 첫 적이나 벽에서 사라진다.", "검풍 피해"),
            new DisplayEntry("T16-1A", ENERGY_NAME, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T16-2A", "넓은 연격", "두 타격의 부채꼴을 함께 넓힌다.", "각도"),
            new DisplayEntry("T16-3A", "검풍 마무리", "두 번째 타격 0.2초 뒤에 검풍을 더한다. 첫 적이나 벽에서 사라진다.", "검풍 피해"),

            new DisplayEntry("T17-1A", ENERGY_NAME, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T17-2A", "긴 칼날", "근접 길이를 늘린다. 삼갈래 교체에는 투사체 사거리를 늘린다.", "길이, 사거리", true),
            new DisplayEntry("T17-3A", "삼갈래 검풍", "근접 타격을 세 방향 검풍으로 바꾼다.", "갈래별 추가 피해", true),
            new DisplayEntry("T18-1A", COOLDOWN_NAME, COOLDOWN_TEXT, COOLDOWN_UPGRADE),
            new DisplayEntry("T18-1B", ENERGY_NAME, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T18-2A", "넓은 검진", "진과 두 폭발 형태의 반경을 늘린다.", "반경"),
            new DisplayEntry("T18-2B", "응축 화력", "반경을 2m로 좁히고 지속 피해와 폭발 피해를 높인다.", "피해 증가율"),
            new DisplayEntry("T18-3A", "화염 분출", "8회 지속 피해를 생성 0.8초 뒤 한 번의 폭발로 바꾼다.", "추가 폭발 피해"),
            new DisplayEntry("T18-3B", "잔불 폭발", "8회 지속 피해를 유지하고 마지막 회차에 폭발 한 번을 더한다.", "마지막 폭발 피해"),
            new DisplayEntry("T19-1A", ENERGY_NAME, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T19-2A", "넓은 봉인", "전방 봉인 검격의 각도를 넓힌다.", "각도", true),
            new DisplayEntry("T19-3A", "검인 응집", "맞힌 일반 적을 전방 쪽으로 당기고 속박을 늘린다.", "추가 속박 시간", true),
            new DisplayEntry("T20-1A", COOLDOWN_NAME, COOLDOWN_TEXT, COOLDOWN_UPGRADE),
            new DisplayEntry("T20-2A", "넓은 검파", "검파의 폭을 늘린다. 연속 검파에도 모두 적용한다.", "투사체 폭"),
            new DisplayEntry("T20-3A", "연속 검파", "첫 검파 0.35초 뒤에 같은 방향으로 두 번째 검파를 더한다.", "두 번째 검파 피해"),

            new DisplayEntry("T21-1A", ARCHER_ENERGY, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T21-2A", "긴 사선", "모든 발사 사거리를 늘린다.", "사거리", true),
            new DisplayEntry("T21-3A", "끝맺음 화살", "마지막 발사 0.25초 뒤에 한 발을 더 쏜다.", "추가 화살 피해", true),
            new DisplayEntry("T22-1A", ARCHER_COOLDOWN, COOLDOWN_TEXT, COOLDOWN_UPGRADE),
            new DisplayEntry("T22-2A", "긴 뒷걸음", "후퇴 이동 거리를 늘린다.", "이동 거리", true),
            new DisplayEntry("T22-3A", "갈림 화살", "단발을 세 갈래 화살로 바꾼다.", "갈래별 추가 피해", true),
            new DisplayEntry("T23-1A", ARCHER_COOLDOWN, COOLDOWN_TEXT, COOLDOWN_UPGRADE),
            new DisplayEntry("T23-1B", ARCHER_ENERGY, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T23-2A", "넓은 화살길", "관통 화살의 폭을 늘린다.", "폭", true),
            new DisplayEntry("T23-2B", "집중 관통", "피해를 높인다. 폭을 고정하는 부분은 지금 기술에 적용되지 않는다.", "피해 증가율"),
            new DisplayEntry("T23-3A", "꿰뚫는 시위", "최대 대상을 6으로 바꾸고 피해를 더한다.", "추가 피해", true),
            new DisplayEntry("T23-3B", "쌍연 시위", "0.3초 뒤에 두 번째 관통 화살을 더 쏜다.", "두 번째 화살 피해", true),
            new DisplayEntry("T24-1A", ARCHER_ENERGY, ENERGY_TEXT, ENERGY_UPGRADE),
            new DisplayEntry("T24-2A", "넓은 부채", "부채꼴 양 끝 각도를 넓힌다.", "각도", true),
            new DisplayEntry("T24-3A", "관통 산개", "각 화살이 최대 2명까지 관통한다.", "추가 피해", true)
        };

        [Header("직업 (기본 공격, 스택 규칙)")]
        [SerializeField] private ClassDisplay[] _classes =
        {
            new ClassDisplay(EPlayerClass.Swordsman, "전방 부채꼴을 베는 무료 근접 공격이다.", ""),
            new ClassDisplay(EPlayerClass.Mageknight, "전방을 베는 무료 근접 3단 공격이다. 단계마다 피해는 같다.",
                "적을 맞힌 기본 공격 +1, E 생성 +5, R 도착 +10. W가 모두 써서 피해로 바꾼다. 사망하면 0"),
            new ClassDisplay(EPlayerClass.Archer, "화살을 쏘는 무료 원거리 3단 공격이다. 3단은 피격이상을 받지 않는다.",
                "속성 화살이 적을 맞히면 직전과 같은 속성 +1, 다른 속성 +2. R이 모두 쓴다. 사망하면 0")
        };

        [NonSerialized] private DefinitionLookup<DisplayEntry> _skillLookup  = new DefinitionLookup<DisplayEntry>();
        [NonSerialized] private DefinitionLookup<DisplayEntry> _effectLookup = new DefinitionLookup<DisplayEntry>();

        public IReadOnlyList<DisplayEntry> Skills  => _skills;
        public IReadOnlyList<DisplayEntry> Effects => _effects;
        public IReadOnlyList<ClassDisplay> Classes => _classes;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            InvalidateLookups();
        }

        protected override void OnValidate()
        {
            InvalidateLookups();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool TryGetSkill(string skillId, out DisplayEntry entry)
        {
            return _skillLookup.TryGet(_skills, skillId, out entry);
        }

        public bool TryGetEffect(string effectId, out DisplayEntry entry)
        {
            return _effectLookup.TryGet(_effects, effectId, out entry);
        }

        public bool TryGetClass(EPlayerClass playerClass, out ClassDisplay display)
        {
            int count = (_classes == null) ? 0 : _classes.Length;
            for (int i = 0; i < count; ++i)
            {
                if ((_classes[i] != null) && (_classes[i].PlayerClass == playerClass))
                {
                    display = _classes[i];
                    return true;
                }
            }

            display = null;
            return false;
        }

        /// <summary> 정의 ID의 표시 이름, 없으면 ID를 그대로 돌려준다 </summary>
        public string DisplayNameOf(string definitionId)
        {
            if ((TryGetEffect(definitionId, out DisplayEntry entry)) || (TryGetSkill(definitionId, out entry)))
            {
                if (!string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    return entry.DisplayName;
                }
            }

            return definitionId ?? string.Empty;
        }

        private void InvalidateLookups()
        {
            _skillLookup.Invalidate();
            _effectLookup.Invalidate();
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(SkillDisplayCatalogSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_skills), _skills);
            DataValidation.RequireDefinitions(errors, OWNER, nameof(_effects), _effects);
            ValidateNames(errors, OWNER, nameof(_skills), _skills);
            ValidateNames(errors, OWNER, nameof(_effects), _effects);

            int count = (_classes == null) ? 0 : _classes.Length;
            var seen  = new HashSet<EPlayerClass>();
            for (int i = 0; i < count; ++i)
            {
                ClassDisplay display = _classes[i];
                if (display == null)
                {
                    errors.Add($"{OWNER}.{nameof(_classes)}[{i}]: 항목이 비어 있다");
                    continue;
                }

                if (!seen.Add(display.PlayerClass))
                {
                    errors.Add($"{OWNER}.{nameof(_classes)}[{i}]: {display.PlayerClass} 직업이 두 번 있다");
                }
            }
        }

        // 기술 표에는 기술 ID만, 효과 표에는 효과 ID만 있어야 한다. 효과 없음 표시는 효과에만 쓴다
        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            const string OWNER = nameof(SkillDisplayCatalogSO);

            for (int i = 0; i < _skills.Length; ++i)
            {
                DisplayEntry entry = _skills[i];
                DataValidation.RequireReference(errors, OWNER, $"{nameof(_skills)}[{i}]", entry.DefinitionId,
                    root.Skills.ContainsSkill(entry.DefinitionId));
                if (entry.NoEffect)
                {
                    errors.Add($"{OWNER}.{nameof(_skills)}[{entry.DefinitionId}]: 기술에는 효과 없음 표시를 쓰지 않는다");
                }
            }

            for (int i = 0; i < _effects.Length; ++i)
            {
                DisplayEntry entry = _effects[i];
                DataValidation.RequireReference(errors, OWNER, $"{nameof(_effects)}[{i}]", entry.DefinitionId,
                    root.Talismans.ContainsEffect(entry.DefinitionId));
            }
        }

        private static void ValidateNames(List<string> errors, string owner, string field, DisplayEntry[] entries)
        {
            int count = (entries == null) ? 0 : entries.Length;
            for (int i = 0; i < count; ++i)
            {
                if ((entries[i] != null) && (string.IsNullOrWhiteSpace(entries[i].DisplayName)))
                {
                    errors.Add($"{owner}.{field}[{entries[i].DefinitionId}]: 표시 이름이 없다");
                }
            }
        }
    }
}
