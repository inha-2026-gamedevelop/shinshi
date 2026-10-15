// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 옵션 한 줄. 종류 0과 값 0은 옵션이 없는 정상 상태다 </summary>
    public readonly struct EquipmentOptionLine
    {
        /****************************************
        *              Properties
        ****************************************/

        // EquipmentOptionRules의 KIND_* 값
        public int  Kind  { get; }

        // 만분율 정수(100 = 1%)
        public int  Value { get; }

        public bool IsNone => Kind == EquipmentOptionRules.KIND_NONE;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentOptionLine(int kind, int value)
        {
            Kind  = kind;
            Value = value;
        }
    }
}
