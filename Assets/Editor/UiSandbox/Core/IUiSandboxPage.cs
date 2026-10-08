namespace Shinshi.Editor
{
    /// <summary> UI 샌드박스에 등록하는 화면 한 장의 계약. 원본 씬이 있으면 실제 View를 쓰고, 없으면 계약 전용 화면으로 값만 편집한다 </summary>
    public interface IUiSandboxPage
    {
        /// <summary> 카탈로그에서 유일한 식별자. 테스트 씬 파일 이름에도 쓰이므로 영문 · 숫자 · 밑줄을 권장한다 </summary>
        string Id { get; }

        string Title { get; }

        /// <summary> 복사해 쓸 원본 씬 경로. 비어 있으면 계약 전용 화면이다 </summary>
        string SourceScenePath { get; }

        /// <summary> 실제 화면이 아직 없을 때 필요한 요구 사항 설명 </summary>
        string Requirement { get; }

        /// <summary> 실제 View를 가진 화면인지 여부 </summary>
        bool HasView { get; }

        /// <summary> 컨텍스트에 연결될 때 한 번 불린다. 비어 있는 시나리오 키만 채우고 사용자 값은 덮어쓰지 않는다 </summary>
        void Attach(UiSandboxContext context);

        /// <summary> 시나리오 값을 View에 반영한다 </summary>
        void Apply(UiSandboxContext context);

        /// <summary> 창에 이 화면 전용 편집 컨트롤을 그린다 </summary>
        void DrawControls(UiSandboxContext context);

        /// <summary> 화면 전환 · 리셋 · 컨텍스트 폐기 때 구독한 이벤트를 모두 해제한다 </summary>
        void Detach();
    }
}
