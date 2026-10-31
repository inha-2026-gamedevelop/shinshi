#if UNITY_EDITOR
// System
using System;
using System.Text;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.AI
{
    /// <summary> F9를 누르면 일정 시간 프레임 시간을 모아 평균 FPS, p95, p99, 최대를 콘솔에 남기는 에디터 전용 측정 장치 </summary>
    public sealed class PrologueFrameRecorder : MonoBehaviour
    {
        // 에디터 Play의 수치라 빌드보다 느리게 나온다. 같은 PC, 같은 해상도에서 장면끼리 비교하는 용도이지 합격 판정의 근거가 아니다.
        // 첫 프레임들은 로딩과 셰이더 준비로 튀므로 시작 후 잠시 건너뛰고 재기 시작한다.
        // 기준은 맵 성능 문서의 클라이언트 목표(평균 60 FPS, p95 25ms 이하)에서 가져왔고 p99와 최대는 보조 지표다

        /****************************************
        *                Fields
        ****************************************/

        private const string  MEASURED_SCENE_PROLOGUE = "Dev_PrologueMap";
        private const string  MEASURED_SCENE_FOREST   = "DungeonForest";
        private const KeyCode RECORD_KEY       = KeyCode.F9;
        private const float   RECORD_SECONDS   = 30f;
        private const float   WARMUP_SECONDS   = 2f;
        private const int     MAX_SAMPLES      = 30000;
        private const float   MS_PER_SECOND    = 1000f;
        private const float   P95              = 0.95f;
        private const float   P99              = 0.99f;
        private const float   TARGET_P95_MS    = 25f;
        private const int     GUI_FONT_SIZE    = 18;
        private const float   LABEL_WIDTH      = 420f;
        private const float   LABEL_HEIGHT     = 32f;
        private const float   LABEL_MARGIN     = 16f;

        private readonly float[] _samples = new float[MAX_SAMPLES];

        private bool     _recording = false;
        private float    _elapsed   = 0f;
        private int      _count     = 0;
        private string   _label     = null;
        private GUIStyle _style     = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((!_recording) && (Input.GetKeyDown(RECORD_KEY)))
            {
                _recording = true;
                _elapsed   = 0f;
                _count     = 0;
            }

            if (!_recording)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;

            if (_elapsed < WARMUP_SECONDS)
            {
                _label = "측정 준비 중";
                return;
            }

            if ((_count < MAX_SAMPLES) && (dt > 0f))
            {
                _samples[_count++] = dt;
            }

            _label = $"측정 중 {Mathf.Min(_elapsed - WARMUP_SECONDS, RECORD_SECONDS):F0}/{RECORD_SECONDS:F0}초";

            if ((_elapsed - WARMUP_SECONDS) >= RECORD_SECONDS)
            {
                Finish();
            }
        }

        private void OnGUI()
        {
            if (!_recording)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    font     = Font.CreateDynamicFontFromOSFont("Malgun Gothic", GUI_FONT_SIZE),
                    fontSize = GUI_FONT_SIZE
                };
                _style.normal.textColor = Color.yellow;
            }

            GUI.Label(new Rect(Screen.width - LABEL_WIDTH - LABEL_MARGIN, LABEL_MARGIN, LABEL_WIDTH, LABEL_HEIGHT), _label, _style);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 이 컴포넌트가 있는 개체에서 F9를 누르면 기록하며, 이 도구는 에디터 Play 때만 있다
        private void Finish()
        {
            _recording = false;
            if (_count == 0)
            {
                Debug.LogWarning("[PrologueFrame] 기록된 프레임이 없다");
                return;
            }

            Array.Sort(_samples, 0, _count);

            double sum = 0.0;
            for (int i = 0; i < _count; ++i)
            {
                sum += _samples[i];
            }

            float avgMs = (float)(sum / _count) * MS_PER_SECOND;
            float p95   = Percentile(P95) * MS_PER_SECOND;
            float p99   = Percentile(P99) * MS_PER_SECOND;
            float maxMs = _samples[_count - 1] * MS_PER_SECOND;

            var report = new StringBuilder();
            report.Append($"[PrologueFrame] {_count}프레임, 해상도 {Screen.width}x{Screen.height}, 품질 {QualitySettings.names[QualitySettings.GetQualityLevel()]}, ");
            report.Append($"평균 {avgMs:F1}ms ({MS_PER_SECOND / avgMs:F1} FPS), p95 {p95:F1}ms, p99 {p99:F1}ms, 최대 {maxMs:F1}ms, ");
            report.Append(p95 <= TARGET_P95_MS ? "p95 목표(25ms) 이내" : "p95 목표(25ms) 초과");
            Debug.Log(report.ToString());
        }

        private float Percentile(float fraction)
        {
            int index = Mathf.Clamp(Mathf.CeilToInt(_count * fraction) - 1, 0, _count - 1);
            return _samples[index];
        }

        /****************************************
        *                 Boot
        ****************************************/

        // 비교에 쓰는 두 씬에서만 붙는다. 다른 팀원의 Play에는 아무것도 붙이지 않으려는 것이다. 씬 파일에는 직렬화하지 않는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if ((sceneName != MEASURED_SCENE_PROLOGUE) && (sceneName != MEASURED_SCENE_FOREST))
            {
                return;
            }

            new GameObject("PrologueFrameRecorder").AddComponent<PrologueFrameRecorder>();
        }
    }
}
#endif
