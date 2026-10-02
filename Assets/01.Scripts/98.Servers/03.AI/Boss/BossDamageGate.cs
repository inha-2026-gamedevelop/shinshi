// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 패턴과 지속 장판, 투사체는 IDamageResolver를 직접 잡지 않고 이 게이트로만 피해를 낸다
    // 그래서 보스가 죽거나 전투가 끝난 뒤에는 어떤 예약 타격이나 남은 지속물이 있어도 피해가 나가지 않는다
    // 이미 적용된 피해는 되돌리지 않는다
    /// <summary> 보스의 피해 요청 창구, 전투 중에만 열려 있고 사망이나 전투 종료로 닫히면 다시 열지 않는다 </summary>
    public sealed class BossDamageGate
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IDamageResolver _resolver;

        private bool _isOpen;
        private bool _isClosedForGood;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => _isOpen;

        /****************************************
        *                Methods
        ****************************************/

        public BossDamageGate(IDamageResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary> 시전과 동작 번호를 발급한다, 게이트가 닫혀 있어도 번호 발급은 막지 않는다 </summary>
        public int IssueInstanceId()
        {
            return _resolver.IssueInstanceId();
        }

        /// <summary> 열려 있을 때만 피해를 적용한다, 닫혀 있으면 false이고 resolver를 부르지 않는다 </summary>
        public bool TryApply(in DamageRequest request, out DamageResult result)
        {
            result = default;

            if (!_isOpen)
            {
                return false;
            }

            result = _resolver.Apply(in request);
            return true;
        }

        /// <summary> 전투 시작에 연다, 한 번 영구히 닫힌 뒤에는 열리지 않는다 </summary>
        public void Open()
        {
            if (_isClosedForGood)
            {
                return;
            }

            _isOpen = true;
        }

        /// <summary> 사망이나 전투 종료로 영구히 닫는다 </summary>
        public void CloseForGood()
        {
            _isOpen          = false;
            _isClosedForGood = true;
        }
    }
}
