// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 부적 핵심 변형 하나의 고정 수치와 레벨로 커진 추가 계수, 쓰지 않는 값은 0이다 </summary>
    public readonly struct SkillCoreVariant
    {
        // 계수는 공격력 배율 A다. BaseCoefficient가 0보다 크면 원래 타격 계수를 BaseCoefficient + AddedCoefficient로 바꾸고,
        // 0이면 원래 타격은 그대로 두고 AddedCoefficient가 후속 타격, 충격파, 마지막 폭발, 관통 뒤 타격의 계수다

        /****************************************
        *                Fields
        ****************************************/

        private readonly bool                _hasValue;
        private readonly ETalismanEffectKind _kind;
        private readonly float               _baseCoefficient;
        private readonly float               _addedCoefficient;
        private readonly float               _delay;
        private readonly float               _radius;
        private readonly float               _length;
        private readonly float               _width;
        private readonly int                 _count;
        private readonly float               _spreadAngle;
        private readonly int                 _extraTargets;
        private readonly float               _rootBonus;
        private readonly float               _speed;
        private readonly float               _pullDistance;
        private readonly float               _pullPointOffset;

        /****************************************
        *              Properties
        ****************************************/

        public bool                HasValue         => _hasValue;
        public ETalismanEffectKind Kind             => _kind;
        public float               BaseCoefficient  => _baseCoefficient;
        public float               AddedCoefficient => _addedCoefficient;

        // 초, 첫 타격 뒤 후속 타격, 충격파, 폭발까지
        public float               Delay            => _delay;

        // m, 원형 교체나 충격파 반경
        public float               Radius           => _radius;

        // m, 직선 파동 길이와 폭
        public float               Length           => _length;
        public float               Width            => _width;

        // 갈래 수, 나눠 쏘는 대상 수
        public int                 Count            => _count;

        // 도, 이웃한 갈래 사이 각도
        public float               SpreadAngle      => _spreadAngle;

        // 관통 뒤 더 맞히는 최대 적 수
        public int                 ExtraTargets     => _extraTargets;

        // 초, 속박에 더하는 시간
        public float               RootBonus        => _rootBonus;

        // m/초, 변형이 만드는 투사체 속도. 0이면 기술 원래 속도를 쓴다
        public float               Speed            => _speed;

        // m, 한 번 당기는 최대 거리
        public float               PullDistance     => _pullDistance;

        // m, 시전자 전방 이 거리의 지점 쪽으로 당긴다. 0이면 판정 중심으로 당긴다
        public float               PullPointOffset  => _pullPointOffset;

        // 원래 타격 계수를 바꾸는 변형인지
        public bool                ReplacesCoefficient => (_hasValue) && (_baseCoefficient > 0f);

        /****************************************
        *                Methods
        ****************************************/

        public SkillCoreVariant(ETalismanEffectKind kind, float baseCoefficient, float addedCoefficient, float delay, float radius,
            float length, float width, int count, float spreadAngle, int extraTargets, float rootBonus,
            float speed = 0f, float pullDistance = 0f, float pullPointOffset = 0f)
        {
            _speed            = speed;
            _pullDistance     = pullDistance;
            _pullPointOffset  = pullPointOffset;
            _hasValue         = true;
            _kind             = kind;
            _baseCoefficient  = baseCoefficient;
            _addedCoefficient = addedCoefficient;
            _delay            = delay;
            _radius           = radius;
            _length           = length;
            _width            = width;
            _count            = count;
            _spreadAngle      = spreadAngle;
            _extraTargets     = extraTargets;
            _rootBonus        = rootBonus;
        }

        /// <summary> 이 핵심 변형이 걸려 있는지 </summary>
        public bool Is(ETalismanEffectKind kind)
        {
            return (_hasValue) && (_kind == kind);
        }
    }
}
