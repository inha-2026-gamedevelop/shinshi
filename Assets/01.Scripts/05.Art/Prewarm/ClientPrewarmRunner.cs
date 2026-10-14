// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Stopwatch = System.Diagnostics.Stopwatch;

// Unity
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Art
{
    /// <summary> 로딩 화면 뒤에서 프리팹을 실제로 스폰해 그려 셰이더와 인스턴스 초기화 비용을 부팅 때 치르게 한다 </summary>
    public sealed class ClientPrewarmRunner : ClientView
    {
        // 예산과 타임아웃은 새 항목을 시작하는 기준일 뿐이다. 동기 Instantiate나 드라이버 컴파일은 도중에 끊을 수 없어
        // 한 항목이 예산을 넘길 수 있으므로 항목 작업 시간과 항목을 켠 프레임의 시간을 따로 집계해 로그에 남긴다.
        // Renderer.isVisible은 다른 카메라나 그림자 패스로도 참이 되어 완료의 증거가 못 되므로 누락 탐지에만 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const string STAGE_SHADER       = "셰이더 준비 중";
        private const string STAGE_SPAWN        = "효과 준비 중";
        private const string STAGE_ROOT_NAME    = "PrewarmStage";
        private const float  SHADER_SHARE       = 0.3f;
        private const int    GRID_COLUMNS       = 4;
        private const float  CENTER_HALF        = 0.5f;
        private const float  MS_PER_SECOND      = 1000f;
        private const float  TRAIL_SWAY_RATIO   = 0.4f;
        private const float  TRAIL_SWAY_SPEED   = 6f;
        private const int    MIN_PER_FRAME      = 1;

        // 코루틴이 예외나 비활성화로 죽었을 때 감시가 Finish를 강제하기까지 전체 제한 시간에 더하는 여유
        private const float  WATCHDOG_GRACE_SECONDS = 5f;

        [SerializeField] private PrewarmCatalogSO   _catalog       = null;
        [SerializeField] private Camera             _camera        = null;
        [SerializeField] private LoadingOverlayView _overlayPrefab = null;

        private readonly List<ActiveItem> _active     = new List<ActiveItem>();
        private readonly Stopwatch        _frameWatch = new Stopwatch();
        private readonly Stopwatch        _itemWatch  = new Stopwatch();
        private readonly Stopwatch        _totalWatch = new Stopwatch();

        private LoadingOverlayView      _overlay;
        private Transform               _stage;
        private GraphicsStateCollection _states;
        private JobHandle               _warmHandle;
        private bool[]                  _slotUsed;
        private Action                  _onFinished;
        private bool                    _isRunning            = false;
        private bool                    _hasFinished          = false;
        private bool                    _isTimedOut           = false;
        private bool                    _routineInterrupted   = false;
        private bool                    _startedItemLastFrame = false;
        private float                   _runStartTime         = 0f;
        private float                   _progressBase         = 0f;
        private float                   _progressSpan         = 1f;
        private int                     _nextIndex            = 0;
        private int                     _doneCount            = 0;
        private int                     _skippedCount         = 0;
        private int                     _failedCount          = 0;
        private int                     _notRenderedCount     = 0;
        private int                     _overBudgetCount      = 0;
        private float                   _maxItemMs            = 0f;
        private float                   _maxItemFrameMs       = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning => _isRunning;

        /****************************************
        *              Unity Event
        ****************************************/

        // 오브젝트가 비활성화되면 코루틴이 함께 죽는다. OnDisable은 파괴 직전에도 불려 여기서 Finish를 부르면
        // 파괴 시 콜백 생략 규칙이 깨지므로 표시만 해 두고, 다시 켜졌을 때 Update가 이어서 처리한다
        private void OnDisable()
        {
            _routineInterrupted = (_isRunning) && (!_hasFinished) && (!gameObject.activeInHierarchy);
        }

        // 코루틴 밖에서 도는 감시다. 코루틴이 try 밖 예외나 비활성화로 죽으면 Finish가 영영 안 불려 부팅이 멈추므로
        // 죽은 것이 확인되거나 전체 제한 시간에 여유를 더한 시간이 지나면 정리하고 Finish를 강제한다
        private void Update()
        {
            if ((!_isRunning) || (_hasFinished) || (_catalog == null))
            {
                return;
            }

            if (_routineInterrupted)
            {
                AbortRun("러너가 비활성화되어 코루틴이 멈췄다");
                return;
            }

            float limit = _catalog.TotalTimeoutSeconds + WATCHDOG_GRACE_SECONDS;
            if ((Time.realtimeSinceStartup - _runStartTime) >= limit)
            {
                AbortRun("제한 시간과 여유를 넘겼다");
            }
        }

        private void OnDestroy()
        {
            ClientPrewarmGate.Unregister(this);

            // 워밍 중 종료는 콜백을 부르지 않는다
            _onFinished = null;
            ReleaseGraphicsStates();
            if (_stage != null)
            {
                Destroy(_stage.gameObject);
            }

            if ((_isRunning) && (_overlay != null))
            {
                _overlay.Hide();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnViewAwake()
        {
            ClientPrewarmGate.Register(this);
        }

        /// <summary> 카탈로그와 카메라를 코드로 지정한다, 씬에 직렬화하지 않고 쓰는 테스트용 </summary>
        public void Configure(PrewarmCatalogSO catalog, Camera camera)
        {
            _catalog = catalog;
            _camera  = camera;
        }

        /// <summary> 워밍을 시작한다, 끝나거나 건너뛰면 onFinished를 정확히 한 번 부른다 </summary>
        public void Run(Action onFinished)
        {
            if (_hasFinished)
            {
                // 한 번 끝난 러너는 다시 돌지 않는다, 콜백이 사라지면 부팅이 멈추므로 바로 부른다
                Debug.LogWarning("[Prewarm] 이미 끝난 러너라 바로 완료로 알린다");
                onFinished?.Invoke();
                return;
            }

            if (_isRunning)
            {
                Debug.LogWarning("[Prewarm] 이미 실행 중이라 무시한다");
                return;
            }

            _onFinished   = onFinished;
            _isRunning    = true;
            _runStartTime = Time.realtimeSinceStartup;

            if (_camera == null)
            {
                // 씬에 주입이 없을 때만 쓰는 1회 폴백
                _camera = Camera.main;
            }

            if ((_catalog == null) || (_camera == null))
            {
                Debug.LogWarning("[Prewarm] 카탈로그나 카메라가 없어 건너뛴다");
                Finish();
                return;
            }

            try
            {
                StartCoroutine(CoRun());
            }
            catch (Exception e)
            {
                // 비활성 오브젝트에서는 StartCoroutine이 던진다
                Debug.LogError($"[Prewarm] 코루틴을 시작하지 못해 건너뛴다: {e.Message}");
                Finish();
            }
        }

        private IEnumerator CoRun()
        {
            _totalWatch.Restart();
            TryCreateOverlay();

            yield return CoShaderPhase();
            yield return CoSpawnPhase();
            Finish();
        }

        // 오버레이가 없어도 워밍은 계속한다, 진행 표시는 ReportProgress가 null을 걸러 낸다
        private void TryCreateOverlay()
        {
            try
            {
                _overlay = (_overlayPrefab != null) ? Instantiate(_overlayPrefab) : LoadingOverlayView.Create();
                _overlay.Show(STAGE_SHADER);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prewarm] 로딩 오버레이를 만들지 못했다: {e.Message}");
            }
        }

        private void ReportProgress(float progress, string stage)
        {
            if (_overlay != null)
            {
                _overlay.SetProgress(progress, stage);
            }
        }

        // 코루틴 밖에서 강제로 끝낸다, 남은 인스턴스와 스테이지를 정리한 뒤 Finish를 정확히 한 번 부른다
        private void AbortRun(string reason)
        {
            Debug.LogWarning($"[Prewarm] 코루틴이 끝나지 않아 강제로 마친다: {reason}");
            StopAllCoroutines();
            _isTimedOut = true;
            DiscardActiveItems();
            ReleaseGraphicsStates();
            Finish();
        }

        private void DiscardActiveItems()
        {
            for (int i = _active.Count - 1; i >= 0; --i)
            {
                ActiveItem item = _active[i];
                StopItem(item);
                ReleaseItem(item);
            }

            _active.Clear();
        }

        private bool IsTimedOut()
        {
            return _isTimedOut || ((Time.realtimeSinceStartup - _runStartTime) >= _catalog.TotalTimeoutSeconds);
        }

        private void Finish()
        {
            if (_hasFinished)
            {
                return;
            }

            _hasFinished = true;
            _isRunning   = false;
            _totalWatch.Stop();
            ReleaseGraphicsStates();

            if (_stage != null)
            {
                Destroy(_stage.gameObject);
                _stage = null;
            }

            int items = (_catalog != null) ? _catalog.Entries.Count : 0;
            Debug.Log($"[Prewarm] done items={items} skipped={_skippedCount} failed={_failedCount} notRendered={_notRenderedCount} " +
                $"totalMs={_totalWatch.ElapsedMilliseconds} maxItemMs={_maxItemMs:F1} maxItemFrameMs={_maxItemFrameMs:F1} " +
                $"overBudget={_overBudgetCount} timedOut={_isTimedOut}");

            // LoadScene은 이 프레임이 끝난 뒤 실행되므로 콜백 전에 씬 로드 대기를 걸어 둬야 새 씬의 첫 화면까지 가려진다
            if (_overlay != null)
            {
                _overlay.HideAfterNextSceneRendered();
            }

            Action callback = _onFinished;
            _onFinished = null;
            callback?.Invoke();
        }

        /****************************************
        *            Shader Phase
        ****************************************/

        private IEnumerator CoShaderPhase()
        {
            string path = ResolveGraphicsStatePath();
            if ((path != null) && (TryLoadGraphicsStates(path, out _states)))
            {
                SetPhaseRange(0f, SHADER_SHARE);
                yield return CoWarmGraphicsStates(_states);

                // 워밍 잡이 컬렉션을 쓰는 중에 파괴하면 안 되므로 끝낸 뒤 해제한다
                ReleaseGraphicsStates();
                SetPhaseRange(SHADER_SHARE, 1f - SHADER_SHARE);
                yield break;
            }

            if (_catalog.VariantCollection != null)
            {
                SetPhaseRange(0f, SHADER_SHARE);
                yield return CoWarmVariants();
                SetPhaseRange(SHADER_SHARE, 1f - SHADER_SHARE);
                yield break;
            }

            SetPhaseRange(0f, 1f);
        }

        private void SetPhaseRange(float progressBase, float progressSpan)
        {
            _progressBase = progressBase;
            _progressSpan = progressSpan;
        }

        private string ResolveGraphicsStatePath()
        {
            string fileName = _catalog.GraphicsStateFileName;
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            return File.Exists(path) ? path : null;
        }

        private bool TryLoadGraphicsStates(string path, out GraphicsStateCollection states)
        {
            states = null;
            try
            {
                var loaded = new GraphicsStateCollection(path);

                // 다른 API나 플랫폼에서 수집한 파일은 맞지 않으므로 쓰지 않는다
                if ((loaded.graphicsDeviceType != SystemInfo.graphicsDeviceType) || (loaded.runtimePlatform != Application.platform))
                {
                    Destroy(loaded);
                    return false;
                }

                states = loaded;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prewarm] 그래픽 상태 파일을 읽지 못했다: {e.Message}");
                return false;
            }
        }

        private IEnumerator CoWarmGraphicsStates(GraphicsStateCollection states)
        {
            int perFrame = Mathf.Max(MIN_PER_FRAME, _catalog.GraphicsStatesPerFrame);
            while (true)
            {
                // 이미 시간이 다 된 채 들어와도 걸리도록 워밍 전에 먼저 본다
                if (CheckShaderTimeout())
                {
                    yield break;
                }

                if (!TryWarmGraphicsStates(states, perFrame, out bool isDone))
                {
                    yield break;
                }

                if (isDone)
                {
                    yield break;
                }

                float total = Mathf.Max(MIN_PER_FRAME, states.totalGraphicsStateCount);
                ReportProgress(_progressBase + (_progressSpan * (states.completedWarmupCount / total)), STAGE_SHADER);
                yield return null;
            }
        }

        private bool TryWarmGraphicsStates(GraphicsStateCollection states, int perFrame, out bool isDone)
        {
            isDone = false;
            try
            {
                JobHandle handle = states.WarmUpProgressively(perFrame, default(JobHandle));
                _warmHandle = JobHandle.CombineDependencies(_warmHandle, handle);
                isDone      = states.isWarmedUp;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prewarm] 그래픽 상태 워밍에 실패했다: {e.Message}");
                return false;
            }
        }

        private IEnumerator CoWarmVariants()
        {
            int perFrame = Mathf.Max(MIN_PER_FRAME, _catalog.VariantsPerFrame);
            while (true)
            {
                if (CheckShaderTimeout())
                {
                    yield break;
                }

                if (!TryWarmVariants(perFrame, out bool isDone))
                {
                    yield break;
                }

                if (isDone)
                {
                    yield break;
                }

                yield return null;
            }
        }

        // 셰이더 단계가 시간 초과로 끝나면 스폰 단계는 남은 항목을 모두 건너뛴다
        private bool CheckShaderTimeout()
        {
            if (!IsTimedOut())
            {
                return false;
            }

            if (!_isTimedOut)
            {
                Debug.LogWarning("[Prewarm] 셰이더 단계에서 타임아웃이 되어 중단한다");
            }

            _isTimedOut = true;
            return true;
        }

        // 잡이 끝나기 전에 컬렉션을 파괴하면 안 된다, 타임아웃과 종료 경로 모두 여기서 마무리한다
        private void ReleaseGraphicsStates()
        {
            _warmHandle.Complete();
            _warmHandle = default(JobHandle);

            if (_states != null)
            {
                Destroy(_states);
                _states = null;
            }
        }

        private bool TryWarmVariants(int perFrame, out bool isDone)
        {
            isDone = false;
            try
            {
                isDone = _catalog.VariantCollection.WarmUpProgressively(perFrame);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prewarm] 셰이더 변형 워밍에 실패했다: {e.Message}");
                return false;
            }
        }

        /****************************************
        *            Spawn Phase
        ****************************************/

        private IEnumerator CoSpawnPhase()
        {
            IReadOnlyList<PrewarmCatalogEntry> entries = _catalog.Entries;
            int slotCount = Mathf.Max(MIN_PER_FRAME, _catalog.MaxConcurrentVisible);
            _slotUsed = new bool[slotCount];
            if (!TryCreateStage())
            {
                SkipRemaining(entries, "스테이지를 만들지 못해");
                yield break;
            }

            ReportProgress(_progressBase, STAGE_SPAWN);
            while ((_nextIndex < entries.Count) || (_active.Count > 0))
            {
                if (_startedItemLastFrame)
                {
                    float frameMs = Time.unscaledDeltaTime * MS_PER_SECOND;
                    _maxItemFrameMs = Mathf.Max(_maxItemFrameMs, frameMs);
                    _startedItemLastFrame = false;
                }

                if (!TryStep(entries, slotCount))
                {
                    yield break;
                }

                float ratio = (entries.Count > 0) ? ((float)_doneCount / entries.Count) : 1f;
                ReportProgress(_progressBase + (_progressSpan * ratio), STAGE_SPAWN);
                yield return null;
            }
        }

        // yield 없는 한 프레임 분량이다, 예기치 못한 예외가 코루틴을 죽이지 않도록 남은 일을 포기하고 끝낸다
        private bool TryStep(IReadOnlyList<PrewarmCatalogEntry> entries, int slotCount)
        {
            try
            {
                TickActive();
                StartPending(entries, slotCount);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prewarm] 스폰 단계 처리 중 예외로 남은 항목을 포기한다: {e.Message}");
                DiscardActiveItems();
                SkipRemaining(entries, "예외로");
                return false;
            }
        }

        private bool TryCreateStage()
        {
            try
            {
                var stageObject = new GameObject(STAGE_ROOT_NAME);
                _stage = stageObject.transform;

                // 컬링으로 빠지면 워밍이 안 되므로 카메라 앞 절두체 안에 둔다
                Transform cam = _camera.transform;
                _stage.SetPositionAndRotation(cam.position + (cam.forward * _catalog.StageDistance), cam.rotation);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prewarm] 스테이지를 만들지 못했다: {e.Message}");
                return false;
            }
        }

        // 남은 항목을 전부 건너뛴 것으로 세고 더 시작하지 않게 한다
        private void SkipRemaining(IReadOnlyList<PrewarmCatalogEntry> entries, string reason)
        {
            int remaining = entries.Count - _nextIndex;
            if (remaining > 0)
            {
                _skippedCount += remaining;
                Debug.LogWarning($"[Prewarm] {reason} 남은 {remaining}개를 건너뛴다");
            }

            _nextIndex = entries.Count;
        }

        private void StartPending(IReadOnlyList<PrewarmCatalogEntry> entries, int slotCount)
        {
            if ((_nextIndex < entries.Count) && (IsTimedOut()))
            {
                // 타임아웃은 새 항목을 시작하지 않는 기준이다, 이미 켠 항목은 끝까지 간다.
                // 셰이더 단계에서 이미 시간 초과로 들어온 경우도 같은 길로 남은 항목을 모두 정리한다
                _isTimedOut = true;
                SkipRemaining(entries, "타임아웃으로");
                return;
            }

            _frameWatch.Restart();
            bool isFirst = true;
            while ((_nextIndex < entries.Count) && (_active.Count < slotCount) && (!_isTimedOut))
            {
                // 한 프레임에 최소 한 개는 시작해 예산이 작아도 진행이 멈추지 않게 한다
                if ((!isFirst) && (_frameWatch.Elapsed.TotalMilliseconds >= _catalog.FrameBudgetMs))
                {
                    break;
                }

                isFirst = false;
                PrewarmCatalogEntry entry = entries[_nextIndex];
                ++_nextIndex;
                StartItem(entry);
                _startedItemLastFrame = true;

                if (IsTimedOut())
                {
                    _isTimedOut = true;
                    SkipRemaining(entries, "타임아웃으로");
                }
            }
        }

        private int AcquireSlot()
        {
            for (int i = 0; i < _slotUsed.Length; ++i)
            {
                if (!_slotUsed[i])
                {
                    _slotUsed[i] = true;
                    return i;
                }
            }

            return -1;
        }

        private Vector3 SlotPosition(int slot)
        {
            int columns = Mathf.Min(GRID_COLUMNS, _slotUsed.Length);
            int col     = slot % columns;
            int row     = slot / columns;
            float x     = (col - ((columns - 1) * CENTER_HALF)) * _catalog.StageSpacing;
            float y     = row * _catalog.StageSpacing;
            return new Vector3(x, y, 0f);
        }

        private void StartItem(PrewarmCatalogEntry entry)
        {
            if ((entry == null) || (entry.Prefab == null))
            {
                Debug.LogWarning("[Prewarm] 프리팹이 비어 있는 항목을 건너뛴다");
                ++_skippedCount;
                ++_doneCount;
                return;
            }

            int slot = AcquireSlot();
            _itemWatch.Restart();
            var item = new ActiveItem { Entry = entry, Slot = slot };
            try
            {
                if (!BeginItem(item))
                {
                    ReleaseItem(item);
                    ++_skippedCount;
                    ++_doneCount;
                    return;
                }
            }
            catch (Exception e)
            {
                ReportFailure(entry, e);
                ReleaseItem(item);
                ++_failedCount;
                ++_doneCount;
                return;
            }
            finally
            {
                RecordItemMs((float)_itemWatch.Elapsed.TotalMilliseconds);
            }

            _active.Add(item);
        }

        private void RecordItemMs(float ms)
        {
            _maxItemMs = Mathf.Max(_maxItemMs, ms);
            if (ms > _catalog.FrameBudgetMs)
            {
                ++_overBudgetCount;
            }
        }

        // false면 건너뛴 항목이다
        private bool BeginItem(ActiveItem item)
        {
            PrewarmCatalogEntry entry = item.Entry;
            GameObject instance = Instantiate(entry.Prefab, _stage, false);
            item.Instance = instance;
            instance.transform.localPosition = SlotPosition(item.Slot);
            instance.SetActive(true);

            item.StartTime     = Time.realtimeSinceStartup;
            item.FramesLeft    = Mathf.Max(MIN_PER_FRAME, entry.VisibleFrames);
            item.Renderers     = instance.GetComponentsInChildren<Renderer>();
            item.RenderedFlags = new bool[item.Renderers.Length];

            switch (entry.Kind)
            {
                case EPrewarmKind.Particle:
                    BeginParticle(item);
                    return true;
                case EPrewarmKind.Scripted:
                    if (!instance.TryGetComponent(out IPrewarmTarget target))
                    {
                        Debug.LogWarning($"[Prewarm] IPrewarmTarget이 없어 건너뛴다: {entry.Prefab.name}");
                        return false;
                    }

                    item.Target = target;
                    item.ScriptedDuration = MaxSample(entry.SampleSeconds);
                    target.BeginPrewarm();
                    return true;
                case EPrewarmKind.Model:
                    if (instance.TryGetComponent(out Animator animator))
                    {
                        animator.Update(0f);
                    }

                    return true;
                default:
                    return false;
            }
        }

        private static float MaxSample(float[] samples)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; ++i)
            {
                max = Mathf.Max(max, samples[i]);
            }

            return max;
        }

        private void BeginParticle(ActiveItem item)
        {
            ParticleSystem[] all = item.Instance.GetComponentsInChildren<ParticleSystem>();
            var roots = new List<ParticleSystem>(all.Length);
            for (int i = 0; i < all.Length; ++i)
            {
                Transform parent = all[i].transform.parent;
                if ((parent == null) || (parent.GetComponentInParent<ParticleSystem>() == null))
                {
                    roots.Add(all[i]);
                }
            }

            item.Roots = roots.ToArray();
            for (int i = 0; i < item.Roots.Length; ++i)
            {
                item.Roots[i].Play(true);
            }

            if (item.Entry.MoveTrail)
            {
                item.Trails = item.Instance.GetComponentsInChildren<TrailRenderer>();
                for (int i = 0; i < item.Trails.Length; ++i)
                {
                    item.Trails[i].emitting = true;
                }
            }

            float[] samples = item.Entry.SampleSeconds;
            if (samples.Length > 0)
            {
                SimulateRoots(item, samples[0]);
            }
        }

        // withChildren로 한 번에 돌리므로 루트에만 부른다, restart를 켜야 이전 시점에서 이어 쌓이지 않는다
        private static void SimulateRoots(ActiveItem item, float seconds)
        {
            for (int i = 0; i < item.Roots.Length; ++i)
            {
                item.Roots[i].Simulate(seconds, true, true);
            }
        }

        private void TickActive()
        {
            for (int i = _active.Count - 1; i >= 0; --i)
            {
                ActiveItem item = _active[i];
                bool isFinished;
                try
                {
                    isFinished = TickItem(item);
                }
                catch (Exception e)
                {
                    ReportFailure(item.Entry, e);
                    _active.RemoveAt(i);
                    ReleaseItem(item);
                    ++_failedCount;
                    ++_doneCount;
                    continue;
                }

                if (!isFinished)
                {
                    continue;
                }

                _active.RemoveAt(i);
                EndItem(item);
            }
        }

        // 끝났으면 true
        private bool TickItem(ActiveItem item)
        {
            MarkRendered(item);
            float elapsed = Time.realtimeSinceStartup - item.StartTime;

            switch (item.Entry.Kind)
            {
                case EPrewarmKind.Particle:
                    MoveTrails(item, elapsed);
                    return TickParticle(item);
                case EPrewarmKind.Scripted:
                    return TickScripted(item, elapsed);
                default:
                    --item.FramesLeft;
                    return item.FramesLeft <= 0;
            }
        }

        private static bool TickParticle(ActiveItem item)
        {
            --item.FramesLeft;
            if (item.FramesLeft > 0)
            {
                return false;
            }

            float[] samples = item.Entry.SampleSeconds;
            ++item.PhaseIndex;
            if (item.PhaseIndex >= samples.Length)
            {
                return true;
            }

            SimulateRoots(item, samples[item.PhaseIndex]);
            item.FramesLeft = Mathf.Max(MIN_PER_FRAME, item.Entry.VisibleFrames);
            return false;
        }

        private static bool TickScripted(ActiveItem item, float elapsed)
        {
            item.Target.TickPrewarm(elapsed);
            if ((!item.IsHolding) && (elapsed >= item.ScriptedDuration))
            {
                item.IsHolding = true;
            }

            if (!item.IsHolding)
            {
                return false;
            }

            --item.FramesLeft;
            return item.FramesLeft <= 0;
        }

        private void MoveTrails(ActiveItem item, float elapsed)
        {
            if (item.Trails == null)
            {
                return;
            }

            float sway = Mathf.Sin(elapsed * TRAIL_SWAY_SPEED) * _catalog.StageSpacing * TRAIL_SWAY_RATIO;
            item.Instance.transform.localPosition = SlotPosition(item.Slot) + new Vector3(sway, 0f, 0f);
        }

        private static void MarkRendered(ActiveItem item)
        {
            for (int i = 0; i < item.Renderers.Length; ++i)
            {
                Renderer renderer = item.Renderers[i];
                if ((renderer != null) && (renderer.isVisible))
                {
                    item.RenderedFlags[i] = true;
                }
            }
        }

        private void EndItem(ActiveItem item)
        {
            try
            {
                if (HasUnrenderedRenderer(item))
                {
                    ++_notRenderedCount;
                    Debug.LogWarning($"[Prewarm] not rendered {item.Entry.Prefab.name}");
                }
            }
            catch (Exception e)
            {
                ReportFailure(item.Entry, e);
            }

            if (!StopItem(item))
            {
                ++_failedCount;
            }

            ReleaseItem(item);
            ++_doneCount;
        }

        // 파티클과 타깃을 멈춘다, 예외는 삼키고 실패했으면 false
        private bool StopItem(ActiveItem item)
        {
            try
            {
                if (item.Roots != null)
                {
                    for (int i = 0; i < item.Roots.Length; ++i)
                    {
                        item.Roots[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }

                item.Target?.EndPrewarm();
                return true;
            }
            catch (Exception e)
            {
                ReportFailure(item.Entry, e);
                return false;
            }
        }

        private static bool HasUnrenderedRenderer(ActiveItem item)
        {
            for (int i = 0; i < item.Renderers.Length; ++i)
            {
                Renderer renderer = item.Renderers[i];
                if ((renderer != null) && (renderer.enabled) && (!item.RenderedFlags[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReleaseItem(ActiveItem item)
        {
            if ((_slotUsed != null) && (item.Slot >= 0) && (item.Slot < _slotUsed.Length))
            {
                _slotUsed[item.Slot] = false;
            }

            if (item.Instance != null)
            {
                Destroy(item.Instance);
            }
        }

        private static void ReportFailure(PrewarmCatalogEntry entry, Exception e)
        {
            string prefabName = (entry.Prefab != null) ? entry.Prefab.name : "(null)";
            string message = $"[Prewarm] failed {prefabName}: {e.Message}";
            if (entry.Required)
            {
                Debug.LogError(message);
                return;
            }

            Debug.LogWarning(message);
        }

        /****************************************
        *               Nested
        ****************************************/

        private sealed class ActiveItem
        {
            public PrewarmCatalogEntry Entry            { get; set; }
            public GameObject          Instance         { get; set; }
            public int                 Slot             { get; set; }
            public float               StartTime        { get; set; }
            public int                 FramesLeft       { get; set; }
            public int                 PhaseIndex       { get; set; }
            public float               ScriptedDuration { get; set; }
            public bool                IsHolding        { get; set; }
            public ParticleSystem[]    Roots            { get; set; }
            public TrailRenderer[]     Trails           { get; set; }
            public Renderer[]          Renderers        { get; set; }
            public bool[]              RenderedFlags    { get; set; }
            public IPrewarmTarget      Target           { get; set; }
        }
    }
}
