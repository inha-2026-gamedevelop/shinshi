// System
using System;

namespace Shinshi.AI
{
    // 수평 방향(요) 하나를 목표 쪽으로 부드럽게 옮기는 계산이다. 엔진 객체를 쓰지 않아 서버 패턴과 시험 구동기의 몸 표현이 같이 쓴다.
    // 회전 방향: 현재 요에서 목표 요까지의 부호 있는 최단 각도(-180 초과 180 이하)로 돈다. 정확히 180도 맞은편이면 오른쪽(+)을 고르고,
    // 이미 돌고 있으면 그 방향을 유지한다. 돌던 방향의 남은 각도가 180도에 히스테리시스를 더한 값 이하이면 목표가 뒤쪽 경계를 넘어도
    // 방향을 뒤집지 않아, 맞은편 근처에서 목표가 왔다 갔다 해도 매 틱 방향이 바뀌어 떨지 않는다. 그보다 더 긴 길이 되면 짧은 쪽으로 바꾼다.
    // 속도: 남은 각도와 도착 시각까지의 시간으로 정한다. 최소 저크 곡선(5차 다항식)이라 시작과 끝의 속도와 가속도가 0이고,
    // 도착 시각 안에 반드시 목표에 닿는다. 정해진 초당 회전 속도 x 시간으로 거리를 막지 않는다.
    // 도달 시간은 기준 회전 속도로 가면 걸릴 시간(최고 속도가 기준 속도가 되는 시간)이되, 최소 시간보다 짧지 않고 도착 시각보다 길지 않다.
    // 목표가 움직이면 지금의 각도와 속도와 가속도를 이어받아 계획을 다시 세우므로 속도가 끊기지 않는다.
    // 목표가 그대로이면 계획을 다시 세우지 않아 틱이 거칠어도 같은 곡선 위의 같은 값이 나온다
    /// <summary> 요 방향을 최단 경로로 부드럽게 옮기는 계산, 도착 시각까지 목표에 닿는다 </summary>
    public sealed class MucheolYawMotion
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double FULL_TURN           = 360.0;
        private const double HALF_TURN           = 180.0;
        private const double TIE_EPSILON         = 0.001;  // 도, 맞은편으로 보는 오차
        private const double SAME_EPSILON        = 0.0001; // 도, 같은 방향으로 보는 오차
        private const double GOAL_EPSILON        = 0.01;   // 도, 목표가 바뀌었다고 보는 오차
        private const double REST_EPSILON        = 0.01;   // 도/초, 도/초^2, 멈춘 것으로 보는 속도와 가속도
        private const double REVERSE_HYSTERESIS  = 20.0;   // 도, 돌던 방향을 유지하는 한도에 더하는 값
        private const double PEAK_FACTOR         = 1.875;  // 최소 저크 곡선의 최고 속도 / (이동 각도 / 시간)
        private const double MIN_PLAN_SECONDS    = 0.0001; // 초, 이보다 짧으면 곧바로 도착한다
        private const double END_EPSILON         = 1e-9;   // 초, 계획 끝 시각의 부동소수 오차
        private const double HALF                = 0.5;
        private const double SIX                 = 6.0;
        private const double TWELVE              = 12.0;
        private const double TWENTY              = 20.0;
        private const double FIFTEEN             = 15.0;
        private const double TEN                 = 10.0;
        private const double FOUR                = 4.0;
        private const double SEVEN               = 7.0;
        private const double THREE               = 3.0;
        private const double FIVE                = 5.0;

        /****************************************
        *                Fields
        ****************************************/

        private double _angle;
        private double _velocity;
        private double _acceleration;
        private double _time;
        private int    _side;

        private bool   _isMoving;
        private bool   _hasPlan;
        private double _planStart;
        private double _planEnd;
        private double _planGoal;
        private double _planStartAngle;
        private double _planStartVelocity;
        private double _planStartAcceleration;
        private double _planRequestedYaw;
        private double _planArrival;
        private double _c3;
        private double _c4;
        private double _c5;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 요(도), -180 초과 180 이하 </summary>
        public float Yaw => (float)WrapSigned(_angle);

        /// <summary> 도/초, 오른쪽(+)으로 도는 쪽이 양수 </summary>
        public float Velocity => (float)_velocity;

        /// <summary> 지금 돌고 있는 쪽, 오른쪽 +1, 왼쪽 -1, 멈췄으면 0 </summary>
        public int Side => _side;

        /// <summary> 목표에 닿아 멈춰 있다 </summary>
        public bool IsAtRest => !_isMoving;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 처음 요와 시각을 정한다, 속도와 방향 기억은 비운다 </summary>
        public void Reset(float yawDegrees, double now)
        {
            _angle        = WrapSigned(yawDegrees);
            _velocity     = 0.0;
            _acceleration = 0.0;
            _time         = now;
            _side         = 0;
            _isMoving     = false;
            _hasPlan      = false;
        }

        /// <summary>
        /// now까지 목표 요(goalYaw)를 향해 진행한다. arrival은 늦어도 이 시각에 목표에 닿는 도착 시각이고, nominalRate(도/초)는 도착 시각이
        /// 넉넉할 때 최고 속도의 기준이다(0 이하이면 도착 시각까지 천천히 간다). minHorizon(초)은 아주 작은 각도도 이보다 빠르게 옮기지 않게 하는 최소 시간이다
        /// </summary>
        public void Advance(double now, float goalYaw, double arrival, float nominalRate, float minHorizon)
        {
            if (now <= _time)
            {
                return;
            }

            if (NeedsPlan(goalYaw, arrival))
            {
                Plan(goalYaw, arrival, nominalRate, minHorizon);
            }

            Evaluate(now);
            _time = now;
        }

        // 계획은 목표나 도착 시각이 달라졌을 때만 다시 세운다. 같은 목표를 쫓는 동안은 한 곡선을 끝까지 따라간다
        private bool NeedsPlan(float goalYaw, double arrival)
        {
            if (!_hasPlan)
            {
                return true;
            }

            return (Math.Abs(WrapSigned(goalYaw - _planRequestedYaw)) > GOAL_EPSILON) || (arrival != _planArrival);
        }

        private void Plan(float goalYaw, double arrival, float nominalRate, float minHorizon)
        {
            _angle = WrapSigned(_angle);

            double delta = ChooseDelta(goalYaw);

            _hasPlan          = true;
            _planRequestedYaw = goalYaw;
            _planArrival      = arrival;
            _planStart        = _time;
            _planGoal         = _angle + delta;

            bool isAtRest = (Math.Abs(_velocity) < REST_EPSILON) && (Math.Abs(_acceleration) < REST_EPSILON);
            if ((Math.Abs(delta) < SAME_EPSILON) && isAtRest)
            {
                Arrive();
                return;
            }

            double available = arrival - _time;
            double horizon   = available;
            if (nominalRate > 0f)
            {
                double nominal = (PEAK_FACTOR * Math.Abs(delta)) / nominalRate;
                horizon = Math.Min(available, Math.Max(nominal, minHorizon));
            }

            if (horizon < MIN_PLAN_SECONDS)
            {
                Arrive();
                return;
            }

            _planEnd               = _time + horizon;
            _planStartAngle        = _angle;
            _planStartVelocity     = _velocity;
            _planStartAcceleration = _acceleration;
            _isMoving              = true;
            _side                  = (delta > 0.0) ? 1 : ((delta < 0.0) ? -1 : _side);

            // 도착 위치, 속도 0, 가속도 0 조건의 5차 다항식 계수. 이동량 delta를 기준으로 풀어 각도 감김과 상관없다
            double t = horizon;
            double a = delta - (_velocity * t) - (HALF * _acceleration * t * t);
            double b = -_velocity - (_acceleration * t);
            double c = -_acceleration;

            _c3 = ((TEN * a) - (FOUR * b * t) + (HALF * c * t * t)) / (t * t * t);
            _c4 = ((-FIFTEEN * a) + (SEVEN * b * t) - (c * t * t)) / (t * t * t * t);
            _c5 = ((SIX * a) - (THREE * b * t) + (HALF * c * t * t)) / (t * t * t * t * t);
        }

        // 부호 있는 최단 각도로 돌되, 맞은편은 한쪽으로 고르고 돌던 방향은 한도 안에서 유지한다
        private double ChooseDelta(float goalYaw)
        {
            double shortest = WrapSigned(goalYaw - _angle);
            int    held     = (_isMoving) ? _side : 0;

            if (Math.Abs(shortest) >= (HALF_TURN - TIE_EPSILON))
            {
                return HALF_TURN * ((held != 0) ? held : 1);
            }

            if ((held != 0) && (Math.Sign(shortest) != held) && (Math.Abs(shortest) > SAME_EPSILON))
            {
                double heldWay = shortest + (FULL_TURN * held);
                if (Math.Abs(heldWay) <= (HALF_TURN + REVERSE_HYSTERESIS))
                {
                    return heldWay;
                }
            }

            return shortest;
        }

        private void Evaluate(double now)
        {
            if (!_isMoving)
            {
                return;
            }

            if (now >= (_planEnd - END_EPSILON))
            {
                _time = _planEnd;
                Arrive();
                return;
            }

            double t = now - _planStart;
            double t2 = t * t;
            double t3 = t2 * t;
            double t4 = t3 * t;
            double t5 = t4 * t;

            double half = HALF * _planStartAcceleration;

            _angle        = _planStartAngle + (_planStartVelocity * t) + (half * t2) + (_c3 * t3) + (_c4 * t4) + (_c5 * t5);
            _velocity     = _planStartVelocity + (_planStartAcceleration * t) + (THREE * _c3 * t2) + (FOUR * _c4 * t3) + (FIVE * _c5 * t4);
            _acceleration = _planStartAcceleration + (SIX * _c3 * t) + (TWELVE * _c4 * t2) + (TWENTY * _c5 * t3);
        }

        // 목표에 닿아 멈춘다. 돌던 방향 기억도 비워 다음 이동은 다시 최단 경로로 고른다
        private void Arrive()
        {
            _angle        = WrapSigned(_planGoal);
            _velocity     = 0.0;
            _acceleration = 0.0;
            _isMoving     = false;
            _side         = 0;
        }

        // -180 초과 180 이하로 감는다
        private static double WrapSigned(double degrees)
        {
            double wrapped = degrees % FULL_TURN;
            if (wrapped > HALF_TURN)
            {
                wrapped -= FULL_TURN;
            }
            else if (wrapped <= -HALF_TURN)
            {
                wrapped += FULL_TURN;
            }

            return wrapped;
        }
    }
}
