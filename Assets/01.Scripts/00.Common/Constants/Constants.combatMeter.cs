namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 딜미터 메시지 갱신 주기, 배열 상한, 표시 계약값 </summary>
        public static class CombatMeter
        {
            /****************************************
            *                Fields
            ****************************************/

            // 자기, 파티 메시지를 보내는 주기(초). 플레이테스트로 조정하는 초기값
            public const double UPDATE_INTERVAL_SECONDS = 1.0;

            // ECombatMeterSlot 값 개수, 슬롯 배열 길이
            public const int SLOT_COUNT = 8;

            // 파티 메시지, 결과 메시지 참가자 배열 상한
            public const int MAX_PARTICIPANTS = 4;

            // 결과, 최종 메시지의 보스 단계별 배열 상한
            public const int MAX_PHASES = 4;

            // 사망 요약에 모으는 사망 직전 받은 피해의 시간 폭(초)
            public const double DEATH_RECAP_WINDOW_SECONDS = 5.0;

            // 사망 요약 배열 상한(건)
            public const int DEATH_RECAP_MAX_ENTRIES = 5;

            // 받은 피해 출처 상위 표시 상한(건)
            public const int TOP_DAMAGE_TAKEN_SOURCES = 5;

            // 분모(구간 경과 초)가 이 값보다 작으면 DPS 대신 "-"를 표시한다
            public const double MIN_DPS_SECONDS = 1.0;
        }
    }
}
