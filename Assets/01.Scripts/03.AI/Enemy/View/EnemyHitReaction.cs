// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 내 적중에 맞은 적이 보이는 표현 한 번의 값, 서버 피해 알림과 카메라 연출 설정으로 만든다 </summary>
    public readonly struct EnemyHitCue
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 공격자에서 맞은 적으로 향하는 수평 방향 </summary>
        public Vector3 Away { get; }

        /// <summary> 굳는 프레임 수, 0이면 굳지 않는다 </summary>
        public int FreezeFrames { get; }

        /// <summary> 굳은 동안 맞은 방향 축으로 떠는 크기(m) </summary>
        public float VibrateMeters { get; }

        /// <summary> 높이가 눌리는 비율, 0이면 찌그러지지 않는다 </summary>
        public float PunchAmount { get; }

        public bool IsCritical { get; }

        public EnemyHitCue(Vector3 away, int freezeFrames, float vibrateMeters, float punchAmount, bool isCritical)
        {
            Away          = away;
            FreezeFrames  = freezeFrames;
            VibrateMeters = vibrateMeters;
            PunchAmount   = punchAmount;
            IsCritical    = isCritical;
        }
    }

    /// <summary> 맞은 적의 렉프레임 떨림과 찌그러짐을 계산하는 순수 계산기, 적 표현은 읽어 간 값만 적용한다 </summary>
    public sealed class EnemyHitReaction
    {
        // 위치와 판정은 건드리지 않는다. 떨림은 모델에만, 찌그러짐은 시각 루트 크기에만 입히고 서버 위치는 그대로 둔다.
        // 엔진 시계를 읽지 않고 시간을 넘겨 받아 EditMode에서 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const float PUNCH_SECONDS  = 0.25f;
        private const float PUNCH_CYCLES   = 2f;
        private const float PUNCH_XZ_RATIO = 0.5f; // 높이가 눌린 만큼 옆으로 퍼지는 비율
        private const float FULL_TURN      = Mathf.PI * 2f;
        private const float MIN_AXIS_SQR   = 0.0001f;

        private Vector3 _axis          = Vector3.right;
        private float   _vibrateMeters = 0f;
        private float   _punchAmount   = 0f;
        private float   _punchLeft     = 0f;
        private int     _freezeFrames  = 0;
        private int     _freezeTotal   = 0;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 이번 프레임이 렉프레임이면 참, 애니메이터를 멈춘다 </summary>
        public bool IsFrozen { get; private set; }

        /// <summary> 모델에 더할 월드 방향 떨림 오프셋 </summary>
        public Vector3 VibrateOffset { get; private set; } = Vector3.zero;

        /// <summary> 시각 루트 크기에 곱할 값, 찌그러지지 않으면 (1, 1, 1)이다 </summary>
        public Vector3 PunchScale { get; private set; } = Vector3.one;

        public bool IsActive => IsFrozen || (_freezeFrames > 0) || (_punchLeft > 0f);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 적중을 받는다. 굳는 프레임은 더 긴 쪽을 쓰고, 찌그러짐은 매번 처음부터 다시 시작한다 </summary>
        public void Begin(in EnemyHitCue cue)
        {
            Vector3 away = cue.Away;
            away.y = 0f;
            if (away.sqrMagnitude > MIN_AXIS_SQR)
            {
                _axis = away.normalized;
            }

            if (cue.FreezeFrames > _freezeFrames)
            {
                _freezeFrames = cue.FreezeFrames;
                _freezeTotal  = cue.FreezeFrames;
            }

            _vibrateMeters = Mathf.Max(0f, cue.VibrateMeters);
            _punchAmount   = Mathf.Clamp01(cue.PunchAmount);
            _punchLeft     = (_punchAmount > 0f) ? PUNCH_SECONDS : 0f;
        }

        /// <summary> 한 프레임 진행한다. 결과는 IsFrozen, VibrateOffset, PunchScale에서 읽는다 </summary>
        public void Tick(float deltaTime)
        {
            TickVibrate();
            TickPunch(deltaTime);
        }

        /// <summary> 진행 중인 반응을 모두 끝낸다 </summary>
        public void Reset()
        {
            _freezeFrames = 0;
            _freezeTotal  = 0;
            _punchLeft    = 0f;
            IsFrozen      = false;
            VibrateOffset = Vector3.zero;
            PunchScale    = Vector3.one;
        }

        // 맞은 방향 축으로 앞뒤를 번갈아 떨고 멈춤이 끝나 갈수록 줄여, 어느 쪽에서 맞았는지 읽히게 한다
        private void TickVibrate()
        {
            if (_freezeFrames <= 0)
            {
                IsFrozen      = false;
                VibrateOffset = Vector3.zero;
                return;
            }

            float decay = _freezeFrames / (float)Mathf.Max(1, _freezeTotal);
            float side  = ((_freezeFrames % 2) == 0) ? 1f : -1f;
            --_freezeFrames;

            IsFrozen      = true;
            VibrateOffset = _axis * (_vibrateMeters * decay * side);
        }

        // 높이가 눌렸다 되튀는 감쇠 진동, 눌린 만큼 옆으로 퍼져 부피가 비슷해 보이게 한다
        private void TickPunch(float deltaTime)
        {
            if (_punchLeft <= 0f)
            {
                PunchScale = Vector3.one;
                return;
            }

            _punchLeft = Mathf.Max(0f, _punchLeft - deltaTime);

            float remain = _punchLeft / PUNCH_SECONDS;
            float phase  = (1f - remain) * PUNCH_CYCLES * FULL_TURN;
            float squash = _punchAmount * remain * Mathf.Cos(phase);
            float spread = 1f + (squash * PUNCH_XZ_RATIO);

            PunchScale = new Vector3(spread, 1f - squash, spread);
        }
    }
}
