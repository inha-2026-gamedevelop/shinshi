// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 일반전 6웨이브 배치, 적 source ID, 활성 상한, 제한 시간 </summary>
    [CreateAssetMenu(fileName = "WaveConfig", menuName = "Shinshi/WaveConfigSO")]
    public class WaveConfigSO : GameDataSO
    {
        /// <summary> 한 웨이브에 나오는 적 종류와 수 </summary>
        [Serializable]
        public class WaveEnemyEntry
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _enemyId; // EnemyCatalogSO의 definitionId
            [SerializeField] private int    _count;

            public string EnemyId => _enemyId;
            public int    Count   => _count;

            /****************************************
            *                Methods
            ****************************************/

            public WaveEnemyEntry(string enemyId, int count)
            {
                _enemyId = enemyId;
                _count   = count;
            }
        }

        /// <summary> 웨이브 시작 기준으로 한꺼번에 출현을 예고하는 묶음 </summary>
        [Serializable]
        public class SpawnGroup
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private float _spawnTime; // 초, 웨이브 시작부터
            [SerializeField] private int   _count;

            public float SpawnTime => _spawnTime;
            public int   Count     => _count;

            /****************************************
            *                Methods
            ****************************************/

            public SpawnGroup(float spawnTime, int count)
            {
                _spawnTime = spawnTime;
                _count     = count;
            }
        }

        /// <summary> 웨이브 하나의 적 구성과 출현 묶음 </summary>
        [Serializable]
        public class WaveDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string           _definitionId;
            [SerializeField] private string           _displayKey;
            [SerializeField] private WaveEnemyEntry[] _enemies;
            [SerializeField] private SpawnGroup[]     _groups;  // 출현 시각 오름차순

            public string                        DefinitionId => _definitionId;
            public string                        DisplayKey   => _displayKey;
            public IReadOnlyList<WaveEnemyEntry> Enemies      => _enemies;
            public IReadOnlyList<SpawnGroup>     Groups       => _groups;

            /****************************************
            *                Methods
            ****************************************/

            public WaveDefinition(string definitionId, WaveEnemyEntry[] enemies, SpawnGroup[] groups)
            {
                _definitionId = definitionId;
                _displayKey   = definitionId + DISPLAY_KEY_SUFFIX;
                _enemies      = enemies;
                _groups       = groups;
            }

            /// <summary> 이 웨이브의 예정 처치 수 </summary>
            public int CountEnemies()
            {
                int total = 0;
                if (_enemies == null)
                {
                    return total;
                }

                for (int i = 0; i < _enemies.Length; ++i)
                {
                    if (_enemies[i] != null)
                    {
                        total += _enemies[i].Count;
                    }
                }

                return total;
            }

            public int CountGroupedSpawns()
            {
                int total = 0;
                if (_groups == null)
                {
                    return total;
                }

                for (int i = 0; i < _groups.Length; ++i)
                {
                    if (_groups[i] != null)
                    {
                        total += _groups[i].Count;
                    }
                }

                return total;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string DISPLAY_KEY_SUFFIX  = ".name";
        private const int    WAVE_COUNT          = 6;
        private const int    TOTAL_ENEMY_COUNT   = 180;
        private const string SOURCE_ID_SEPARATOR = ".";
        private const string SOURCE_ID_FORMAT    = "{0}" + SOURCE_ID_SEPARATOR + "{1:D3}";

        private const string MELEE_ID   = "enemy_melee";
        private const string RANGED_ID  = "enemy_ranged";
        private const string CHARGER_ID = "enemy_charger";
        private const string ELITE_ID   = "enemy_elite";

        /****************************************
        *                Fields
        ****************************************/

        // 적 능력치의 Aref, Href를 이 지역 정의에서 읽는다
        [Header("지역")]
        [SerializeField] private string _regionId = "region_forest"; // ProgressionCatalogSO 지역 ID, 일반전이 있는 지역이어야 한다

        [Header("전장")]
        [SerializeField] private float _arenaSize             = 32f; // m, 이동 가능한 정사각형 한 변
        [SerializeField] private int   _spawnGateCount        = 4;   // 외곽의 표시된 출현 방향 수
        [SerializeField] private float _spawnTelegraphSeconds = 1f;  // 초, 출현 예고
        [SerializeField] private float _spawnExclusionRadius  = 4f;  // m, 플레이어와 복귀 지점 주변에는 생성하지 않는다

        [Header("진행")]
        [SerializeField] private int   _maxActiveEnemies    = 24;   // 동시 활성 상한, 넘친 출현은 지연될 뿐 사라지지 않는다
        [SerializeField] private float _timeLimitSeconds    = 420f; // 초, 전투 시작부터, 완료 확정 뒤 정산 화면은 제외
        [SerializeField] private float _intermissionSeconds = 5f;   // 초, 웨이브 사이 정비, 체력 자동 회복 없음
        [SerializeField] private float _settlementSeconds   = 20f;  // 초, 최종 정산 화면

        // 적 수는 시작 인원과 무관하다. 묶음 시각은 모든 웨이브가 첫 웨이브와 같다
        // TODO(기웅): 묶음별 역할 분배, 네 출현 방향 배정, 모든 방향이 생성 금지 반경에 걸릴 때 지연 처리는 기획 미정이다
        // TODO(민성): 420초 시각과 마지막 적 처치가 같은 틱일 때 성공, 실패 우선은 기획 미정이다
        [Header("웨이브")]
        [SerializeField] private WaveDefinition[] _waves =
        {
            new WaveDefinition("wave_forest_01",
                new[] { new WaveEnemyEntry(MELEE_ID, 20) },
                new[] { new SpawnGroup(0f, 4), new SpawnGroup(10f, 4), new SpawnGroup(20f, 4), new SpawnGroup(30f, 4), new SpawnGroup(40f, 4) }),
            new WaveDefinition("wave_forest_02",
                new[] { new WaveEnemyEntry(MELEE_ID, 18), new WaveEnemyEntry(RANGED_ID, 6) },
                new[] { new SpawnGroup(0f, 5), new SpawnGroup(10f, 5), new SpawnGroup(20f, 5), new SpawnGroup(30f, 5), new SpawnGroup(40f, 4) }),
            new WaveDefinition("wave_forest_03",
                new[] { new WaveEnemyEntry(MELEE_ID, 24), new WaveEnemyEntry(CHARGER_ID, 4) },
                new[] { new SpawnGroup(0f, 6), new SpawnGroup(10f, 6), new SpawnGroup(20f, 6), new SpawnGroup(30f, 5), new SpawnGroup(40f, 5) }),
            new WaveDefinition("wave_forest_04",
                new[] { new WaveEnemyEntry(MELEE_ID, 28), new WaveEnemyEntry(ELITE_ID, 4) },
                new[] { new SpawnGroup(0f, 7), new SpawnGroup(10f, 7), new SpawnGroup(20f, 6), new SpawnGroup(30f, 6), new SpawnGroup(40f, 6) }),
            new WaveDefinition("wave_forest_05",
                new[] { new WaveEnemyEntry(MELEE_ID, 22), new WaveEnemyEntry(RANGED_ID, 8), new WaveEnemyEntry(CHARGER_ID, 6) },
                new[] { new SpawnGroup(0f, 8), new SpawnGroup(10f, 7), new SpawnGroup(20f, 7), new SpawnGroup(30f, 7), new SpawnGroup(40f, 7) }),
            new WaveDefinition("wave_forest_06",
                new[] { new WaveEnemyEntry(MELEE_ID, 24), new WaveEnemyEntry(RANGED_ID, 8), new WaveEnemyEntry(CHARGER_ID, 6), new WaveEnemyEntry(ELITE_ID, 2) },
                new[] { new SpawnGroup(0f, 8), new SpawnGroup(10f, 8), new SpawnGroup(20f, 8), new SpawnGroup(30f, 8), new SpawnGroup(40f, 8) })
        };

        [NonSerialized] private readonly DefinitionLookup<WaveDefinition> _lookup = new DefinitionLookup<WaveDefinition>();

        public string                        RegionId              => _regionId;
        public float                         ArenaSize             => _arenaSize;
        public int                           SpawnGateCount        => _spawnGateCount;
        public float                         SpawnTelegraphSeconds => _spawnTelegraphSeconds;
        public float                         SpawnExclusionRadius  => _spawnExclusionRadius;
        public int                           MaxActiveEnemies      => _maxActiveEnemies;
        public float                         TimeLimitSeconds      => _timeLimitSeconds;
        public float                         IntermissionSeconds   => _intermissionSeconds;
        public float                         SettlementSeconds     => _settlementSeconds;
        public IReadOnlyList<WaveDefinition> Waves                 => _waves;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _lookup.Invalidate();
        }

        protected override void OnValidate()
        {
            _lookup.Invalidate();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool ContainsWave(string waveId)
        {
            return _lookup.Contains(_waves, waveId);
        }

        public bool TryGetWave(string waveId, out WaveDefinition wave)
        {
            return _lookup.TryGet(_waves, waveId, out wave);
        }

        /// <summary> 출정 전체의 예정 처치 수 </summary>
        public int CountTotalEnemies()
        {
            int total = 0;
            if (_waves == null)
            {
                return total;
            }

            for (int i = 0; i < _waves.Length; ++i)
            {
                if (_waves[i] != null)
                {
                    total += _waves[i].CountEnemies();
                }
            }

            return total;
        }

        // 몬스터 한 마리의 개인 드롭 판정 키. 출정 ID, 받는 사람과 묶여 보상 원장의 유일 키가 된다.
        // 실제 출현 시각이 아니라 배치 계획의 순번으로 정한다. 활성 상한으로 출현이 늦어지거나
        // 판정을 재시도해도 같은 몬스터는 같은 키를 받아야 보상이 두 번 나가거나 빠지지 않는다
        /// <summary> 웨이브 순서와 웨이브 안 배치 순번(0부터)으로 source ID를 만든다 </summary>
        public string GetSourceId(int waveIndex, int spawnIndex)
        {
            if ((_waves == null) || (waveIndex < 0) || (waveIndex >= _waves.Length) || (_waves[waveIndex] == null))
            {
                throw new ArgumentOutOfRangeException(nameof(waveIndex), "정의되지 않은 웨이브다");
            }

            WaveDefinition wave = _waves[waveIndex];
            if ((spawnIndex < 0) || (spawnIndex >= wave.CountEnemies()))
            {
                throw new ArgumentOutOfRangeException(nameof(spawnIndex), "웨이브 예정 수를 벗어난 배치 순번이다");
            }

            return string.Format(SOURCE_ID_FORMAT, wave.DefinitionId, spawnIndex);
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(WaveConfigSO);

            if (string.IsNullOrWhiteSpace(_regionId))
            {
                errors.Add($"{OWNER}.{nameof(_regionId)}: 지역 ID가 비어 있다");
            }

            DataValidation.RequirePositive(errors, OWNER, nameof(_arenaSize), _arenaSize);
            DataValidation.RequirePositive(errors, OWNER, nameof(_spawnGateCount), _spawnGateCount);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_spawnTelegraphSeconds), _spawnTelegraphSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_spawnExclusionRadius), _spawnExclusionRadius);
            DataValidation.RequirePositive(errors, OWNER, nameof(_maxActiveEnemies), _maxActiveEnemies);
            DataValidation.RequirePositive(errors, OWNER, nameof(_timeLimitSeconds), _timeLimitSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_intermissionSeconds), _intermissionSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_settlementSeconds), _settlementSeconds);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_waves), _waves);
            DataValidation.RequireCount(errors, OWNER, nameof(_waves), _waves, WAVE_COUNT);
            if (_waves == null)
            {
                return;
            }

            for (int i = 0; i < _waves.Length; ++i)
            {
                if (_waves[i] != null)
                {
                    ValidateWave(errors, _waves[i]);
                }
            }

            int total = CountTotalEnemies();
            if (total != TOTAL_ENEMY_COUNT)
            {
                errors.Add($"{OWNER}.{nameof(_waves)}: 출정 전체 적 수 {total} != {TOTAL_ENEMY_COUNT}");
            }
        }

        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            const string OWNER = nameof(WaveConfigSO);

            if ((root == null) || (root.Enemies == null) || (root.Progression == null))
            {
                errors.Add($"{OWNER}: 적 또는 진행 카탈로그가 없어 참조를 검사할 수 없다");
                return;
            }

            bool hasRegion = root.Progression.TryGetRegion(_regionId, out ProgressionCatalogSO.RegionDefinition region);
            DataValidation.RequireReference(errors, OWNER, nameof(_regionId), _regionId, hasRegion);
            if ((hasRegion) && (!region.HasWaveBattle))
            {
                errors.Add($"{OWNER}.{nameof(_regionId)}: 지역 '{_regionId}'은 일반전이 없는 지역이다");
            }

            if (_waves == null)
            {
                return;
            }

            for (int i = 0; i < _waves.Length; ++i)
            {
                WaveDefinition wave = _waves[i];
                if ((wave == null) || (wave.Enemies == null))
                {
                    continue;
                }

                for (int j = 0; j < wave.Enemies.Count; ++j)
                {
                    if (wave.Enemies[j] == null)
                    {
                        continue;
                    }

                    string enemyId = wave.Enemies[j].EnemyId;
                    DataValidation.RequireReference(errors, OWNER, $"{nameof(_waves)}[{wave.DefinitionId}].Enemies[{j}]", enemyId, root.Enemies.ContainsEnemy(enemyId));
                }
            }
        }

        private static void ValidateWave(List<string> errors, WaveDefinition wave)
        {
            const string OWNER = nameof(WaveConfigSO);

            string field = $"{nameof(_waves)}[{wave.DefinitionId}]";

            // source ID가 웨이브 ID와 순번을 구분자로 잇기 때문에 ID 안에 구분자가 있으면 다른 웨이브의 키와 겹칠 수 있다
            if ((!string.IsNullOrEmpty(wave.DefinitionId)) && (wave.DefinitionId.Contains(SOURCE_ID_SEPARATOR)))
            {
                errors.Add($"{OWNER}.{field}: definitionId에 source ID 구분자 '{SOURCE_ID_SEPARATOR}'를 쓸 수 없다");
            }

            if ((wave.Enemies == null) || (wave.Enemies.Count == 0))
            {
                errors.Add($"{OWNER}.{field}.Enemies: 적 구성이 없다");
            }
            else
            {
                var seenEnemies = new HashSet<string>();
                for (int i = 0; i < wave.Enemies.Count; ++i)
                {
                    WaveEnemyEntry entry = wave.Enemies[i];
                    if (entry == null)
                    {
                        errors.Add($"{OWNER}.{field}.Enemies[{i}]: 항목이 비어 있다");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(entry.EnemyId))
                    {
                        errors.Add($"{OWNER}.{field}.Enemies[{i}]: 적 ID가 비어 있다");
                    }
                    else if (!seenEnemies.Add(entry.EnemyId))
                    {
                        errors.Add($"{OWNER}.{field}.Enemies[{i}]: 적 ID '{entry.EnemyId}'가 중복된다");
                    }

                    DataValidation.RequirePositive(errors, OWNER, $"{field}.Enemies[{i}].Count", entry.Count);
                }
            }

            if ((wave.Groups == null) || (wave.Groups.Count == 0))
            {
                errors.Add($"{OWNER}.{field}.Groups: 출현 묶음이 없다");
                return;
            }

            float previousTime = float.NegativeInfinity;
            for (int i = 0; i < wave.Groups.Count; ++i)
            {
                SpawnGroup group = wave.Groups[i];
                if (group == null)
                {
                    errors.Add($"{OWNER}.{field}.Groups[{i}]: 항목이 비어 있다");
                    continue;
                }

                DataValidation.RequireNonNegative(errors, OWNER, $"{field}.Groups[{i}].SpawnTime", group.SpawnTime);
                DataValidation.RequirePositive(errors, OWNER, $"{field}.Groups[{i}].Count", group.Count);

                if (group.SpawnTime <= previousTime)
                {
                    errors.Add($"{OWNER}.{field}.Groups[{i}].SpawnTime = {group.SpawnTime}: 앞 묶음보다 늦어야 한다");
                }

                previousTime = group.SpawnTime;
            }

            int enemyCount   = wave.CountEnemies();
            int groupedCount = wave.CountGroupedSpawns();
            if (enemyCount != groupedCount)
            {
                errors.Add($"{OWNER}.{field}: 적 구성 합 {enemyCount} != 출현 묶음 합 {groupedCount}");
            }
        }
    }
}
