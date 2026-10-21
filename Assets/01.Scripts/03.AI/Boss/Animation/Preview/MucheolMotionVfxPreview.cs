#if UNITY_EDITOR
// Unity
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    /// <summary> 무철 모션과 VFX의 로컬 비교 도구. 전투 판정이나 서버 시계를 생성하지 않는다. </summary>
    public sealed class MucheolMotionVfxPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_SPEED = 0.1f;
        private const float MAX_SPEED = 2f;
        private const float MAX_DELAY = 5f;
        private const float PANEL_WIDTH = 330f;
        private const float PANEL_MARGIN = 12f;
        private const float LIST_HEIGHT = 210f;
        private const float DEFAULT_DURATION = 6f;
        private static readonly string[] G_VfxLabels = { "01", "02", "03", "04", "05", "06", "07" };

        [SerializeField] private Animator _animator;
        [SerializeField] private AnimationClip _idle;
        [SerializeField] private AnimationClip[] _clips;
        [SerializeField] private GameObject[] _vfxRoots;
        [SerializeField] private MucheolBatchVfxPlayer[] _batchPlayers;
        [SerializeField] private MucheolImpactVfxPlayer[] _impactPlayers;
        [SerializeField] private Camera _camera;
        [SerializeField, Min(MIN_SPEED)] private float _speed = 1f;
        [SerializeField, Min(0f)] private float _motionDelay;
        [SerializeField, Min(0f)] private float _vfxDelay;
        [SerializeField, Min(1f)] private float _duration = DEFAULT_DURATION;
        [SerializeField] private bool _repeat;
        [SerializeField] private Vector3 _vfxOffset = new Vector3(0f, 0.03f, 3f);

        private PlayableGraph _graph;
        private AnimationClipPlayable _playable;
        private AnimationClip _activeClip;
        private Vector3 _origin;
        private Quaternion _rotation;
        private Vector3 _cameraOrigin;
        private Quaternion _cameraRotation;
        private Vector2 _scroll;
        private Vector2 _panelScroll;
        private int _clipIndex;
        private int _vfxIndex;
        private float _elapsed;
        private float _idleElapsed;
        private bool _running;
        private bool _paused;
        private bool _motionEnabled;
        private bool _vfxEnabled;
        private string _status = "준비";

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            _origin = _animator.transform.localPosition;
            _rotation = _animator.transform.localRotation;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _cameraOrigin = _camera.transform.position;
            _cameraRotation = _camera.transform.rotation;
            StopPreview();
        }

        private void Update()
        {
            if (!_running)
            {
                _idleElapsed += Time.unscaledDeltaTime;
                SampleMotion(_idle, _idleElapsed, true);
                return;
            }
            if (_paused)
            {
                return;
            }
            _elapsed += Time.unscaledDeltaTime * _speed;
            if (_elapsed >= GetDuration())
            {
                if (_repeat)
                {
                    PlayPreview(_motionEnabled, _vfxEnabled);
                }
                else
                {
                    StopPreview();
                }
                return;
            }
            Sample();
        }

        private void OnDisable()
        {
            StopEffects();
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _activeClip = null;
            _running = false;
        }

        private void OnGUI()
        {
            float panelWidth = Mathf.Min(PANEL_WIDTH, Screen.width * 0.36f);
            float viewportStart = (panelWidth + PANEL_MARGIN * 2f) / Screen.width;
            _camera.rect = new Rect(viewportStart, 0f, 1f - viewportStart, 1f);
            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, panelWidth + PANEL_MARGIN * 2f, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUILayout.BeginArea(new Rect(PANEL_MARGIN, PANEL_MARGIN, panelWidth,
                Screen.height - PANEL_MARGIN * 2f), GUI.skin.box);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            GUILayout.Label("무철 · 모션 / VFX 프리뷰");
            GUILayout.Label("로컬 연출 비교 전용 · 패턴 배정 미확정");
            GUILayout.Label(_status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("모션만"))
            {
                PlayPreview(true, false);
            }
            if (GUILayout.Button("VFX만"))
            {
                PlayPreview(false, true);
            }
            if (GUILayout.Button("함께 재생"))
            {
                PlayPreview(true, true);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_paused ? "계속" : "일시정지") && _running)
            {
                _paused = !_paused;
            }
            if (GUILayout.Button("정지 / 초기화"))
            {
                StopPreview();
            }
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(LIST_HEIGHT, Screen.height * 0.28f)));
            for (int i = 0; i < _clips.Length; ++i)
            {
                if (_clips[i] == null)
                {
                    continue;
                }
                if (GUILayout.Toggle(_clipIndex == i, _clips[i].name, GUI.skin.button, GUILayout.Width(panelWidth - PANEL_MARGIN * 4f)) && _clipIndex != i)
                {
                    StopPreview();
                    _clipIndex = i;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label("VFX 선택 (번호는 기존 에셋 번호)");
            int selectedVfx = GUILayout.SelectionGrid(_vfxIndex, G_VfxLabels, 4);
            if (selectedVfx != _vfxIndex)
            {
                StopPreview();
                _vfxIndex = selectedVfx;
            }
            GUILayout.Label("공통 재생 속도: " + _speed.ToString("F2") + "x");
            _speed = GUILayout.HorizontalSlider(_speed, MIN_SPEED, MAX_SPEED);
            GUILayout.Label("모션 시작 지연: " + _motionDelay.ToString("F2") + "초");
            _motionDelay = GUILayout.HorizontalSlider(_motionDelay, 0f, MAX_DELAY);
            GUILayout.Label("VFX 시작 지연: " + _vfxDelay.ToString("F2") + "초");
            _vfxDelay = GUILayout.HorizontalSlider(_vfxDelay, 0f, MAX_DELAY);
            _repeat = GUILayout.Toggle(_repeat, "반복 재생");
            GUILayout.Label("재생 위치: " + _elapsed.ToString("F2") + "초");
            GUI.enabled = _running;
            float position = GUILayout.HorizontalSlider(_elapsed, 0f, GetDuration());
            if (!Mathf.Approximately(position, _elapsed))
            {
                _elapsed = position;
                _paused = true;
                Sample();
            }
            GUI.enabled = true;
            if (GUILayout.Button("카메라 원위치"))
            {
                _camera.transform.SetPositionAndRotation(_cameraOrigin, _cameraRotation);
            }
            GUILayout.Label("카메라: WASD / Shift 빠르게\nVFX 위치·재생 길이: Inspector에서 조절");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void PlayPreview(bool motion, bool vfx)
        {
            StopPreview();
            _motionEnabled = motion;
            _vfxEnabled = vfx;
            _running = true;
            _status = motion ? "모션 재생 중" : "VFX 재생 중";
            Sample();
        }

        public void StopPreview()
        {
            _running = false;
            _paused = false;
            _elapsed = 0f;
            _idleElapsed = 0f;
            StopEffects();
            if (_animator != null)
            {
                SampleMotion(_idle, 0f, true);
            }
            _status = "준비 · 정지 시 Idle 복원";
        }

        private void StopEffects()
        {
            for (int i = 0; i < _vfxRoots.Length; ++i)
            {
                if (_batchPlayers[i] != null)
                {
                    _batchPlayers[i].Stop();
                }
                if (_impactPlayers[i] != null)
                {
                    _impactPlayers[i].Stop();
                }
                _vfxRoots[i].SetActive(false);
            }
        }

        private void Sample()
        {
            AnimationClip clip = _motionEnabled ? _clips[_clipIndex] : _idle;
            float motionTime = _elapsed - _motionDelay;
            SampleMotion(motionTime < 0f ? _idle : clip, Mathf.Max(0f, motionTime), !_motionEnabled);
            GameObject root = _vfxRoots[_vfxIndex];
            bool show = _vfxEnabled && _elapsed >= _vfxDelay;
            root.SetActive(show);
            if (!show)
            {
                return;
            }
            root.transform.localPosition = _vfxOffset;
            float vfxTime = _elapsed - _vfxDelay;
            if (_batchPlayers[_vfxIndex] != null)
            {
                _batchPlayers[_vfxIndex].SamplePreview(vfxTime);
            }
            else if (_impactPlayers[_vfxIndex] != null)
            {
                _impactPlayers[_vfxIndex].SamplePreview(vfxTime);
            }
        }

        private void SampleMotion(AnimationClip clip, float time, bool loop)
        {
            if (clip == null || _animator == null)
            {
                return;
            }
            if (!_graph.IsValid() || _activeClip != clip)
            {
                if (_graph.IsValid())
                {
                    _graph.Destroy();
                }
                _graph = PlayableGraph.Create("Mucheol local preview");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                _playable = AnimationClipPlayable.Create(_graph, clip);
                _playable.SetApplyFootIK(false);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Motion", _animator);
                output.SetSourcePlayable(_playable);
                _graph.Play();
                _activeClip = clip;
            }
            float sampleTime = loop && clip.length > 0f ? time % clip.length : Mathf.Min(time, clip.length);
            _playable.SetTime(sampleTime);
            _graph.Evaluate(0f);
            _animator.transform.localPosition = _origin;
            _animator.transform.localRotation = _rotation;
        }

        private float GetDuration()
        {
            float motion = _motionEnabled && _clips[_clipIndex] != null
                ? _motionDelay + _clips[_clipIndex].length : 0f;
            float effect = _vfxEnabled && _batchPlayers[_vfxIndex] != null
                ? _vfxDelay + _batchPlayers[_vfxIndex].PreviewDuration : _vfxDelay + _duration;
            return Mathf.Max(_duration, Mathf.Max(motion, effect));
        }
    }
}
#endif
