// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 운영 계약의 값을 한국어로 표시하며 일반 결과에서 비밀 필드를 제외한다 </summary>
    public static class AdminLabels
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<string, string> G_Labels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["accountId"] = "계정 ID", ["targetAccountIds"] = "대상 계정", ["accounts"] = "계정 검색 결과",
            ["characterId"] = "캐릭터 ID", ["characters"] = "캐릭터", ["characterCount"] = "캐릭터 수",
            ["adminId"] = "운영자 ID", ["loginName"] = "로그인 이름", ["operators"] = "운영자 목록", ["role"] = "역할",
            ["revision"] = "리비전", ["expectedRevision"] = "예상 리비전", ["operation"] = "작업 결과",
            ["operationId"] = "작업 ID", ["operationKind"] = "작업 종류", ["actorId"] = "행위자 ID", ["actorKind"] = "행위자 종류",
            ["receiptId"] = "영수증 ID", ["reason"] = "사유", ["reasonCode"] = "사유 코드", ["status"] = "상태", ["state"] = "상태",
            ["matchedBy"] = "검색 일치 유형", ["matchedId"] = "검색 일치 ID", ["matchedCharacterDeleted"] = "일치 캐릭터 삭제 여부",
            ["includeDeletedCharacters"] = "삭제 캐릭터 포함", ["wallet"] = "지갑", ["currency"] = "엽전", ["currencyReserved"] = "예약 엽전",
            ["tickets"] = "입장권", ["ticketsReserved"] = "예약 입장권", ["essence"] = "정기", ["shards"] = "파편",
            ["online"] = "접속 중", ["lease"] = "접속 임대", ["leaseId"] = "임대 ID", ["instanceId"] = "서버 ID",
            ["items"] = "아이템", ["definitionId"] = "아이템 정의 ID", ["bindingKind"] = "귀속", ["quantity"] = "수량",
            ["reserved"] = "예약 수량", ["activeTokenFamilies"] = "활성 로그인 묶음", ["invites"] = "초대 목록",
            ["inviteId"] = "초대 ID", ["maxUses"] = "최대 사용 횟수", ["usedCount"] = "사용 횟수",
            ["revokeExistingSessions"] = "기존 로그인·초대 폐기", ["revokeInvites"] = "초대 폐기", ["invitesRevoked"] = "초대 폐기됨",
            ["currentSessionKept"] = "현재 게임 접속 유지", ["expiresInHours"] = "유효 시간", ["grantId"] = "지급 ID",
            ["rewards"] = "보상 목록", ["claimDays"] = "수령 기한(일)", ["sourceKind"] = "지급 출처", ["sourceRef"] = "출처 참조",
            ["targetCount"] = "대상 수", ["grant"] = "지급 상세", ["assetKind"] = "자산 종류", ["assetId"] = "자산 ID",
            ["delta"] = "증감량", ["requestedDelta"] = "요청 증감량", ["appliedDelta"] = "적용 증감량", ["shortfall"] = "부족분",
            ["balanceAfter"] = "처리 후 잔액", ["maintenance"] = "점검 중", ["disabledFeatures"] = "차단 기능",
            ["allowedBuildIds"] = "허용 빌드 ID", ["knownFeatures"] = "지원 기능", ["source"] = "설정 출처",
            ["notices"] = "공지 목록", ["noticeId"] = "공지 ID", ["title"] = "제목", ["body"] = "본문", ["target"] = "대상", ["severity"] = "중요도",
            ["sessions"] = "세션 목록", ["session"] = "세션", ["sessionId"] = "세션 ID", ["sessionState"] = "세션 상태",
            ["participantId"] = "참가자 ID", ["participants"] = "참가자", ["participantState"] = "참가자 상태",
            ["recentSessions"] = "최근 세션", ["result"] = "결과", ["mapId"] = "맵 ID", ["mode"] = "출정 종류",
            ["action"] = "복구 동작", ["livenessDeferred"] = "서버 생존 판정 보류", ["instanceLost"] = "서버 소실 판정",
            ["quarantined"] = "격리됨", ["settlingStale"] = "정산 지연", ["recoveryFailures"] = "복구 실패 횟수",
            ["auctionOverdue"] = "경매 기한 초과", ["overdue"] = "기한 초과", ["itemState"] = "품목 상태", ["auctionId"] = "경매 ID",
            ["reservedCurrency"] = "예약 엽전", ["bidSeq"] = "입찰 순번", ["entries"] = "원장 항목", ["ledger"] = "원장",
            ["totals"] = "필터 전체 합계", ["deltaTotal"] = "증감 합계", ["count"] = "건수", ["ledgerId"] = "원장 ID",
            ["sourceType"] = "원장 출처 종류", ["sourceId"] = "원장 출처 ID", ["events"] = "세션 이벤트",
            ["eventSeq"] = "이벤트 순번", ["eventId"] = "이벤트 ID", ["eventKind"] = "이벤트 종류", ["payload"] = "내용",
            ["operations"] = "관련 작업", ["history"] = "이력", ["itemHistory"] = "아이템 이력", ["audit"] = "감사 이력",
            ["records"] = "감사 기록", ["auditId"] = "감사 ID", ["actionName"] = "감사 동작", ["targetKind"] = "대상 종류",
            ["targetId"] = "대상 ID", ["beforeState"] = "변경 전", ["afterState"] = "변경 후", ["selfApproved"] = "단독 승인",
            ["outbox"] = "전송 대기 이벤트", ["retries"] = "재시도 횟수", ["nextCursor"] = "다음 페이지 커서",
            ["requestHash"] = "요청 해시", ["committedRevision"] = "확정 리비전",
            ["createdAtUnixMs"] = "생성 시각", ["updatedAtUnixMs"] = "수정 시각", ["startsAtUnixMs"] = "시작 시각",
            ["endsAtUnixMs"] = "종료 시각", ["expiresAtUnixMs"] = "만료 시각", ["occurredAtUnixMs"] = "발생 시각",
            ["committedAtUnixMs"] = "확정 시각", ["disabledAtUnixMs"] = "비활성화 시각", ["lockedUntilUnixMs"] = "잠금 해제 시각",
            ["endedAtUnixMs"] = "종료 처리 시각", ["createdBy"] = "작성자", ["updatedBy"] = "수정자", ["endedBy"] = "종료 처리자",
            ["actionAnchorUnixMs"] = "행동력 기준 시각",
            ["actionValue"] = "행동력",
            ["adminRole"] = "행위자 역할",
            ["aggregateId"] = "집합 ID",
            ["approvalId"] = "승인 ID",
            ["assignedBootAlive"] = "배정 서버 실행 생존",
            ["auctionEndsAtUnixMs"] = "경매 종료 시각",
            ["baseEssenceAccrued"] = "누적 기본 정기",
            ["bootId"] = "서버 실행 ID",
            ["cancelledAtUnixMs"] = "취소 시각",
            ["cancelledCount"] = "취소 수",
            ["characterName"] = "캐릭터 이름",
            ["claimedCount"] = "수령 수",
            ["classId"] = "직업 ID",
            ["closedAtUnixMs"] = "세션 종결 시각",
            ["consumerKey"] = "이벤트 소비자",
            ["contentVersion"] = "콘텐츠 버전",
            ["deliveredAtUnixMs"] = "전달 시각",
            ["deliveredCount"] = "전달 수",
            ["endedEventSeq"] = "종료 이벤트 순번",
            ["endReason"] = "종료 사유",
            ["essenceAccrued"] = "누적 정기",
            ["executedAtUnixMs"] = "실행 시각",
            ["expiredCount"] = "만료 수",
            ["hardEndsAtUnixMs"] = "최종 마감 시각",
            ["heartbeatAtUnixMs"] = "서버 생존 확인 시각",
            ["instance"] = "배정 서버",
            ["instanceBootId"] = "배정 서버 실행 ID",
            ["itemId"] = "아이템 인스턴스 ID",
            ["lastEventSeq"] = "마지막 이벤트 순번",
            ["nextAttemptAtUnixMs"] = "다음 전송 시도 시각",
            ["nextRecoveryAtUnixMs"] = "다음 복구 시각",
            ["openCount"] = "미수령 수",
            ["ownerId"] = "소유자 ID",
            ["query"] = "검색값",
            ["recoveryActorId"] = "복구 행위자 ID",
            ["recoveryOperationId"] = "복구 작업 ID",
            ["requestIpHashPrefix"] = "요청 IP 해시 앞부분",
            ["revokedAtUnixMs"] = "폐기 시각",
            ["seq"] = "순번",
            ["settledAtUnixMs"] = "정산 시각",
            ["settlementOperationId"] = "정산 작업 ID",
            ["settlingAtUnixMs"] = "정산 시작 시각",
            ["startedAtUnixMs"] = "시작 시각",
            ["startingCount"] = "출발 인원",
            ["usedAtUnixMs"] = "사용 시각"
        };

        /****************************************
        *                Methods
        ****************************************/

        public static string Name(string key) => G_Labels.TryGetValue(key, out string? label) ? label : key;

        public static bool IsSecret(string key)
        {
            return (key.Equals("inviteCode", StringComparison.OrdinalIgnoreCase))
                || (key.Contains("password", StringComparison.OrdinalIgnoreCase))
                || (key.Contains("totp", StringComparison.OrdinalIgnoreCase))
                || (key.Contains("accessToken", StringComparison.OrdinalIgnoreCase));
        }

        public static string Role(int role) => role switch
        {
            0 => "Viewer · 조회", 1 => "Operator · 운영", 2 => "Admin · 관리자", 3 => "Owner · 소유자", _ => "알 수 없음"
        };

        public static string Scalar(JToken value, string key)
        {
            if (value.Type == JTokenType.Null)
            {
                return "없음";
            }
            if ((key == "status") && (value.Type == JTokenType.Integer))
            {
                return value.Value<int>() switch { 0 => "처리 중", 1 => "확정", 2 => "거절", _ => value.ToString() };
            }
            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>() ? "예" : "아니요";
            }
            if ((key.Equals("role", StringComparison.OrdinalIgnoreCase)) && (int.TryParse(value.ToString(), out int role)))
            {
                return Role(role);
            }
            if ((key.EndsWith("UnixMs", StringComparison.Ordinal)) && (long.TryParse(value.ToString(), out long unixMs)))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return value.ToString();
                }
            }
            return value.ToString();
        }
    }
}