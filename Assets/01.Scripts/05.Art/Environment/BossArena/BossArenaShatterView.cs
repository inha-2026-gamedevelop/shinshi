// System
using System;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 보스 아레나 바깥 링이 깨져 떨어지는 연출을 Blender에서 구운 낙하 표대로 재생하는 클라이언트 표현 </summary>
    [DisallowMultipleComponent]
    public sealed class BossArenaShatterView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  POSITION_MISMATCH_WARN = 0.01f;
        private const string EMISSION_COLOR_NAME    = "_EmissionColor";

        [Header("낙하 표")]
        [SerializeField] private TextAsset _timelineJson;

        [Header("링")]
        [SerializeField] private GameObject _intactRoot;
        [SerializeField] private Transform  _fracturedRoot;

        // 평소에는 씬의 기존 아레나가 링을 보여 주고, 재생하는 동안만 이 링으로 바꿔 친다.
        // 둘을 함께 켜 두면 같은 자리의 벽과 바닥이 겹쳐 깜박인다
        [Header("재생하는 동안 숨길 기존 아레나 표현")]
        [SerializeField] private GameObject[] _hideWhilePlaying          = Array.Empty<GameObject>();
        [SerializeField] private Renderer[]   _hideRenderersWhilePlaying = Array.Empty<Renderer>();

        // 기존 포장 메시는 링까지 한 장이라 숨기면 가운데도 사라지므로 반경 18m 안쪽만 잘라 둔 것을 대신 켠다
        [Header("재생하는 동안 켤 대체 표현")]
        [SerializeField] private GameObject[] _showWhilePlaying          = Array.Empty<GameObject>();

        // 링 밖에 서 있어 링이 사라지면 공중에 뜨는 씬 오브젝트(사당 건물 등). 낙하 표에 없으므로 한 덩어리로 묶어
        // 렌더러 범위 중심을 축으로 바깥쪽으로 기울며 떨어뜨린다
        [Header("링과 함께 떨어질 씬 오브젝트")]
        [SerializeField] private Transform[] _extraFallers         = Array.Empty<Transform>();
        [SerializeField] private float       _extraDelay           = 0.35f;
        [SerializeField] private float       _extraDuration        = 2.6f;
        [SerializeField] private float       _extraFallDistance    = 55f;
        [SerializeField] private float       _extraTiltAngle       = 25f;
        [SerializeField] private float       _extraOutwardDrift    = 4f;

        [Header("균열 발광")]
        [SerializeField] private Color _glowColor     = new Color(0.86f, 0.9f, 0.95f);
        [SerializeField] private float _glowIntensity = 4f;

        [Header("끝")]
        [SerializeField] private bool _hideFracturedWhenDone = true;

        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID(EMISSION_COLOR_NAME);

        private Piece[]               _pieces         = Array.Empty<Piece>();
        private Renderer[]            _glowRenderers  = Array.Empty<Renderer>();
        private MaterialPropertyBlock _block;
        private Tween                 _playTween;
        private float                 _time;
        private float                 _endTime;
        private float                 _swapTime;
        private float                 _glowStart;
        private float                 _glowPeak;
        private float                 _glowEnd;
        private float                 _appliedGlow;
        private EShatterStage         _stage;
        private bool                  _hasKeptPieces;
        private bool                  _externalFinished;
        private Transform[]           _extraTargets   = Array.Empty<Transform>();
        private bool[]                _extraActive    = Array.Empty<bool>();

        /****************************************
        *              Properties
        ****************************************/

        public bool IsPlaying => (_playTween != null) && _playTween.IsPlaying();

        /// <summary> 마지막으로 평가한 붕괴 시각(초) </summary>
        public float EvaluatedTime => _time;

        /// <summary> 지금 켜져 있는 씬의 붕괴 표현, 보스 프리팹의 바인더가 씬 참조 없이 찾는 자리이고 없으면 null </summary>
        public static BossArenaShatterView Current { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Current = null;
        }

        private void OnEnable()
        {
            if ((Current != null) && (Current != this))
            {
                Debug.LogError("[BossArenaShatter] 붕괴 표현이 씬에 둘 이상이다. 먼저 켜진 것만 쓴다", this);
                return;
            }

            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        protected override void OnViewAwake()
        {
            if ((_timelineJson == null) || (_intactRoot == null) || (_fracturedRoot == null))
            {
                Debug.LogError($"[BossArenaShatter] 낙하 표나 링 참조가 비어 있다 - {name}", this);
                enabled = false;
                return;
            }

            if (!TryBuildPieces())
            {
                enabled = false;
                return;
            }

            _block = new MaterialPropertyBlock();
            _glowRenderers = CollectGlowRenderers();

            // 재생할 때마다 새로 만들면 할당이 쌓이므로 한 번 만들어 Restart로 재사용한다.
            // 조각 155개를 각각 트윈으로 돌리지 않고 시간 하나를 트윈해 모든 조각을 같은 시각으로 계산한다.
            // 낙하 표의 회전 중심, 2차 낙하식은 DOTween 이징으로 표현할 수 없다
            _playTween = DOTween.To(ReadTime, ApplyTime, _endTime, _endTime)
                .SetEase(Ease.Linear)
                .SetAutoKill(false)
                .Pause()
                .OnComplete(OnPlayFinished)
                .SetLink(gameObject);

            ResetToIntact();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 처음부터 붕괴를 재생한다, 재생 중이면 되감고 다시 시작한다 </summary>
        [ContextMenu("Play")]
        public void Play()
        {
            if (_playTween == null)
            {
                return;
            }

            ResetToIntact();
            SetStage(EShatterStage.Cracking);
            _playTween.Restart();
        }

        /// <summary> 서버 신호를 늦게 받았거나 복원할 때 이미 지난 시간만큼 건너뛴 지점부터 재생한다, 전체 길이를 넘으면 끝난 모습으로 둔다 </summary>
        public void PlayFrom(float elapsedSeconds)
        {
            if (_playTween == null)
            {
                return;
            }

            if (elapsedSeconds <= 0f)
            {
                Play();
                return;
            }

            ResetToIntact();
            SetStage(EShatterStage.Cracking);

            if (elapsedSeconds >= _endTime)
            {
                ApplyTime(_endTime);
                OnPlayFinished();
                return;
            }

            _playTween.Restart();
            _playTween.Goto(elapsedSeconds, true);
        }

        // 프레임이 멈춰도 DOTween deltaTime 상한 때문에 뒤처지지 않도록, 호출자가 서버 시각 기준 경과 시간을 매 프레임 넘겨 그 시각을 그대로 평가한다.
        // 자유 재생(Play, PlayFrom)과 섞어 쓰지 않는다. 끝난 뒤에는 ResetToIntact 전까지 평가하지 않는다
        /// <summary> 외부 시계의 경과 시간으로 붕괴를 평가한다, 아직 진행 중이면 true이고 끝난 모습으로 둔 뒤에는 false다 </summary>
        public bool EvaluateAt(float elapsedSeconds)
        {
            if ((_playTween == null) || _externalFinished)
            {
                return false;
            }

            if (_playTween.IsPlaying())
            {
                _playTween.Pause();
            }

            if (_stage == EShatterStage.Idle)
            {
                SetStage(EShatterStage.Cracking);
            }

            if (elapsedSeconds >= _endTime)
            {
                ApplyTime(_endTime);
                OnPlayFinished();
                _externalFinished = true;
                return false;
            }

            ApplyTime(Mathf.Max(0f, elapsedSeconds));
            return true;
        }

        /// <summary> 재생을 멈추고 씬의 기존 아레나 표현으로 되돌린다 </summary>
        [ContextMenu("Reset To Intact")]
        public void ResetToIntact()
        {
            if (_playTween == null)
            {
                return;
            }

            _playTween.Pause();
            _time = 0f;
            _externalFinished = false;

            for (int i = 0; i < _pieces.Length; ++i)
            {
                ref Piece piece = ref _pieces[i];
                piece.Target.SetPositionAndRotation(
                    transform.TransformPoint(piece.InitialPosition),
                    transform.rotation * piece.InitialRotation);
            }

            for (int i = 0; i < _extraTargets.Length; ++i)
            {
                _extraTargets[i].gameObject.SetActive(_extraActive[i]);
            }

            SetStage(EShatterStage.Idle);
            ApplyGlow(0f);
        }

        private float ReadTime() { return _time; }

        private void ApplyTime(float time)
        {
            _time = time;
            ApplyGlow(EvaluateGlow(time));

            if (time < _swapTime)
            {
                return;
            }

            if (_stage != EShatterStage.Falling)
            {
                SetStage(EShatterStage.Falling);
            }

            Quaternion arenaRotation = transform.rotation;
            for (int i = 0; i < _pieces.Length; ++i)
            {
                EvaluatePiece(ref _pieces[i], time - _swapTime, transform, arenaRotation);
            }
        }

        private void OnPlayFinished()
        {
            ApplyGlow(0f);

            // 남는 조각이 있으면 링을 통째로 끄면 그것까지 사라진다
            if ((_hideFracturedWhenDone) && (!_hasKeptPieces))
            {
                _fracturedRoot.gameObject.SetActive(false);

                for (int i = 0; i < _extraTargets.Length; ++i)
                {
                    _extraTargets[i].gameObject.SetActive(false);
                }
            }
        }

        // Blender 스크립트가 낙하 표에 적은 식과 같아야 한다. 바꾸면 Blender 미리보기와 게임 화면이 어긋난다.
        // 낙하 표는 아레나 중심 기준 좌표라 이 컴포넌트의 위치를 아레나 중심으로 보고 계산한다
        private static void EvaluatePiece(ref Piece piece, float sinceSwap, Transform arena, Quaternion arenaRotation)
        {
            if (piece.Kept)
            {
                return;
            }

            float progress = Mathf.Clamp01((sinceSwap - piece.StartDelay) / piece.Duration);
            float eased    = progress * progress;

            Quaternion spin = Quaternion.AngleAxis(piece.SpinAngle * progress, piece.SpinAxis);

            Vector3 position = piece.RotationCenter
                + (spin * (piece.InitialPosition - piece.RotationCenter))
                + (piece.Drift * eased)
                + (Vector3.down * (piece.FallDistance * eased));

            piece.Target.SetPositionAndRotation(arena.TransformPoint(position), arenaRotation * (spin * piece.InitialRotation));
        }

        private float EvaluateGlow(float time)
        {
            if ((time <= _glowStart) || (time >= _glowEnd))
            {
                return 0f;
            }

            if (time <= _glowPeak)
            {
                return Mathf.InverseLerp(_glowStart, _glowPeak, time);
            }

            return 1f - Mathf.InverseLerp(_glowPeak, _glowEnd, time);
        }

        private void ApplyGlow(float glow)
        {
            // 꺼진 상태가 이어지는 동안 렌더러마다 블록을 다시 넣지 않는다
            if (Mathf.Approximately(glow, _appliedGlow))
            {
                return;
            }

            _appliedGlow = glow;
            _block.SetColor(EMISSION_COLOR_ID, _glowColor * (_glowIntensity * glow));

            // 블록이 붙은 렌더러는 SRP Batcher에서 빠지므로 발광이 꺼지면 떼어 낸다
            MaterialPropertyBlock block = (glow > 0f) ? _block : null;
            for (int i = 0; i < _glowRenderers.Length; ++i)
            {
                _glowRenderers[i].SetPropertyBlock(block);
            }
        }

        private void SetStage(EShatterStage stage)
        {
            _stage = stage;

            bool playing = stage != EShatterStage.Idle;
            _intactRoot.SetActive(stage == EShatterStage.Cracking);
            _fracturedRoot.gameObject.SetActive(stage == EShatterStage.Falling);

            for (int i = 0; i < _hideWhilePlaying.Length; ++i)
            {
                if (_hideWhilePlaying[i] != null)
                {
                    _hideWhilePlaying[i].SetActive(!playing);
                }
            }

            for (int i = 0; i < _hideRenderersWhilePlaying.Length; ++i)
            {
                if (_hideRenderersWhilePlaying[i] != null)
                {
                    _hideRenderersWhilePlaying[i].enabled = !playing;
                }
            }

            for (int i = 0; i < _showWhilePlaying.Length; ++i)
            {
                if (_showWhilePlaying[i] != null)
                {
                    _showWhilePlaying[i].SetActive(playing);
                }
            }
        }

        private Renderer[] CollectGlowRenderers()
        {
            Renderer[] intact    = _intactRoot.GetComponentsInChildren<Renderer>(true);
            Renderer[] fractured = _fracturedRoot.GetComponentsInChildren<Renderer>(true);
            Renderer[] all       = new Renderer[intact.Length + fractured.Length];

            Array.Copy(intact, all, intact.Length);
            Array.Copy(fractured, 0, all, intact.Length, fractured.Length);
            return all;
        }

        private bool TryBuildPieces()
        {
            TimelineData data;
            try
            {
                data = JsonUtility.FromJson<TimelineData>(_timelineJson.text);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[BossArenaShatter] 낙하 표를 읽지 못했다 - {e.Message}", this);
                return false;
            }

            if ((data == null) || (data.objects == null) || (data.objects.Length == 0))
            {
                Debug.LogError("[BossArenaShatter] 낙하 표에 조각이 없다", this);
                return false;
            }

            _swapTime  = data.swap_time;
            _glowStart = data.crack_glow_start;
            _glowPeak  = data.crack_glow_peak;
            _glowEnd   = data.crack_glow_end;
            _endTime   = Mathf.Max(_glowEnd, _swapTime);

            _pieces = new Piece[data.objects.Length];
            int count = 0;
            for (int i = 0; i < data.objects.Length; ++i)
            {
                PieceData source = data.objects[i];

                // 초기화 1회만 이름으로 찾는다. 조각 155개를 인스펙터에 하나씩 넣으면 FBX를 다시 뽑을 때마다 연결이 끊긴다
                Transform target = _fracturedRoot.Find(source.object_name);
                if (target == null)
                {
                    Debug.LogWarning($"[BossArenaShatter] 링에 {source.object_name}이(가) 없어 건너뛴다", this);
                    continue;
                }

                // FBX를 Unity로 가져오면 Blender 축 변환 때문에 링 루트를 Y축 180도 돌려 둬야 낙하 표 좌표와 맞는다
                Vector3 initial = ToVector(source.initial_position);
                if ((transform.InverseTransformPoint(target.position) - initial).sqrMagnitude > (POSITION_MISMATCH_WARN * POSITION_MISMATCH_WARN))
                {
                    Debug.LogWarning($"[BossArenaShatter] {source.object_name} 위치가 낙하 표와 다르다 - 링 루트의 위치와 회전을 확인", this);
                }

                _pieces[count] = new Piece
                {
                    Target          = target,
                    InitialPosition = initial,
                    InitialRotation = Quaternion.Inverse(transform.rotation) * target.rotation,
                    RotationCenter  = ToVector(source.rotation_center),
                    Drift           = ToVector(source.outward_drift_vector),
                    SpinAxis        = ToVector(source.spin_axis),
                    SpinAngle       = source.total_spin_angle,
                    FallDistance    = source.fall_distance,
                    StartDelay      = source.start_delay,
                    Duration        = Mathf.Max(source.duration, Mathf.Epsilon),
                    Kept            = source.kept
                };
                _endTime       = Mathf.Max(_endTime, _swapTime + source.start_delay + source.duration);
                _hasKeptPieces = _hasKeptPieces || source.kept;
                ++count;
            }

            if (count < _pieces.Length)
            {
                Array.Resize(ref _pieces, count);
            }

            AppendExtraFallers();
            return count > 0;
        }

        private void AppendExtraFallers()
        {
            int targetCount = 0;
            for (int i = 0; i < _extraFallers.Length; ++i)
            {
                if (_extraFallers[i] != null)
                {
                    ++targetCount;
                }
            }

            if (targetCount == 0)
            {
                return;
            }

            _extraTargets = new Transform[targetCount];
            _extraActive  = new bool[targetCount];

            int start = _pieces.Length;
            Array.Resize(ref _pieces, start + targetCount);

            // 사당은 북쪽, 입구 문은 남쪽에 있어 하나로 묶으면 기울어지는 방향이 어긋난다.
            // 그래서 오브젝트마다 자기 렌더러 범위 중심을 축으로 자기 바깥 방향으로 기울인다
            int next = 0;
            for (int i = 0; i < _extraFallers.Length; ++i)
            {
                Transform target = _extraFallers[i];
                if (target == null)
                {
                    continue;
                }

                _extraTargets[next] = target;
                _extraActive[next]  = target.gameObject.activeSelf;

                bool   hasBounds = false;
                Bounds bounds    = default;
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; ++r)
                {
                    if (!hasBounds)
                    {
                        bounds    = renderers[r].bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[r].bounds);
                    }
                }

                Vector3 center  = transform.InverseTransformPoint(hasBounds ? bounds.center : target.position);
                Vector3 outward = new Vector3(center.x, 0f, center.z).normalized;

                _pieces[start + next] = new Piece
                {
                    Target          = target,
                    InitialPosition = transform.InverseTransformPoint(target.position),
                    InitialRotation = Quaternion.Inverse(transform.rotation) * target.rotation,
                    RotationCenter  = center,
                    Drift           = outward * _extraOutwardDrift,
                    // 위쪽과 바깥 방향의 외적을 축으로 돌리면 윗부분이 바깥으로 넘어간다
                    SpinAxis        = Vector3.Cross(Vector3.up, outward),
                    SpinAngle       = _extraTiltAngle,
                    FallDistance    = _extraFallDistance,
                    StartDelay      = _extraDelay,
                    Duration        = Mathf.Max(_extraDuration, Mathf.Epsilon),
                    Kept            = false
                };

                ++next;
            }

            _endTime = Mathf.Max(_endTime, _swapTime + _extraDelay + _extraDuration);
        }

        private static Vector3 ToVector(float[] values)
        {
            if ((values == null) || (values.Length < 3))
            {
                return Vector3.zero;
            }

            return new Vector3(values[0], values[1], values[2]);
        }

        /****************************************
        *               Types
        ****************************************/

        private enum EShatterStage
        {
            Idle,
            // 멀쩡한 링에 균열이 번지는 구간
            Cracking,
            // 조각으로 바꿔 친 뒤 떨어지는 구간, 끝나도 되돌리기 전까지 이 상태로 남는다
            Falling
        }

        private struct Piece
        {
            public Transform  Target;
            public Vector3    InitialPosition;
            public Quaternion InitialRotation;
            public Vector3    RotationCenter;
            public Vector3    Drift;
            public Vector3    SpinAxis;
            public float      SpinAngle;
            public float      FallDistance;
            public float      StartDelay;
            public float      Duration;
            public bool       Kept;
        }

        // JsonUtility는 필드 이름으로 키를 맞추므로 Blender가 쓴 낙하 표 키 이름을 그대로 쓴다
        [Serializable]
        private sealed class TimelineData
        {
            public float       swap_time;
            public float       crack_glow_start;
            public float       crack_glow_peak;
            public float       crack_glow_end;
            public PieceData[] objects;
        }

        [Serializable]
        private sealed class PieceData
        {
            public string  object_name;
            public float   start_delay;
            public float   duration;
            public float   fall_distance;
            public float   total_spin_angle;
            public float[] initial_position;
            public float[] rotation_center;
            public float[] outward_drift_vector;
            public float[] spin_axis;
            public bool    kept;
        }
    }
}
