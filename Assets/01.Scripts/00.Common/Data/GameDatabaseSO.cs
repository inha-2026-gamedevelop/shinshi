// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 모든 도메인 SO를 모으는 루트, Resources/GameDB.asset 하나만 둔다 </summary>
    [CreateAssetMenu(fileName = "GameDB", menuName = "Shinshi/GameDatabaseSO")]
    public class GameDatabaseSO : ScriptableObject, IDataValidatable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버와 클라이언트의 콘텐츠가 같은지 비교하는 값
        [SerializeField] private string _contentVersion = "0.1.0";

        [Header("플레이어와 출정")]
        [SerializeField] private PlayerConfigSO  _player;
        [SerializeField] private SessionConfigSO _session;

        [Header("전투")]
        [SerializeField] private SkillCatalogSO    _skills;
        [SerializeField] private TalismanCatalogSO _talismans;

        [Header("적과 보스")]
        [SerializeField] private EnemyCatalogSO       _enemies;
        [SerializeField] private BossPatternCatalogSO _bossPatterns;
        [SerializeField] private WaveConfigSO         _waves;

        [Header("성장")]
        [SerializeField] private EquipmentCatalogSO _equipment;
        [SerializeField] private GrowthConfigSO     _growth;

        [Header("보상, 생활, 진행")]
        [SerializeField] private RewardCatalogSO      _rewards;
        [SerializeField] private LifeConfigSO         _life;
        [SerializeField] private ProgressionCatalogSO _progression;

        [Header("외형")]
        [SerializeField] private AvatarCatalogSO _avatar;

        [Header("카메라")]
        [SerializeField] private CameraConfigSO _camera;

        public string               ContentVersion => _contentVersion;
        public PlayerConfigSO       Player         => _player;
        public SessionConfigSO      Session        => _session;
        public SkillCatalogSO       Skills         => _skills;
        public TalismanCatalogSO    Talismans      => _talismans;
        public EnemyCatalogSO       Enemies        => _enemies;
        public BossPatternCatalogSO BossPatterns   => _bossPatterns;
        public WaveConfigSO         Waves          => _waves;
        public EquipmentCatalogSO   Equipment      => _equipment;
        public GrowthConfigSO       Growth         => _growth;
        public RewardCatalogSO      Rewards        => _rewards;
        public LifeConfigSO         Life           => _life;
        public ProgressionCatalogSO Progression    => _progression;
        public AvatarCatalogSO      Avatar         => _avatar;
        public CameraConfigSO       Camera         => _camera;

        /****************************************
        *                Methods
        ****************************************/

        // 아이템 ID 공간 하나를 여러 카탈로그가 나눠 가진다. 카탈로그를 묶는 별도 SO를 두면
        // 같은 목록을 두 곳에서 고쳐야 하므로 루트가 조회만 모아 준다
        /// <summary> 재화와 재료, 약초와 물약, 장비 중 어느 카탈로그에든 있는 아이템 ID인지 본다 </summary>
        public bool ContainsItem(string itemId)
        {
            return ((_rewards != null) && (_rewards.ContainsItem(itemId)))
                || ((_life != null) && (_life.ContainsLifeItem(itemId)))
                || ((_equipment != null) && (_equipment.ContainsEquipment(itemId)));
        }

        /// <summary> 루트와 하위 설정을 모두 검사한다 </summary>
        public void Validate(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(_contentVersion))
            {
                errors.Add($"{nameof(GameDatabaseSO)}.{nameof(_contentVersion)}: 콘텐츠 버전이 비어 있다");
            }

            GameDataSO[] children =
            {
                _player, _session, _skills, _talismans, _enemies, _bossPatterns,
                _waves, _equipment, _growth, _rewards, _life, _progression, _avatar, _camera
            };
            string[] names =
            {
                nameof(_player), nameof(_session), nameof(_skills), nameof(_talismans), nameof(_enemies), nameof(_bossPatterns),
                nameof(_waves), nameof(_equipment), nameof(_growth), nameof(_rewards), nameof(_life), nameof(_progression),
                nameof(_avatar), nameof(_camera)
            };

            int errorCountBefore = errors.Count;
            for (int i = 0; i < children.Length; ++i)
            {
                if (children[i] == null)
                {
                    errors.Add($"{nameof(GameDatabaseSO)}.{names[i]}: 참조가 없다");
                    continue;
                }

                children[i].Validate(errors);
            }

            // 값이 틀린 카탈로그를 기준으로 참조를 검사하면 원인과 무관한 오류가 쏟아진다
            if (errors.Count > errorCountBefore)
            {
                return;
            }

            for (int i = 0; i < children.Length; ++i)
            {
                children[i].ValidateReferences(this, errors);
            }

            ValidateItemIdsAcrossCatalogs(errors);
        }

        // 카탈로그 안의 중복은 각 카탈로그가 본다. 여기서는 서로 다른 카탈로그에 같은 ID가 있어
        // 저장 데이터와 보상 원장의 아이템 ID가 어느 정의를 가리키는지 모호해지는 경우만 막는다
        private void ValidateItemIdsAcrossCatalogs(List<string> errors)
        {
            var owners = new Dictionary<string, string>();

            AddItemIds(errors, owners, nameof(RewardCatalogSO), _rewards.Items);
            AddItemIds(errors, owners, nameof(LifeConfigSO), _life.Herbs);
            AddItemIds(errors, owners, nameof(LifeConfigSO), _life.Potions);
            AddItemIds(errors, owners, nameof(EquipmentCatalogSO), _equipment.Equipment);
        }

        private static void AddItemIds<T>(List<string> errors, Dictionary<string, string> owners, string catalog, IReadOnlyList<T> definitions)
            where T : class, IDefinition
        {
            int count = (definitions == null) ? 0 : definitions.Count;
            for (int i = 0; i < count; ++i)
            {
                T item = definitions[i];
                if ((item == null) || (string.IsNullOrEmpty(item.DefinitionId)))
                {
                    continue;
                }

                if (!owners.TryGetValue(item.DefinitionId, out string owner))
                {
                    owners.Add(item.DefinitionId, catalog);
                    continue;
                }

                if (owner != catalog)
                {
                    errors.Add($"{nameof(GameDatabaseSO)}: 아이템 ID '{item.DefinitionId}'가 {owner}와 {catalog}에 함께 있다");
                }
            }
        }
    }
}
