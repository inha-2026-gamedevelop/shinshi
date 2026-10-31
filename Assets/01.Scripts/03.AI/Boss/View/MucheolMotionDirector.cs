// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 시각으로 바꾼 구간(MucheolMotionTrack)을 Animator 컨트롤러 상태로 재생한다. 판정에는 관여하지 않는다.
    // 클립 시간은 매 프레임 서버 시각에서 다시 계산하고 재생 속도만 Animator에 맡긴다. 프레임 오차가 쌓여 어긋나면 그 순간 위치로 바로잡는다.
    // 컨트롤러 전이는 쓰지 않고 코드가 CrossFade로만 옮긴다. 원본 클립이 모두 반복 클립이라 컨트롤러 전이에 맡기면 끝에서 처음으로 돌아가기 때문이다.
    // Animator.speed는 Idle로 돌아갈 때, 끊길 때, 초기화할 때 항상 1로 되돌린다
    /// <summary> 무철 모션 구간을 서버 시각에 맞춰 컨트롤러 상태로 재생하는 표현 도우미 </summary>
    public sealed class MucheolMotionDirector
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float ENTER_BLEND_SECONDS   = 0.12f; // 프리뷰가 클립 사이를 섞은 시간
        private const float EXIT_BLEND_SECONDS    = 0.25f;
        private const float DEATH_BLEND_SECONDS   = 0.1f;
        private const float DRIFT_TOLERANCE       = 0.05f; // 초, 클립 시간이 이보다 어긋나면 바로잡는다
        private const float CONTINUITY_TOLERANCE  = 0.02f; // 초, 앞 구간 끝과 다음 구간 시작이 이만큼 가까우면 섞지 않고 잇는다
        private const float MIN_TRANSITION_SPEED  = 0.05f; // 속도 0으로 섞기를 시작하면 전이가 끝나지 않는다
        private const float DEFAULT_SPEED         = 1f;
        private const int   NO_CAST               = -1;
        private const int   NO_TRACK              = -1;
        private const int   BASE_LAYER            = 0;

        /****************************************
        *               Types
        ****************************************/

        private enum EOwner
        {
            None     = 0,
            Cast     = 1,
            Reaction = 2,
            Groggy   = 3
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Animator                   _animator;
        private readonly int                        _idleHash;
        private readonly Dictionary<string, float>  _clipLengths = new Dictionary<string, float>();
        private readonly Dictionary<string, int>    _stateHashes = new Dictionary<string, int>();
        private readonly List<MucheolMotionTrack>   _tracks      = new List<MucheolMotionTrack>();

        private int    _castId       = NO_CAST;
        private EOwner _owner        = EOwner.None;
        private bool   _awaitsNext;
        private int    _playingIndex = NO_TRACK;
        private string _playingState;
        private string _playingClip;
        private float  _clipSeconds;
        private bool   _isBlocked;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 재생하거나 기다리는 시전 번호, 없으면 -1이다 </summary>
        public int CastId => _castId;

        /// <summary> 확정 모션이 몸을 쓰는 중이다, 끝나 Idle로 돌아갔으면 거짓이다 </summary>
        public bool IsActive => _tracks.Count > 0;

        /// <summary> 그로기 구간을 재생 중이다, 시전의 끝과 취소가 이 구간을 끊지 않는다 </summary>
        public bool IsGroggy => IsActive && (_owner == EOwner.Groggy);

        /// <summary> 사망으로 막혀 새 모션을 받지 않는다 </summary>
        public bool IsBlocked => _isBlocked;

        /// <summary> 지금 재생 중인 컨트롤러 상태 이름, Idle이면 null이다 </summary>
        public string PlayingState => _playingState;

        /// <summary> 지금 재생 중인 클립 이름, Idle이면 null이다 </summary>
        public string PlayingClip => _playingClip;

        /// <summary> 마지막 Tick이 계산한 클립 시간 </summary>
        public float ClipSeconds => _clipSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolMotionDirector(Animator animator)
        {
            _animator = animator;
            _idleHash = Animator.StringToHash(MucheolMotionCatalog.IDLE_CLIP);

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                return;
            }

            foreach (AnimationClip clip in controller.animationClips)
            {
                if ((clip != null) && (!_clipLengths.ContainsKey(clip.name)))
                {
                    _clipLengths.Add(clip.name, clip.length);
                }
            }
        }

        /// <summary> 컨트롤러에 이 클립의 상태가 있다 </summary>
        public bool HasClip(string clipName)
        {
            return (clipName != null) && _clipLengths.ContainsKey(clipName)
                && _animator.HasState(BASE_LAYER, FullHash(clipName));
        }

        /// <summary> 시전의 모션을 새로 시작한다. awaitsNext가 참이면 마지막 키에 멈춰 다음 구간(돌진 시작 같은)을 기다린다 </summary>
        public void PlayCast(int castId, IReadOnlyList<MucheolMotionTrack> tracks, bool awaitsNext, double now)
        {
            Play(castId, EOwner.Cast, tracks, awaitsNext, now);
        }

        /// <summary> 충돌 반동처럼 시전 안에서 나머지 구간을 갈아 끼우는 짧은 모션 </summary>
        public void PlayReaction(int castId, IReadOnlyList<MucheolMotionTrack> tracks, double now)
        {
            Play(castId, EOwner.Reaction, tracks, false, now);
        }

        /// <summary> 그로기 내려앉기와 일어나기, 그로기 끝까지 시전의 끝과 취소가 끊지 않는다. awaitsRise가 참이면 일어나기 구간이 올 때까지 유지한다 </summary>
        public void PlayGroggy(int castId, IReadOnlyList<MucheolMotionTrack> tracks, bool awaitsRise, double now)
        {
            Play(castId, EOwner.Groggy, tracks, awaitsRise, now);
        }

        /// <summary> 같은 시전이나 그로기에 구간 하나를 잇는다, 시전이 다르거나 이미 Idle로 돌아갔으면 무시한다 </summary>
        public bool Append(int castId, MucheolMotionTrack track, bool awaitsNext, double now)
        {
            bool isAppendable = (_owner == EOwner.Cast) || (_owner == EOwner.Groggy);
            if ((_isBlocked) || (track == null) || (castId != _castId) || (_tracks.Count == 0) || (!isAppendable))
            {
                return false;
            }

            _tracks.Add(track);
            _awaitsNext = awaitsNext;
            Tick(now);
            return true;
        }

        // 일찍 멈춘 돌진처럼 이미 받은 구간의 남은 부분을 바꾼다. 시전이 몸을 쓰는 중일 때만 받는다.
        // 반동(충돌 피격)과 그로기가 몸을 쓰는 중이면 거절해 그쪽이 우선한다. 다음 구간 대기 여부는 그대로 둔다
        /// <summary> at을 품은 구간을 at에서 끊고 뒤 구간을 버린 다음 tail을 잇는다, 받지 못했으면 거짓이다 </summary>
        public bool ReplaceFrom(int castId, double at, MucheolMotionTrack tail, double now)
        {
            if ((_isBlocked) || (tail == null) || (castId != _castId) || (_owner != EOwner.Cast) || (_tracks.Count == 0))
            {
                return false;
            }

            int index = NO_TRACK;
            for (int i = 0; i < _tracks.Count; ++i)
            {
                if (at > _tracks[i].StartTime)
                {
                    index = i;
                }
            }

            if (index == NO_TRACK)
            {
                return false;
            }

            MucheolMotionTrack cut = MucheolMotionPlan.TruncateAt(_tracks[index], at);
            if (cut == null)
            {
                return false;
            }

            _tracks.RemoveRange(index, _tracks.Count - index);
            _tracks.Add(cut);
            _tracks.Add(tail);
            if (_playingIndex > index)
            {
                _playingIndex = index;
            }

            Tick(now);
            return true;
        }

        // 이미 한 번 끊은 돌진을 다른 시각에서 다시 끊는다. run은 끊기 전 원래 구간이고, 같은 시작 시각과 클립의 구간부터 뒤를 모두 갈아 끼운다.
        // 이미 갈아 끼운 뒤 구간을 재생 중이었으면 재생 위치를 다시 잡도록 다음 Tick에서 새로 들어간다
        /// <summary> run을 at에서 끊고 그 뒤를 tail로 바꾼다, run이 이 시전에 없거나 at이 run 밖이면 거짓이다 </summary>
        public bool ReplaceRunFrom(int castId, MucheolMotionTrack run, double at, MucheolMotionTrack tail, double now)
        {
            if ((_isBlocked) || (run == null) || (tail == null) || (castId != _castId) || (_owner != EOwner.Cast))
            {
                return false;
            }

            int index = NO_TRACK;
            for (int i = _tracks.Count - 1; i >= 0; --i)
            {
                if ((_tracks[i].StartTime == run.StartTime) && (_tracks[i].ClipName == run.ClipName))
                {
                    index = i;
                    break;
                }
            }

            MucheolMotionTrack cut = MucheolMotionPlan.TruncateAt(run, at);
            if ((index == NO_TRACK) || (cut == null))
            {
                return false;
            }

            _tracks.RemoveRange(index, _tracks.Count - index);
            _tracks.Add(cut);
            _tracks.Add(tail);
            if (_playingIndex > index)
            {
                _playingIndex = NO_TRACK;
            }

            Tick(now);
            return true;
        }

        /// <summary> 다음 구간을 더 기다리지 않는다, 마지막 키를 지나면 Idle로 돌아간다 </summary>
        public void StopAwaiting(int castId, double now)
        {
            if (castId != _castId)
            {
                return;
            }

            _awaitsNext = false;
            Tick(now);
        }

        // 서버 정상 종료는 회복까지 끝났다는 뜻이다. 모션이 아직 남았으면(늦게 받은 사건으로 뒤로 밀린 경우) 그때 Idle로 돌아간다.
        // 그로기는 시전 종료 뒤에도 서버 그로기 끝까지 이어지므로 끊지 않는다
        /// <summary> 시전이 정상 종료됐다 </summary>
        public void Finish(int castId)
        {
            if ((castId != _castId) || (_owner == EOwner.Groggy))
            {
                return;
            }

            Exit(EXIT_BLEND_SECONDS);
        }

        /// <summary> 시전이 취소됐다, 그로기는 끊지 않는다 </summary>
        public void Cancel(int castId)
        {
            if ((castId != _castId) || (_owner == EOwner.Groggy))
            {
                return;
            }

            Exit(EXIT_BLEND_SECONDS);
        }

        /// <summary> 서버가 그로기를 접었다, 이 시전의 그로기만 끊고 Idle로 돌아간다 </summary>
        public void CancelGroggy(int castId)
        {
            if ((castId != _castId) || (_owner != EOwner.Groggy))
            {
                return;
            }

            Exit(EXIT_BLEND_SECONDS);
        }

        // 공격 모션이 사망 표현을 덮지 않도록 막는다. 사망 클립은 아직 확정되지 않아 여기서 재생하지 않는다
        /// <summary> 사망이면 진행 중인 모션을 끊고 새 모션을 막는다, 거짓이면 다시 받는다 </summary>
        public void SetBlocked(bool isBlocked)
        {
            if (_isBlocked == isBlocked)
            {
                return;
            }

            if (isBlocked)
            {
                Exit(DEATH_BLEND_SECONDS);
            }

            _isBlocked = isBlocked;
        }

        /// <summary> 진행 중인 모션과 막힘을 모두 비우고 재생 속도를 되돌린다, snapToIdle이면 Idle 상태로 바로 옮긴다 </summary>
        public void Reset(bool snapToIdle)
        {
            _tracks.Clear();
            _castId       = NO_CAST;
            _owner        = EOwner.None;
            _awaitsNext   = false;
            _playingIndex = NO_TRACK;
            _playingState = null;
            _playingClip  = null;
            _isBlocked    = false;

            if (_animator == null)
            {
                return;
            }

            _animator.speed = DEFAULT_SPEED;
            if (snapToIdle && (_animator.isActiveAndEnabled) && (_animator.runtimeAnimatorController != null))
            {
                _animator.Play(_idleHash, BASE_LAYER, 0f);
            }
        }

        /// <summary> 매 프레임 부른다. 서버 시각의 클립 시간과 속도로 재생하고 끝나면 Idle로 돌아간다 </summary>
        public void Tick(double now)
        {
            if ((_isBlocked) || (_tracks.Count == 0))
            {
                return;
            }

            int index = FindTrack(now);
            if (index == NO_TRACK)
            {
                return;
            }

            int lastIndex = _tracks.Count - 1;
            if ((index == lastIndex) && (now >= _tracks[lastIndex].EndTime) && (!_awaitsNext))
            {
                Exit(EXIT_BLEND_SECONDS);
                return;
            }

            MucheolMotionTrack track = _tracks[index];
            MucheolMotionPlan.Sample(track, now, out float clipSeconds, out float speed);
            _clipSeconds = clipSeconds;

            if (index != _playingIndex)
            {
                Enter(index, track, clipSeconds, speed);
                return;
            }

            _animator.speed = speed;
            CorrectDrift(clipSeconds);
        }

        private void Play(int castId, EOwner owner, IReadOnlyList<MucheolMotionTrack> tracks, bool awaitsNext, double now)
        {
            if ((_isBlocked) || (tracks == null) || (tracks.Count == 0))
            {
                return;
            }

            // 늦게 받아 이미 끝난 일회성 동작은 다시 보이지 않는다. 다만 지금 같은 시전을 재생 중이면 그 구간을 정리한다
            MucheolMotionTrack last = tracks[tracks.Count - 1];
            if ((!awaitsNext) && (now >= last.EndTime))
            {
                if (castId == _castId)
                {
                    Exit(EXIT_BLEND_SECONDS);
                }
                return;
            }

            _tracks.Clear();
            for (int i = 0; i < tracks.Count; ++i)
            {
                _tracks.Add(tracks[i]);
            }

            _castId       = castId;
            _owner        = owner;
            _awaitsNext   = awaitsNext;
            _playingIndex = NO_TRACK;

            // 시계 오차로 시작 시각이 아직 오지 않았으면 앞 모션의 정지 자세(속도 0)에 묶이지 않게 Idle로 먼저 돌려 둔다
            if ((FindTrack(now) == NO_TRACK) && (_playingState != null))
            {
                ReturnToIdle(EXIT_BLEND_SECONDS);
            }

            Tick(now);
        }

        // 지금 시각에 해당하는 마지막 구간, 첫 구간 시작 전이면 아직 몸을 쓰지 않는다
        private int FindTrack(double now)
        {
            int found = NO_TRACK;
            for (int i = 0; i < _tracks.Count; ++i)
            {
                if (now >= _tracks[i].StartTime)
                {
                    found = i;
                }
            }

            return found;
        }

        private void Enter(int index, MucheolMotionTrack track, float clipSeconds, float speed)
        {
            MucheolMotionTrack previous = (index > 0) ? _tracks[index - 1] : null;
            bool isContinuous = (_playingIndex == (index - 1)) && (_playingClip == track.ClipName)
                && MucheolMotionPlan.IsContinuous(previous, track, CONTINUITY_TOLERANCE);

            _playingIndex   = index;
            _animator.speed = speed;
            if (isContinuous)
            {
                return;
            }

            // 같은 클립을 다른 순간에서 다시 시작하면 두 번째 상태로 옮겨야 앞 자세와 섞인다
            string state = track.ClipName;
            if (_playingState == state)
            {
                state += MucheolMotionCatalog.ALTERNATE_SUFFIX;
            }

            float blend = ENTER_BLEND_SECONDS * Mathf.Max(speed, MIN_TRANSITION_SPEED);
            _animator.CrossFadeInFixedTime(ShortHash(state), blend, BASE_LAYER, clipSeconds);
            _playingState = state;
            _playingClip  = track.ClipName;
        }

        // 섞는 중이 아니고 지금 상태가 재생하려던 상태일 때만 바로잡는다. 반복 클립이라 끝과 처음은 같은 거리로 본다
        private void CorrectDrift(float clipSeconds)
        {
            if ((_playingState == null) || (_animator.IsInTransition(BASE_LAYER)))
            {
                return;
            }

            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(BASE_LAYER);
            int hash = ShortHash(_playingState);
            if ((info.shortNameHash != hash) || (!_clipLengths.TryGetValue(_playingClip, out float length)) || (length <= 0f))
            {
                return;
            }

            float actual = Mathf.Repeat(info.normalizedTime, 1f) * length;
            float diff   = Mathf.Abs(actual - clipSeconds);
            diff = Mathf.Min(diff, Mathf.Abs(length - diff));
            if (diff > DRIFT_TOLERANCE)
            {
                _animator.Play(hash, BASE_LAYER, clipSeconds / length);
            }
        }

        private void Exit(float blendSeconds)
        {
            _tracks.Clear();
            _castId       = NO_CAST;
            _owner        = EOwner.None;
            _awaitsNext   = false;
            _playingIndex = NO_TRACK;
            ReturnToIdle(blendSeconds);
        }

        // 재생 중이던 상태가 있을 때만 Idle로 섞는다. 이미 Idle이면 다시 시작하지 않아 호흡이 끊기지 않는다
        private void ReturnToIdle(float blendSeconds)
        {
            bool wasPlaying = _playingState != null;
            _playingState = null;
            _playingClip  = null;

            if (_animator == null)
            {
                return;
            }

            _animator.speed = DEFAULT_SPEED;
            if (wasPlaying)
            {
                _animator.CrossFadeInFixedTime(_idleHash, blendSeconds, BASE_LAYER, 0f);
            }
        }

        private int ShortHash(string state)
        {
            if (!_stateHashes.TryGetValue(state, out int hash))
            {
                hash = Animator.StringToHash(state);
                _stateHashes.Add(state, hash);
            }

            return hash;
        }

        // HasState는 레이어 이름을 앞에 붙인 전체 경로 해시를 받는다
        private int FullHash(string state)
        {
            return Animator.StringToHash(_animator.GetLayerName(BASE_LAYER) + "." + state);
        }
    }
}
