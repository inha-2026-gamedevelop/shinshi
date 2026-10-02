// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Characters
{
    /// <summary> 캐릭터 생성 작업 결과와, 확정이면 그 작업이 만든 캐릭터. 같은 operationId 재전송도 같은 캐릭터를 읽는다 </summary>
    public sealed class CharacterCreateOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Result { get; }
        public CharacterRecord? Character { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CharacterCreateOutcome(OperationResult result, CharacterRecord? character)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result    = result;
            Character = character;
        }
    }
}
