// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 서버가 보낸 피해 알림이 마검사의 어느 기술에서 나왔는지 시전 시각으로 짐작하는 순수 분류기 </summary>
    public sealed class MageknightHitClassifier
    {
        // 피해 알림에는 어느 기술인지가 없다. 검파는 시전 확정부터 사거리를 날아가는 동안, 섬광 일섬은 발동 구간이 끝나는 순간에
        // 피해가 들어가므로 그 시간대에 도착한 알림을 해당 기술의 것으로 본다. 서버 시각끼리만 비교한다

        /****************************************
        *                Fields
        ****************************************/

        private const double NO_TIME = double.NegativeInfinity;

        private int    _waveCastId   = 0;
        private int    _blinkCastId  = 0;
        private double _waveResolveAt  = NO_TIME;
        private double _blinkResolveAt = NO_TIME;
        private float  _waveSeconds;
        private float  _blinkSeconds;
        private float  _margin;

        /// <summary> 알림이 속한 기술 </summary>
        public enum EKind
        {
            Other,
            Wave,
            Blink
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 검파가 날아가는 시간, 섬광 일섬 발동 구간 길이, 시각 오차 여유를 정한다 </summary>
        public void Configure(float waveSeconds, float blinkSeconds, float marginSeconds)
        {
            _waveSeconds  = Mathf.Max(0f, waveSeconds);
            _blinkSeconds = Mathf.Max(0f, blinkSeconds);
            _margin       = Mathf.Max(0f, marginSeconds);
        }

        /// <summary> 새 시전을 알린다. 같은 시전 번호를 다시 알려도 무시한다 </summary>
        public void ObserveCast(bool isWave, bool isBlink, int castId, double resolveAt)
        {
            if ((isWave) && (castId != _waveCastId))
            {
                _waveCastId    = castId;
                _waveResolveAt = resolveAt;
            }
            else if ((isBlink) && (castId != _blinkCastId))
            {
                _blinkCastId    = castId;
                _blinkResolveAt = resolveAt;
            }
        }

        public void Reset()
        {
            _waveCastId     = 0;
            _blinkCastId    = 0;
            _waveResolveAt  = NO_TIME;
            _blinkResolveAt = NO_TIME;
        }

        /// <summary> 서버 시각에 도착한 피해 알림이 어느 기술의 것인지 돌려준다. 둘 다 해당하면 나중에 시전한 쪽이다 </summary>
        public EKind Classify(double serverTime)
        {
            bool isWave  = IsWithin(serverTime, _waveResolveAt, _waveSeconds);
            bool isBlink = IsWithin(serverTime, _blinkResolveAt, _blinkSeconds);
            if ((isWave) && (isBlink))
            {
                return (_blinkResolveAt > _waveResolveAt) ? EKind.Blink : EKind.Wave;
            }

            if (isWave)
            {
                return EKind.Wave;
            }

            return isBlink ? EKind.Blink : EKind.Other;
        }

        private bool IsWithin(double serverTime, double resolveAt, float seconds)
        {
            return (resolveAt > NO_TIME) && (serverTime >= (resolveAt - _margin)) && (serverTime <= (resolveAt + seconds + _margin));
        }
    }
}
