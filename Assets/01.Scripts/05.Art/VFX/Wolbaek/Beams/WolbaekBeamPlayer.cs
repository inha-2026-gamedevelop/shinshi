// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 외부 확정 선 데이터만 표시한다. 배열·취소 tombstone은 한 cast당 최대 32개다 </summary>
    public sealed class WolbaekBeamPlayer : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   RECORD_CAPACITY = 32;
        private const float MIN_SIZE        = 0.001f;

        [SerializeField] private EWolbaekBeamKind     _kind;
        [SerializeField] private WolbaekBeamSettingsSO _settings;
        [SerializeField] private WolbaekBeamLineView[] _views;

        private struct LineRecord
        {
            public int    Id;
            public uint   Revision;
            public int    Slot;
            public double WarningStart;
            public double ExpectedActive;
            public double ActiveStart;
            public double End;
            public bool   Confirmed;
        }

        private LineRecord[] _records;
        private int          _count;
        private uint         _playbackId;
        private bool         _running;
        private double       _lastNow;
        private float _appear, _fade, _grace, _cycle, _height, _veil, _core;

        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekBeamKind Kind         => _kind;
        public WolbaekBeamSettingsSO Settings => _settings;
        public uint PlaybackId               => _playbackId;
        public bool IsRunning                => _running;
        public ulong CastId         { get; private set; }
        public int VisibleLineCount { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _records = new LineRecord[RECORD_CAPACITY];
            if (_views != null)
            {
                for (int i = 0; i < _views.Length; ++i)
                {
                    _views[i].Initialize();
                }
            }

            Stop();
        }

        protected override void OnServerBuildAwake()
        {
            Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        /****************************************
        *                Methods
        ****************************************/

        public uint BeginCast(ulong castId, double now)
        {
            if ((IsServerBuild) || (_records == null) || (_settings == null)
                || (!gameObject.activeInHierarchy) || (!Finite(now)))
            {
                return 0;
            }

            Stop();
            ++_playbackId;
            if (_playbackId == 0)
            {
                ++_playbackId;
            }

            CastId   = castId;
            _lastNow = now;
            _running = true;
            _appear  = Positive(_settings.AppearSeconds);
            _fade    = Positive(_settings.FadeSeconds);
            _grace   = Mathf.Max(0f, Safe(_settings.ConfirmationGrace));
            _cycle   = Positive(_settings.FlowCycle);
            _height  = Positive(_settings.Height);
            _veil    = Mathf.Clamp01(Safe(_settings.VeilOpacity));
            _core    = Mathf.Clamp01(Safe(_settings.CoreOpacity));
            return _playbackId;
        }

        /// <summary> revision은 호출자가 정한 단조 증가 선 버전이다. 취소한 같은 revision은 부활하지 않는다 </summary>
        public bool Warn(uint playbackId, int lineId, uint revision, WolbaekBeamShape shape,
            double warningStart, double expectedActive, double expectedEnd, double now)
        {
            if ((!Current(playbackId, now)) || (revision == 0) || (!Valid(shape)) || (!Finite(warningStart))
                || (!Finite(expectedActive)) || (!Finite(expectedEnd)) || (expectedActive < warningStart)
                || (expectedEnd < expectedActive))
            {
                return false;
            }

            int index = Find(lineId);
            if ((index >= 0) && (revision <= _records[index].Revision))
            {
                return false;
            }

            if ((index < 0) && (_count >= RECORD_CAPACITY))
            {
                return false;
            }

            int slot = index >= 0 ? _records[index].Slot : -1;
            if (slot < 0)
            {
                slot = FreeSlot();
                if (slot < 0)
                {
                    return false;
                }
            }

            if (index < 0)
            {
                index = _count;
                ++_count;
            }

            _views[slot].Hide();
            _views[slot].SetGeometry(shape, _height);
            _records[index] = new LineRecord
            {
                Id             = lineId,
                Revision       = revision,
                Slot           = slot,
                WarningStart   = warningStart,
                ExpectedActive = expectedActive,
                End            = expectedEnd
            };

            Tick(playbackId, now);
            return true;
        }

        public bool ConfirmActive(uint playbackId, int lineId, uint revision,
            double confirmedTime, double endTime, double now)
        {
            int index = Find(lineId);
            if ((!Current(playbackId, now)) || (index < 0) || (!Finite(confirmedTime)) || (!Finite(endTime)))
            {
                return false;
            }

            ref LineRecord record = ref _records[index];
            if ((record.Revision != revision) || (record.Slot < 0) || (record.Confirmed) || (confirmedTime > now)
                || (confirmedTime < record.WarningStart) || (endTime < confirmedTime)
                || (now > record.ExpectedActive + _grace))
            {
                return false;
            }

            record.Confirmed   = true;
            record.ActiveStart = confirmedTime;
            record.End         = endTime;
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 서버가 확정한 끝점을 즉시 적용한다. Star 예고는 고정이고 활성 후에만 회전 입력을 받는다 </summary>
        public bool UpdateGeometry(uint playbackId, int lineId, uint revision, WolbaekBeamShape shape, double now)
        {
            int index = Find(lineId);
            if ((!Current(playbackId, now)) || (index < 0) || (!Valid(shape)))
            {
                return false;
            }

            ref LineRecord record = ref _records[index];
            if ((record.Revision != revision) || (record.Slot < 0)
                || ((!record.Confirmed) && (_kind != EWolbaekBeamKind.Release)))
            {
                return false;
            }

            _views[record.Slot].SetGeometry(shape, _height);
            _lastNow = now;
            return true;
        }

        public void Tick(uint playbackId, double now)
        {
            if (!Current(playbackId, now))
            {
                return;
            }

            _lastNow         = now;
            VisibleLineCount = 0;
            int next = -1;
            if (_kind == EWolbaekBeamKind.Grid)
            {
                for (int i = 0; i < _count; ++i)
                {
                    ref LineRecord r = ref _records[i];
                    if ((r.Slot >= 0) && (!r.Confirmed) && (now >= r.WarningStart)
                        && (now <= r.ExpectedActive + _grace)
                        && ((next < 0) || (r.ExpectedActive < _records[next].ExpectedActive)))
                    {
                        next = i;
                    }
                }
            }

            for (int i = 0; i < _count; ++i)
            {
                ref LineRecord r = ref _records[i];
                if (r.Slot < 0)
                {
                    continue;
                }

                if (((r.Confirmed) && (now >= r.End)) || ((!r.Confirmed) && (now > r.ExpectedActive + _grace)))
                {
                    _views[r.Slot].Hide();
                    r.Slot = -1;
                    continue;
                }

                bool visible = (now >= (r.Confirmed ? r.ActiveStart : r.WarningStart))
                    && ((_kind != EWolbaekBeamKind.Grid) || (r.Confirmed) || (i == next));
                if (!visible)
                {
                    _views[r.Slot].Hide();
                    continue;
                }

                if (r.Confirmed)
                {
                    _views[r.Slot].Sample((float)(now - r.ActiveStart), (float)(r.End - r.ActiveStart),
                        _kind == EWolbaekBeamKind.Release, _appear, _fade, _cycle, _veil, _core);
                }
                else
                {
                    _views[r.Slot].ShowWarning(_kind == EWolbaekBeamKind.Star ? 0.08f : 0.52f);
                }

                ++VisibleLineCount;
            }
        }

        public bool StopLine(uint playbackId, int lineId, uint revision)
        {
            int index = Find(lineId);
            if ((!_running) || (playbackId != _playbackId) || (index < 0) || (_records[index].Revision != revision))
            {
                return false;
            }

            ref LineRecord r = ref _records[index];
            if (r.Slot < 0)
            {
                return false;
            }

            if (_views[r.Slot].IsVisible)
            {
                VisibleLineCount = Mathf.Max(0, VisibleLineCount - 1);
            }

            _views[r.Slot].Hide();
            r.Slot = -1;
            return true;
        }

        public bool Stop(uint playbackId)
        {
            if ((!_running) || (playbackId != _playbackId))
            {
                return false;
            }

            Stop();
            return true;
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            _running         = false;
            _count           = 0;
            VisibleLineCount = 0;
            if (_views != null)
            {
                for (int i = 0; i < _views.Length; ++i)
                {
                    if (_views[i] != null)
                    {
                        _views[i].Hide();
                    }
                }
            }
        }

        private int Find(int id)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_records[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FreeSlot()
        {
            for (int slot = 0; slot < _views.Length; ++slot)
            {
                bool used = false;
                for (int i = 0; i < _count; ++i)
                {
                    if (_records[i].Slot == slot)
                    {
                        used = true;
                    }
                }

                if (!used)
                {
                    return slot;
                }
            }

            return -1;
        }

        private bool Current(uint id, double now) => (!_running ? false : (!IsServerBuild)
            && (id == _playbackId) && (Finite(now)) && (now >= _lastNow));

        private static bool Valid(WolbaekBeamShape shape)
        {
            return (Finite(shape.Start)) && (Finite(shape.End)) && (Finite(shape.Width)) && (shape.Width >= MIN_SIZE)
                && ((new Vector2(shape.End.x - shape.Start.x, shape.End.z - shape.Start.z)).sqrMagnitude > MIN_SIZE * MIN_SIZE)
                && (Finite(shape.ArenaCenter)) && (Finite(shape.ArenaRadius)) && (shape.ArenaRadius >= 0f);
        }

        private static bool Finite(Vector3 p) => (Finite(p.x)) && (Finite(p.y)) && (Finite(p.z));
        private static bool Finite(double n) => (!double.IsNaN(n)) && (!double.IsInfinity(n));
        private static float Safe(float n) => Finite(n) ? n : 0f;
        private static float Positive(float n) => Mathf.Max(MIN_SIZE, Safe(n));
    }
}
