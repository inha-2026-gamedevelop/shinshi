// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부팅 때 로딩 화면 뒤에서 미리 그려 둘 프리팹 목록과 예산, 로컬 표현 전용이며 서버 판정에 쓰지 않는다 </summary>
    [CreateAssetMenu(fileName = "PrewarmCatalog", menuName = "Shinshi/PrewarmCatalogSO")]
    public sealed class PrewarmCatalogSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_FRAME_BUDGET_MS       = 8f;
        private const float DEFAULT_TOTAL_TIMEOUT_SECONDS = 10f;
        private const int   DEFAULT_MAX_CONCURRENT        = 8;
        private const float DEFAULT_STAGE_DISTANCE        = 12f;
        private const float DEFAULT_STAGE_SPACING         = 2f;
        private const int   DEFAULT_VARIANTS_PER_FRAME    = 8;
        private const int   DEFAULT_STATES_PER_FRAME      = 16;

        [Header("스폰 워밍")]
        [SerializeField] private PrewarmCatalogEntry[] _entries = new PrewarmCatalogEntry[0];

        [Header("예산")]
        [Tooltip("프레임당 새 항목을 시작할 수 있는 시간(ms), 시작한 항목은 도중에 끊지 못한다")]
        [SerializeField] private float _frameBudgetMs        = DEFAULT_FRAME_BUDGET_MS;
        [Tooltip("이 시간을 넘으면 새 항목을 시작하지 않고 남은 항목을 건너뛴다")]
        [SerializeField] private float _totalTimeoutSeconds  = DEFAULT_TOTAL_TIMEOUT_SECONDS;
        [SerializeField] private int   _maxConcurrentVisible = DEFAULT_MAX_CONCURRENT;

        [Header("스테이지")]
        [Tooltip("카메라 앞 거리(m)")]
        [SerializeField] private float _stageDistance = DEFAULT_STAGE_DISTANCE;
        [SerializeField] private float _stageSpacing  = DEFAULT_STAGE_SPACING;

        [Header("셰이더 워밍 (2차)")]
        [SerializeField] private ShaderVariantCollection _variantCollection     = null;
        [SerializeField] private int                     _variantsPerFrame      = DEFAULT_VARIANTS_PER_FRAME;
        [Tooltip("StreamingAssets 기준 상대 경로, GraphicsStateCollection 수집 파일")]
        [SerializeField] private string                  _graphicsStateFileName = string.Empty;
        [SerializeField] private int                     _graphicsStatesPerFrame = DEFAULT_STATES_PER_FRAME;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<PrewarmCatalogEntry> Entries => _entries;
        public float                  FrameBudgetMs          => _frameBudgetMs;
        public float                  TotalTimeoutSeconds    => _totalTimeoutSeconds;
        public int                    MaxConcurrentVisible   => _maxConcurrentVisible;
        public float                  StageDistance          => _stageDistance;
        public float                  StageSpacing           => _stageSpacing;
        public ShaderVariantCollection VariantCollection     => _variantCollection;
        public int                    VariantsPerFrame       => _variantsPerFrame;
        public string                 GraphicsStateFileName  => _graphicsStateFileName;
        public int                    GraphicsStatesPerFrame => _graphicsStatesPerFrame;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 코드에서 목록과 예산을 직접 채운다, 테스트와 임시 카탈로그용 </summary>
        public void Configure(PrewarmCatalogEntry[] entries, float frameBudgetMs, float totalTimeoutSeconds,
            int maxConcurrentVisible)
        {
            _entries              = entries ?? new PrewarmCatalogEntry[0];
            _frameBudgetMs        = frameBudgetMs;
            _totalTimeoutSeconds  = totalTimeoutSeconds;
            _maxConcurrentVisible = maxConcurrentVisible;
        }

        /// <summary> 코드에서 셰이더 수집본을 직접 지정한다, 테스트와 임시 카탈로그용 </summary>
        public void ConfigureShaderWarmup(ShaderVariantCollection variantCollection, int variantsPerFrame)
        {
            _variantCollection = variantCollection;
            _variantsPerFrame  = variantsPerFrame;
        }

        /// <summary> null 프리팹, 같은 프리팹 중복, Scripted 루트의 IPrewarmTarget 누락, 유지 프레임 부족을 찾는다 </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();
            var seen = new HashSet<GameObject>();
            for (int i = 0; i < _entries.Length; ++i)
            {
                PrewarmCatalogEntry entry = _entries[i];
                if ((entry == null) || (entry.Prefab == null))
                {
                    errors.Add($"[{i}] 프리팹이 비어 있다");
                    continue;
                }

                if (!seen.Add(entry.Prefab))
                {
                    errors.Add($"[{i}] 같은 프리팹이 중복됐다: {entry.Prefab.name}");
                }

                if (entry.VisibleFrames < 1)
                {
                    errors.Add($"[{i}] VisibleFrames는 1 이상이어야 한다: {entry.Prefab.name}");
                }

                if ((entry.Kind == EPrewarmKind.Scripted) && (!entry.Prefab.TryGetComponent(out IPrewarmTarget _)))
                {
                    errors.Add($"[{i}] Scripted 항목의 루트에 IPrewarmTarget이 없다: {entry.Prefab.name}");
                }
            }

            return errors.Count == 0;
        }
    }
}
