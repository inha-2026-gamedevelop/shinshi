// System
using System;

namespace Shinshi.Backend
{
    /// <summary> 외부 설정으로 제공하는 초기 서비스 상태, 허용 빌드는 명시적으로 등록한다.
    /// 연결 문자열(ConnectionStrings:Postgres)이 있으면 로그인, 공개 상태, 출정 게이트는 DB service_state를 읽는다 </summary>
    // TODO(민성): JoinTokenController는 아직 Maintenance를 이 설정에서 직접 읽는다. 진행 중 세션 입장을 막을지 정한 뒤 ServiceStateStore로 옮긴다
    public sealed class ServiceStateOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "ServiceState";

        public const int DEFAULT_REFRESH_SECONDS = 5;
        public const int MIN_REFRESH_SECONDS     = 1;
        public const int MAX_REFRESH_SECONDS     = 60;

        /****************************************
        *              Properties
        ****************************************/

        public bool Maintenance { get; set; } = true;
        public string Notice { get; set; } = "서비스 준비 중";
        public string[] DisabledFeatures { get; set; } = Array.Empty<string>();
        public string[] AllowedBuildIds { get; set; } = Array.Empty<string>();

        // DB 모드에서 다른 API 인스턴스의 변경을 다시 읽는 주기. 이 인스턴스가 쓴 변경은 주기와 무관하게 바로 반영한다
        public int RefreshSeconds { get; set; } = DEFAULT_REFRESH_SECONDS;
    }
}
