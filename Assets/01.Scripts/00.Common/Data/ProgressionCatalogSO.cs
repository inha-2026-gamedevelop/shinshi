// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 지역, 보스 해금, 여우의 흔적, 최초 지원, 체험판 플래그 </summary>
    [CreateAssetMenu(fileName = "ProgressionCatalog", menuName = "Shinshi/ProgressionCatalogSO")]
    public class ProgressionCatalogSO : GameDataSO
    {
        /// <summary> 해금 조건, 빈 칸은 검사하지 않고 채운 조건은 모두 만족해야 열린다 </summary>
        [Serializable]
        public class UnlockRequirement
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private bool   _requiresIntro;    // 신시 안내 완료
            [SerializeField] private string _clearedRegionId;  // 이 지역의 일반전 성공 기록
            [SerializeField] private string _unlockedRegionId; // 이 지역이 열려 있어야 한다
            [SerializeField] private string _clearedBattleId;  // 이 보스전의 첫 처치 기록
            [SerializeField] private int    _foxTraceCount;    // 계정이 모은 여우의 흔적 수, 소모하지 않는다

            public bool   RequiresIntro    => _requiresIntro;
            public string ClearedRegionId  => _clearedRegionId;
            public string UnlockedRegionId => _unlockedRegionId;
            public string ClearedBattleId  => _clearedBattleId;
            public int    FoxTraceCount    => _foxTraceCount;

            /****************************************
            *                Methods
            ****************************************/

            public UnlockRequirement(bool requiresIntro = false, string clearedRegionId = "", string unlockedRegionId = "",
                string clearedBattleId = "", int foxTraceCount = 0)
            {
                _requiresIntro    = requiresIntro;
                _clearedRegionId  = clearedRegionId;
                _unlockedRegionId = unlockedRegionId;
                _clearedBattleId  = clearedBattleId;
                _foxTraceCount    = foxTraceCount;
            }
        }

        /// <summary> 출정 지역, 한 번 열면 계정에 영구로 남고 자유롭게 반복한다 </summary>
        [Serializable]
        public class RegionDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string            _definitionId;
            [SerializeField] private string            _displayKey;
            [SerializeField] private bool              _hasWaveBattle;     // 6웨이브 일반전을 여는 지역인지
            [SerializeField] private int               _foxTracesPerClear; // 일반전 성공마다 자격자 계정에 주는 흔적 수
            [SerializeField] private bool              _availableInTrial;  // 체험판에 들어 있는지
            [SerializeField] private bool              _trialPreUnlocked;  // 체험판에서 조건 없이 열려 있는지
            [SerializeField] private UnlockRequirement _requirement;

            // 일반전 적 능력치의 기준이다. 실제 참가자의 장비, 수련이 아니라 지역마다 고정한 권장 캐릭터 값이라
            // 성장한 캐릭터가 들어와도 적이 함께 강해지지 않는다. 일반전이 없는 지역은 0
            [SerializeField] private float _referenceAttack;      // Aref, 권장 공격력, 소수 유지
            [SerializeField] private float _referenceHealth;      // Href, 권장 최대 체력, 버림 정수

            // 체험판은 지급 희귀 +17 기준이라 같은 지역도 정식과 값이 다르다. 체험판에 없는 지역은 0
            [SerializeField] private float _trialReferenceAttack; // Aref, 체험판
            [SerializeField] private float _trialReferenceHealth; // Href, 체험판

            public string            DefinitionId         => _definitionId;
            public string            DisplayKey           => _displayKey;
            public bool              HasWaveBattle        => _hasWaveBattle;
            public int               FoxTracesPerClear    => _foxTracesPerClear;
            public bool              AvailableInTrial     => _availableInTrial;
            public bool              TrialPreUnlocked     => _trialPreUnlocked;
            public UnlockRequirement Requirement          => _requirement;
            public float             ReferenceAttack      => _referenceAttack;
            public float             ReferenceHealth      => _referenceHealth;
            public float             TrialReferenceAttack => _trialReferenceAttack;
            public float             TrialReferenceHealth => _trialReferenceHealth;

            /****************************************
            *                Methods
            ****************************************/

            public RegionDefinition(string definitionId, bool hasWaveBattle, int foxTracesPerClear, bool availableInTrial,
                bool trialPreUnlocked, UnlockRequirement requirement, float referenceAttack = 0f, float referenceHealth = 0f,
                float trialReferenceAttack = 0f, float trialReferenceHealth = 0f)
            {
                _definitionId         = definitionId;
                _displayKey           = definitionId + DISPLAY_KEY_SUFFIX;
                _hasWaveBattle        = hasWaveBattle;
                _foxTracesPerClear    = foxTracesPerClear;
                _availableInTrial     = availableInTrial;
                _trialPreUnlocked     = trialPreUnlocked;
                _requirement          = requirement;
                _referenceAttack      = referenceAttack;
                _referenceHealth      = referenceHealth;
                _trialReferenceAttack = trialReferenceAttack;
                _trialReferenceHealth = trialReferenceHealth;
            }
        }

        /// <summary> 보스 하나의 한 난이도 전투, 파티 전원이 해금 조건을 갖춰야 입장한다 </summary>
        [Serializable]
        public class BossBattleDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string            _definitionId;
            [SerializeField] private string            _displayKey;
            [SerializeField] private string            _bossId;
            [SerializeField] private EBossDifficulty   _difficulty;
            [SerializeField] private int               _ticketCost;       // 출정마다 참가자 각자가 쓰는 공용 입장권 수, 해금이 대신하지 않는다
            [SerializeField] private bool              _availableInTrial; // 체험판에 들어 있는지
            [SerializeField] private bool              _trialPreUnlocked; // 체험판에서 조건 없이 열려 있는지
            [SerializeField] private UnlockRequirement _requirement;

            public string            DefinitionId     => _definitionId;
            public string            DisplayKey       => _displayKey;
            public string            BossId           => _bossId;
            public EBossDifficulty   Difficulty       => _difficulty;
            public int               TicketCost       => _ticketCost;
            public bool              AvailableInTrial => _availableInTrial;
            public bool              TrialPreUnlocked => _trialPreUnlocked;
            public UnlockRequirement Requirement      => _requirement;

            /****************************************
            *                Methods
            ****************************************/

            public BossBattleDefinition(string definitionId, string bossId, EBossDifficulty difficulty, int ticketCost,
                bool availableInTrial, bool trialPreUnlocked, UnlockRequirement requirement)
            {
                _definitionId     = definitionId;
                _displayKey       = definitionId + DISPLAY_KEY_SUFFIX;
                _bossId           = bossId;
                _difficulty       = difficulty;
                _ticketCost       = ticketCost;
                _availableInTrial = availableInTrial;
                _trialPreUnlocked = trialPreUnlocked;
                _requirement      = requirement;
            }
        }

        /// <summary> 수량형 아이템 지급, ID는 RewardCatalogSO 또는 LifeConfigSO에 있어야 한다 </summary>
        [Serializable]
        public class ItemGrant
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _itemId;
            [SerializeField] private int    _quantity;

            public string ItemId   => _itemId;
            public int    Quantity => _quantity;

            /****************************************
            *                Methods
            ****************************************/

            public ItemGrant(string itemId, int quantity)
            {
                _itemId   = itemId;
                _quantity = quantity;
            }
        }

        /// <summary> 장비 한 개 지급, ID는 EquipmentCatalogSO에 있어야 한다 </summary>
        [Serializable]
        public class EquipmentGrant
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _equipmentId;
            [SerializeField] private int    _enhancement; // 지급 때 강화 단계

            public string EquipmentId => _equipmentId;
            public int    Enhancement => _enhancement;

            /****************************************
            *                Methods
            ****************************************/

            public EquipmentGrant(string equipmentId, int enhancement)
            {
                _equipmentId = equipmentId;
                _enhancement = enhancement;
            }
        }

        /// <summary> 한 번만 주는 초기 지원 묶음, 지급품은 모두 거래와 분해가 불가하다 </summary>
        [Serializable]
        public class GrantBundle
        {
            /****************************************
            *                Fields
            ****************************************/

            // 지급 기록의 키라서 삭제, 재생성, 재접속으로 같은 묶음을 다시 받지 못한다
            [SerializeField] private string           _grantId;
            [SerializeField] private ItemGrant[]      _items;
            [SerializeField] private EquipmentGrant[] _equipment;
            [SerializeField] private int              _actionEnergy; // 생활 행동력 지급량

            public string                        GrantId      => _grantId;
            public IReadOnlyList<ItemGrant>      Items        => _items;
            public IReadOnlyList<EquipmentGrant> Equipment    => _equipment;
            public int                           ActionEnergy => _actionEnergy;

            /****************************************
            *                Methods
            ****************************************/

            public GrantBundle(string grantId, ItemGrant[] items, EquipmentGrant[] equipment, int actionEnergy)
            {
                _grantId      = grantId;
                _items        = items;
                _equipment    = equipment;
                _actionEnergy = actionEnergy;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string DISPLAY_KEY_SUFFIX = ".name";

        // 숲, 인간 마을 폐허, 오래된 제단, 월백 결계
        private const int    REGION_COUNT       = 4;

        // 최종 보스 2종 x 이지, 하드
        private const int    BOSS_BATTLE_COUNT  = 4;

        /****************************************
        *                Fields
        ****************************************/

        // 체험판의 진행 기록은 정식 해금, 첫 처치와 분리해 저장하고 서로 대신하지 않는다
        [Header("체험판")]
        [SerializeField] private bool _trialEdition = true; // 이 콘텐츠 묶음이 체험판인지

        [Header("여우의 흔적")]
        [SerializeField] private int _foxTraceCap = 3; // 계정 보유 상한, 결계를 연 뒤에는 더 쌓지 않는다

        [Header("지역")]
        [SerializeField] private RegionDefinition[] _regions =
        {
            new RegionDefinition("region_forest", true, 0, true, false,
                new UnlockRequirement(requiresIntro: true), 220f, 1950f, 644.32f, 3541f),
            new RegionDefinition("region_ruins", true, 0, false, false,
                new UnlockRequirement(clearedRegionId: "region_forest"), 268f, 2130f),
            new RegionDefinition("region_altar", true, 1, false, false,
                new UnlockRequirement(clearedBattleId: "battle_mucheol_easy"), 553.6f, 3201f),
            new RegionDefinition("region_wolbaek_barrier", false, 0, true, true,
                new UnlockRequirement(foxTraceCount: 3))
        };

        // 무철은 체험판에 넣지 않는 후속 콘텐츠이고 체험판은 월백 이지를 미리 연 전투 시험이다
        [Header("보스전")]
        [SerializeField] private BossBattleDefinition[] _bossBattles =
        {
            new BossBattleDefinition("battle_mucheol_easy", "boss_mucheol", EBossDifficulty.Easy, 1, false, false,
                new UnlockRequirement(clearedRegionId: "region_ruins")),
            new BossBattleDefinition("battle_mucheol_hard", "boss_mucheol", EBossDifficulty.Hard, 1, false, false,
                new UnlockRequirement(clearedBattleId: "battle_mucheol_easy")),
            new BossBattleDefinition("battle_wolbaek_easy", "boss_wolbaek", EBossDifficulty.Easy, 1, true, true,
                new UnlockRequirement(unlockedRegionId: "region_wolbaek_barrier", clearedBattleId: "battle_mucheol_hard")),
            new BossBattleDefinition("battle_wolbaek_hard", "boss_wolbaek", EBossDifficulty.Hard, 1, false, false,
                new UnlockRequirement(clearedBattleId: "battle_wolbaek_easy"))
        };

        // 새 캐릭터는 수련용 일반 +0 6부위와 정기 500을 받는다
        [Header("정식 초기 지원")]
        [SerializeField] private GrantBundle _standardCharacterGrant = new GrantBundle("grant_character_standard",
            new[] { new ItemGrant("item_essence", 500) },
            new[]
            {
                new EquipmentGrant("equip_weapon_training", 0),
                new EquipmentGrant("equip_head_training", 0),
                new EquipmentGrant("equip_body_training", 0),
                new EquipmentGrant("equip_hands_training", 0),
                new EquipmentGrant("equip_feet_training", 0),
                new EquipmentGrant("equip_accessory_training", 0)
            },
            0);

        // 계정을 처음 만들 때만 준다. 캐릭터 추가, 삭제, 재생성으로 다시 주지 않는다
        [SerializeField] private GrantBundle _standardAccountGrant = new GrantBundle("grant_account_standard",
            new[] { new ItemGrant("item_potion_small", 9) },
            Array.Empty<EquipmentGrant>(),
            100);

        // 체험판은 정식 지원을 대신한다. 물약은 정식 소형 9개에 더하지 않는 합계다
        // TODO(기웅): 체험판 캐릭터가 수련용 6부위도 함께 받는지 기획 미정이라 희귀 +17 6부위만 넣었다
        [Header("체험판 초기 지원")]
        [SerializeField] private GrantBundle _trialCharacterGrant = new GrantBundle("grant_character_trial",
            new[] { new ItemGrant("item_essence", 500) },
            new[]
            {
                new EquipmentGrant("equip_weapon_rare", 17),
                new EquipmentGrant("equip_head_rare", 17),
                new EquipmentGrant("equip_body_rare", 17),
                new EquipmentGrant("equip_hands_rare", 17),
                new EquipmentGrant("equip_feet_rare", 17),
                new EquipmentGrant("equip_accessory_rare", 17)
            },
            0);

        [SerializeField] private GrantBundle _trialAccountGrant = new GrantBundle("grant_account_trial",
            new[] { new ItemGrant("item_potion_small", 9), new ItemGrant("item_potion_large", 9) },
            Array.Empty<EquipmentGrant>(),
            100);

        [NonSerialized] private DefinitionLookup<RegionDefinition>     _regionLookup = new DefinitionLookup<RegionDefinition>();
        [NonSerialized] private DefinitionLookup<BossBattleDefinition> _battleLookup = new DefinitionLookup<BossBattleDefinition>();

        public bool                                TrialEdition           => _trialEdition;
        public int                                 FoxTraceCap            => _foxTraceCap;
        public IReadOnlyList<RegionDefinition>     Regions                => _regions;
        public IReadOnlyList<BossBattleDefinition> BossBattles            => _bossBattles;
        public GrantBundle                         StandardCharacterGrant => _standardCharacterGrant;
        public GrantBundle                         StandardAccountGrant   => _standardAccountGrant;
        public GrantBundle                         TrialCharacterGrant    => _trialCharacterGrant;
        public GrantBundle                         TrialAccountGrant      => _trialAccountGrant;

        /// <summary> 이 콘텐츠 묶음에서 새 캐릭터에게 줄 지원, 체험판이면 체험판 묶음 </summary>
        public GrantBundle CharacterGrant => _trialEdition ? _trialCharacterGrant : _standardCharacterGrant;

        /// <summary> 이 콘텐츠 묶음에서 계정 최초 생성 때 줄 지원, 체험판이면 체험판 묶음 </summary>
        public GrantBundle AccountGrant => _trialEdition ? _trialAccountGrant : _standardAccountGrant;

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

        public bool ContainsRegion(string regionId) { return _regionLookup.Contains(_regions, regionId); }

        /// <summary> 이 콘텐츠 묶음에서 쓰는 지역 일반전 Aref, 체험판이면 체험판 기준 </summary>
        public float GetReferenceAttack(RegionDefinition region)
        {
            if (region == null)
            {
                throw new ArgumentNullException(nameof(region));
            }

            return _trialEdition ? region.TrialReferenceAttack : region.ReferenceAttack;
        }

        /// <summary> 이 콘텐츠 묶음에서 쓰는 지역 일반전 Href, 체험판이면 체험판 기준 </summary>
        public float GetReferenceHealth(RegionDefinition region)
        {
            if (region == null)
            {
                throw new ArgumentNullException(nameof(region));
            }

            return _trialEdition ? region.TrialReferenceHealth : region.ReferenceHealth;
        }

        public bool ContainsBattle(string battleId) { return _battleLookup.Contains(_bossBattles, battleId); }

        public bool TryGetRegion(string regionId, out RegionDefinition region) { return _regionLookup.TryGet(_regions, regionId, out region); }

        public bool TryGetBattle(string battleId, out BossBattleDefinition battle) { return _battleLookup.TryGet(_bossBattles, battleId, out battle); }

        public bool TryGetBattle(string bossId, EBossDifficulty difficulty, out BossBattleDefinition battle)
        {
            battle = null;
            if ((_bossBattles == null) || (string.IsNullOrEmpty(bossId)))
            {
                return false;
            }

            for (int i = 0; i < _bossBattles.Length; ++i)
            {
                BossBattleDefinition candidate = _bossBattles[i];
                if ((candidate != null) && (candidate.BossId == bossId) && (candidate.Difficulty == difficulty))
                {
                    battle = candidate;
                    return true;
                }
            }

            return false;
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_foxTraceCap), _foxTraceCap);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_regions), _regions);
            DataValidation.RequireCount(errors, OWNER, nameof(_regions), _regions, REGION_COUNT);
            DataValidation.RequireDefinitions(errors, OWNER, nameof(_bossBattles), _bossBattles);
            DataValidation.RequireCount(errors, OWNER, nameof(_bossBattles), _bossBattles, BOSS_BATTLE_COUNT);

            ValidateRegions(errors);
            ValidateBattles(errors);

            ValidateGrant(errors, nameof(_standardCharacterGrant), _standardCharacterGrant);
            ValidateGrant(errors, nameof(_standardAccountGrant), _standardAccountGrant);
            ValidateGrant(errors, nameof(_trialCharacterGrant), _trialCharacterGrant);
            ValidateGrant(errors, nameof(_trialAccountGrant), _trialAccountGrant);

            ValidateGrantIds(errors);
        }

        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            for (int i = 0; i < _bossBattles.Length; ++i)
            {
                BossBattleDefinition battle = _bossBattles[i];
                if (battle == null)
                {
                    continue;
                }

                DataValidation.RequireReference(errors, OWNER, $"{nameof(_bossBattles)}[{battle.DefinitionId}].BossId", battle.BossId,
                    root.BossPatterns.ContainsBoss(battle.BossId));
            }

            ValidateGrantReferences(root, errors, nameof(_standardCharacterGrant), _standardCharacterGrant);
            ValidateGrantReferences(root, errors, nameof(_standardAccountGrant), _standardAccountGrant);
            ValidateGrantReferences(root, errors, nameof(_trialCharacterGrant), _trialCharacterGrant);
            ValidateGrantReferences(root, errors, nameof(_trialAccountGrant), _trialAccountGrant);
        }

        private void ValidateRegions(List<string> errors)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            if (_regions == null)
            {
                return;
            }

            bool anyTrialRegion = false;
            for (int i = 0; i < _regions.Length; ++i)
            {
                RegionDefinition region = _regions[i];
                if (region == null)
                {
                    continue;
                }

                string field = $"{nameof(_regions)}[{region.DefinitionId}]";

                DataValidation.RequireNonNegative(errors, OWNER, field + ".FoxTracesPerClear", region.FoxTracesPerClear);
                if ((region.FoxTracesPerClear > 0) && (!region.HasWaveBattle))
                {
                    errors.Add($"{OWNER}.{field}: 일반전이 없는 지역은 흔적을 줄 수 없다");
                }

                ValidateTrialFlags(errors, field, region.AvailableInTrial, region.TrialPreUnlocked);
                ValidateRequirement(errors, field, region.DefinitionId, region.Requirement);
                ValidateRegionReference(errors, field, region);

                if (region.AvailableInTrial)
                {
                    anyTrialRegion = true;
                }
            }

            if ((_trialEdition) && (!anyTrialRegion))
            {
                errors.Add($"{OWNER}.{nameof(_regions)}: 체험판인데 체험판에 들어 있는 지역이 없다");
            }
        }

        // 일반전을 여는 지역은 적 능력치를 정할 기준이 있어야 하고, 체험판에 들어 있으면 체험판 기준도 있어야 한다
        private static void ValidateRegionReference(List<string> errors, string field, RegionDefinition region)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            DataValidation.RequireNonNegative(errors, OWNER, field + ".ReferenceAttack", region.ReferenceAttack);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ReferenceHealth", region.ReferenceHealth);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".TrialReferenceAttack", region.TrialReferenceAttack);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".TrialReferenceHealth", region.TrialReferenceHealth);

            if (!region.HasWaveBattle)
            {
                return;
            }

            DataValidation.RequirePositive(errors, OWNER, field + ".ReferenceAttack", region.ReferenceAttack);
            DataValidation.RequirePositive(errors, OWNER, field + ".ReferenceHealth", region.ReferenceHealth);

            if (region.AvailableInTrial)
            {
                DataValidation.RequirePositive(errors, OWNER, field + ".TrialReferenceAttack", region.TrialReferenceAttack);
                DataValidation.RequirePositive(errors, OWNER, field + ".TrialReferenceHealth", region.TrialReferenceHealth);
            }
        }

        private void ValidateBattles(List<string> errors)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            if (_bossBattles == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < _bossBattles.Length; ++i)
            {
                BossBattleDefinition battle = _bossBattles[i];
                if (battle == null)
                {
                    continue;
                }

                string field = $"{nameof(_bossBattles)}[{battle.DefinitionId}]";

                if (string.IsNullOrWhiteSpace(battle.BossId))
                {
                    errors.Add($"{OWNER}.{field}: 보스 ID가 비어 있다");
                }
                else if (!seen.Add($"{battle.BossId}/{battle.Difficulty}"))
                {
                    errors.Add($"{OWNER}.{field}: '{battle.BossId}' {battle.Difficulty} 전투가 중복된다");
                }

                // 지역과 보스전은 같은 해금 기록 공간을 쓰므로 ID가 겹치면 조건이 모호해진다
                if (ContainsRegion(battle.DefinitionId))
                {
                    errors.Add($"{OWNER}.{field}: 보스전 ID가 지역 ID와 겹친다");
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".TicketCost", battle.TicketCost);
                ValidateTrialFlags(errors, field, battle.AvailableInTrial, battle.TrialPreUnlocked);
                ValidateRequirement(errors, field, battle.DefinitionId, battle.Requirement);
            }
        }

        private static void ValidateTrialFlags(List<string> errors, string field, bool availableInTrial, bool trialPreUnlocked)
        {
            if ((trialPreUnlocked) && (!availableInTrial))
            {
                errors.Add($"{nameof(ProgressionCatalogSO)}.{field}: 체험판에 없는 콘텐츠를 체험판에서 미리 열 수 없다");
            }
        }

        private void ValidateRequirement(List<string> errors, string field, string ownerId, UnlockRequirement requirement)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            if (requirement == null)
            {
                errors.Add($"{OWNER}.{field}: 해금 조건이 비어 있다");
                return;
            }

            DataValidation.RequireRange(errors, OWNER, field + ".FoxTraceCount", requirement.FoxTraceCount, 0f, _foxTraceCap);

            if (!string.IsNullOrEmpty(requirement.ClearedRegionId))
            {
                bool exists = TryGetRegion(requirement.ClearedRegionId, out RegionDefinition cleared);
                DataValidation.RequireReference(errors, OWNER, field + ".ClearedRegionId", requirement.ClearedRegionId, exists);
                if ((exists) && (!cleared.HasWaveBattle))
                {
                    errors.Add($"{OWNER}.{field}: 일반전이 없는 지역 '{requirement.ClearedRegionId}'의 성공을 조건으로 삼았다");
                }
            }

            if (!string.IsNullOrEmpty(requirement.UnlockedRegionId))
            {
                DataValidation.RequireReference(errors, OWNER, field + ".UnlockedRegionId", requirement.UnlockedRegionId,
                    ContainsRegion(requirement.UnlockedRegionId));
            }

            if (!string.IsNullOrEmpty(requirement.ClearedBattleId))
            {
                DataValidation.RequireReference(errors, OWNER, field + ".ClearedBattleId", requirement.ClearedBattleId,
                    ContainsBattle(requirement.ClearedBattleId));
            }

            if ((requirement.ClearedRegionId == ownerId) || (requirement.UnlockedRegionId == ownerId) || (requirement.ClearedBattleId == ownerId))
            {
                errors.Add($"{OWNER}.{field}: 자기 자신을 해금 조건으로 삼았다");
            }
        }

        private static void ValidateGrant(List<string> errors, string field, GrantBundle grant)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            if (grant == null)
            {
                errors.Add($"{OWNER}.{field}: 지원 묶음이 비어 있다");
                return;
            }

            if (string.IsNullOrWhiteSpace(grant.GrantId))
            {
                errors.Add($"{OWNER}.{field}: 지급 ID가 비어 있다");
            }

            DataValidation.RequireNonNegative(errors, OWNER, field + ".ActionEnergy", grant.ActionEnergy);

            int itemCount = (grant.Items == null) ? 0 : grant.Items.Count;
            for (int i = 0; i < itemCount; ++i)
            {
                ItemGrant item      = grant.Items[i];
                string    itemField = $"{field}.Items[{i}]";

                if ((item == null) || (string.IsNullOrWhiteSpace(item.ItemId)))
                {
                    errors.Add($"{OWNER}.{itemField}: 아이템 ID가 비어 있다");
                    continue;
                }

                DataValidation.RequirePositive(errors, OWNER, itemField + ".Quantity", item.Quantity);
            }

            int equipmentCount = (grant.Equipment == null) ? 0 : grant.Equipment.Count;
            for (int i = 0; i < equipmentCount; ++i)
            {
                EquipmentGrant equipment      = grant.Equipment[i];
                string         equipmentField = $"{field}.Equipment[{i}]";

                if ((equipment == null) || (string.IsNullOrWhiteSpace(equipment.EquipmentId)))
                {
                    errors.Add($"{OWNER}.{equipmentField}: 장비 ID가 비어 있다");
                    continue;
                }

                // 상한은 장비 카탈로그가 정하므로 참조 검사에서 본다
                DataValidation.RequireNonNegative(errors, OWNER, equipmentField + ".Enhancement", equipment.Enhancement);
            }
        }

        private void ValidateGrantIds(List<string> errors)
        {
            GrantBundle[] grants = { _standardCharacterGrant, _standardAccountGrant, _trialCharacterGrant, _trialAccountGrant };

            var seen = new HashSet<string>();
            for (int i = 0; i < grants.Length; ++i)
            {
                if ((grants[i] == null) || (string.IsNullOrWhiteSpace(grants[i].GrantId)))
                {
                    continue;
                }

                if (!seen.Add(grants[i].GrantId))
                {
                    errors.Add($"{nameof(ProgressionCatalogSO)}: 지급 ID '{grants[i].GrantId}'가 중복된다");
                }
            }
        }

        private static void ValidateGrantReferences(GameDatabaseSO root, List<string> errors, string field, GrantBundle grant)
        {
            const string OWNER = nameof(ProgressionCatalogSO);

            if (grant == null)
            {
                return;
            }

            DataValidation.RequireRange(errors, OWNER, field + ".ActionEnergy", grant.ActionEnergy, 0f, root.Life.MaxActionEnergy);

            int itemCount = (grant.Items == null) ? 0 : grant.Items.Count;
            for (int i = 0; i < itemCount; ++i)
            {
                ItemGrant item = grant.Items[i];
                if (item == null)
                {
                    continue;
                }

                bool exists = (root.Rewards.ContainsItem(item.ItemId)) || (root.Life.ContainsLifeItem(item.ItemId));
                DataValidation.RequireReference(errors, OWNER, $"{field}.Items[{i}]", item.ItemId, exists);
            }

            int equipmentCount = (grant.Equipment == null) ? 0 : grant.Equipment.Count;
            for (int i = 0; i < equipmentCount; ++i)
            {
                EquipmentGrant equipment = grant.Equipment[i];
                if (equipment == null)
                {
                    continue;
                }

                DataValidation.RequireReference(errors, OWNER, $"{field}.Equipment[{i}]", equipment.EquipmentId,
                    root.Equipment.ContainsEquipment(equipment.EquipmentId));
                DataValidation.RequireRange(errors, OWNER, $"{field}.Equipment[{i}].Enhancement", equipment.Enhancement, 0f,
                    root.Equipment.MaxEnhancement);
            }
        }

        private void InvalidateLookups()
        {
            _regionLookup.Invalidate();
            _battleLookup.Invalidate();
        }
    }
}
