// System
using System.Collections.Generic;

namespace Shinshi.Common
{
    /// <summary> GameDB 정의가 스스로 값을 검사하고 오류 문장을 모은다 </summary>
    public interface IDataValidatable
    {
        // 오류가 없으면 아무것도 넣지 않는다. 경고성 판단은 넣지 않고 시작을 막아야 하는 오류만 넣는다
        void Validate(List<string> errors);
    }
}
