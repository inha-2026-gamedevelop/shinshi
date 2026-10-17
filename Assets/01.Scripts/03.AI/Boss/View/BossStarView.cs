// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 서버가 확정한 별형 회전 빔을 화면에 그리는 표현이다. 구조 하나(여우불 꼭짓점과 연결 빔 전체)가 한 건이고 구조 번호로만 찾는다
    // 클라이언트는 좌우 추첨, 기준 각도 계산, 타깃 추적, 피해 판정을 하지 않는다. 받은 배치와 회전 방향과 속도와 활성 시각으로 현재 서버 시각의 각도를 그린다
    // 서버가 활성을 알리기 전에는 예고(정지한 별과 회전 방향을 알리는 여우불 맥동)만 보이고 예고 시간이 끝나는 것만으로는 회전하지 않는다
    // 이 클래스는 서버 어셈블리와 Mirror에 의존하지 않는다
    /// <summary> 별형 회전 빔의 예고, 활성, 회전, 취소, 복원을 구조 번호별로 관리한다 </summary>
    public sealed class BossStarView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float CONFIRM_GRACE_SECONDS = 0.5f; // 예고가 끝난 뒤 서버 확정을 더 기다리는 시간
        private const float FADE_SECONDS          = 0.3f; // 사라질 때 옅어지는 시간
        private const int   ENDED_HISTORY         = 32;   // 끝난 구조 번호를 기억하는 수, 늦게 온 알림이 되살리지 못하게 한다
        private const int   DEFAULT_CAPACITY      = 2;
        private const string CONTROLLER_NAME      = "StarGroundVFX";

        /****************************************
        *                Fields
        ****************************************/

        private sealed class Entry
        {
            public BossStarTrack           Track;
            public StarGroundVFXController Visual;
            public bool                    EndSeen;
            public double                  EndSeenAt;
        }

        [SerializeField] private Material _beamMaterial; // 비어 있으면 기본 재질로 보인다
        [SerializeField] private Material _orbMaterial;
        [SerializeField, Min(1)] private int _capacity = DEFAULT_CAPACITY;

        private readonly List<Entry>   _entries = new List<Entry>();
        private readonly Stack<StarGroundVFXController> _free = new Stack<StarGroundVFXController>();
        private readonly HashSet<int>  _endedIds   = new HashSet<int>();
        private readonly Queue<int>    _endedOrder = new Queue<int>();
        private readonly Dictionary<int, double> _pendingActivations = new Dictionary<int, double>();
        private readonly Queue<int>              _pendingOrder       = new Queue<int>();
        private readonly Dictionary<int, BossStarStart> _expiredStarts = new Dictionary<int, BossStarStart>();
        private readonly Queue<int>                     _expiredOrder  = new Queue<int>();

        private Func<double> _serverClock;
        private bool         _isServerBuild;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 진행 중인 구조 수(예고 중과 활성, 옅어지는 중 포함) </summary>
        public int Count => _entries.Count;

        /// <summary> 빌려줄 수 있는 표현 수, 시험용이다 </summary>
        public int FreeCount => _free.Count;

        /// <summary> 참이면 기본 별형 표현을 그리지 않고 서버 사건 기록만 유지한다, 다른 표현이 그릴 때 쓴다 </summary>
        public bool HasExternalVisual { get; set; }

        /// <summary> 소멸이 확인될 때 알린다, 구조 번호와 이유. 시험과 진단용이다 </summary>
        public event Action<int, EBossAreaFate> FateChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnServerBuildAwake()
        {
            _isServerBuild = true;
        }

        protected override void OnViewAwake()
        {
            for (int i = 0; i < _capacity; ++i)
            {
                GameObject go = new GameObject(CONTROLLER_NAME);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                StarGroundVFXController visual = go.AddComponent<StarGroundVFXController>();
                visual.Initialize(_beamMaterial, _orbMaterial);
                go.SetActive(false);
                _free.Push(visual);
            }
        }

        private void Update()
        {
            double now = ReadServerTime();
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                if (UpdateEntry(_entries[i], now))
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        private void OnDisable()
        {
            ClearAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 예고를 시작한다. 이미 있는 구조 번호나 끝난 번호면 무시한다. 활성이 먼저 와 있었으면 이어서 활성으로 그린다 </summary>
        public bool Begin(in BossStarStart start)
        {
            int id = start.StarId;
            if (_isServerBuild || IsKnown(id))
            {
                return false;
            }

            BossStarTrack track = new BossStarTrack(start);
            if (_pendingActivations.TryGetValue(id, out double activateTime))
            {
                _pendingActivations.Remove(id);
                track.TryActivate(activateTime);
            }

            return TryAdd(track);
        }

        /// <summary> 서버가 알린 활성, 서버 확정이 있어야만 회전을 시작한다. 예고가 없으면 활성 시각을 기억해 두었다가 예고가 오면 쓴다 </summary>
        public bool Activate(int starId, double activateServerTime)
        {
            if (_isServerBuild || _endedIds.Contains(starId))
            {
                return false;
            }

            if (TryFind(starId, out Entry entry))
            {
                return entry.Track.TryActivate(activateServerTime);
            }

            if (_expiredStarts.TryGetValue(starId, out BossStarStart start))
            {
                _expiredStarts.Remove(starId);
                BossStarTrack revived = new BossStarTrack(start);
                revived.TryActivate(activateServerTime);
                return TryAdd(revived);
            }

            if (_pendingActivations.ContainsKey(starId))
            {
                return false;
            }

            _pendingActivations[starId] = activateServerTime;
            _pendingOrder.Enqueue(starId);
            while (_pendingOrder.Count > ENDED_HISTORY)
            {
                _pendingActivations.Remove(_pendingOrder.Dequeue());
            }

            return false;
        }

        public bool Tick(int starId, int hitIndex, double roundServerTime)
        {
            return TryFind(starId, out Entry entry) && entry.Track.TryTick(hitIndex, roundServerTime);
        }

        /// <summary> 소멸, Expired는 옅어지며 사라지고 Cancelled는 즉시 지운다. 모르는 번호면 나중에 늦게 오는 예고와 활성이 되살아나지 않게 기억해 둔다 </summary>
        public bool End(int starId, EBossAreaFate fate)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (fate == EBossAreaFate.Cancelled)
            {
                return Cancel(starId);
            }

            if (!TryFind(starId, out Entry entry))
            {
                RememberEnded(starId);
                return false;
            }

            if (!entry.Track.TryEnd(fate))
            {
                return false;
            }

            FateChanged?.Invoke(starId, fate);
            return true;
        }

        /// <summary> 구조 제거(활성 전 취소, 사망, 페이즈 전환, 전투 종료), 표시는 바로 지운다 </summary>
        public bool Cancel(int starId)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (!TryFind(starId, out Entry entry))
            {
                RememberEnded(starId);
                return false;
            }

            if (!entry.Track.TryCancel())
            {
                return false;
            }

            Remove(entry);
            FateChanged?.Invoke(starId, EBossAreaFate.Cancelled);
            return true;
        }

        /// <summary> 진행 중인 구조를 스냅샷으로 복원한다. 처음부터 다시 돌리지 않고 서버 시각의 현재 각도부터 이어서 그린다 </summary>
        public bool Restore(in BossStarSnapshot snapshot)
        {
            int id = snapshot.Start.StarId;
            if (_isServerBuild || IsKnown(id))
            {
                return false;
            }

            if (snapshot.Fate == EBossAreaFate.Cancelled)
            {
                RememberEnded(id);
                return false;
            }

            return TryAdd(BossStarTrack.FromSnapshot(in snapshot));
        }

        public bool TryGetSnapshot(int starId, out BossStarSnapshot snapshot)
        {
            if (TryFind(starId, out Entry entry))
            {
                snapshot = entry.Track.ToSnapshot();
                return true;
            }

            snapshot = default;
            return false;
        }

        /// <summary> 진행 중인 구조의 모델과 그리는 대상, 시험과 진단용이다. 표현이 모자랐으면 visual은 null이다 </summary>
        public bool TryGet(int starId, out BossStarTrack track, out StarGroundVFXController visual)
        {
            if (TryFind(starId, out Entry entry))
            {
                track  = entry.Track;
                visual = entry.Visual;
                return true;
            }

            track  = null;
            visual = null;
            return false;
        }

        /// <summary> 진행 중인 표현을 모두 즉시 지우고 돌려준다. 장면 전환이나 보스전 종료 때 부른다 </summary>
        public void ClearAll()
        {
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                _entries[i].Track.TryCancel();
                Remove(_entries[i]);
            }
        }

        /// <summary> 새 네트워크 관찰의 복원을 위해 이전 연결의 종료, 지연 활성 기록을 비운다 </summary>
        public void ResetNetworkSession()
        {
            ClearAll();
            _endedIds.Clear();
            _endedOrder.Clear();
            _pendingActivations.Clear();
            _pendingOrder.Clear();
            _expiredStarts.Clear();
            _expiredOrder.Clear();
        }

        // 이미 끝난 구조(서버 확정 없이 예고 대기 시간이 지났거나 소멸 연출까지 지난 것)는 그리지 않는다
        private bool TryAdd(BossStarTrack track)
        {
            double now = ReadServerTime();
            bool isGone = track.IsActivated
                ? (now >= (track.EndServerTime + FADE_SECONDS))
                : (now >= (track.Start.TelegraphEndServerTime + CONFIRM_GRACE_SECONDS));

            if (track.IsEnded || isGone)
            {
                if (track.IsActivated || track.IsEnded)
                {
                    RememberEnded(track.StarId);
                }
                else
                {
                    RememberExpired(track.Start);
                }

                return false;
            }

            Entry entry = new Entry { Track = track };
            if ((_free.Count > 0) && (!HasExternalVisual))
            {
                entry.Visual = _free.Pop();
                entry.Visual.gameObject.SetActive(true);
                BossStarShape shape = track.Start.Shape;
                entry.Visual.Show(shape.ArenaCenter, shape.BaseDirection, shape.RingRadius, shape.VertexCount, shape.ConnectStep,
                    shape.BeamWidth, shape.RotatesRight);
            }

            _entries.Add(entry);
            return true;
        }

        private bool UpdateEntry(Entry entry, double now)
        {
            BossStarTrack track = entry.Track;

            if ((!track.IsActivated) && (!track.IsEnded)
                && (now >= (track.Start.TelegraphEndServerTime + CONFIRM_GRACE_SECONDS)))
            {
                ReleaseVisual(entry);
                RememberExpired(track.Start);
                return true;
            }

            if ((!entry.EndSeen) && (track.IsEnded || (track.IsActivated && (now >= track.EndServerTime))))
            {
                entry.EndSeen   = true;
                entry.EndSeenAt = now;
            }

            float fadeAge = entry.EndSeen ? Mathf.Max(0f, (float)(now - entry.EndSeenAt)) : 0f;
            float fade    = entry.EndSeen ? (1f - Mathf.Clamp01(fadeAge / FADE_SECONDS)) : 1f;
            bool  isDone  = entry.EndSeen && (fadeAge >= FADE_SECONDS);

            if (entry.Visual != null)
            {
                float progress = track.IsActivated ? track.GetLifeRatio(now) : track.GetTelegraphRatio(now);
                entry.Visual.Render(track.IsActivated, progress, track.GetPulseAge(now), fade, track.GetRotationDegrees(now), now);
            }

            if (isDone)
            {
                Remove(entry, false);
            }

            return isDone;
        }

        private void ReleaseVisual(Entry entry)
        {
            if (entry.Visual != null)
            {
                entry.Visual.Hide();
                entry.Visual.gameObject.SetActive(false);
                _free.Push(entry.Visual);
                entry.Visual = null;
            }
        }

        private void Remove(Entry entry, bool removeFromList = true)
        {
            ReleaseVisual(entry);
            if (removeFromList)
            {
                _entries.Remove(entry);
            }

            RememberEnded(entry.Track.StarId);
        }

        private bool TryFind(int starId, out Entry entry)
        {
            for (int i = 0; i < _entries.Count; ++i)
            {
                if (_entries[i].Track.StarId == starId)
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsKnown(int starId)
        {
            return _endedIds.Contains(starId) || _expiredStarts.ContainsKey(starId) || TryFind(starId, out Entry _);
        }

        // 끝난 구조 번호를 기억한다. 기다리던 활성과 서버 확정 없이 지운 예고도 함께 버려 되살아나지 않게 한다
        private void RememberEnded(int starId)
        {
            _pendingActivations.Remove(starId);
            _expiredStarts.Remove(starId);

            if (!_endedIds.Add(starId))
            {
                return;
            }

            _endedOrder.Enqueue(starId);
            while (_endedOrder.Count > ENDED_HISTORY)
            {
                _endedIds.Remove(_endedOrder.Dequeue());
            }
        }

        private void RememberExpired(in BossStarStart start)
        {
            if (_expiredStarts.ContainsKey(start.StarId))
            {
                return;
            }

            _expiredStarts[start.StarId] = start;
            _expiredOrder.Enqueue(start.StarId);
            while (_expiredOrder.Count > ENDED_HISTORY)
            {
                _expiredStarts.Remove(_expiredOrder.Dequeue());
            }
        }

        private double ReadServerTime()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }
    }
}
