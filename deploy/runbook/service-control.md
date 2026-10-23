# 서비스 제어: 점검, 기능 차단, 버전 차단, 공지

모든 명령은 `shinshi-admin`이다(준비와 재시도 원칙은 [README](README.md)). 서비스 상태는 API의 `service_state` 한 행(점검, 차단 기능, 허용 빌드, `revision`)이고, 저장은 `revision` 낙관적 검사다. 실행한 적 없는 정적 작성본이다.

## 반영 시간과 공통 주의

- 상태를 저장한 API 인스턴스는 바로 반영하고, 다른 인스턴스는 최대 `ServiceState__RefreshSeconds`(기본 5초, 1~60) 늦게 본다.
- `revision`이 다르면(다른 운영자가 먼저 저장) 409 `INVALID_STATE`로 거절되고 아무것도 바뀌지 않는다. `service-state show`로 현재 값을 보고 새 명령을 실행한다(`--operation-id` 없이).
- `--disable`, `--allow-builds`는 목록을 **통째로** 바꾼다. 하나만 더하거나 빼려면 먼저 `show`로 현재 목록을 보고 최종 목록을 적는다. 허용 빌드 하나만 더하거나 빼는 `service-state allow-build add|remove`만 따로 있고, 기능 차단에는 그런 하위 명령이 없다.
- 점검, 기능 차단, 허용 빌드는 모두 Admin 이상이다.
- DB 연결 문자열이 없는 API는 DB 모드가 아니라 설정(`ServiceState__*`) 모드로 돈다. 운영은 DB 모드 전제다.

- 동시에 출정할 수 있는 파티 수는 서버 풀의 Idle 던전 슬롯 수다(던전 프로세스 하나 = 출정 하나). 슬롯이 모두 차면 새 출정은 `NO_CAPACITY`로 마을 대기열에서 기다린다. 점검 전후 출정이 몰리면 `shinshi-admin sessions`의 진행 수와 슬롯 수를 비교한다. 슬롯 설치는 [게임 서버 배포](../gameserver/README.md)를 본다.

## 점검 시작

**언제.** 배포, 마이그레이션, DB 복원, 서버 교체 전에 신규 진입을 막을 때, 또는 사고 중 새 출정을 멈출 때.

**사전 확인.**

1. `shinshi-admin service-state show`로 현재 점검, 차단 기능, 허용 빌드, `revision`을 기록한다(해제와 되돌리기에 쓴다).
2. 운영 환경의 API 설정 `ServiceState__Maintenance`가 `false`인지 확인한다. 입장 토큰 교환은 DB 점검이 아니라 이 설정값을 직접 읽는다. 기본값이 `true`여서 운영에서 `false`로 두지 않으면 DB에서 점검을 풀어도 마을·던전 입장이 503 `MAINTENANCE`다(`ServiceStateOptions`의 TODO, API 시작 시 경고 로그).
3. 진행 중인 출정을 센다: `shinshi-admin sessions --state Running --all`, `--state Auction`, `--state Settling`. (`sessions`는 `--state` 없이 부르면 복구 대상만 보여 주므로 상태를 지정한다.)
4. 가능하면 먼저 공지를 올린다(아래 "공지 작성과 종료", `--starts`로 예고 시각 지정).

**명령.**

```powershell
shinshi-admin service-state set --maintenance on --reason "10/15 정기 점검"
```

다른 항목은 현재 값 그대로 유지된다. 출력되는 재시도 명령과 `operationId`를 기록한다.

**효과 범위(코드·문서 근거).** 점검은 게임 로그인, 토큰 갱신 교환, 새 출정 예약을 `MAINTENANCE`로 거절한다. 이미 예약했거나 시작한 출정의 입장, 정산, 복구는 막지 않는다. 입장 토큰 교환은 위 2번의 설정을 읽으므로 점검을 켜도 이미 로그인한 접속자의 마을·던전 입장 교환은 막히지 않을 수 있다. 접속 중인 플레이어를 내보내지도 않는다. 운영자 게임 계정의 점검 예외는 없다.

**확인.**

- `shinshi-admin service-state show`에서 점검이 `켜짐`.
- 공개 응답: `Invoke-RestMethod http://<API 주소>/v1/service-state`에서 점검 값 확인(인스턴스가 여러 개면 몇 초 뒤 다시).
- 런처에서 로그인이 점검으로 거절되는지(미확인: 실제 화면 문구).
- 진행 세션이 끝났는지: `sessions --state Running --all`이 빌 때까지. 세션이 안 끝나고 서버가 죽었다면 [장애 세션 복구](incident-response.md#장애-세션-복구).

**되돌리기.** 아래 "점검 해제".

## 점검 해제

**언제.** 교체·복원·마이그레이션이 끝나고 검증을 마친 뒤.

**사전 확인.**

- `service-state show`의 **허용 빌드가 비어 있지 않은지.** 초기 상태는 점검 중이고 허용 빌드가 비어 있어서, 비어 있으면 점검을 풀어도 아무도 로그인하지 못한다.
- 서버를 교체했다면: 마을·던전 프로세스가 떠 있고(`systemctl status`), API에 등록됐고, 새 클라이언트 `buildId`가 허용 목록에 있다. 이 확인은 [servers-and-alerts.md](servers-and-alerts.md)와 [data-and-release.md](data-and-release.md)에 있다.
- DB를 복원했다면 [DB 복원](data-and-release.md#db-복원)의 확인(대사, 운영자 로그인)을 마쳤다.

**명령.**

```powershell
shinshi-admin service-state set --maintenance off --reason "점검 종료"
```

**확인.** `service-state show`에서 점검 `꺼짐`, 공개 `GET /v1/service-state`에서 점검 값, 시험 계정으로 로그인과 마을 입장. `ServiceState__Maintenance`가 `true`로 남아 있으면 입장이 503이다.

**되돌리기.** 다시 `--maintenance on`.

## 기능 차단

**언제.** 사고 때 서버 전체를 내리지 않고 문제 기능만 멈출 때. 경제 사고의 1차 대응이다([경제 사고 1차 대응](incident-response.md#경제-사고-1차-대응-경매-중복-지급-의심)).

기능 키(`ServiceFeatureKeys`, 목록 밖은 저장이 거절된다): `auction`, `crafting`, `enhance`, `inherit`, `gathering`, `boss_entry`, `inbox_claim`, `dungeon_entry`, `talisman`, `training`, `market`, `trade`, `reroll_options`, `social`. 현재 키 목록은 `service-state show`의 "기능 키"에도 나온다.

**막히는 범위(Backend README와 운영 설계 기준, 실행 미검증).**

| 키 | 막는 곳 |
|---|---|
| `dungeon_entry` | 새 출정 예약 |
| `boss_entry` | 보스전 새 출정 예약 |
| `inbox_claim` | 우편 수령(`POST /v1/inbox/claim`) |
| `auction` | 경매 **입찰**. 경매 개시, 마감, 단계 종료는 막지 않는다 |
| `gathering`, `crafting` | 생활 채집, 물약 제작 |
| `talisman`, `training`, `enhance`, `inherit`, 장비 제작(`crafting`) | 마을 서버가 중계하는 성장 API. 문서마다 연결 범위가 다르다(Backend README는 `enhance`, `inherit`, 장비 제작을 "진입점이 생길 때 연결"이라 하고 운영 설계는 연결됐다고 한다). 차단이 실제로 걸리는지 사고 때 가정하지 말고 확인한다 |

이미 시작한 진행(정산, 경매 마감 등)은 막지 않는다. 막힌 요청은 409 `FEATURE_DISABLED(1100)`이고 저장되지 않는다.

**사전 확인.** `shinshi-admin service-state show`로 현재 차단 목록과 `revision`을 기록한다. 이미 차단된 키가 있으면 최종 목록에 **함께** 적는다(통째 교체).

**명령.**

```powershell
# 현재 차단이 없을 때 경매만 끄기
shinshi-admin service-state set --disable auction --reason "경매 중복 지급 의심 조사"

# 이미 inbox_claim이 차단돼 있을 때 경매를 더하기
shinshi-admin service-state set --disable inbox_claim,auction --reason "경매 중복 지급 의심 조사"
```

**확인.** `service-state show`의 차단 기능, 공개 `GET /v1/service-state`의 `disabledFeatures`, 해당 기능 요청이 `FEATURE_DISABLED`로 거절되는지 시험 계정으로.

**주의.**

- 차단 중에 처음 보낸 operationId는 저장되지 않는다. 차단이 풀린 뒤 같은 ID가 다시 오면 그때 실행된다. 클라이언트가 차단 중 요청을 계속 재시도하면 해제 직후 실행될 수 있다.
- 기능 차단은 이미 접속한 플레이어의 다른 행동을 막지 않는다.

**되돌리기(해제).** 원인 해결과 검증 뒤 목록에서 해당 키만 뺀 목록을 적는다. 모두 풀려면 `none`.

```powershell
shinshi-admin service-state set --disable none --reason "원인 수정 확인, 경매 재개"
```

## 버전 차단

**언제.** 취약하거나 사고를 일으킨 클라이언트 빌드의 로그인을 막을 때, 새 빌드로 교체하며 이전 빌드를 닫을 때.

허용 `buildId` 목록(최대 64개, 각 128자)에서 빠진 빌드는 로그인과 토큰 갱신 교환에서 거절된다. 목록이 비면 아무도 로그인할 수 없다. 판정은 로그인·토큰 갱신 시점이라 이미 접속한 세션은 그대로다(빌드 판정 위치는 `AuthBuildPolicy`, 진행 중 세션의 영향은 미확인).

**사전 확인.**

- `shinshi-admin service-state show`에서 허용 빌드 목록을 기록한다.
- 막을 `buildId`와 새로 허용할 `buildId`를 빌드 폴더의 `*_Data/StreamingAssets/build-identity.json`의 `buildId`에서 확인한다. 서버·클라이언트·API가 같은 `contentHash`와 `protocolVersion`을 쓰는지는 별도다(런처 게시 도구는 이 파일 없이는 게시하지 않는다).
- 교체라면 새 빌드를 **먼저 더하고** 이전 빌드를 나중에 뺀다. 순서를 바꾸면 그 사이 로그인이 막힌다.

**명령.** 한 빌드만 바꾸는 하위 명령이 안전하다. 현재 목록과 `revision`을 읽어 하나만 바꾼 목록을 그 revision으로 저장한다.

```powershell
shinshi-admin service-state allow-build add <새 buildId> --reason "빌드 b2 허용"
shinshi-admin service-state allow-build remove <이전 buildId> --reason "빌드 b1 차단: 정산 오류"
```

이미 있거나 없으면 저장하지 않고 성공(종료 코드 0)으로 끝난다. 읽은 뒤 다른 운영자가 먼저 저장했으면 409로 거절되고 자동 재시도하지 않는다. 그때는 `show`로 확인하고 같은 명령을 새로 실행한다.

여러 항목을 한 번에 바꿀 때만 `service-state set --allow-builds <b1,b2> --reason ...`을 쓴다(통째 교체, 허용 빌드가 비면 요약에 경고가 나온다).

**확인.** `service-state show`의 허용 빌드, 공개 `GET /v1/service-state`의 `allowedBuildIds`, 차단한 빌드로 로그인이 거절되고 허용 빌드는 되는지 시험 계정으로(미확인: 거절 시 런처의 안내 문구).

**되돌리기.** `allow-build add <buildId>`로 다시 넣는다.

## 공지 작성과 종료

**언제.** 점검 예고, 장애 안내, 보상 안내.

공지는 `service_notices`에 저장되고 공개 `GET /v1/service-state`의 `notices`(지금 노출 중인 것만, 중요도·시작 시각 내림차순 최대 20개)로 나간다. 런처는 런처 대상 공지를 표시한다(구현됨). **게임(마을) 안 표시는 확인하지 못했다.** 2026-10-07 조사 기준으로 게임 안 공지 수신·표시 구현은 없었다. `--target Game`으로 올려도 플레이어에게 보인다고 가정하지 않는다. 채팅의 `System` 방송은 서버 공지 경로가 아니다.

**사전 확인.** 제목 100자 이하, 본문 4000자 이하. `--ends`는 필수이고 현재보다 뒤여야 하며(DB 시각 기준, 아니면 409 `INVALID_REQUEST`), `--starts`가 있으면 그보다 뒤여야 한다. 시각은 `2026-10-15T09:00:00Z`처럼 Z나 오프셋이 붙은 ISO 8601만 받는다. 공지에 비밀값과 개인정보를 쓰지 않는다. Operator 이상.

**명령.**

```powershell
shinshi-admin notices create --title "정기 점검 안내" --body "10월 15일 18:00(KST)부터 점검합니다." `
    --target Both --severity warning --starts 2026-10-15T08:00:00Z --ends 2026-10-15T11:00:00Z `
    --reason "정기 점검 예고"
shinshi-admin notices list
```

`--target`은 `Launcher`, `Game`, `Both`, `--severity`는 `info`, `warning`, `critical`이다.

**확인.** `notices list`의 상태와 시각(진행 중, 예정, 최근 7일 안에 끝난 공지), 공개 `GET /v1/service-state`의 `notices`, 런처 화면(런처 대상일 때).

**종료·정정.** 일찍 내릴 때:

```powershell
shinshi-admin notices end <noticeId> --reason "점검 연기로 공지 철회"
```

공지를 수정하는 명령은 없다. 틀렸으면 `end`로 내리고 새로 만든다. 만료 뒤 같은 operationId를 재전송해도 처음 결과가 돌아온다.
