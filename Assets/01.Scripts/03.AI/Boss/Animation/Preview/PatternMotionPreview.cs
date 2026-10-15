// System
using System;
using System.Collections;
using System.Text;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Shinshi.AI
{
    // 서버 판정 시각에 클립에서 가장 힘이 실리는 순간이 오도록 모션을 늦게 시작하거나 속도를 맞춰 재생한다
    // 판정 시각마다 화면이 잠깐 붉게 번쩍이므로 모션의 힘 순간과 비교해 볼 수 있다
    // 연사는 shots가 2 이상이고, 쓸기처럼 짧은 구간은 slow 값으로 늘리며, 팔을 뻗은 자세는 hold 값으로 유지한다
    /// <summary> 패턴별 모션을 서버 판정 시각에 맞춰 재생해 보는 확인용 도구 </summary>
    public sealed class PatternMotionPreview : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string IDLE_STATE = "Idle";

        private const float PANEL_WIDTH   = 250f;
        private const float BUTTON_WIDTH  = 240f;
        private const float BUTTON_HEIGHT = 24f;
        private const float BUTTON_STEP   = 26f;
        private const float TITLE_HEIGHT  = 22f;
        private const float TITLE_STEP    = 24f;
        private const float START_Y       = 190f;
        private const float STATUS_SHIFT  = 120f;
        private const float STATUS_WIDTH  = 360f;
        private const float STATUS_HEIGHT = 70f;
        private const float STATUS_GAP    = 6f;
        private const float BAR_Y         = 80f;
        private const float BAR_WIDTH     = 220f;
        private const float BAR_HEIGHT    = 10f;
        private const float MARK_SHIFT    = 6f;
        private const float MARK_WIDTH    = 120f;
        private const float MARK_HEIGHT   = 20f;
        private const float MARK_Y        = 10f;

        private const float RESET_BLEND_SECONDS  = 0.05f;
        private const float CLIP_ENTER_SECONDS   = 0.05f;
        private const float FINISH_BLEND_SECONDS = 0.25f;
        private const float MIN_SPEED            = 0.01f;

        private const string STATUS_FORMAT_DELAY  = "모션 시작 {0:F2}초 뒤, 재생 속도 {1:F2}배";
        private const string STATUS_FORMAT_STRIKE = "판정 {0:F2}초";
        private const string STATUS_FORMAT_SHOTS  = " x{0} (간격 {1:F2}초)";
        private const string STATUS_FORMAT_RECOV  = ", 회복 {0:F2}초";
        private const string TITLE                = "패턴 타이밍 맞춤 재생";
        private const string MARK_TEXT            = "^";

        private static readonly Color FLASH_COLOR = new Color(1f, 0.1f, 0.1f, 0.35f);

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 패턴 하나의 서버 시간과 클립 시간 설정 </summary>
        [Serializable]
        public sealed class Entry
        {
            [FormerlySerializedAs("label")]
            [SerializeField] private string _label;

            [FormerlySerializedAs("stateName")]
            [SerializeField] private string _stateName;

            [FormerlySerializedAs("strikeSeconds")]
            [SerializeField] private float _strikeSeconds;

            [FormerlySerializedAs("clipPeakSeconds")]
            [SerializeField] private float _clipPeakSeconds;

            [FormerlySerializedAs("recoverySeconds")]
            [SerializeField] private float _recoverySeconds;

            [FormerlySerializedAs("shots")]
            [SerializeField] private int _shots = 1;

            [FormerlySerializedAs("shotInterval")]
            [SerializeField] private float _shotInterval;

            [FormerlySerializedAs("repeatFromSeconds")]
            [SerializeField] private float _repeatFromSeconds;

            [FormerlySerializedAs("restartOnShots")]
            [SerializeField] private bool _restartOnShots = true;

            [FormerlySerializedAs("forcedSpeed")]
            [SerializeField] private float _forcedSpeed;

            [FormerlySerializedAs("holdClipSeconds")]
            [SerializeField] private float _holdClipSeconds;

            [FormerlySerializedAs("holdReleaseSeconds")]
            [SerializeField] private float _holdReleaseSeconds;

            [FormerlySerializedAs("holdSpeed")]
            [SerializeField] private float _holdSpeed;

            [FormerlySerializedAs("holdAmplitude")]
            [SerializeField] private float _holdAmplitude;

            [FormerlySerializedAs("releaseSpeed")]
            [SerializeField] private float _releaseSpeed = 1f;

            [FormerlySerializedAs("slowFromClip")]
            [SerializeField] private float _slowFromClip;

            [FormerlySerializedAs("slowToClip")]
            [SerializeField] private float _slowToClip;

            [FormerlySerializedAs("slowSpeed")]
            [SerializeField] private float _slowSpeed;

            [SerializeField] private bool _isConfirmed;

            public string Label              => _label;
            public string StateName          => _stateName;
            public float  StrikeSeconds      => _strikeSeconds;
            public float  ClipPeakSeconds    => _clipPeakSeconds;
            public float  RecoverySeconds    => _recoverySeconds;
            public int    Shots              => Mathf.Max(1, _shots);
            public float  ShotInterval       => _shotInterval;
            public float  RepeatFromSeconds  => _repeatFromSeconds;
            public bool   RestartOnShots     => _restartOnShots;
            public float  ForcedSpeed        => _forcedSpeed;
            public float  HoldClipSeconds    => _holdClipSeconds;
            public float  HoldReleaseSeconds => _holdReleaseSeconds;
            public float  HoldSpeed          => _holdSpeed;
            public float  HoldAmplitude      => _holdAmplitude;
            public float  ReleaseSpeed       => (_releaseSpeed > 0f) ? _releaseSpeed : 1f;
            public float  SlowFromClip       => _slowFromClip;
            public float  SlowToClip         => _slowToClip;
            public float  SlowSpeed          => _slowSpeed;
            public bool   IsConfirmed        => _isConfirmed;

            public float TotalSeconds => _strikeSeconds + ((Shots - 1) * _shotInterval) + _recoverySeconds;
        }

        /****************************************
        *                Fields
        ****************************************/

        [FormerlySerializedAs("animator")]
        [SerializeField] private Animator _animator;

        [FormerlySerializedAs("entries")]
        [SerializeField] private Entry[] _entries;

        [FormerlySerializedAs("flashSeconds")]
        [SerializeField] private float _flashSeconds = 0.18f;

        private Coroutine _running;
        private string    _status = string.Empty;
        private float     _flashUntil;
        private float     _patternStart;
        private Entry     _current;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 판정 시각마다 패턴 라벨과 몇 번째 판정인지(0부터)를 알린다 </summary>
        public event Action<string, int> Struck;

        /// <summary> 패턴이 시작될 때 패턴 라벨과 첫 판정까지의 시간(초)을 알린다 </summary>
        public event Action<string, float> Started;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnGUI()
        {
            if ((_animator == null) || (_entries == null))
            {
                return;
            }

            float x = Screen.width - PANEL_WIDTH;
            float y = START_Y;

            GUI.Label(new Rect(x, y, BUTTON_WIDTH, TITLE_HEIGHT), TITLE);
            y += TITLE_STEP;

            for (int i = 0; i < _entries.Length; ++i)
            {
                if (_entries[i].IsConfirmed)
                {
                    continue;
                }

                if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), _entries[i].Label))
                {
                    StartEntry(_entries[i]);
                }

                y += BUTTON_STEP;
            }

            DrawStatus(x, y);
            DrawFlash();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 라벨이 같은 패턴을 확정한 타이밍으로 재생하고, 없으면 false를 돌려준다 </summary>
        public bool PlayByLabel(string label)
        {
            if (_entries == null)
            {
                return false;
            }

            for (int i = 0; i < _entries.Length; ++i)
            {
                if (_entries[i].Label == label)
                {
                    StartEntry(_entries[i]);
                    return true;
                }
            }

            return false;
        }

        /// <summary> 재생 중인 패턴을 멈추고 재생 속도를 되돌린다 </summary>
        public void StopPlaying()
        {
            if (_running == null)
            {
                return;
            }

            StopCoroutine(_running);
            _running = null;
            _current = null;
            _animator.speed = 1f;
        }

        private void StartEntry(Entry entry)
        {
            if (_running != null)
            {
                StopCoroutine(_running);
            }

            _running = StartCoroutine(Play(entry));
        }

        private void DrawStatus(float x, float y)
        {
            if (!string.IsNullOrEmpty(_status))
            {
                GUI.Label(new Rect(x - STATUS_SHIFT, y + STATUS_GAP, STATUS_WIDTH, STATUS_HEIGHT), _status);
            }

            if (_current == null)
            {
                return;
            }

            float total = _current.TotalSeconds;
            float fill  = Mathf.Clamp01((Time.time - _patternStart) / total) * BAR_WIDTH;
            Rect  bar   = new Rect(x, y + BAR_Y, BAR_WIDTH, BAR_HEIGHT);

            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x, bar.y, fill, bar.height), GUIContent.none);

            for (int shot = 0; shot < _current.Shots; ++shot)
            {
                float mark = Mathf.Clamp01((_current.StrikeSeconds + (shot * _current.ShotInterval)) / total) * BAR_WIDTH;
                GUI.Label(new Rect(bar.x + mark - MARK_SHIFT, bar.y + MARK_Y, MARK_WIDTH, MARK_HEIGHT), MARK_TEXT);
            }
        }

        private void DrawFlash()
        {
            if (Time.time >= _flashUntil)
            {
                return;
            }

            Color old = GUI.color;
            GUI.color = FLASH_COLOR;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // 힘이 실리는 순간이 판정 시각보다 늦으면 속도를 올려 맞추고, 이르면 그만큼 늦게 시작한다
        private static void ResolveStart(Entry entry, out float speed, out float delay)
        {
            if (entry.ForcedSpeed > 0f)
            {
                speed = entry.ForcedSpeed;
                delay = Mathf.Max(0f, entry.StrikeSeconds - (entry.ClipPeakSeconds / speed));
                return;
            }

            if (entry.ClipPeakSeconds > entry.StrikeSeconds)
            {
                speed = entry.ClipPeakSeconds / entry.StrikeSeconds;
                delay = 0f;
                return;
            }

            speed = 1f;
            delay = entry.StrikeSeconds - entry.ClipPeakSeconds;
        }

        private static string BuildStatus(Entry entry, float speed, float delay)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(entry.Label);
            text.AppendLine(string.Format(STATUS_FORMAT_DELAY, delay, speed));
            text.Append(string.Format(STATUS_FORMAT_STRIKE, entry.StrikeSeconds));

            if (entry.Shots > 1)
            {
                text.Append(string.Format(STATUS_FORMAT_SHOTS, entry.Shots, entry.ShotInterval));
            }

            text.Append(string.Format(STATUS_FORMAT_RECOV, entry.RecoverySeconds));
            return text.ToString();
        }

        private IEnumerator Play(Entry entry)
        {
            _current = entry;
            _animator.speed = 1f;
            _animator.CrossFade(IDLE_STATE, RESET_BLEND_SECONDS, 0, 0f);

            ResolveStart(entry, out float speed, out float delay);
            _status = BuildStatus(entry, speed, delay);

            _patternStart = Time.time;
            Started?.Invoke(entry.Label, entry.StrikeSeconds);
            float firstStrike = _patternStart + entry.StrikeSeconds;
            float end         = _patternStart + entry.TotalSeconds;

            bool  isStarted     = false;
            bool  isHolding     = false;
            bool  isReleased    = false;
            int   nextFlash     = 0;
            int   nextRestart   = 1;
            float clipTime      = 0f;
            float slowClipTime  = 0f;
            float oscDirection  = 1f;

            while (Time.time < end)
            {
                if ((!isStarted) && (Time.time >= (_patternStart + delay)))
                {
                    isStarted = true;
                    _animator.speed = speed;
                    _animator.CrossFade(entry.StateName, CLIP_ENTER_SECONDS, 0, 0f);
                }

                if (isStarted)
                {
                    UpdateHold(entry, ref isHolding, ref isReleased, ref clipTime, ref oscDirection);
                    UpdateSlow(entry, speed, ref slowClipTime);
                    UpdateRestart(entry, firstStrike, speed, ref nextRestart);
                }

                if ((nextFlash < entry.Shots) && (Time.time >= (firstStrike + (nextFlash * entry.ShotInterval))))
                {
                    _flashUntil = Time.time + _flashSeconds;
                    Struck?.Invoke(entry.Label, nextFlash);
                    ++nextFlash;
                }

                yield return null;
            }

            // 회복이 끝났는데 클립이 남아 있으면(길거나 배속으로 늘어난 클립) 다음 패턴을 위해 Idle로 섞어 자른다
            _animator.speed = 1f;
            _animator.CrossFade(IDLE_STATE, FINISH_BLEND_SECONDS, 0, 0f);
            _current = null;
        }

        // 정해진 지점에서 멈춰 팔을 뻗은 채 유지하고, 지정한 시각에 풀어 ReleaseSpeed로 거둔다
        // 클립 진행은 Animator에서 읽지 않고 재생 속도를 적분해 직접 센다
        private void UpdateHold(Entry entry, ref bool isHolding, ref bool isReleased, ref float clipTime, ref float oscDirection)
        {
            if ((entry.HoldClipSeconds <= 0f) || (isReleased))
            {
                return;
            }

            if (!isHolding)
            {
                clipTime += Time.deltaTime * _animator.speed;

                if (clipTime >= entry.HoldClipSeconds)
                {
                    isHolding = true;
                    oscDirection = 1f;
                    _animator.speed = entry.HoldSpeed;
                }

                return;
            }

            if (Time.time >= (_patternStart + entry.HoldReleaseSeconds))
            {
                isHolding = false;
                isReleased = true;
                _animator.speed = entry.ReleaseSpeed;
                return;
            }

            if (entry.HoldAmplitude <= 0f)
            {
                return;
            }

            // 유지 지점 앞뒤로 진폭만큼 왕복한다
            clipTime += Time.deltaTime * _animator.speed;

            if (clipTime >= (entry.HoldClipSeconds + entry.HoldAmplitude))
            {
                oscDirection = -1f;
            }
            else if (clipTime <= (entry.HoldClipSeconds - entry.HoldAmplitude))
            {
                oscDirection = 1f;
            }

            _animator.speed = entry.HoldSpeed * oscDirection;
        }

        // 쓸기처럼 짧은 구간만 느리게 늘려 긴 판정 구간(연쇄 폭발)에 맞춘다
        private void UpdateSlow(Entry entry, float baseSpeed, ref float slowClipTime)
        {
            if ((entry.SlowSpeed <= 0f) || (entry.HoldClipSeconds > 0f))
            {
                return;
            }

            slowClipTime += Time.deltaTime * _animator.speed;
            bool isInside = (slowClipTime >= entry.SlowFromClip) && (slowClipTime <= entry.SlowToClip);
            _animator.speed = isInside ? entry.SlowSpeed : baseSpeed;
        }

        // 다음 발의 힘 순간이 판정 시각에 오도록 던지는 구간 직전으로 되돌린다
        private void UpdateRestart(Entry entry, float firstStrike, float speed, ref int nextRestart)
        {
            if ((!entry.RestartOnShots) || (nextRestart >= entry.Shots))
            {
                return;
            }

            float strikeAt  = firstStrike + (nextRestart * entry.ShotInterval);
            float restartAt = strikeAt - ((entry.ClipPeakSeconds - entry.RepeatFromSeconds) / Mathf.Max(MIN_SPEED, speed));

            if (Time.time < restartAt)
            {
                return;
            }

            _animator.CrossFadeInFixedTime(entry.StateName, CLIP_ENTER_SECONDS, 0, entry.RepeatFromSeconds);
            ++nextRestart;
        }
    }
}
