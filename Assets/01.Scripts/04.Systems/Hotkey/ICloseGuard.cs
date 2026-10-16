// System
using System;

namespace Shinshi.Systems
{
    // 저장하지 않은 변경이 있는 창이 닫기를 붙잡는다. Esc, 단축키, HUD 메뉴, 창의 닫기 버튼이 모두 이 확인을 거친다
    /// <summary> 창 닫기 확인 훅. 바로 닫아도 되면 참, 거짓이면 가드가 확인을 띄우고 닫기로 하면 confirmClose를 부른다 </summary>
    public interface ICloseGuard
    {
        bool RequestClose(Action confirmClose);
    }
}
