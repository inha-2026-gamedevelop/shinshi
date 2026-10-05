// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 역할별 이동 속도를 읽는다, 읽기 불가와 유효한 정지 속도 0을 구분한다 </summary>
    public interface IPlayerFemaleCharacterMotionSource
    {
        /// <summary> 이 소스가 표현하는 게임플레이 루트 </summary>
        Transform MovementRoot { get; }

        /// <summary> 같은 비배율 시각 기준으로 초당 이동 속도를 읽는다, 기준 표본만 있는 경우에도 실패한다 </summary>
        bool TryRead(double unscaledTime, out float speed);

        /// <summary> 소스 교체, 재활성, 생사 전환 뒤의 누적값 기준을 버린다 </summary>
        void ResetMotionSample();
    }
}
