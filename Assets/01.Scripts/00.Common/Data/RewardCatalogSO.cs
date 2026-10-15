// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 개인 드롭, 웨이브 기본 보상, 추가 정기, 보스 보상, 희귀 확률 </summary>
    [CreateAssetMenu(fileName = "RewardCatalog", menuName = "Shinshi/RewardCatalogSO")]
    public class RewardCatalogSO : GameDataSO
    {
        /// <summary> 재화와 재료 아이템 정의, 약초와 물약은 LifeConfigSO가 가진다 </summary>
        [Serializable]
        public class ItemDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string     _definitionId;
            [SerializeField] private string     _displayKey;
            [SerializeField] private EItemGrade _grade;        // 보스 재료가 쓰이는 제작 등급, 재화는 Common
            [SerializeField] private bool       _perCharacter; // true면 캐릭터별, false면 계정 공동 보관
            [SerializeField] private int        _stackLimit;   // 품목별 보관 상한, 0이면 상한 없음

            public string     DefinitionId  => _definitionId;
            public string     DisplayKey    => _displayKey;
            public EItemGrade Grade         => _grade;
            public bool       PerCharacter  => _perCharacter;
            public int        StackLimit    => _stackLimit;
            public bool       HasStackLimit => _stackLimit > NO_STACK_LIMIT;

            /****************************************
            *                Methods
            ****************************************/

            public ItemDefinition(string definitionId, EItemGrade grade, bool perCharacter, int stackLimit)
            {
                _definitionId = definitionId;
                _displayKey   = definitionId + DISPLAY_KEY_SUFFIX;
                _grade        = grade;
                _perCharacter = perCharacter;
                _stackLimit   = stackLimit;
            }
        }

        /// <summary> 적 한 마리 처치마다 보상 자격자 각자에게 따로 판정하는 개인 보상 </summary>
        [Serializable]
        public class EnemyDropSpec
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _enemyId;
            [SerializeField] private int    _extraEssence;     // 처치당 추가 정기, 실패 정산에서 손실 대상
            [SerializeField] private float  _ticketChance;     // 0에서 1 사이, 공용 보스 입장권
            [SerializeField] private int    _ticketQuantity;   // 당첨 때 지급 수
            [SerializeField] private float  _talismanChance;   // 0에서 1 사이, 자기 직업 효과 중 균등 선택
            [SerializeField] private int    _talismanQuantity; // 당첨 때 지급 수

            public string EnemyId          => _enemyId;
            public int    ExtraEssence     => _extraEssence;
            public float  TicketChance     => _ticketChance;
            public int    TicketQuantity   => _ticketQuantity;
            public float  TalismanChance   => _talismanChance;
            public int    TalismanQuantity => _talismanQuantity;

            /****************************************
            *                Methods
            ****************************************/

            public EnemyDropSpec(string enemyId, int extraEssence, float ticketChance, int ticketQuantity,
                float talismanChance, int talismanQuantity)
            {
                _enemyId          = enemyId;
                _extraEssence     = extraEssence;
                _ticketChance     = ticketChance;
                _ticketQuantity   = ticketQuantity;
                _talismanChance   = talismanChance;
                _talismanQuantity = talismanQuantity;
            }
        }

        /// <summary> 보스 처치 성공 때만 주는 보상, 확정 지급과 개인 재료 판정, 파티 경매 출현 </summary>
        [Serializable]
        public class BossRewardSpec
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string          _bossId;
            [SerializeField] private EBossDifficulty _difficulty;

            [SerializeField] private int    _essence;            // 확정 기본 정기
            [SerializeField] private int    _tradeCurrency;      // 확정 엽전, 새 엽전은 여기서만 발행한다

            [SerializeField] private string _materialItemId;     // 개인 판정 일반 보스 재료
            [SerializeField] private int    _materialRolls;      // 자격자마다 독립 판정 횟수
            [SerializeField] private float  _materialRollChance; // 0에서 1 사이, 판정 한 번의 성공 확률
            [SerializeField] private int    _materialPerSuccess; // 판정 성공 한 번의 지급 수

            // 개인 보상이 아니라 파티가 한 번의 출현 판정을 공유한다. 인원이 늘어도 개수는 같다
            [SerializeField] private string _auctionItemId;      // 경매 전용 희귀 장신구 재료
            [SerializeField] private float  _auctionChance;      // 0에서 1 사이, 처치당 출현 확률
            [SerializeField] private int    _auctionQuantity;    // 출현 때 경매 품목 수

            public string          BossId             => _bossId;
            public EBossDifficulty Difficulty         => _difficulty;
            public int             Essence            => _essence;
            public int             TradeCurrency      => _tradeCurrency;
            public string          MaterialItemId     => _materialItemId;
            public int             MaterialRolls      => _materialRolls;
            public float           MaterialRollChance => _materialRollChance;
            public int             MaterialPerSuccess => _materialPerSuccess;
            public string          AuctionItemId      => _auctionItemId;
            public float           AuctionChance      => _auctionChance;
            public int             AuctionQuantity    => _auctionQuantity;

            /****************************************
            *                Methods
            ****************************************/

            public BossRewardSpec(string bossId, EBossDifficulty difficulty, int essence, int tradeCurrency,
                string materialItemId, int materialRolls, float materialRollChance, int materialPerSuccess,
                string auctionItemId, float auctionChance, int auctionQuantity)
            {
                _bossId             = bossId;
                _difficulty         = difficulty;
                _essence            = essence;
                _tradeCurrency      = tradeCurrency;
                _materialItemId     = materialItemId;
                _materialRolls      = materialRolls;
                _materialRollChance = materialRollChance;
                _materialPerSuccess = materialPerSuccess;
                _auctionItemId      = auctionItemId;
                _auctionChance      = auctionChance;
                _auctionQuantity    = auctionQuantity;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string DISPLAY_KEY_SUFFIX = ".name";
        private const float  MAX_RATE           = 1f;
        private const int    NO_STACK_LIMIT     = 0;
        private const int    MATERIAL_STACK     = 9999; // 보스 재료, 정련편, 티켓의 품목별 상한

        // 최종 보스 2종 x 이지, 하드
        private const int    BOSS_BATTLE_COUNT  = 4;

        /****************************************
        *                Fields
        ****************************************/

        // TODO(진욱): 정기와 엽전의 보관 상한은 기획 미정이라 상한 없음으로 둔다
        [Header("아이템 정의")]
        [SerializeField] private ItemDefinition[] _items =
        {
            new ItemDefinition("item_essence",                    EItemGrade.Common,    true,  NO_STACK_LIMIT),
            new ItemDefinition("item_refine_shard",               EItemGrade.Common,    true,  MATERIAL_STACK),
            new ItemDefinition("item_boss_ticket",                EItemGrade.Common,    false, MATERIAL_STACK),
            new ItemDefinition("item_yeopjeon",                   EItemGrade.Common,    false, NO_STACK_LIMIT),
            new ItemDefinition("item_boss_material_common",       EItemGrade.Common,    false, MATERIAL_STACK),
            new ItemDefinition("item_boss_material_rare",         EItemGrade.Rare,      false, MATERIAL_STACK),
            new ItemDefinition("item_boss_material_epic",         EItemGrade.Epic,      false, MATERIAL_STACK),
            new ItemDefinition("item_boss_material_legendary",    EItemGrade.Legendary, false, MATERIAL_STACK),
            new ItemDefinition("item_auction_material_common",    EItemGrade.Common,    false, MATERIAL_STACK),
            new ItemDefinition("item_auction_material_rare",      EItemGrade.Rare,      false, MATERIAL_STACK),
            new ItemDefinition("item_auction_material_epic",      EItemGrade.Epic,      false, MATERIAL_STACK),
            new ItemDefinition("item_auction_material_legendary", EItemGrade.Legendary, false, MATERIAL_STACK)
        };

        // 코드가 재화를 이름이 아니라 역할로 찾도록 ID를 데이터에 둔다
        [Header("재화 역할")]
        [SerializeField] private string _essenceItemId       = "item_essence";      // 정기, 캐릭터별
        [SerializeField] private string _refineShardItemId   = "item_refine_shard"; // 강화 전용 정련편, 캐릭터별
        [SerializeField] private string _bossTicketItemId    = "item_boss_ticket";  // 모든 보스 공용 입장권, 계정 공유
        [SerializeField] private string _tradeCurrencyItemId = "item_yeopjeon";     // 거래 재화 엽전, 계정 공유

        // 실패, 포기 때 추가 정기를 얼마나 남길지는 정산 규칙이 정한다. 여기는 지급량만 둔다
        [Header("웨이브 완료 보상 (웨이브마다)")]
        [SerializeField] private int _baseEssencePerWave  = 100; // 확보 기본 정기, 실패해도 보존
        [SerializeField] private int _extraEssencePerWave = 10;  // 추가 정기, 실패 정산에서 손실 대상
        [SerializeField] private int _refineShardsPerWave = 20;  // 정련편, 실패해도 보존

        // 역할마다 같은 값이다. 드롭에서 빠지는 적은 두지 않는다
        [Header("적별 개인 드롭 (EEnemyRole 수만큼)")]
        [SerializeField] private EnemyDropSpec[] _enemyDrops =
        {
            new EnemyDropSpec("enemy_melee",   8, 0.005f, 1, 0.02f, 1),
            new EnemyDropSpec("enemy_ranged",  8, 0.005f, 1, 0.02f, 1),
            new EnemyDropSpec("enemy_charger", 8, 0.005f, 1, 0.02f, 1),
            new EnemyDropSpec("enemy_elite",   8, 0.005f, 1, 0.02f, 1)
        };

        // 철각 이지, 하드는 일반, 희귀 재료, 월백 이지, 하드는 영웅, 전설 재료를 준다
        [Header("보스 처치 보상")]
        [SerializeField] private BossRewardSpec[] _bossRewards =
        {
            new BossRewardSpec("boss_mucheol", EBossDifficulty.Easy, 600, 120,
                "item_boss_material_common", 2, 0.75f, 3, "item_auction_material_common", 0.35f, 1),
            new BossRewardSpec("boss_mucheol", EBossDifficulty.Hard, 800, 160,
                "item_boss_material_rare", 2, 0.75f, 4, "item_auction_material_rare", 0.35f, 1),
            new BossRewardSpec("boss_wolbaek", EBossDifficulty.Easy, 1000, 200,
                "item_boss_material_epic", 2, 0.75f, 5, "item_auction_material_epic", 0.35f, 1),
            new BossRewardSpec("boss_wolbaek", EBossDifficulty.Hard, 1400, 260,
                "item_boss_material_legendary", 2, 0.75f, 6, "item_auction_material_legendary", 0.35f, 1)
        };

        [NonSerialized] private DefinitionLookup<ItemDefinition> _itemLookup = new DefinitionLookup<ItemDefinition>();

        public IReadOnlyList<ItemDefinition> Items               => _items;
        public string                        EssenceItemId       => _essenceItemId;
        public string                        RefineShardItemId   => _refineShardItemId;
        public string                        BossTicketItemId    => _bossTicketItemId;
        public string                        TradeCurrencyItemId => _tradeCurrencyItemId;
        public int                           BaseEssencePerWave  => _baseEssencePerWave;
        public int                           ExtraEssencePerWave => _extraEssencePerWave;
        public int                           RefineShardsPerWave => _refineShardsPerWave;
        public IReadOnlyList<EnemyDropSpec>  EnemyDrops          => _enemyDrops;
        public IReadOnlyList<BossRewardSpec> BossRewards         => _bossRewards;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _itemLookup.Invalidate();
        }

        protected override void OnValidate()
        {
            _itemLookup.Invalidate();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool ContainsItem(string itemId) { return _itemLookup.Contains(_items, itemId); }

        public bool TryGetItem(string itemId, out ItemDefinition item) { return _itemLookup.TryGet(_items, itemId, out item); }

        public bool TryGetEnemyDrop(string enemyId, out EnemyDropSpec drop)
        {
            drop = null;
            if ((_enemyDrops == null) || (string.IsNullOrEmpty(enemyId)))
            {
                return false;
            }

            for (int i = 0; i < _enemyDrops.Length; ++i)
            {
                EnemyDropSpec candidate = _enemyDrops[i];
                if ((candidate != null) && (candidate.EnemyId == enemyId))
                {
                    drop = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetBossReward(string bossId, EBossDifficulty difficulty, out BossRewardSpec reward)
        {
            reward = null;
            if ((_bossRewards == null) || (string.IsNullOrEmpty(bossId)))
            {
                return false;
            }

            for (int i = 0; i < _bossRewards.Length; ++i)
            {
                BossRewardSpec candidate = _bossRewards[i];
                if ((candidate != null) && (candidate.BossId == bossId) && (candidate.Difficulty == difficulty))
                {
                    reward = candidate;
                    return true;
                }
            }

            return false;
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(RewardCatalogSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_items), _items);
            if (_items != null)
            {
                for (int i = 0; i < _items.Length; ++i)
                {
                    if (_items[i] == null)
                    {
                        continue;
                    }

                    DataValidation.RequireNonNegative(errors, OWNER, $"{nameof(_items)}[{i}].StackLimit", _items[i].StackLimit);
                }
            }

            RequireOwnItem(errors, nameof(_essenceItemId), _essenceItemId);
            RequireOwnItem(errors, nameof(_refineShardItemId), _refineShardItemId);
            RequireOwnItem(errors, nameof(_bossTicketItemId), _bossTicketItemId);
            RequireOwnItem(errors, nameof(_tradeCurrencyItemId), _tradeCurrencyItemId);

            DataValidation.RequireNonNegative(errors, OWNER, nameof(_baseEssencePerWave), _baseEssencePerWave);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_extraEssencePerWave), _extraEssencePerWave);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_refineShardsPerWave), _refineShardsPerWave);

            ValidateEnemyDrops(errors);
            ValidateBossRewards(errors);
        }

        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            const string OWNER = nameof(RewardCatalogSO);

            for (int i = 0; i < _enemyDrops.Length; ++i)
            {
                EnemyDropSpec drop = _enemyDrops[i];
                if (drop == null)
                {
                    continue;
                }

                DataValidation.RequireReference(errors, OWNER, $"{nameof(_enemyDrops)}[{i}].EnemyId", drop.EnemyId,
                    root.Enemies.ContainsEnemy(drop.EnemyId));
            }

            for (int i = 0; i < _bossRewards.Length; ++i)
            {
                BossRewardSpec reward = _bossRewards[i];
                if (reward == null)
                {
                    continue;
                }

                DataValidation.RequireReference(errors, OWNER, $"{nameof(_bossRewards)}[{i}].BossId", reward.BossId,
                    root.BossPatterns.ContainsBoss(reward.BossId));
            }
        }

        private void ValidateEnemyDrops(List<string> errors)
        {
            const string OWNER = nameof(RewardCatalogSO);

            int roleCount = Enum.GetValues(typeof(EEnemyRole)).Length;
            DataValidation.RequireCount(errors, OWNER, nameof(_enemyDrops), _enemyDrops, roleCount);
            if (_enemyDrops == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < _enemyDrops.Length; ++i)
            {
                EnemyDropSpec drop  = _enemyDrops[i];
                string        field = $"{nameof(_enemyDrops)}[{i}]";

                if (drop == null)
                {
                    errors.Add($"{OWNER}.{field}: 드롭 명세가 비어 있다");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(drop.EnemyId))
                {
                    errors.Add($"{OWNER}.{field}: 적 ID가 비어 있다");
                }
                else if (!seen.Add(drop.EnemyId))
                {
                    errors.Add($"{OWNER}.{field}: 적 ID '{drop.EnemyId}'의 드롭 명세가 중복된다");
                }

                DataValidation.RequireNonNegative(errors, OWNER, field + ".ExtraEssence", drop.ExtraEssence);
                DataValidation.RequireRange(errors, OWNER, field + ".TicketChance", drop.TicketChance, 0f, MAX_RATE);
                DataValidation.RequirePositive(errors, OWNER, field + ".TicketQuantity", drop.TicketQuantity);
                DataValidation.RequireRange(errors, OWNER, field + ".TalismanChance", drop.TalismanChance, 0f, MAX_RATE);
                DataValidation.RequirePositive(errors, OWNER, field + ".TalismanQuantity", drop.TalismanQuantity);
            }
        }

        private void ValidateBossRewards(List<string> errors)
        {
            const string OWNER = nameof(RewardCatalogSO);

            DataValidation.RequireCount(errors, OWNER, nameof(_bossRewards), _bossRewards, BOSS_BATTLE_COUNT);
            if (_bossRewards == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < _bossRewards.Length; ++i)
            {
                BossRewardSpec reward = _bossRewards[i];
                string         field  = $"{nameof(_bossRewards)}[{i}]";

                if (reward == null)
                {
                    errors.Add($"{OWNER}.{field}: 보상 명세가 비어 있다");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(reward.BossId))
                {
                    errors.Add($"{OWNER}.{field}: 보스 ID가 비어 있다");
                }
                else if (!seen.Add($"{reward.BossId}/{reward.Difficulty}"))
                {
                    errors.Add($"{OWNER}.{field}: '{reward.BossId}' {reward.Difficulty} 보상이 중복된다");
                }

                DataValidation.RequireNonNegative(errors, OWNER, field + ".Essence", reward.Essence);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".TradeCurrency", reward.TradeCurrency);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".MaterialRolls", reward.MaterialRolls);
                DataValidation.RequireRange(errors, OWNER, field + ".MaterialRollChance", reward.MaterialRollChance, 0f, MAX_RATE);
                DataValidation.RequirePositive(errors, OWNER, field + ".MaterialPerSuccess", reward.MaterialPerSuccess);
                DataValidation.RequireRange(errors, OWNER, field + ".AuctionChance", reward.AuctionChance, 0f, MAX_RATE);
                DataValidation.RequirePositive(errors, OWNER, field + ".AuctionQuantity", reward.AuctionQuantity);

                bool hasMaterial = TryGetItem(reward.MaterialItemId, out ItemDefinition material);
                bool hasAuction  = TryGetItem(reward.AuctionItemId, out ItemDefinition auction);
                DataValidation.RequireReference(errors, OWNER, field + ".MaterialItemId", reward.MaterialItemId, hasMaterial);
                DataValidation.RequireReference(errors, OWNER, field + ".AuctionItemId", reward.AuctionItemId, hasAuction);

                // 낮은 등급 재료로 상위 등급을 제작하지 않으므로 같은 처치의 두 재료는 등급이 같아야 한다
                if ((hasMaterial) && (hasAuction) && (material.Grade != auction.Grade))
                {
                    errors.Add($"{OWNER}.{field}: 개인 재료 등급 {material.Grade} != 경매 재료 등급 {auction.Grade}");
                }
            }
        }

        private void RequireOwnItem(List<string> errors, string field, string itemId)
        {
            DataValidation.RequireReference(errors, nameof(RewardCatalogSO), field, itemId, ContainsItem(itemId));
        }
    }
}
