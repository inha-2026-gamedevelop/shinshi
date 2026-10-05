// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 진중 응집 같은 끌어오기 변형을 받는 적, 서버 위치만 옮기고 보스는 구현하지 않는다 </summary>
    public interface ISkillPullTarget
    {
        // ISkillControlTarget에 넣지 않은 이유: 보스와 이미 있는 적 구현이 끌기를 몰라도 컴파일이 깨지지 않게 따로 둔다.
        // 구현하지 않은 적은 끌려오지 않고 그 자리에서 속박만 받는다
        // TODO(민성): 일반 적 서버(기웅)가 생기면 내비 메시 위 가장 가까운 지점으로 옮기는 구현을 붙인다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 진 중심 point로 한 번 옮긴다, 이동할 수 없으면 그 자리에 둔다 </summary>
        void PullTo(Vector3 point, double serverTime);
    }
}
