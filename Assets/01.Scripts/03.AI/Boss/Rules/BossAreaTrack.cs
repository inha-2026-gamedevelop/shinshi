// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 알림을 받아 장판 한 개의 상태를 정하는 순수 모델이다. 지속 시간은 서버가 보낸 활성 시각과 값으로 세고 피해와 대상은 계산하지 않는다
    // 장판은 서버가 활성을 알린 뒤에만 생기고, 지속 시간이 지나면 서버 알림이 없어도 사라진 것으로 보여 준다
    /// <summary> 장판 한 개의 수명과 타격 회차 표시 상태 </summary>
    public sealed class BossAreaTrack
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int NO_TICK = -1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossAreaStart _start;

        private EBossAreaFate _fate;
        private int           _lastTickIndex;
        private double        _lastTickServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public BossAreaStart Start => _start;

        public int     BossId          => _start.BossId;
        public int     AreaId          => _start.AreaId;
        public Vector3 Center          => _start.Center;
        public float   Radius          => _start.Radius;
        public float   DurationSeconds => _start.DurationSeconds;
        public float   TickInterval    => _start.TickInterval;
        public double  ActivateServerTime => _start.ActivateServerTime;
        public double  EndServerTime   => _start.EndServerTime;

        public EBossAreaFate Fate => _fate;

        /// <summary> 서버가 소멸을 알렸다 </summary>
        public bool IsEnded => _fate != EBossAreaFate.Active;

        /// <summary> 서버가 마지막으로 알린 타격 회차, 아직 없으면 음수 </summary>
        public int LastTickIndex => _lastTickIndex;

        /// <summary> 마지막 타격 표시 시각, 활성 순간이 첫 타격이라 활성 시각에서 시작한다 </summary>
        public double LastPulseServerTime => _lastTickServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public BossAreaTrack(in BossAreaStart start)
        {
            _start              = start;
            _fate               = EBossAreaFate.Active;
            _lastTickIndex      = NO_TICK;
            _lastTickServerTime = start.ActivateServerTime;
        }

        /// <summary> 스냅샷으로 복원한다 </summary>
        public static BossAreaTrack FromSnapshot(in BossAreaSnapshot snapshot)
        {
            BossAreaTrack track = new BossAreaTrack(snapshot.Start);
            track._fate = snapshot.Fate;
            if (snapshot.LastTickIndex >= 0)
            {
                track._lastTickIndex      = snapshot.LastTickIndex;
                track._lastTickServerTime = snapshot.LastTickServerTime;
            }

            return track;
        }

        public BossAreaSnapshot ToSnapshot()
        {
            return new BossAreaSnapshot
            {
                Start              = _start,
                Fate               = _fate,
                LastTickIndex      = _lastTickIndex,
                LastTickServerTime = _lastTickServerTime
            };
        }

        /// <summary> 활성 뒤 흐른 시간(초), 활성 전이면 0 </summary>
        public float GetAge(double serverNow)
        {
            return Mathf.Max(0f, (float)(serverNow - _start.ActivateServerTime));
        }

        /// <summary> 남은 수명 비율 1~0, 지속 시간이 0이면 0이다 </summary>
        public float GetLifeRatio(double serverNow)
        {
            if (_start.DurationSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - (GetAge(serverNow) / _start.DurationSeconds));
        }

        /// <summary> 지속 시간이 지났다, 서버 알림이 없어도 사라진 것으로 본다 </summary>
        public bool HasExpiredByTime(double serverNow)
        {
            return serverNow >= _start.EndServerTime;
        }

        /// <summary> 마지막 타격 표시 뒤 흐른 시간(초) </summary>
        public float GetPulseAge(double serverNow)
        {
            return Mathf.Max(0f, (float)(serverNow - _lastTickServerTime));
        }

        /// <summary> 서버가 알린 타격 회차, 이미 표시한 회차나 끝난 장판이면 거절한다 </summary>
        public bool TryTick(int index, double roundServerTime)
        {
            if (IsEnded || (index <= _lastTickIndex))
            {
                return false;
            }

            _lastTickIndex      = index;
            _lastTickServerTime = roundServerTime;
            return true;
        }

        /// <summary> 서버가 알린 소멸, 활성 중일 때만 받아들인다 </summary>
        public bool TryEnd(EBossAreaFate fate)
        {
            if ((fate == EBossAreaFate.Active) || IsEnded)
            {
                return false;
            }

            _fate = fate;
            return true;
        }

        /// <summary> 즉시 제거 </summary>
        public bool TryCancel()
        {
            return TryEnd(EBossAreaFate.Cancelled);
        }
    }
}
