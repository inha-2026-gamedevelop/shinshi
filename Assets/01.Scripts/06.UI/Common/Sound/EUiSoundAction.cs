// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> UI에서 일어난 동작, 소리 이름의 두 번째 단계 </summary>
    // 값은 카탈로그에 숫자로 저장되므로 순서를 바꾸거나 중간에 끼우지 말고 끝에 새 번호로 추가한다
    public enum EUiSoundAction
    {
        [InspectorName("열기")]        Open    = 0,
        [InspectorName("닫기")]        Close   = 1,
        [InspectorName("클릭")]        Click   = 2,
        [InspectorName("마우스 올림")] Hover   = 3,
        [InspectorName("탭 전환")]     Tab     = 4,
        [InspectorName("확인")]        Confirm = 5,
        [InspectorName("취소")]        Cancel  = 6,
        [InspectorName("성공")]        Success = 7,
        [InspectorName("실패")]        Fail    = 8,
        [InspectorName("거절")]        Reject  = 9,
        [InspectorName("도착")]        Arrive  = 10,
        [InspectorName("장비 변경")]   Equipment = 11,
        [InspectorName("물약 사용")]   Potion = 12,
    }
}
