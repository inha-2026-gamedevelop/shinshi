// System
using System;

namespace Shinshi.Data.Sessions
{
    /// <summary> 서버 풀과 출정 예약의 시간 계약. 판정은 모두 DB 시각으로 한다 </summary>
    public static class ServerPoolTimings
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int HEARTBEAT_INTERVAL_SECONDS = 5;

        // heartbeat 3회 누락이면 소실로 보고 새 배정을 멈춘다. 계정 임대 저장소의 서버 생존 판정과 같은 값이다
        public const int HEARTBEAT_TIMEOUT_SECONDS  = 15;

        // 로딩 준비 제한 120초에 응답 지연 여유 30초를 더한다
        public const int RESERVATION_READY_SECONDS  = 120;
        public const int RESERVATION_MARGIN_SECONDS = 30;

        public const int JOIN_TOKEN_SECONDS         = 60;

        // 종료 이벤트 저장 뒤 이 시간 안에 /settlement가 없으면 던전이 살아 있어도 복구 작업이 같은 operationId로 정산한다.
        // 던전의 정산 재시도(3초 타임아웃, 1/2/4초 간격)가 끝나고도 남는 시간이다
        public const int SETTLING_RECOVERY_SECONDS  = 60;

        // API가 막 시작했을 때 이 시간 동안은 소실 복구를 하지 않는다. API가 내려가 있던 동안 서버들이 heartbeat를
        // 보내지 못했으므로, 시작 직후의 오래된 heartbeat는 서버 소실이 아니라 API 정지의 흔적이다
        public const int STARTUP_RECOVERY_GRACE_SECONDS = HEARTBEAT_TIMEOUT_SECONDS * 2;

        // TODO(민성): 던전 접속 임대 갱신 주기가 인증 작업에서 정해지면 맞춘다
        public const int DUNGEON_LEASE_SECONDS      = 60;

        // 진행 중 출정의 장애 취소(환급)는 heartbeat가 이만큼 끊겨야 시작한다. 배정 중지(15초)보다 길게 두어
        // 짧은 네트워크 단절이나 API, DB 지연을 서버 소실로 보지 않는다. 정산안이 있는 Settling 복구는 15초 기준을 쓴다
        public const int SERVICE_CANCEL_HEARTBEAT_SECONDS = 60;

        // 마을 입장 교환이 만든 계정 접속 임대의 수명. 인스턴스 heartbeat가 같은 길이로 함께 연장하므로
        // 서버가 비정상 종료하면 이 시간 뒤 같은 계정이 다시 들어올 수 있다(사용자 결정 7.1, 예: 15초)
        public const int ACCOUNT_LEASE_SECONDS = HEARTBEAT_TIMEOUT_SECONDS;

        // 생존 판정 일시 실패와 다른 boot 생존 거절의 503 Retry-After. heartbeat 한 주기 뒤 다시 보면 된다
        public const int LIVENESS_RETRY_AFTER_SECONDS = HEARTBEAT_INTERVAL_SECONDS;

        // 복구 실패 백오프: 기본 간격(유지 작업 주기와 같은 5초) * 2^(이전 실패 횟수), 지수는 상한에서 멈춘다(5, 10, 20, 40초 ...)
        public const int RECOVERY_BACKOFF_BASE_SECONDS = HEARTBEAT_INTERVAL_SECONDS;
        public const int RECOVERY_BACKOFF_MAX_SHIFT    = 6;

        // 만료된 입장 토큰은 이만큼 지난 뒤 정리 작업이 지운다. 진단용으로 짧게 남긴다
        public const int JOIN_TOKEN_RETENTION_MINUTES = 60;

        /****************************************
        *              Properties
        ****************************************/

        public static TimeSpan HeartbeatTimeout    => TimeSpan.FromSeconds(HEARTBEAT_TIMEOUT_SECONDS);
        public static TimeSpan ReservationLifetime => TimeSpan.FromSeconds(RESERVATION_READY_SECONDS + RESERVATION_MARGIN_SECONDS);
        public static TimeSpan JoinTokenLifetime   => TimeSpan.FromSeconds(JOIN_TOKEN_SECONDS);
        public static TimeSpan DungeonLease        => TimeSpan.FromSeconds(DUNGEON_LEASE_SECONDS);
        public static TimeSpan SettlingRecoveryDelay => TimeSpan.FromSeconds(SETTLING_RECOVERY_SECONDS);
        public static TimeSpan StartupRecoveryGrace  => TimeSpan.FromSeconds(STARTUP_RECOVERY_GRACE_SECONDS);
        public static TimeSpan ServiceCancelTimeout  => TimeSpan.FromSeconds(SERVICE_CANCEL_HEARTBEAT_SECONDS);
        public static TimeSpan AccountLease          => TimeSpan.FromSeconds(ACCOUNT_LEASE_SECONDS);
        public static TimeSpan JoinTokenRetention    => TimeSpan.FromMinutes(JOIN_TOKEN_RETENTION_MINUTES);
    }
}
