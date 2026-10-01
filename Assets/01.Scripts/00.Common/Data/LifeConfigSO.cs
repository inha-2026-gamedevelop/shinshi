// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 약초, 행동력, 채집 선점과 재생, 물약 제작 레시피 </summary>
    [CreateAssetMenu(fileName = "LifeConfig", menuName = "Shinshi/LifeConfigSO")]
    public class LifeConfigSO : GameDataSO
    {
        /// <summary> 채집으로 얻는 약초 정의, 계정 공동 보관 </summary>
        [Serializable]
        public class HerbDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _definitionId;
            [SerializeField] private string _displayKey;
            [SerializeField] private int    _stackLimit; // 품목별 보관 상한

            public string DefinitionId => _definitionId;
            public string DisplayKey   => _displayKey;
            public int    StackLimit   => _stackLimit;

            /****************************************
            *                Methods
            ****************************************/

            public HerbDefinition(string definitionId, int stackLimit)
            {
                _definitionId = definitionId;
                _displayKey   = definitionId + DISPLAY_KEY_SUFFIX;
                _stackLimit   = stackLimit;
            }
        }

        /// <summary> 물약 완제품 정의와 제작 레시피, 계정 공동 보관 </summary>
        [Serializable]
        public class PotionDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string      _definitionId;
            [SerializeField] private string      _displayKey;
            [SerializeField] private EPotionSize _size;
            [SerializeField] private float       _healRate;    // 최종 최대 체력 대비 비율, 초과 회복은 버린다
            [SerializeField] private string      _herbId;      // 재료 약초, 같은 약초만 쓴다
            [SerializeField] private int         _herbCost;    // 한 번 제작에 드는 약초 수
            [SerializeField] private int         _outputCount; // 한 번 제작의 완제품 수
            [SerializeField] private int         _stackLimit;  // 거래 속성별 스택 상한

            public string      DefinitionId => _definitionId;
            public string      DisplayKey   => _displayKey;
            public EPotionSize Size         => _size;
            public float       HealRate     => _healRate;
            public string      HerbId       => _herbId;
            public int         HerbCost     => _herbCost;
            public int         OutputCount  => _outputCount;
            public int         StackLimit   => _stackLimit;

            /****************************************
            *                Methods
            ****************************************/

            public PotionDefinition(string definitionId, EPotionSize size, float healRate, string herbId,
                int herbCost, int outputCount, int stackLimit)
            {
                _definitionId = definitionId;
                _displayKey   = definitionId + DISPLAY_KEY_SUFFIX;
                _size         = size;
                _healRate     = healRate;
                _herbId       = herbId;
                _herbCost     = herbCost;
                _outputCount  = outputCount;
                _stackLimit   = stackLimit;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string DISPLAY_KEY_SUFFIX = ".name";
        private const float  MAX_RATE           = 1f;
        private const int    HERB_STACK         = 9999;
        private const int    POTION_STACK       = 999;

        // 체험판은 약초 한 종목만 둔다
        private const int    HERB_KIND_COUNT    = 1;

        /****************************************
        *                Fields
        ****************************************/

        [Header("약초")]
        [SerializeField] private HerbDefinition[] _herbs =
        {
            new HerbDefinition("item_herb", HERB_STACK)
        };

        // EPotionSize 값을 인덱스로 쓴다: 소형, 대형
        [Header("물약 (EPotionSize 순서)")]
        [SerializeField] private PotionDefinition[] _potions =
        {
            new PotionDefinition("item_potion_small", EPotionSize.Small, 0.3f, "item_herb", 5, 1, POTION_STACK),
            new PotionDefinition("item_potion_large", EPotionSize.Large, 0.5f, "item_herb", 12, 1, POTION_STACK)
        };

        // 전투 중 사용 규칙(공용 재사용, 사용 표시)은 PlayerConfigSO, 보스전 합산 사용 횟수는 SessionConfigSO가 가진다
        [Header("물약 제작")]
        [SerializeField] private float _potionCraftSeconds = 1f; // 초, 선택 수량을 한 번에 정산하는 제작 동작

        // 생활 구역 정원은 신시 전체 정원과 별개다
        [Header("생활 구역")]
        [SerializeField] private int _zoneCapacity = 8;  // 명, 한 구역 최대 인원
        [SerializeField] private int _nodesPerZone = 12; // 구역별 약초 노드 수

        [Header("채집")]
        [SerializeField] private string _gatherHerbId             = "item_herb";
        [SerializeField] private float  _interactRange            = 1.5f; // m, 노드에서 시작할 수 있는 거리
        [SerializeField] private float  _gatherSeconds            = 3f;   // 초, 선점 뒤 완료까지
        [SerializeField] private float  _claimTimeoutSeconds      = 10f;  // 초, 시작부터 이 안에 완료하지 못하면 선점 해제
        [SerializeField] private float  _disconnectReleaseSeconds = 5f;   // 초, 연결 종료 뒤 선점 해제
        [SerializeField] private float  _regrowSeconds            = 60f;  // 초, 완료 뒤 같은 노드 재생
        [SerializeField] private int    _herbsPerGather           = 2;    // 완료 한 번의 약초 지급 수
        [SerializeField] private int    _maxClaimsPerAccount      = 1;    // 계정당 동시 선점 수

        // 미접속 시간에도 같은 간격으로 회복하고 상한을 넘는 회복은 쌓지 않는다.
        // 회복 기준 시각은 서버 DB 시각이며 캐릭터 변경, 재접속으로 초기화하지 않는다
        [Header("행동력 (계정 공용)")]
        [SerializeField] private int   _maxActionEnergy       = 100;
        [SerializeField] private int   _actionCostPerGather   = 2;    // 시작 때 예약, 완료 때 차감, 취소면 0
        [SerializeField] private float _actionRecoverInterval = 300f; // 초, 회복 한 번의 간격
        [SerializeField] private int   _actionRecoverAmount   = 1;    // 간격마다 회복량

        [NonSerialized] private DefinitionLookup<HerbDefinition>   _herbLookup   = new DefinitionLookup<HerbDefinition>();
        [NonSerialized] private DefinitionLookup<PotionDefinition> _potionLookup = new DefinitionLookup<PotionDefinition>();

        public IReadOnlyList<HerbDefinition>   Herbs                    => _herbs;
        public IReadOnlyList<PotionDefinition> Potions                  => _potions;
        public float                           PotionCraftSeconds       => _potionCraftSeconds;
        public int                             ZoneCapacity             => _zoneCapacity;
        public int                             NodesPerZone             => _nodesPerZone;
        public string                          GatherHerbId             => _gatherHerbId;
        public float                           InteractRange            => _interactRange;
        public float                           GatherSeconds            => _gatherSeconds;
        public float                           ClaimTimeoutSeconds      => _claimTimeoutSeconds;
        public float                           DisconnectReleaseSeconds => _disconnectReleaseSeconds;
        public float                           RegrowSeconds            => _regrowSeconds;
        public int                             HerbsPerGather           => _herbsPerGather;
        public int                             MaxClaimsPerAccount      => _maxClaimsPerAccount;
        public int                             MaxActionEnergy          => _maxActionEnergy;
        public int                             ActionCostPerGather      => _actionCostPerGather;
        public float                           ActionRecoverInterval    => _actionRecoverInterval;
        public int                             ActionRecoverAmount      => _actionRecoverAmount;

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

        /// <summary> 약초와 물약 완제품 ID를 모두 찾는다 </summary>
        public bool ContainsLifeItem(string itemId)
        {
            return (_herbLookup.Contains(_herbs, itemId)) || (_potionLookup.Contains(_potions, itemId));
        }

        public bool TryGetHerb(string herbId, out HerbDefinition herb) { return _herbLookup.TryGet(_herbs, herbId, out herb); }

        public bool TryGetPotion(string potionId, out PotionDefinition potion) { return _potionLookup.TryGet(_potions, potionId, out potion); }

        public PotionDefinition GetPotion(EPotionSize size)
        {
            int index = (int)size;
            if ((_potions == null) || (index < 0) || (index >= _potions.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(size), "물약 정의가 없는 종류다");
            }

            return _potions[index];
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(LifeConfigSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_herbs), _herbs);
            DataValidation.RequireCount(errors, OWNER, nameof(_herbs), _herbs, HERB_KIND_COUNT);
            if (_herbs != null)
            {
                for (int i = 0; i < _herbs.Length; ++i)
                {
                    if (_herbs[i] == null)
                    {
                        continue;
                    }

                    DataValidation.RequirePositive(errors, OWNER, $"{nameof(_herbs)}[{i}].StackLimit", _herbs[i].StackLimit);
                }
            }

            ValidatePotions(errors);

            DataValidation.RequireNonNegative(errors, OWNER, nameof(_potionCraftSeconds), _potionCraftSeconds);

            DataValidation.RequirePositive(errors, OWNER, nameof(_zoneCapacity), _zoneCapacity);
            DataValidation.RequirePositive(errors, OWNER, nameof(_nodesPerZone), _nodesPerZone);

            DataValidation.RequireReference(errors, OWNER, nameof(_gatherHerbId), _gatherHerbId, _herbLookup.Contains(_herbs, _gatherHerbId));
            DataValidation.RequirePositive(errors, OWNER, nameof(_interactRange), _interactRange);
            DataValidation.RequirePositive(errors, OWNER, nameof(_gatherSeconds), _gatherSeconds);
            DataValidation.RequireRange(errors, OWNER, nameof(_claimTimeoutSeconds), _claimTimeoutSeconds, _gatherSeconds, float.MaxValue);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_disconnectReleaseSeconds), _disconnectReleaseSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_regrowSeconds), _regrowSeconds);
            DataValidation.RequirePositive(errors, OWNER, nameof(_herbsPerGather), _herbsPerGather);
            DataValidation.RequirePositive(errors, OWNER, nameof(_maxClaimsPerAccount), _maxClaimsPerAccount);

            DataValidation.RequirePositive(errors, OWNER, nameof(_maxActionEnergy), _maxActionEnergy);
            DataValidation.RequirePositive(errors, OWNER, nameof(_actionCostPerGather), _actionCostPerGather);
            DataValidation.RequireRange(errors, OWNER, nameof(_actionCostPerGather), _actionCostPerGather, 0f, _maxActionEnergy);
            DataValidation.RequirePositive(errors, OWNER, nameof(_actionRecoverInterval), _actionRecoverInterval);
            DataValidation.RequirePositive(errors, OWNER, nameof(_actionRecoverAmount), _actionRecoverAmount);
        }

        private void ValidatePotions(List<string> errors)
        {
            const string OWNER = nameof(LifeConfigSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_potions), _potions);

            int sizeCount = Enum.GetValues(typeof(EPotionSize)).Length;
            DataValidation.RequireCount(errors, OWNER, nameof(_potions), _potions, sizeCount);
            if (_potions == null)
            {
                return;
            }

            for (int i = 0; i < _potions.Length; ++i)
            {
                PotionDefinition potion = _potions[i];
                string           field  = $"{nameof(_potions)}[{i}]";

                if (potion == null)
                {
                    continue;
                }

                if ((int)potion.Size != i)
                {
                    errors.Add($"{OWNER}.{field}: 종류 {potion.Size}가 EPotionSize 순서와 다르다");
                }

                // 약초와 물약 ID는 같은 계정 보관함을 쓰므로 서로 겹치면 안 된다
                if (_herbLookup.Contains(_herbs, potion.DefinitionId))
                {
                    errors.Add($"{OWNER}.{field}: 물약 ID '{potion.DefinitionId}'가 약초 ID와 겹친다");
                }

                DataValidation.RequirePositiveAtMost(errors, OWNER, field + ".HealRate", potion.HealRate, MAX_RATE);
                DataValidation.RequireReference(errors, OWNER, field + ".HerbId", potion.HerbId, _herbLookup.Contains(_herbs, potion.HerbId));
                DataValidation.RequirePositive(errors, OWNER, field + ".HerbCost", potion.HerbCost);
                DataValidation.RequirePositive(errors, OWNER, field + ".OutputCount", potion.OutputCount);
                DataValidation.RequirePositive(errors, OWNER, field + ".StackLimit", potion.StackLimit);
            }
        }

        private void InvalidateLookups()
        {
            _herbLookup.Invalidate();
            _potionLookup.Invalidate();
        }
    }
}
