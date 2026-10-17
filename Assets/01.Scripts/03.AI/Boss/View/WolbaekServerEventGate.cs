// System
using System;
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 기믹 사건의 서버 시각이 클라이언트가 추정한 지금보다 조금 앞서 도착하면(시계 동기 오차) 버리지 않고 그 서버 시각까지 보류했다가 처리한다.
    // 보류 중인 사건이 있으면 뒤에 온 사건도 같은 줄에 세워 도착 순서를 지킨다. 한계보다 먼 미래 시각은 잘못된 사건으로 보고 버린다
    /// <summary> 서버 시각이 아직 오지 않은 기믹 사건을 그 시각까지 미뤄 순서대로 처리하는 보류 줄 </summary>
    public sealed class WolbaekServerEventGate
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double MAX_FUTURE_SECONDS = 0.5; // 이보다 먼 미래 시각은 시계 오차가 아니라 잘못된 사건이다, 기본 예고의 확정 대기와 같은 크기

        /****************************************
        *                Fields
        ****************************************/

        private readonly Queue<Pending> _pending = new Queue<Pending>();
        private double _lastDue;
        private bool   _isFlushing;

        private struct Pending
        {
            public double Due;
            public Action Action;
        }

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _pending.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 처리하면 안 되는 사건이면 참이다. 참이면 처리하지 말고 Defer로 넘긴다 </summary>
        public bool MustDefer(double serverTime, double now)
        {
            // 보류했던 사건을 다시 처리하는 중이면 같은 검사를 통과시킨다
            if (_isFlushing)
            {
                return false;
            }

            return (serverTime > now) || (_pending.Count > 0);
        }

        /// <summary> MustDefer가 참인 사건을 그 서버 시각까지 보류한다. 한계보다 먼 미래 시각이면 버린다 </summary>
        public void Defer(double serverTime, double now, Action replay)
        {
            if (serverTime > (now + MAX_FUTURE_SECONDS))
            {
                return;
            }

            double due = Math.Max(serverTime, _lastDue);
            _lastDue = due;
            _pending.Enqueue(new Pending { Due = due, Action = replay });
        }

        /// <summary> 매 프레임 부른다. 서버 시각이 된 사건을 들어온 순서대로 처리한다 </summary>
        public void Flush(double now)
        {
            while ((_pending.Count > 0) && (_pending.Peek().Due <= now))
            {
                Pending next = _pending.Dequeue();
                _isFlushing = true;
                try
                {
                    next.Action();
                }
                finally
                {
                    _isFlushing = false;
                }
            }

            if (_pending.Count == 0)
            {
                _lastDue = 0d;
            }
        }

        /// <summary> 정리, 실패, 새 전투에서 보류 중인 지난 사건을 버린다 </summary>
        public void Clear()
        {
            // 보류했던 시작 사건을 처리하다 그 시작이 표현을 비우는 경우에는 뒤에 줄 선 같은 시전의 사건을 지우지 않는다
            if (_isFlushing)
            {
                return;
            }

            _pending.Clear();
            _lastDue = 0d;
        }
    }
}
