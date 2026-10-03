// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 알림을 받아 빔 한 줄의 상태를 정하는 순수 모델이다. 서버가 활성을 알리기 전에는 예고 시간이 끝나도 지속 빔이 되지 않는다
    // 지속 시간은 서버가 보낸 활성 시각과 값으로 세고, 종료 알림이 없어도 지속 시간이 지나면 사라진 것으로 보여 준다. 피해와 대상은 계산하지 않는다
    // 종료 이유는 장판과 같은 값(EBossAreaFate)을 쓴다
    /// <summary> 빔 한 줄의 예고, 활성, 타격 회차 표시 상태 </summary>
    public sealed class BossBeamTrack
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    NO_TICK       = -1;
        private const double GRACE_SECONDS = 0.5; // 예고가 끝난 뒤 서버 활성을 더 기다리는 시간

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossBeamStart _start;

        private bool           _isActivated;
        private double         _activateServerTime;
        private EBossAreaFate  _fate;
        private int            _lastTickIndex;
        private double         _lastTickServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public BossBeamStart Start => _start;

        public int BeamId => _start.BeamId;
        public int CastId => _start.CastId;

        public bool IsActivated => _isActivated;

        public double ActivateServerTime => _activateServerTime;

        /// <summary> 활성된 빔이 끝나는 서버 시각, 활성 전에는 의미가 없다 </summary>
        public double EndServerTime => _activateServerTime + _start.DurationSeconds;

        public EBossAreaFate Fate => _fate;

        /// <summary> 서버가 소멸을 알렸다 </summary>
        public bool IsEnded => _fate != EBossAreaFate.Active;

        public int LastTickIndex => _lastTickIndex;

        public double LastPulseServerTime => _lastTickServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public BossBeamTrack(in BossBeamStart start)
        {
            _start         = start;
            _fate          = EBossAreaFate.Active;
            _lastTickIndex = NO_TICK;
        }

        public static BossBeamTrack FromSnapshot(in BossBeamSnapshot snapshot)
        {
            BossBeamTrack track = new BossBeamTrack(snapshot.Start);
            track._fate = snapshot.Fate;
            if (snapshot.IsActivated)
            {
                track._isActivated        = true;
                track._activateServerTime = snapshot.ActivateServerTime;
                track._lastTickServerTime = snapshot.ActivateServerTime;
            }

            if (snapshot.LastTickIndex >= 0)
            {
                track._lastTickIndex      = snapshot.LastTickIndex;
                track._lastTickServerTime = snapshot.LastTickServerTime;
            }

            return track;
        }

        public BossBeamSnapshot ToSnapshot()
        {
            return new BossBeamSnapshot
            {
                Start              = _start,
                IsActivated        = _isActivated,
                ActivateServerTime = _activateServerTime,
                Fate               = _fate,
                LastTickIndex      = _lastTickIndex,
                LastTickServerTime = _lastTickServerTime
            };
        }

        public EBossBeamPhase GetPhase(double serverNow)
        {
            if (IsEnded)
            {
                return EBossBeamPhase.Ended;
            }

            if (!_isActivated)
            {
                return (serverNow >= (_start.TelegraphEndServerTime + GRACE_SECONDS)) ? EBossBeamPhase.Ended : EBossBeamPhase.Telegraph;
            }

            return (serverNow >= EndServerTime) ? EBossBeamPhase.Ended : EBossBeamPhase.Active;
        }

        /// <summary> 예고 진행 비율 0~1 </summary>
        public float GetTelegraphRatio(double serverNow)
        {
            if (_start.TelegraphSeconds <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)((serverNow - _start.StartServerTime) / _start.TelegraphSeconds));
        }

        /// <summary> 남은 수명 비율 1~0, 활성 전이면 1이다 </summary>
        public float GetLifeRatio(double serverNow)
        {
            if ((!_isActivated) || (_start.DurationSeconds <= 0f))
            {
                return 1f;
            }

            return Mathf.Clamp01(1f - (float)((serverNow - _activateServerTime) / _start.DurationSeconds));
        }

        /// <summary> 마지막 타격 표시 뒤 흐른 시간(초) </summary>
        public float GetPulseAge(double serverNow)
        {
            return Mathf.Max(0f, (float)(serverNow - _lastTickServerTime));
        }

        /// <summary> 서버가 알린 활성, 한 번만 받아들인다. 예고 대기 시간이 지났어도 서버 확정이면 받아들인다 </summary>
        public bool TryActivate(double activateServerTime)
        {
            if (_isActivated || IsEnded)
            {
                return false;
            }

            _isActivated        = true;
            _activateServerTime = activateServerTime;
            _lastTickServerTime = activateServerTime;
            return true;
        }

        /// <summary> 서버가 알린 타격 회차, 활성 전이거나 이미 표시한 회차나 끝난 빔이면 거절한다 </summary>
        public bool TryTick(int index, double roundServerTime)
        {
            if ((!_isActivated) || IsEnded || (index <= _lastTickIndex))
            {
                return false;
            }

            _lastTickIndex      = index;
            _lastTickServerTime = roundServerTime;
            return true;
        }

        /// <summary> 서버가 알린 소멸, 한 번만 받아들인다 </summary>
        public bool TryEnd(EBossAreaFate fate)
        {
            if ((fate == EBossAreaFate.Active) || IsEnded)
            {
                return false;
            }

            _fate = fate;
            return true;
        }

        public bool TryCancel()
        {
            return TryEnd(EBossAreaFate.Cancelled);
        }
    }
}
