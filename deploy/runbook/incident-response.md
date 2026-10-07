# 사고 대응: 경제 사고, 장애 세션, 지급·회수, 강제 종료

모든 명령은 `shinshi-admin`이다(준비, 재시도 원칙, 종료 코드는 [README](README.md)). 모든 쓰기에는 `--reason`이 필요하고 감사 로그에 남는다. 실행한 적 없는 정적 작성본이다. 조회 명령 중 원장, 아이템 이력, 세션 이벤트, 작업, 계정 상세는 `--reason`이 필수이며 읽기 전에 감사 행이 남는다.

## 사고 기록

사고마다 아래를 손으로 남긴다. CLI가 출력하는 `operationId`는 재시도에 필요하다.

- 시각(UTC), 증상, 최초 인지 경로(경보 이름, 플레이어 제보)
- 한 조치와 각 쓰기의 `operationId`, 종료 코드
- 영향 범위 추정(계정, 세션, 자산)과 판단 근거
- 해제 조건과 해제 시각

## 경제 사고 1차 대응 (경매 중복 지급 의심)

**언제.** 같은 낙찰·분배가 두 번 지급됐다는 제보나 `ShinshiReconciliationMismatch` 경보, 원장에서 같은 경매의 중복 행이 보일 때. 원칙은 서버 전체를 내리지 않고 문제 기능만 끈 뒤 원장으로 범위를 확인하는 것이다.

**사전 확인.** Admin 이상으로 로그인. `service-state show`로 현재 차단 목록과 `revision` 기록.

**1. 경매 차단.**

```powershell
shinshi-admin service-state show
# 기존 차단이 없으면:
shinshi-admin service-state set --disable auction --reason "경매 중복 지급 의심, 원장 조사"
# 기존 차단이 있으면 전체 목록을 적는다(통째 교체): --disable inbox_claim,auction
```

**한계를 알고 쓴다.** `auction` 차단은 **입찰만** 막는다. 경매 개시, 마감, 단계 종료는 막지 않는다. 이미 열린 경매의 마감과 분배 지급은 계속 진행될 수 있다. 중복 지급이 마감·분배 경로에서 난다면 이 차단만으로 멈추지 않는다. 경매는 보스 성공 정산 뒤 시작하므로 새 경매를 줄이려면 `boss_entry`(보스 새 출정)나 `dungeon_entry`(새 출정 전체)를 더하고, 완전히 멈추려면 [점검](service-control.md#점검-시작)을 켠다. 이미 진행 중인 출정은 계속된다. 서버를 직접 멈추는 선택은 모든 접속자에게 영향이 크므로 마지막 수단이다([서버 재시작](servers-and-alerts.md#서버-재시작과-드레인)).

**2. 범위 확인(읽기 전용).** 날짜는 UTC ISO 8601이다.

```powershell
# 멈춘 경매(기한+60초 경과 또는 서버 소실). 닫기는 자동 복구가 한다
shinshi-admin auctions list --all
shinshi-admin sessions --all

# 의심 계정의 경매 관련 원장. 이름은 ContractCodes.EconomyReason
shinshi-admin ledger --account <accountId> --economy-reason AUCTION_PAYOUT --from 2026-10-15T00:00:00Z --all --reason "경매 중복 지급 조사"
shinshi-admin ledger --account <accountId> --economy-reason AUCTION_SETTLE --from 2026-10-15T00:00:00Z --all --reason "경매 중복 지급 조사"

# 세션 한 건의 이벤트, 참가자, 관련 원장, 작업
shinshi-admin session-events <sessionId> --reason "경매 중복 지급 조사"

# 아이템 한 개의 이력(seq 오름차순)
shinshi-admin item-history <itemId> --reason "경매 중복 지급 조사"

# 작업 한 건과 같은 작업 키의 원장, 이력, outbox, 감사
shinshi-admin operation <actorId> <operationId> --reason "경매 중복 지급 조사"
```

원장 사유 코드는 `AUCTION_RESERVE(8)`, `AUCTION_RELEASE(9)`, `AUCTION_SETTLE(10)`, `AUCTION_PAYOUT(11)`이다. 원장 첫 페이지의 `totals`는 같은 조건 전체의 사유·자산별 합계다. 같은 `source`에서 같은 계정으로 지급이 두 번 있는지, 낙찰 합계와 분배 합계가 맞는지를 본다. 계정을 모르면 `accounts search <계정ID|캐릭터ID|초대 코드>`로 찾는다(캐릭터 이름 검색은 작업 트리에서 추가 중이라 확정되기 전까지 `accounts search --help`로 확인).

**확인.** 결론은 원장 행으로 낸다. 대사 불일치는 일일 대사 서비스가 ERROR 로그와 경보로 알린다([경보 대응](servers-and-alerts.md#경보-대응)). 대사는 하루 한 번이라 사고 직후의 불일치를 바로 보여 주지 못한다.

**3. 회수.** 중복 지급분은 계정마다 개인 조정으로 회수한다([개인 지급과 회수](#개인-지급과-회수)). 가용량(예약분 제외)까지만 차감되고 모자란 양은 `shortfall`로 남는다. 이미 소비된 재화는 회수되지 않는다.

**4. 해제.** 원인 수정과 검증 뒤 `--disable none`(또는 해당 키만 뺀 목록). 사고 기록에 해제 근거를 남긴다.

**되돌리기.** 차단은 해제로 되돌린다. 회수는 반대 부호의 `adjust`로 되돌린다(새 operationId).

## 장애 세션 복구

**언제.** `sessions` 목록에 복구 대상이 있을 때: 정산이 지연된 Settling, 서버가 사라진 Running, 격리된 세션, `ShinshiStorageQueueStuck`·`ShinshiServerDown` 등 경보 뒤. 복구는 Admin 이상이다.

**사전 확인.**

```powershell
shinshi-admin sessions --all                 # 상태 지정 없음: 복구 관심 대상만
shinshi-admin sessions --state Running --all
shinshi-admin session-events <sessionId> --reason "장애 세션 조사"
```

`sessions` 표의 열: 상태, 결과, 모드, 생성, 서버, **생존**(배정 서버 생존), **격리**, **정산 지연**, **서버 소실**, **복구 실패**(횟수). 먼저 아래를 본다.

- 표 아래에 "서버 생존 판정 보류 중" 경고가 있으면(API 시작 직후나 DB 단절 직후 30초) 서버 소실이 모두 `아니오`로 보인다. `service-cancel`과 `force-close`는 503 `BACKEND_UNAVAILABLE`(종료 코드 3)로 거절된다. 잠시 뒤 다시 조회하고 같은 `--operation-id`로 다시 실행한다. 보류는 API 인스턴스마다 따로 판정한다.
- 서버 프로세스가 실제로 죽었는지는 게임 서버 호스트에서 확인한다: `sudo systemctl status shinshi-dungeon@<슬롯>.service`, `sudo journalctl -u shinshi-dungeon@<슬롯>.service`. 살아 있는 서버의 세션을 취소하지 않는다.

**판단 기준.**

| 상태·표시 | 판단 | action |
|---|---|---|
| Settling이고 정산 지연, 저장된 종료 이벤트가 있음(`session-events`로 확인) | 자동 복구와 같은 서버 actor·같은 정산 operationId로 저장된 이벤트를 정산한다. 같은 정산이 한 번만 적용된다 | `retry-settlement` |
| Running이고 서버 소실, 서버 프로세스 실제 부재 확인 | 서비스 장애 취소: 티켓·사용 물약 환급, 완료 웨이브 기본 정기 전액과 추가 정기 50%(내림) 보상. 서버가 살아 있으면 `INVALID_STATE` | `service-cancel` |
| Settling이고 격리(복구 실패 5회 이상) | 장애 취소와 같은 보상 규칙으로 닫는다. **원래 정산안이 아니므로** 원래 보상과 차이가 생길 수 있다. 정산이 반복 실패한 원인(예: 정산 안전 상한, 콘텐츠 허용 목록)을 먼저 조사한다. 격리 세션에 `retry-settlement`가 허용되는지는 문서에 없다(미확인). 되돌릴 수 없는 마지막 수단으로 본다 | `force-close` |
| RecoveryPending | 세 action 모두 `INVALID_STATE`. 처리 경로가 없고 격리 카운터를 되돌리는 API도 없다 | 개발자에게 넘긴다(원인 수정 뒤 자동 복구 재시도 경로는 미구현) |
| Auction 단계에서 멈춤(`auctions list`) | 닫기는 유지 작업의 자동 복구가 한다. 수동 쓰기 명령은 없다. 기한+60초가 지나도 안 닫히면 개발자에게 넘긴다 | 없음 |

**명령.**

```powershell
shinshi-admin recover <sessionId> --action retry-settlement --reason "정산 지연 수동 재시도"
shinshi-admin recover <sessionId> --action service-cancel   --reason "던전 서버 소실 확인, 장애 취소"
shinshi-admin recover <sessionId> --action force-close      --reason "정산 5회 실패 격리 세션 종료"
```

**동작 순서(재시도 판단에 필요).** `retry-settlement`와 `service-cancel`은 두 트랜잭션이다: 의도 감사 행을 먼저 커밋하고, 자동 복구와 같은 서버 actor·operationId로 복구를 확정한 뒤, 어드민 작업과 감사 행을 확정한다. 마지막 단계가 실패해 503이 나면 같은 `--operation-id`로 다시 실행하면 저장된 결과를 받는다. `force-close`는 한 트랜잭션이다. 같은 operationId 재전송은 어떤 경우에도 처음 결과다. 다른 세션에 같은 ID를 쓰지 않는다.

**확인.**

- 출력의 `복구 actorId`, `복구 operationId`로 원장을 본다(도구가 명령을 안내한다): `shinshi-admin operation <복구 actorId> <복구 operationId> --reason "복구 결과 확인"`. `force-close`는 운영자 ID와 이번 `operationId`로 본다.
- `shinshi-admin session-events <sessionId> --reason ...`, 이어서 `shinshi-admin sessions --all`에서 해당 세션이 사라졌는지.
- 결과는 `Closed`/`ServiceCancelled`이고 배정 서버는 Draining이 된다(`force-close`).

**되돌리기.** 복구는 확정이다. 되돌리는 명령은 없다. 잘못 지급했거나 부족하면 [개인 지급과 회수](#개인-지급과-회수), 원래 보상과의 차이는 [보정 지급](#보정-지급)으로 메운다. 늦게 온 `/settlement`는 강제 종료가 이기면 `ALREADY_SETTLED`다.

## 보정 지급

**언제.** `force-close`나 장애 취소로 닫은 세션의 보상이 원래 정산안과 달라 플레이어에게 부족분이 있을 때, 장애로 플레이어가 입은 손실을 보상할 때.

**사전 확인.**

- 부족분을 계산한다: 원래 보상은 `session-events`와 해당 세션 원장 재생에서, 실제 지급은 `ledger`(`--source-type`/`--source-id` 또는 계정)에서 본다. 임의 수량을 지급하지 않는다.
- 대상 계정 목록: `--accounts id,id,...` 또는 `--accounts '@파일'`(PowerShell에서는 `@`로 시작하는 값을 반드시 작은따옴표로 감싼다)(쉼표, 공백, 줄바꿈 구분, `#` 주석, 중복·비GUID는 거절, 1000명 이하).
- 보상: `--reward kind[:assetId[:binding]]=수량`(kind는 `Currency`, `Tickets`, `Essence`, `Shards`, `AccountItem`. 한 줄 수량 1~1000000은 임시 상한). `Essence`, `Shards`는 수령 때 플레이어가 캐릭터를 고른다. `AccountItem`만 assetId가 필요하다.
- 같은 `--source-ref`(복구한 세션 ID)로 취소되지 않은 `RecoveryCompensation` 지급이 이미 있으면 작성이 409 `INVALID_STATE`다. 앞 지급을 `grants cancel`한 뒤에만 다시 작성한다.
- 실행은 Admin 이상이다(`RecoveryCompensation`은 대상이 1명이어도). 작성은 Operator.
- `inbox_claim` 기능 차단이 켜져 있으면 플레이어가 수령할 수 없다. 점검 중에도 수령은 막히지 않는다(현재 설정).

**명령.** 작성(초안) → 확인 → 실행의 세 단계다. 우편함에 넣는 것은 `execute`다.

```powershell
shinshi-admin grants create --accounts '@targets.txt' --reward Currency=500 --reward AccountItem:item_herb=3 `
    --source-kind RecoveryCompensation --source-ref <복구한 세션 ID> --claim-days 30 `
    --reason "10/15 던전 서버 장애 보정"
shinshi-admin grants show <grantId>
shinshi-admin grants execute <grantId> --reason "보정 지급 실행"
```

`--claim-days`를 생략하면 30일이다. 실행은 모든 대상을 한 트랜잭션으로 처리한다(일부 성공 없음). 감사에는 단독 승인(`self_approved=true`)으로 남는다(2인 승인 없음).

**확인.** `shinshi-admin grants show <grantId>`의 상태와 수령 집계. 플레이어가 수령하면 원장에 `INBOX_CLAIM(24)` 행이 생기므로 `shinshi-admin ledger --account <id> --economy-reason INBOX_CLAIM --from <시각> --reason "보정 수령 확인"`으로 본다(사유 이름은 `ContractCodes.EconomyReason`, 숫자도 받는다).

**되돌리기.** 초안이나 미수령·기한 안 우편은 `shinshi-admin grants cancel <grantId> --reason "..."`로 취소한다. **이미 수령한 우편은 되돌리지 않는다.** 회수는 개인 조정으로 한다.

## 개인 지급과 회수

**언제.** 한 계정의 재화·아이템을 즉시 더하거나 빼야 할 때(중복 지급 회수, 개별 보상). Operator 이상.

**사전 확인.**

- 계정과 캐릭터: `shinshi-admin accounts search <계정ID|캐릭터ID|초대 코드>`, `shinshi-admin accounts show <accountId> --reason "문의 확인"`.
- 자산 표기: `--asset kind[:assetId[:binding]]`. `Essence`, `Shards`는 `--character <characterId>`가 필요하다. `--delta`는 0이 아니고 절댓값 1000000 이하(임시 상한).
- 감소는 잔액에서 **예약분**(티켓 예약, 경매 예약 엽전, 아이템 예약)을 뺀 가용량까지만 빼고, 모자란 양은 `shortfall`로 응답과 감사에 남는다. 잔액은 음수가 되지 않는다. 가용량이 0이어도 감사와 함께 확정된다.

**명령.**

```powershell
# 즉시 지급(원장 AdminGrant 26)
shinshi-admin adjust <accountId> --asset Currency --delta 500 --reason "보상 지급: 문의 #123"
# 회수(원장 AdminRevoke 27)
shinshi-admin adjust <accountId> --asset Currency --delta -200 --reason "경매 중복 지급 회수"
shinshi-admin adjust <accountId> --asset AccountItem:item_herb --delta -3 --reason "중복 지급 회수"
```

다수에게 우편으로 주려면 [보정 지급](#보정-지급)의 `grants`(`--source-kind AdminGrant`)를 쓴다. 대상 2명 이상의 실행은 Admin이다.

**확인.**

```powershell
shinshi-admin ledger --account <accountId> --from 2026-10-15T00:00:00Z --all --reason "조정 결과 확인"
shinshi-admin accounts show <accountId> --reason "조정 결과 확인"
```

회수 응답의 `shortfall`이 0보다 크면 가용량이 모자랐다는 뜻이다. 부족분을 다른 방법으로 메우려는 판단은 사용자 결정이다.

**되돌리기.** 반대 부호의 `adjust`를 **새 operationId**로 한다. 같은 명령을 그냥 다시 실행하면 같은 조정이 두 번 일어난다. 응답을 못 받았다면 같은 `--operation-id`로 재전송한다.

## 강제 종료

**언제.** 방해 플레이어의 토큰과 초대를 끊을 때. Operator 이상.

**현재 접속은 끊지 못한다.** `accounts kick`은 계정의 로그인 묶음, 접근·갱신 토큰, 게임 하위 세션, 미소비 입장 토큰을 폐기하고(기본으로 미사용 초대도), 응답에 `currentSessionKept=true`가 나온다. 게임 서버가 쥔 계정 접속 임대는 지우지 않으므로 지금 접속 중인 마을·던전 연결은 이어진다. 폐기된 토큰의 갱신, 재접속, 새 입장 토큰 발급부터 거절된다. 아이디·비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다. 로그인 차단은 제재(P1)로 미구현이고, 서버 쪽 즉시 연결 종료 경로(heartbeat 응답에 폐기 사실을 싣는 확장)도 구현되지 않았다. 체험판에서 접속 중인 계정을 즉시 내보내고 다시 못 들어오게 하는 수단은 지금 없다.

**대안과 한계.**

- 접속을 끊으려면 그 서버 프로세스를 내려야 한다. 마을을 내리면 모든 접속자에게 영향이 있고, 던전은 출정 하나가 끝나면 스스로 종료·재기동한다([서버 재시작](servers-and-alerts.md#서버-재시작과-드레인)). 영향이 크므로 사용자 결정이 필요하다.
- 계정 비밀번호를 재설정하는 CLI 명령은 확인되지 않았다(미확인).
- 새 기기 로그인 등 소유자 변경은 `invites issue --account <accountId>`(기존 세션과 이전 초대 폐기, `--keep-sessions`면 유지)를 쓴다. 초대 원문은 한 번만 출력된다.

**명령.**

```powershell
shinshi-admin accounts kick <accountId> --reason "방해 행위 신고 #45"
shinshi-admin accounts kick <accountId> --keep-invites --reason "토큰만 폐기"
```

**확인.** 출력의 초대 폐기 여부와 `현재 접속 유지`. 폐기된 토큰으로 재접속이 거절되는지는 별도 확인한다. `accounts show <accountId> --reason "..."`의 로그인 묶음 수(살아 있는 묶음 수)가 줄었는지 본다.

**되돌리기.** 폐기한 토큰과 초대는 복구되지 않는다. 계정 소유자는 다시 로그인하거나 새 초대를 받는다(`invites issue --account <id>`).

## 제보를 받았을 때

플레이어 제보 양식은 [PLAYER_BUG_REPORT.md](PLAYER_BUG_REPORT.md)다. 운영자는 아래로 단서를 찾는다.

- 제보의 `buildId`가 허용 목록에 있는지: `service-state show`. 목록에 없으면 로그인이 거절됐을 수 있다.
- 제보 시각 전후: `audit --from ... --to ...`(운영자 조치), `sessions --all`, `ledger --account <id> --from ... --reason "버그 제보 조사"`.
- 계정은 `accounts search`로 찾는다. 캐릭터 이름으로 찾는 기능은 작업 트리에서 추가 중이라 확정 전에는 `accounts search --help`를 확인한다.
