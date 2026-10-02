// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 타깃 추적, 벽 검사, 피해 판정을 하지 않고 이 값을 그대로 그린다
    // 시각은 모두 서버 시각이다. 늦게 받아도 StartServerTime부터 흐른 만큼만 건너뛰어 그린다
    /// <summary> 예고 시작에 필요한 입력, 네트워크 수신부가 서버 알림을 이 값으로 바꿔 넘긴다 </summary>
    public readonly struct BossTelegraphStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int                 _bossId;
        private readonly int                 _castId;
        private readonly int                 _groupId;
        private readonly string              _patternId;
        private readonly EBossTelegraphShape _shape;
        private readonly Vector3             _origin;
        private readonly Vector3             _direction;
        private readonly float               _length;
        private readonly float               _width;
        private readonly float               _arcAngle;
        private readonly double              _startServerTime;
        private readonly float               _telegraphSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 CombatId, 같은 보스의 새 시전이 오면 이전 표현을 끝낸다 </summary>
        public int BossId => _bossId;

        /// <summary> 예고부터 회복까지 한 번의 시전 번호, 표현을 구분하는 키다 </summary>
        public int CastId => _castId;

        /// <summary> 한 번의 시전이 동시에 시작한 예고들의 묶음 번호, 같은 묶음의 예고는 서로 끝내지 않는다. 묶음을 따로 주지 않으면 시전 번호와 같다 </summary>
        public int GroupId => _groupId;

        /// <summary> 패턴 정의 ID, 로그와 진단용이며 표현은 형태로만 고른다 </summary>
        public string PatternId => _patternId;

        public EBossTelegraphShape Shape => _shape;

        /// <summary> 공격 기준점, 원형은 중심이다. 시전 동안 바뀌지 않는다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 시작 방향, 수평 단위 벡터. 추적이 끝나기 전까지 바뀐다 </summary>
        public Vector3 Direction => _direction;

        /// <summary> 부채꼴과 원형은 반경, 직사각형은 길이(m). 돌진은 벽 앞까지 실제로 갈 수 있는 거리다 </summary>
        public float Length => _length;

        /// <summary> 직사각형 폭(m), 다른 형태는 0 </summary>
        public float Width => _width;

        /// <summary> 부채꼴 전체 각도(도), 다른 형태는 0 </summary>
        public float ArcAngle => _arcAngle;

        /// <summary> 예고가 시작된 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        /// <summary> 예고 길이(초), 끝나는 서버 시각은 시작 시각에 더한 값이다 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossTelegraphStart(int bossId, int castId, string patternId, EBossTelegraphShape shape, Vector3 origin,
            Vector3 direction, float length, float width, float arcAngle, double startServerTime, float telegraphSeconds)
            : this(bossId, castId, castId, patternId, shape, origin, direction, length, width, arcAngle, startServerTime,
                telegraphSeconds)
        {
        }

        public BossTelegraphStart(int bossId, int castId, int groupId, string patternId, EBossTelegraphShape shape, Vector3 origin,
            Vector3 direction, float length, float width, float arcAngle, double startServerTime, float telegraphSeconds)
        {
            _bossId           = bossId;
            _castId           = castId;
            _groupId          = groupId;
            _patternId        = patternId;
            _shape            = shape;
            _origin           = origin;
            _direction        = direction;
            _length           = length;
            _width            = width;
            _arcAngle         = arcAngle;
            _startServerTime  = startServerTime;
            _telegraphSeconds = telegraphSeconds;
        }

        /// <summary> P02 부채꼴, 서버 IBossFanTelegraphListener.OnTelegraphStarted 인자와 같은 값이다 </summary>
        public static BossTelegraphStart Fan(int bossId, int castId, string patternId, Vector3 origin, Vector3 direction,
            float radius, float arcAngle, double startServerTime, float telegraphSeconds)
        {
            return new BossTelegraphStart(bossId, castId, patternId, EBossTelegraphShape.Fan, origin, direction,
                radius, 0f, arcAngle, startServerTime, telegraphSeconds);
        }

        /// <summary> P03 원형, 방향이 없어 앞쪽을 기본으로 둔다 </summary>
        public static BossTelegraphStart Circle(int bossId, int castId, string patternId, Vector3 center, float radius,
            double startServerTime, float telegraphSeconds)
        {
            return new BossTelegraphStart(bossId, castId, patternId, EBossTelegraphShape.Circle, center, Vector3.forward,
                radius, 0f, 0f, startServerTime, telegraphSeconds);
        }

        /// <summary> 한 시전이 원형 예고를 여러 개 동시에 시작하는 경우, castId는 예고 하나의 번호(장판 번호)이고 groupId는 시전 번호다 </summary>
        public static BossTelegraphStart Circle(int bossId, int castId, string patternId, Vector3 center, float radius,
            double startServerTime, float telegraphSeconds, int groupId)
        {
            return new BossTelegraphStart(bossId, castId, groupId, patternId, EBossTelegraphShape.Circle, center, Vector3.forward,
                radius, 0f, 0f, startServerTime, telegraphSeconds);
        }

        /// <summary> P06 직사각형 </summary>
        public static BossTelegraphStart Line(int bossId, int castId, string patternId, Vector3 origin, Vector3 direction,
            float length, float width, double startServerTime, float telegraphSeconds)
        {
            return new BossTelegraphStart(bossId, castId, patternId, EBossTelegraphShape.Line, origin, direction,
                length, width, 0f, startServerTime, telegraphSeconds);
        }

        /// <summary> P04 투사체, length는 서버가 보낸 벽 앞까지의 경로 길이고 width는 투사체 폭이다 </summary>
        public static BossTelegraphStart Projectile(int bossId, int castId, string patternId, Vector3 origin, Vector3 direction,
            float length, float width, double startServerTime, float telegraphSeconds)
        {
            return new BossTelegraphStart(bossId, castId, patternId, EBossTelegraphShape.Projectile, origin, direction,
                length, width, 0f, startServerTime, telegraphSeconds);
        }

        /// <summary> P01 돌진, length는 서버가 보낸 벽 제한 길이다 </summary>
        public static BossTelegraphStart Dash(int bossId, int castId, string patternId, Vector3 origin, Vector3 direction,
            float length, float width, double startServerTime, float telegraphSeconds)
        {
            return new BossTelegraphStart(bossId, castId, patternId, EBossTelegraphShape.Dash, origin, direction,
                length, width, 0f, startServerTime, telegraphSeconds);
        }
    }
}
