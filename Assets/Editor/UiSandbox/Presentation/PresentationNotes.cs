namespace Shinshi.Editor
{
    /// <summary> 화면 Requirement에 공통으로 넣는 현행 계약 주의사항. 오래된 고정 데모 값을 현행 게임 규칙으로 오해하지 않게 한다 </summary>
    public static class PresentationNotes
    {
        public const string FIXTURE_NOTE =
            "씬에 없는 View는 시험 전용 배치(UiSandboxFixtureCanvas)를 만들어 붙인다. 이 배치의 위치 · 크기 · 색은 운영 레이아웃이 아니다.";

        public const string WAVE_NOTE =
            "웨이브 현행 계약: 6웨이브 · 적 180마리 · 웨이브별 처치 수 20/24/28/32/36/40 · 제한 420초. " +
            "웨이브 전멸 -> 다음 지점 활성화 -> 생존 접속자 1명 도착 -> 다음 웨이브 시작이며 고정 5초 휴식과 10초 간격은 폐기되었다. " +
            "CombatHudDemo의 고정 휴식 · 공명 최대 100 · 승천 비용 {60, 80, 40} 같은 값은 오래된 데모 값이다.";

        public const string LEVEL_NOTE =
            "레벨 계약: Lv1~30, HUD는 확정 누적 경험치에서 레벨과 단계 진행을 표시한다. 첫 바인딩에서는 레벨업 알림을 생략한다. 이 화면의 값은 표시 시험용이다.";

        public const string CLASS_NOTE =
            "직업 계약: 현행 3직업은 검사(3) · 마검사(4) · 궁수(5)다. 구 직업 무사(0) · 도술사(1) · 신령사(2)는 보존용이며 " +
            "공명 · 강림은 구 신령사 전용이라 신규 3직업에는 공명 슬롯이 없다. " +
            "HudPartyView와 SortiePrepView의 직업 배지 · 이름 배열은 구 직업(0/1/2)분 기준이라 3/4/5는 표시가 비거나 어긋날 수 있다. " +
            "시험 배치는 이를 가리지 않고 View가 실제로 보여 주는 대로 둔다.";

        public const string BOSS_NOTE =
            "보스 계약: HP와 페이즈 번호, 공유 부활 횟수, 물약 사용 횟수를 보여 준다. 월백 2페이즈는 소유자의 외부 설계 입력을 기다리는 미정 항목이라 " +
            "구현된 전투가 아니며, 이 화면의 페이즈 값은 View 표시 확인용 수치일 뿐이다.";

        public const string COMMON_UI_NOTE =
            "공통 UI 적용 범위: UiPopupHub(얼러트 · 컨펌 · 토스트)는 이 화면이 직접 연다. ItemSlotView · ItemSlotGridView · ListRowView는 Economy의 인벤토리 · 우편 · 경매장 · 거래 페이지에서 쓴다. " +
            "TooltipView · SaveStatusView · UiButtonFeedback · UiWindowDragHandle · UiCursor는 '공통 UI 샌드박스'(common.ui) 페이지에서 조작한다. " +
            "툴팁 표시 · 숨김 · 긴 글 보정, 저장 상태 4종, 끌 수 있는 창의 영역 보정은 같은 공개 메서드를 부르는 보조 버튼으로 확인할 수 있다. " +
            "그러나 실제 마우스 올림 · 누름 · 끌기, 버튼 반응 DOTween(Play에서만 움직임), 게임 커서 그림(Play 전용, PF_UiCursor 프리팹 필요)은 직접 확인해야 하며 이 샌드박스가 검증했다고 말하지 않는다.";

        public const string NETWORK_NOTE =
            "서버 호출은 하지 않는다. 모든 응답은 샌드박스의 모의 요청(Play Mode 전용)이다.";
    }
}
