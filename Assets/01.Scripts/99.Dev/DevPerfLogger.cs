#if SHINSHI_DEV_TEST
// System
using System;
using System.Text;

// Unity
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Dev
{
    // 시험 빌드(windows-devtest)에서만 컴파일된다. 씬별 프레임 비용을 Player.log에 남겨 병목이 CPU인지 GPU인지,
    // 그리기 호출과 그림자 수가 얼마인지 프로파일러 연결 없이 비교하려고 둔다. 통계 카운터는 개발 빌드에서만 값이 나온다
    /// <summary> 시험 빌드의 주기적 프레임 비용 기록 </summary>
    public sealed class DevPerfLogger : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  LOG_INTERVAL_SECONDS = 2f;
        private const float  SETTLE_SECONDS       = 3f;
        private const float  VIEW_SETTLE_SECONDS  = 2f;
        private const float  P95_RATIO            = 0.95f;
        private const float  SECOND_TO_MILLI      = 1000f;
        private const double NANO_TO_MILLI        = 1e-6;
        private const double BYTE_TO_KILO         = 1.0 / 1024.0;
        private const int    MAX_FRAMES           = 1024;
        private const double MAX_VALID_GPU_MS     = 1000.0;
        private const float  HITCH_SECONDS        = 0.1f;

        private readonly StringBuilder _builder = new StringBuilder(256);
        private readonly float[]       _frames  = new float[MAX_FRAMES];

        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _renderThread;
        private ProfilerRecorder _gpu;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _setPass;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _triangles;
        private ProfilerRecorder _shadowCasters;
        private ProfilerRecorder _gcAlloc;

        private int    _frameCount       = 0;
        private float  _nextLogTime      = 0f;
        private float  _collectStartTime = 0f;
        private double _mainSum          = 0.0;
        private double _renderSum        = 0.0;
        private double _gpuSum           = 0.0;
        private int    _gpuCount         = 0;
        private double _gcSum            = 0.0;
        private string _sceneName        = string.Empty;
        private int    _viewVersion      = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var host = new GameObject(nameof(DevPerfLogger));
            DontDestroyOnLoad(host);
            host.AddComponent<DevPerfLogger>();
        }

        private void OnEnable()
        {
            _mainThread    = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            _renderThread  = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
            _gpu           = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");
            _batches       = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Total Batches Count");
            _setPass       = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _drawCalls     = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _triangles     = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _gcAlloc       = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

            SceneManager.sceneLoaded += OnSceneLoaded;
            ResetWindow(SceneManager.GetActiveScene().name, SETTLE_SECONDS);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _mainThread.Dispose();
            _renderThread.Dispose();
            _gpu.Dispose();
            _batches.Dispose();
            _setPass.Dispose();
            _drawCalls.Dispose();
            _triangles.Dispose();
            _shadowCasters.Dispose();
            _gcAlloc.Dispose();
        }

        private void Update()
        {
            // 평균에 묻히는 긴 멈춤은 따로 남겨 언제(어느 시점 전환 직후) 생기는지 본다
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime > HITCH_SECONDS)
            {
                Debug.Log("[Hitch] " + _sceneName + " " + DevTownPerfViews.Label + " frame=" + Time.frameCount
                    + " ms=" + (deltaTime * SECOND_TO_MILLI).ToString("F0"));
            }

            // 마을 측정 시점이 바뀌면 이전 시점 값이 섞이지 않게 구간을 새로 연다
            if (_viewVersion != DevTownPerfViews.Version)
            {
                _viewVersion = DevTownPerfViews.Version;
                ResetWindow(_sceneName, VIEW_SETTLE_SECONDS);
                return;
            }

            // 안정화 구간의 프레임은 표본에 넣지 않는다. 긴 멈춤은 위의 [Hitch]로 따로 남는다
            if (Time.unscaledTime < _collectStartTime)
            {
                return;
            }

            // 모든 합계와 분모가 같은 표본을 쓰도록 버퍼가 차면 구간을 닫는다
            _frames[_frameCount] = deltaTime;
            ++_frameCount;

            _mainSum   += _mainThread.LastValue * NANO_TO_MILLI;
            _renderSum += _renderThread.LastValue * NANO_TO_MILLI;
            _gcSum     += _gcAlloc.LastValue * BYTE_TO_KILO;

            // GPU 시간은 아직 결과가 오지 않은 프레임에 잘못된 값을 돌려줄 때가 있어 범위 밖 값은 뺀다
            double gpuMs = _gpu.LastValue * NANO_TO_MILLI;
            if ((gpuMs > 0.0) && (gpuMs < MAX_VALID_GPU_MS))
            {
                _gpuSum += gpuMs;
                ++_gpuCount;
            }

            if ((_frameCount >= MAX_FRAMES) || (Time.unscaledTime >= _nextLogTime))
            {
                WriteLog();
                ResetWindow(_sceneName, 0f);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 씬을 막 연 직후는 로딩, 셰이더 준비 비용이 섞이므로 안정화 구간을 두고 그 뒤부터 잰다
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResetWindow(scene.name, SETTLE_SECONDS);
        }

        private void ResetWindow(string sceneName, float wait)
        {
            _sceneName   = sceneName;
            _frameCount  = 0;
            _mainSum     = 0.0;
            _renderSum   = 0.0;
            _gpuSum      = 0.0;
            _gpuCount    = 0;
            _gcSum       = 0.0;
            // 씬 진입 대기 중에 시점이 바뀌어도 더 늦은 대기 종료 시각을 지킨다
            _collectStartTime = Mathf.Max(_collectStartTime, Time.unscaledTime + wait);
            _nextLogTime      = _collectStartTime + LOG_INTERVAL_SECONDS;
        }

        private void WriteLog()
        {
            if (_frameCount == 0)
            {
                return;
            }

            Array.Sort(_frames, 0, _frameCount);
            float sum = 0f;
            for (int i = 0; i < _frameCount; ++i)
            {
                sum += _frames[i];
            }

            double count = _frameCount;
            float  avgMs = (sum / _frameCount) * SECOND_TO_MILLI;
            float  p95Ms = _frames[Mathf.Min(_frameCount - 1, (int)(_frameCount * P95_RATIO))] * SECOND_TO_MILLI;
            float  maxMs = _frames[_frameCount - 1] * SECOND_TO_MILLI;

            _builder.Clear();
            _builder.Append("[Perf] ").Append(_sceneName).Append(' ').Append(DevTownPerfViews.Label)
                .Append(" frames=").Append(_frameCount)
                .Append(" avgMs=").Append(avgMs.ToString("F2"))
                .Append(" p95Ms=").Append(p95Ms.ToString("F2"))
                .Append(" maxMs=").Append(maxMs.ToString("F2"))
                .Append(" cpuMainMs=").Append((_mainSum / count).ToString("F2"))
                .Append(" cpuRenderMs=").Append((_renderSum / count).ToString("F2"))
                .Append(" gpuMs=").Append((_gpuCount > 0) ? (_gpuSum / _gpuCount).ToString("F2") : "-")
                .Append(" batches=").Append(_batches.LastValue)
                .Append(" setPass=").Append(_setPass.LastValue)
                .Append(" drawCalls=").Append(_drawCalls.LastValue)
                .Append(" tris=").Append(_triangles.LastValue)
                .Append(" shadowCasters=").Append(_shadowCasters.LastValue)
                .Append(" gcKBPerFrame=").Append((_gcSum / count).ToString("F2"))
                .Append(" vSync=").Append(QualitySettings.vSyncCount)
                .Append(" res=").Append(Screen.width).Append('x').Append(Screen.height);
            Debug.Log(_builder.ToString());
        }
    }
}
#endif
