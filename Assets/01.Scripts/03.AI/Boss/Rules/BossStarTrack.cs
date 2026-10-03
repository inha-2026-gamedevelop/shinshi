// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 알림을 받아 별형 빔 구조 하나의 상태를 정하는 순수 모델이다. 서버가 활성을 알리기 전에는 예고 시간이 끝나도 회전과 위험 빔이 시작되지 않는다
    // 회전각은 서버가 보낸 활성 시각과 속도로 현재 서버 시각에서 직접 계산한다. 늦게 받은 시작과 복원도 같은 식이라 처음부터 다시 돌지 않는다
    /// <summary> 별형 빔 구조 하나의 예고, 활성, 회전 표시 상태 </summary>
    public sealed class BossStarTrack
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    NO_TICK       = -1;
        private const double GRACE_SECONDS = 0.5; // 예고가 끝난 뒤 서버 활성을 더 기다리는 시간

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossStarStart _start;

        private bool          _isActivated;
        private double        _activateServerTime;
        private EBossAreaFate _fate;
        private int           _lastTickIndex;
        private double        _lastTickServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public BossStarStart Start => _start;

        public int StarId => _start.StarId;
        public int CastId => _start.CastId;

        public bool IsActivated => _isActivated;

        public double ActivateServerTime => _activateServerTime;

        /// <summary> 활성된 구조가 끝나는 서버 시각, 활성 전에는 의미가 없다 </summary>
        public double EndServerTime => _activateServerTime + _start.DurationSeconds;

        public EBossAreaFate Fate => _fate;

        public bool IsEnded => _fate != EBossAreaFate.Active;

        public int LastTickIndex => _lastTickIndex;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarTrack(in BossStarStart start)
        {
            _start         = start;
            _fate          = EBossAreaFate.Active;
            _lastTickIndex = NO_TICK;
        }

        public static BossStarTrack FromSnapshot(in BossStarSnapshot snapshot)
        {
            BossStarTrack track = new BossStarTrack(snapshot.Start);
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

        public BossStarSnapshot ToSnapshot()
        {
            return new BossStarSnapshot
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

        /// <summary> 현재 서버 시각의 회전각(도), 활성 전에는 0이다. 유지 시간이 지나면 마지막 각도에서 멈춘다 </summary>
        public float GetRotationDegrees(double serverNow)
        {
            if (!_isActivated)
            {
                return 0f;
            }

            BossStarShape shape = _start.Shape;
            return BossStarGeometry.RotationAt(in shape, serverNow - _activateServerTime, _start.DurationSeconds);
        }

        public float GetTelegraphRatio(double serverNow)
        {
            if (_start.TelegraphSeconds <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)((serverNow - _start.StartServerTime) / _start.TelegraphSeconds));
        }

        public float GetLifeRatio(double serverNow)
        {
            if ((!_isActivated) || (_start.DurationSeconds <= 0f))
            {
                return 1f;
            }

            return Mathf.Clamp01(1f - (float)((serverNow - _activateServerTime) / _start.DurationSeconds));
        }

        public float GetPulseAge(double serverNow)
        {
            return Mathf.Max(0f, (float)(serverNow - _lastTickServerTime));
        }

        /// <summary> 서버가 알린 활성, 한 번만 받아들인다 </summary>
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
