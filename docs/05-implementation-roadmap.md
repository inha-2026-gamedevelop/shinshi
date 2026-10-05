# 신시 구현 로드맵

> 기준일: 2026-10-02 · 버전: 1.1 · M-A 구현 현황과 체험판 계획
> 현재 확인: Unity 6000.4.7f1·URP·Mirror 96.11.3 기반 GameDB·부트스트랩·인증·서버 권위 전투·출정 저장/정산, .NET 10 ASP.NET Core·PostgreSQL 18 백엔드(Npgsql + Dapper, SQL 마이그레이션 0001~0010), 배포 템플릿과 M2 실행 도구가 있다. 미커밋 구현을 포함한 정적 대조 결과이며, 세부 범위와 남은 연결은 3장과 8장에 적었다. 사용자 제공 실행 결과는 Unity EditMode 780개 통과, 실제 PostgreSQL 18 Docker를 사용하는 Backend 396개 통과다. 보스 AI 서버 뼈대(`98.Servers/03.AI/Boss/`)와 월백 P02·P03·P06 실행은 가짜 의존성 EditMode로만 검증했다. 서버 빌드·M2/M3·IL2CPP·배포는 미실행이며 전체 체험판 완료를 뜻하지 않는다.

## 1. 범위와 완료의 의미

[시스템 개요](01-system-design.md)의 외부 체험판 범위를 그대로 구현한다. 게임 규칙은 [02](02-game-design.md), API·영속성 계약은 [03](03-technical-design.md), 모듈 배치는 [04](04-architecture.md)를 따른다.

작업 ID는 완료 후에도 재사용하지 않는다. 상태는 `미착수 → 구현 중 → 기능 검증 → 통합 검증 → 완료(검증됨)`로 진행한다. 문서·클래스·씬 파일 존재만으로 완료 처리하지 않는다. 진행 단계와 [검증 플레이북](ai/03_VERIFICATION_PLAYBOOK.md)의 증거 등급·상태 어휘는 아래처럼 대응한다.

| 로드맵 표기 | 플레이북 기준 |
|---|---|
| 기능 검증 | 증거 등급 2(담당 씬·Test Runner에서 목표 동작 관찰), L1+L2 |
| 통합 검증 | 증거 등급 3, L3. 전용 서버 + 클라이언트 2 이상(M2 이상), 지연 관련은 M3 |
| 완료(검증됨) | 아래 표의 최소 검증과 수용 관찰을 모두 충족 |
| 구현됨·실행 미검증 / 검증 대기 | 해당 구현은 있으나 필요한 실행 검증 미실행 / 인원·PC·환경 대기. 일부 단위 시험 통과가 있어도 서버 빌드·M2/M3 수용을 대신하지 않음 |
| 일부 | 작업에 속한 구현만 존재. 상태 열에 구현 범위와 남은 범위를 구분하며, 카탈로그·DTO·시험 대역만으로 항목 전체 구현을 인정하지 않음 |
| 미착수 | 작업을 수행하는 구현이 없음. 관련 계약·SO·문서가 있어도 실행 경로가 없으면 미착수 |
| 입력 대기 | 플레이북의 `차단됨` 중 외부 설계·자산 입력이 없는 경우 |

API/DB 통합은 [기술설계서](03-technical-design.md) 11장의 "API/DB 통합" 항목을 기준으로 한다. 호스트 모드만으로 확인한 네트워크 기능은 통합 검증으로 세지 않는다.

## 2. 먼저 연결할 경로

```text
U01 데이터·서버 부트스트랩 (직후 U22 시험 기반 병행)
  ├→ U02 인증·저장 → U03 서버 풀·입장   (저장 경로)
  └→ U04 이동·기본 공격 → U05 피해·사망  (전투 경로, 병행)
  → U09 숲 1웨이브 + U14 보상 정산
  → 귀환·저장·재접속 확인 (U17의 최소 경로)
  → 세 직업·6웨이브·보스·성장·경매·생활·진행 확장
```

첫 연결 시험용 임시 입력은 개발 빌드에만 둔다. 이 검증이 전체 체험판 범위를 대체하지 않는다. 1인 전용 서버와 2인 접속을 초기부터 시험해 마지막 날까지 네트워크 통합을 미루지 않는다.

현재 M-A는 이 연결 경로의 기반 구현이다. `DungeonSessionHost`는 아직 웨이브 없는 개발용 세션을 열며, 실제 일반전 디렉터·드롭 원본 연결은 남아 있다. 따라서 다음 수용 확인은 실제 서버 빌드와 M2 최소 경로이고, 보스 전체 실행 시험은 다른 연결 이후 마지막 `M-Boss`에서 한다(2026-10-01 사용자 결정).

[아키텍처](04-architecture.md) 9장의 ①~⑦은 모듈 의존 순서다. 담당·착수 순서·일정의 기준은 이 문서이며, 두 문서가 어긋나면 이 문서의 선행 열을 먼저 확인하고 04를 함께 고친다.

## 3. 작업 목록

| ID | 작업·산출물 | 주 담당 | 선행 | 완료를 판정할 관찰 | 최소 검증 | 현재 상태(2026-10-02) |
|---|---|---|---|---|---|---|
| U01 | GameDB 정의·검증·초기화, client/town/dungeon 부트스트랩, Windows 클라이언트·Linux 서버 빌드 진입점 | 민성 | 없음 | 필수 데이터 누락은 시작 중단, 중복 초기화 없음, Linux 서버 기동·역할/contentVersion 보고, 코딩 컨벤션 0장의 asmdef(`Shinshi.Runtime`·`Shinshi.Server`·`Shinshi.Dev`·테스트) 생성, `UNITY_SERVER` 없이 클라이언트 컴파일 통과 | L1+L2(EditMode GameDB 검증), Linux 서버 빌드 실행 | 구현됨·실행 미검증 — GameDB/SO·asmdef·Bootstrap·빌드/컴파일 검사 도구 존재. 실제 Windows/Linux 빌드·기동 미실행 |
| U02 | 체험 계정(초대 코드 교환·토큰)·캐릭터·세션, ASP.NET Core API 골격·로컬 PostgreSQL·마이그레이션, 로그인·캐릭터 선택 화면 | 민성(API/DB), 진욱(화면) | U01 | 소유권 위조 거절, 계정/캐릭터 공유 범위, 재로그인 동일 데이터, 같은 operationId 재시도 1회 적용, 동시 접속은 `AlreadyConnected`와 "이미 접속 중인 계정"으로 거절 | L2 + API/DB 통합 | 일부 — 인증/토큰·계정 임대·API/DB·멱등 기반 구현. 캐릭터 목록·이름 확인·생성 API 구현. 클라이언트 진입 흐름(`Release/` 로비 → 캐릭터 선택 → 캐릭터 생성 → 마을, `01.Network/Lobby/`)과 에디터·개발 빌드용 아이디 로그인 연결(2026-10-03). 에디터 클라이언트 + Windows 개발 마을 서버(API 입장 토큰 검증) + 로컬 API·임시 PostgreSQL로 가입·로그인 → 캐릭터 생성 → 선택 → 마을 입장·스폰·서버 이동 → 끊김 시 선택 복귀를 1회 확인(M1, 2026-10-03). M2 다인·런처 경유 실행은 미검증. 외형 슬라이더·캐릭터 삭제·게임 세션 갱신 미구현 |
| U03 | 공유 신시·방 목록·초대·준비·서버 풀·대기열·입장권 예약/소모, 신시 맵 | 민성(세션/API), 진욱(신시 맵·방 목록 UI) | U02 | 1/2/4인 입장, 전원 해금·티켓, 일부 부족 시 전원 무소비, 중복 시작 1회 차감, 취소 복구, 대기 중 무소비 | L3 M2(4인은 클라이언트 4) + API/DB 통합 | 일부 — 풀 등록/heartbeat·예약/시작/취소·입장 토큰·임대 이전 구현. 방/초대/준비 UI·대기열·해금 검사·맵 통합 미착수 |
| U04 | 클릭 이동·기본 공격·조준·회피·카메라 | 민성 | U01 | 서버 위치 판정, 무적 없는 회피, 원격 보간·입력 지연 확인 | L3 M3 + 지연 0/50/150ms | 일부 — 서버 이동/기본 공격/조준/회피·소유 예측·원격 동기화와 입력 Reader 프리팹 연결 구현. 게임 카메라 프리팹(`PF_GameCamera`: 휠 3단계 줌과 단계 전환 애니메이션·가림 투명화) 구현, 미리보기 렌더와 EditMode로만 확인. 실제 입력·M3 미실행 |
| U05 | 공통 체력·기력·피해·제어·사망·부활 | 민성 | U04 | 일반 무한 부활, 보스 솔로 사망/협동 전멸 즉시 실패, 공용 3회, 같은 틱 전멸 우선 | L2(EditMode 생사 규칙) + L3 M2 | 일부 — 피해/생사·기력·부활 예산/지연·틱 끝 전멸 판정 구현. 플레이어 이동 불가/기절과 맵 지정 안전 복귀 지점 선택 미구현, 실제 일반전/보스 M2 미실행 |
| U06 | 무사 4기술·대검 표현·전방 반격 | 민성 | U05 | 네 기술 판정·자원·쿨다운, 후방/방어 불가 공격 구별 | L2(EditMode 판정) + L3 M2 | 일부 — 서버 기술/자원/쿨다운·전방 반격 구현. Q/W/E/R 클라이언트 입력(`PlayerInputReader`·`SkillInputRules`)은 연결됨·실행 미검증. 소유 플레이어의 체력·기력·Q/W/E/R 쿨다운을 HUD 뷰에 넣는 `LocalPlayerHudBinder`는 구현됨(Play Mode 단독 호출 확인, 접속 상태 미검증). 대검/기술 표현과 M2 미완성(캐릭터 직업 바인딩은 토큰 교환 `classId`로 서버에 연결, 실행 미검증) |
| U07 | 도술사 4기술·장판·속박·낙뢰 | 민성 | U05 | 반복 피해 중복 방지, 보스 제어 허용 구간만 적용 | L2(EditMode 판정) + L3 M2 | 일부 — 서버 기술·장판/반복 피해·속박 허용 검사 구현. 지점 기술 범위 표시·클릭 확정/취소 입력은 연결됨·실행 미검증. 쿨다운·기력 HUD 연결은 U06 행과 같다. 기술 표현·보스 제어 창 연결과 M2 미완성(직업 바인딩은 토큰 교환 `classId`로 서버에 연결, 실행 미검증) |
| U08 | 신령사 4기술·동료 2종·공명·3강림 | 민성 | U05 | 소환 수명·소유·공명 중복 차단, 보스 주변 인원에서 소환물 제외 | L2(EditMode 판정) + L3 M2 | 일부 — 서버 기술·동료/공명/강림 판정 구현. Q/W/E/R과 신령사 전용 Z/X/C 강림 입력은 연결됨·실행 미검증. 공명 값은 `ILocalPlayerView.ReadGauges`로 읽을 수 있으나 현재 HUD에는 공명 게이지와 Z/X/C 칸이 없어 표시되지 않는다. 동료 네트워크 개체는 `PF_NetCompanion`(서버 소유·`NetworkTransformReliable` 10Hz·임시 도형 표현)과 서버 스포너로 연결했다(소환 확정 시 스폰, 만료·사망·피격·재호출·세션 종료·보존 해제 시 내림, 재접속 보존 중 유지. 추적 규칙은 EditMode, 스폰·풀 재사용은 에디터 Play Mode 서버 단독 호출로만 확인, 실제 시전·M2 미검증). 보스 주변 인원은 보스 두뇌가 `CountLivingInRadius(includeSummons=false)`로 세고 동료가 `IsSummon`이라 이미 제외된다(EditMode). 최종 동료 모델·애니메이션·VFX와 M2 미완성(직업 바인딩은 토큰 교환 `classId`로 서버에 연결, 실행 미검증) |
| U09 | 적 4역할·6웨이브·스폰·420초 실패·숲 맵 | 기웅(적), 진욱(맵), 민성(통합) | U03,U05 | 180마리·동시 상한 24·웨이브 종료·420초 실패 판정과 정산 요청 발행·인원별 배율. 420초 도달과 마지막 처치가 같은 틱이면 성공 우선. 정산 결과는 U14에서 판정 | L3 M2(1/2/4인) | 일부 — 웨이브/스폰 인터페이스·세션 종료/시간 제한·배율 산출 구현. 적 AI/스폰 디렉터·일반전 연결·배율 적용·숲 맵 미착수 |
| U10 | 월백 1페이즈 선택·6공격·예고·제어 창, 월백 전장(지름 36m) | 기웅(보스), 진욱(전장 맵) | U05 | 가중치 경계·최근 패턴·무작위 생존자·공격 범위·서버 시각, 50% 전환 요청에서 1페이즈 종료 표시 | L2(EditMode 가중치) + 마지막 M-Boss(M3·지연 포함) | 구현 중 — 서버 뼈대와 1페이즈 6패턴·지속물 수명·서버 틱 구동·보스 이동(전장 경계·지형)·네트워크 개체 스폰·사건 복제·늦은 접속 복원 구현, EditMode만 검증(2026-10-03). 클라이언트 표현 연결부·전장 맵·보스 생성 지점·M-Boss 미완 |
| U11 | 월백 2페이즈 설계 입력·전환·최종 처치 | 기웅(통합), 2페이즈 설계 제공자(미정) | U10 + 실제 패턴안(수령 기한 미정) | 최종안의 상태·예고·피해·제어·종료와 10~15분 목표 확인 | 마지막 M-Boss(M3·지연 포함) | 미착수 — 실제 2페이즈 입력 대기. 개발용 종료 훅은 전체 구현에 포함하지 않음 |
| U12 | 부적 45효과·등록·강화·출정 구성 잠금 | 진욱(성장/UI), 민성(전투 적용/API) | U02,U03,U06~08 | 45개 ID·단계 선택·등록 보정·확정 강화·금지 장소 거절 | L2(EditMode 45 ID·대표 조합) + API/DB 통합 + L3 M2 | 일부 — 카탈로그·부적 보상 정산·기술 보정 인터페이스 존재. 실제 45효과 resolver·등록/강화 API/UI·출정 구성 잠금 미착수 |
| U13 | 장비 제작·25강·계승·수련·인벤토리 UI/API | 진욱(기능/UI), 민성(API/저장) | U02 | 17강 경계·환산표·+0 대상·소스 소모·강화 보정·수련 59,500 | L2(EditMode 계승 9행·비용) + API/DB 통합(동시 강화) | 진행 중 — Equipment/Growth SO·예고 계약과 인벤토리 창 화면(`InventoryView`, 가짜 값으로 `Dev_ItemSlot`에서 확인)만 존재. 인벤토리 데이터·API 연결과 제작/강화/계승/수련 실행 경로 없음 |
| U14 | 개인 드롭·전리품 원장·성공/실패 정산·보상 회수함(30일) | 민성 | U02,U09 | 킬마다 개인 판정, 추가 정기만 절반 손실, 재요청 중복 없음 | API/DB 통합 + L3 M2 | 일부 — 드롭/전리품·이벤트 저장/정산·지갑/계정 귀속 아이템 지급 구현. 실제 적/웨이브 연결·30일 보상 회수함 미착수, M2 미실행 |
| U15 | 보스 보상·파티 경매·단독 구매·분배 UI/API | 진욱(UI), 민성(경매/정산) | U03,U14,U10 | 기본 보상 후 입찰, 오프라인 분배 제외, 유찰 소멸·원자 낙찰. U11 전에는 개발 빌드의 처치 시험으로만 확인 | API/DB 통합(동시 입찰) + 마지막 M-Boss | 일부 — 보스 기본 보상 정산·경매/단독 구매/분배 API와 던전·Mirror 연동 구현됨. 실제 PostgreSQL·EditMode 검증, 실제 서버 경매 실행 미검증. 경매 UI·경매 중 재입장 미구현 |
| U16 | 생활 구역·약초 선점·행동력·물약 제작/지참/사용 | 진욱(생활 구역 맵/성장/UI), 민성(공유 판정/API) | U02,U03,U05 | 선점 경합 1승자, 취소 미소모, 오프라인 회복, 보스 합산 9회, 사용 확정 후 회복·회복 지연 측정 | API/DB 통합 + L3 M2 | 일부 — 물약 예약/소모 이벤트/미사용 해제/장애 반환 저장 경로 존재. 채집·행동력·제작·사용/회복 판정·생활 맵/UI 미착수 |
| U17 | 접속 끊김·120초 복귀·중도 포기·서버 장애 복구 | 민성 | U03,U05,U14,U15 | 잔류 피격·기존 상태 복귀·단일 정산·검증된 장애만 보정, 종료 이벤트 저장 전·후 프로세스 종료 각각의 복구 결과 | L3 M3 + 장애 주입(프로세스 종료·API 타임아웃·DB 재시작) | 일부 — 잔류 캐릭터/재접속·포기 판정·DB 복구/장애 보정·프로세스 종료 구현. 포기/귀환 사용자 흐름 연결·실제 M3/장애 주입 미완성 |
| U18 | 프롤로그(약 90초)·컷신·튜토리얼·HUD·음향·접근성 | 기웅(서사), 진욱(UI), 민성(입력/공통) | U04,U09,U10. 수용 판정은 U13,U14,U21 이후 | 처음 온 사용자가 출정·보상·성장·귀환 수행, 예고 가독성 | L2 + 처음 온 사용자 관찰 기록 | 미착수 — Dev_HUD 씬·공통 View 계약이 전체 사용자 흐름 구현을 뜻하지 않음 |
| U19 | Windows 클라이언트·Linux 서버·API 배포·관측 | 민성 | U01~03,U17 | 깨끗한 환경 실행, 버전 불일치 거절, 로그로 정산 추적·복구, 예약 만료와 출정 시작 경합에서 한쪽만 성공 | 외부 PC M3 + 버전 불일치 시험 | 일부 — 빌드 도구·API Dockerfile·systemd·버전 거절/서버 등록·종료/재기동 경로 구현됨·실행 미검증. 실제 배포·공통 구조화 로그/지표 연결 미완성 |
| U20 | 수치 조정·성능 측정·외부 체험·다인/지연/경제 회귀 | 전원 | U06~19,U21~23 | 아래 배포 기준 충족, 실제 결과·빌드·PC 조건 기록 | 5장 전체 | 미착수 |
| U21 | 체험판 초기 지급·계정 진행/해금·NPC 서비스 진입 | 민성(지급·진행 저장/API), 진욱(NPC 서비스 화면), 진행 SO 내용은 기웅/진욱 계약([04](04-architecture.md) 6장), 대사 작성 담당 미정 | U02 | [02](02-game-design.md) 9장 초기 지급을 계정/캐릭터별 1회만 지급, 재생성·재접속 중복 없음, 체험판 진행과 정식 해금 분리 | L2 + API/DB 통합 | 미착수 — Progression SO·잔액 스키마가 초기 지급/진행/해금/NPC 구현을 대신하지 않음 |
| U22 | 시험 기반: `Assets/Tests/EditMode`·`PlayMode`, M2/M3 실행 방법, 지연 주입, `DevCombat`/`DevGrowth` 개발 씬 | 민성, 개발 씬은 해당 파트 | U01 | Test Runner 실행, 전용 서버 1 + 클라이언트 2 실행 절차 재현, 0/50/150ms 지연과 손실 1%/3% 주입 확인 | M2 1회 실행 기록 | 일부 — EditMode·Backend 시험 및 Start-M2 도구 존재. PlayMode 시험·DevCombat/DevGrowth·지연/손실 주입 실증·M2/M3 실행 기록 없음 |
| U23 | 외부 체험판 자산 확보·권리 확인(모델·애니메이션·VFX·맵·UI·음향) | 자산을 쓰는 각 파트, 대장 최종 확인 담당 미정 | 없음 | 빌드에 포함한 외부 자산 전부 [에셋 대장](licenses/ASSET_REGISTRY.md) 기재·배포 조건 확인, 미보유 자산을 완료로 세지 않음 | 대장과 빌드 포함 목록 대조 | 일부 — 에셋 대장·일부 자산 존재. 전체 체험판 자산 확보/권리와 최종 빌드 포함 목록 대조 미완료 |
| U24 | 경제 원장·아이템 이력·운영자 감사 로그, `EEconomyReason`, 일일 대사 | 민성 | U02 | U12~U16·U21의 모든 잔액 변경이 원장을 거침, 원장 합계=잔액, 감사 로그 수정·삭제 거절 | API/DB 통합 | 일부 — 멱등 원장/아이템 이력/outbox·추가 전용 제약·대사 함수·EEconomyReason 구현. 운영자 감사 로그는 U25에서 구현(0008 `admin_audit_log`, 추가 전용). 성장/생활/초기 지급 연결·일일 실행 스케줄러 미착수 |
| U25 | 최소 어드민(P0, API·CLI만): 운영자 로그인·역할, 계정 조회, 초대 코드, 개인 지급·회수·우편, 점검·기능 차단·공지, `RecoveryPending` 복구 ([운영 도구](design/operations-admin.md)) | 민성 | U24,U17 | 지급 재실행 1회 적용, 점검 중 신규 로그인·출정 거절, 기능 차단 `FeatureDisabled`, 게임 토큰으로 어드민 거절 | API/DB 통합 + CLI 조작 기록 | 구현됨·CLI·운영 실행 미검증 — 마이그레이션 0008(0009 `currency_reserved` 의존, 함께 배포), 운영자 인증(PBKDF2+TOTP)·역할·허용 네트워크(신뢰 프록시 오설정 차단), 추가 전용 감사 로그(계정 상세·로그 조회는 읽기 전 감사), 계정 조회(초대 원문은 POST 검색)·강제 종료(기존 토큰·초대 폐기, 현재 접속·비밀번호 재로그인 허용), 초대 발급·폐기, 지급·우편함·개인 조정과 플레이어 `/v1/inbox`(계정별 한도), 대상 2명 이상·보정 지급 실행은 Admin, 점검·기능 차단·허용 빌드·공지 DB 이관과 기능 게이트(출정 예약·우편 수령 연결), 로그 조회, 수동 세션 복구(정산 재시도·장애 취소·격리 세션 강제 종료, 생존 판정 보류) API. 명령줄 도구 `Backend/src/Shinshi.AdminCli`. 백엔드 자동 테스트 통과(실제 PostgreSQL). 남음: RecoveryPending 상태 처리·경매 복구 목록, 나머지 기능 키 연결, 강제 종료의 접속 중 연결 차단, 점검 중 우편 수령 정책 |
| U26 | 런처: 로그인·자동 로그인, 설치·업데이트·검증, 공지·버전 차단, 게임 실행·파이프 세션 전달, 게임 쪽 수신기 ([런처·패치](design/launcher-and-patch.md)) | 민성 | U02, U27 | 깨끗한 PC 설치·업데이트·손상 복구, 허용 목록 밖 빌드 실행 차단, 인자·로그에 토큰 없음 | 단위 + L3 M2(런처→마을→출정) | 일부 - 체험판 범위 가(최소 런처) 확정(2026-10-02). 업데이트 엔진, 아이디 로그인과 DPAPI 자동 로그인, Named Pipe 세션 전달, Unity 수신기, WPF 화면 구현됨(`Launcher/`, `01.Network/Session/`), 단위 시험 통과. 실제 백엔드와 게임을 붙인 M2 실행 미검증 |
| U27 | 빌드 신원(`build-identity.json`)·서명 매니페스트·게시 도구·허용 `buildId` 등록 | 민성 | U01, U19 | 서명 위조·`sequence` 역행·경로 탈출 거절, stable 채널에 개발 빌드 게시 거부, 서버·클라이언트 빌드 ID 일치 | 단위 + 실제 게시 1회 | 일부 — API 허용 buildId 환경 설정/거절 기반 존재. 서명 매니페스트와 게시 도구(`Launcher/src/Shinshi.Release.Packager`), 빌드 신원 DTO 단위 시험 통과. Unity 빌드 신원 생성, 업로드, 허용 buildId 등록, 실제 게시 미착수 |

U01~U20의 ID와 의미는 유지하고, 기존 문서가 요구하나 ID가 없던 작업을 U21~U23으로, 운영 도구와 런처 작업을 U24~U27로 추가했다. U24는 잔액을 바꾸는 첫 API 작업(U13·U14) 전에 들어가야 한다. 나중에 넣으면 그 전 데이터는 추적할 수 없다. 선행이 없는 작업은 첫 주부터 병행할 수 있으나 수용 판정은 선행 작업의 통합 검증 뒤에 한다.

API·공용·네트워크 코드는 기존 소유 경계에 따라 민성이 통합한다. 표의 공동 작업은 같은 파일 동시 편집을 뜻하지 않는다. 파트별 씬·순수 판정·표현 작업을 나누고 통합 씬 소유자는 한 명으로 유지한다.

## 4. 2주 목표의 점검 시점

사용자 목표는 3인·2주이며, 아래는 2026-10-01 기준 진행을 점검할 초기 일정이다. 작업량에 대한 보장이나 검증된 공수 추정은 아니다. 일정의 기준은 이 표이며 기획 부록에 적힌 날짜와 다르면 이 표를 따른다.

| 시점 | 확인할 결과 | 관련 작업 | 구간 끝 통합 시험 | 미달 시 처리 |
|---|---|---|---|---|
| 10/01~10/03 | 전용 서버 입장→한 번 공격→정산→재접속, 직업·성장·보스 계약 공유 | U01~U05·U14·U22 최소 경로 | 10/03 M2(서버 1+클라이언트 2) | 다른 기능의 가짜 통합으로 덮지 말고 접속/저장 병목 우선 해결 |
| 10/04~10/07 | 세 직업 기본 동작·숲 6웨이브·월백 P1 구현·성장 화면, P2 입력 수령 | U06~U10·U13, U11 입력 | 10/07 M2(보스 전체 실행은 마지막 M-Boss) | P2 수령 일정과 누락 계약을 별도 가시화 |
| 10/08~10/10 | 4인 전체 루프·경매·생활·45효과·서버 간 이동·초기 지급 | U03(4인)·U12·U15·U16·U17·U21 | 10/10 M2 4인 + 첫 M3 | 동일 계정/재접속/중복 요청 정합성부터 점검 |
| 10/11~10/13 | P2 포함 보스 구현·그래픽/설명·외부 환경 설치·지연 테스트·자산 권리 | U11·U18·U19·U23 | 10/13 M3 + 지연 0/50/150ms·손실 1%/3% + 장애 주입 | 차단 결함별 담당·재현·해결 근거 기록 |
| 10/14~10/15 | 마지막 M-Boss·동결 빌드·외부 체험·결함 수정·배포 판단 | U10,U11,U15,U20 | 보스 수용을 마지막 M-Boss에서 확인한 뒤 동결 빌드 외부 PC M3 | 기준 미달이면 미달 사실과 일정/범위 선택지를 보고 |

통합 시험은 [다인 테스트 절차](ai/04_MULTIPLAYER_TEST_PROTOCOL.md) 7장의 주간 통합을 2주 일정에 맞춰 각 구간 끝에 실행하는 초기안이다. 통합 담당은 민성([01](01-system-design.md) 7장)이다. 통합 브랜치는 `develop`이고 각자 `feature/{팀원이름}`에서 PR로 병합한다. M3용 서버 PC·클라이언트 PC는 미정이다. 결과는 플레이북 6장 형식으로 남기고 실패 항목은 이슈로 등록한다.

### 일정 초과 시 판단 규칙

1. 범위를 조용히 삭제하거나 축소하지 않는다([01](01-system-design.md) 8장). 구간 끝 점검에서 미달이면 병목 작업 ID·원인·남은 작업·영향받는 수용 기준을 적고 아래 선택지를 사용자에게 제시한다.
2. 착수·인력 배치는 A→B→C 묶음 순서로 한다. 이 순서는 착수 순서이며 체험판 필수 여부나 삭제 순서를 뜻하지 않는다.

| 묶음 | 작업 | 이유 |
|---|---|---|
| A 연결 경로 | U01~U05, U14, U22, U24, U17의 재접속 최소 경로 | 없으면 다른 기능의 통합 검증이 성립하지 않음 |
| B 체험판 루프 | U06~U10, U12, U13, U15, U16, U21 | 파트별 규칙·화면은 A와 병행 제작하되 수용 판정은 A 이후 |
| C 완성·배포 | U11(입력 의존), U17 장애 복구, U18, U19, U20, U23, U25(API·CLI만), U26~U27(체험판 범위는 사용자 결정) | 외부 배포 필수이나 A·B 산출물과 외부 입력이 필요. 보스 전체 실행은 마지막 M-Boss |

3. 사용자에게 제시할 선택지 형식이다. 어느 것을 택할지는 미정이며 사용자 결정 전에는 현재 범위와 목표일을 유지한다.

| 선택지 | 내용 | 영향 |
|---|---|---|
| 가. 범위 유지·일정 조정 | 10/15에는 달성한 검증 결과를 보고하고 외부 체험일을 다시 정함 | 목표일 미충족 |
| 나. 일정 유지·범위 이동 | 사용자가 지정한 항목을 후속 작업으로 옮김 | 01·02·05 본문을 함께 수정, 외부 안내에 누락 명시 |
| 다. 일정 유지·체험 규모 조정 | 8명·20회 외부 체험 대신 규모나 대상을 조정 | 5장 체험 기준의 근거 약화 |
| 라. 입력·자원 보강 | 자산·인력 보강 또는 P2 입력 일정 앞당김 | 권리 확인·통합 비용 증가 |

## 5. 외부 배포 수용 기준

| 분야 | 필수 검증 | 실패 조건 |
|---|---|---|
| 기획 적용 | 3직업·12기술·45효과, 일반 6웨이브, 월백 이지, 성장·경매·생활 | 임시 무적/자동 처치/무료 재화에 의존해야 루프가 성립 |
| 월백 | 제공된 P2 실제 구현 및 P1→P2→처치/실패 | P1만 있으면서 전체 보스를 완료로 표시 |
| 네트워크 | 전용 서버 1/2/4인, 직업 중복, M2·M3, 0/50/150ms 지연·손실 1%/3%, 타이밍 판정 10회 반복 | 호스트 단독 실행만으로 협동 완료 주장 |
| 경제 | 동일 요청/순서 역전/동시 강화/입찰/재접속, 장애 시 복구 | 중복 지급·음수 잔액·소스 장비 복제·미저장 성공 표시 |
| 장애 | 접속 끊김·게임 서버 종료·API 타임아웃·DB 재시작 | 보상 유실을 탐지할 로그·재처리 수단 없음 |
| 성능 | 지정 PC 1080p 평균 60fps·프레임 P95 25ms·메모리 4GB 이하, 서버 프로세스별 CPU·메모리·tick, 마을 10→50→100명·접속/스폰 부하 | 측정 없이 목표 성능/100명 수용을 보장 |
| 체험 | 8명·20회·90분 계획, 재접속 10회, 조작 안내·결과 화면 | 출정/귀환/성장 진행을 막는 결함 잔존 |
| 배포 | 외부 PC 설치·접속, 계정 발급·서버 주소·버전·롤백 안내 | 제작자 PC에서만 실행되거나 운영 권한/비밀값 포함 |

재현 로그에는 buildId·contentVersion·instanceId·accountId의 비식별 참조·requestId·서버 시각을 남긴다. 인증 토큰·개인 비밀번호는 기록하지 않는다. 상세 기준은 [검증 플레이북](ai/03_VERIFICATION_PLAYBOOK.md)과 [다인 테스트 절차](ai/04_MULTIPLAYER_TEST_PROTOCOL.md)를 따른다.

측정 조건의 결정과 실행 상태는 다음과 같다. 조건이 미정이거나 시험을 실행하지 않았으면 해당 기준을 통과했다고 보고하지 않는다.

| 항목 | 현재 문서 상태 | 상태 |
|---|---|---|
| 지연 시험의 패킷 손실률 | 2026-10-01 사용자 결정: 1%/3%. 실제 주입 시험은 미실행 | 확정·실행 미검증 |
| 성능 기준 PC·그래픽 설정 | 목표만 있고 실제 PC 미지정 | 미정 |
| 외부 체험 서버 호스팅·접속 주소·계정 발급 | AWS 방향만 있음 | 미정 |

## 6. 위험과 대응

| 위험 | 영향 | 우선 대응·판정 |
|---|---|---|
| 운영 도구·런처 추가(U24~U27) | 민성의 잔여 운영/배포 작업이 있어 10/15 일정 위험이 남음 | U24 기반은 존재하나 모든 경제 기능 연결과 감사/일일 대사가 남음. U25는 API·CLI만으로 확정, U26의 체험판 범위는 [런처](design/launcher-and-patch.md) 8장 선택지에서 사용자 결정 필요 |
| 민성에게 네트워크·전투·백엔드·배포 집중 | U01~U23 중 20개가 민성 주·공동 담당, 첫 구간(A 묶음)은 거의 민성 단독. 파트 산출물이 있어도 통합 지연 | A 묶음 계약 우선, 파트별 UI·순수 판정 독립 제작, 10/03 점검에서 4장 판단 규칙 적용 |
| 월백 P2 외부 입력 지연 | 전체 보스·외부 체험 검증 차단. 설계 제공자와 수령 기한 미정 | P1을 완성하고 전환 인터페이스 준비; P2를 임의 창작하지 않음. 10/07 점검까지 미수령이면 4장 선택지 제시 |
| M-A와 체험판 수용 사이의 연결 잔여 | 게임 C#·API·DB와 단위/DB 시험은 있으나 실제 서버 빌드·일반전/화면 연결·다인 검증이 남음 | 구현 범위와 검증 수준을 별도로 기록. 780/396개 시험 통과를 M2/M3·전체 체험판 완료로 세지 않음 |
| 솔로 다수의 던전 프로세스 | 100명/4=25개 가정이 깨짐 | 전원 솔로면 100개; 풀 상한·대기열·실측 용량 적용 |
| 확률 드롭·등록·강화 반복 | 성장 피로·티켓 고갈 | 초기 기대값과 실제 분포 비교, 성공/실패 조건을 유지하며 수치 조정 |
| 동시에 발생하는 지급·경매·장애 | 데이터 복제·유실 | 백엔드 transaction·멱등 원장·복구 시나리오를 기능 전제에 포함 |
| 공격 표현과 판정 불일치 | 무적 없는 회피가 불공정하게 느껴짐 | 서버 예고 시간/좌표 사용, 지연별 피격·회피 재현 |
| 외부 에셋 의존·권리 | 빌드 누락·배포 문제 | 실제 의존성·라이선스 대장 확인, 미보유 자산을 완료로 세지 않음 |

## 7. 후속 작업

| ID | 후속 범위 | 착수 조건 |
|---|---|---|
| F01 | 철각 이지·하드 및 일반·희귀 재료 획득 | 체험판 전투·성장 루프 검증 |
| F02 | 월백 하드·전설 제작 | 월백 전체 패턴 입력·이지 검증 |
| F03 | 폐허·제단·여우의 흔적·정식 이야기 해금 | 지역 콘텐츠·서사 제작 계획 |
| F04 | 유저 경매장 | 거래 허용 품목·귀속·원자 거래·운영 도구 구현 |
| F05 | 추가 부적·생활·규모 확장 | 체험 데이터로 우선순위 결정 |
| F06 | 운영 도구 P1·P2: 일괄 지급·2인 승인, 기간제 이벤트, 경제 대시보드·이상 탐지, 제재, 쿠폰·출석 | U24·U25 운영 검증 |
| F07 | 런처 확장: 체험판에서 뺀 기능, 코드 서명, 설치 프로그램 | U26 범위 결정·체험판 피드백 |

## 8. 검증 상태

2026-10-02 현재 미커밋 M-A 구현을 코드·SO·씬·프리팹·설정과 대조했다. 아래 근거는 작업 범위를 확인하는 것이며, 파일 존재를 실제 플레이나 배포 성공으로 해석하지 않는다.

| 관련 작업 | 현재 구현 근거 | 남은 범위/한계 |
|---|---|---|
| U01 | [GameDBBootstrap](../Assets/01.Scripts/00.Common/Data/GameDBBootstrap.cs), [GameDatabaseSO](../Assets/01.Scripts/00.Common/Data/GameDatabaseSO.cs), [ServerBootstrap](../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerBootstrap.cs), [ShinshiBuild](../Assets/Editor/Build/ShinshiBuild.cs), [Bootstrap 씬](../Assets/00.Scenes/Bootstrap.unity) | client/town/dungeon 시작·실패 중단·빌드 진입점 존재. 실제 서버 빌드/기동은 미실행 |
| U02,U03 | [Backend 안내](../Backend/README.md), [인증 저장소](../Backend/src/Shinshi.Data/Auth/PostgresAuthRepository.cs), [예약 저장소](../Backend/src/Shinshi.Data/Reservations/PostgresReservationRepository.cs), [서버 풀 저장소](../Backend/src/Shinshi.Data/Sessions/PostgresServerPoolRepository.cs), [ServerPresence](../Assets/01.Scripts/98.Servers/01.Network/Presence/ServerPresence.cs) | .NET 10·Npgsql/Dapper·SQL 0001~0010·Authorization Bearer. [Contracts 프로젝트](../Backend/src/Shinshi.Contracts/Shinshi.Contracts.csproj)가 Unity DTO 원본을 링크. 방/캐릭터 화면·대기열·진행 해금 검사는 남음. 풀 부족 API는 NO_CAPACITY로 거절하며 대기열 구현으로 인정하지 않음 |
| U04,U05 | [PlayerServer](../Assets/01.Scripts/98.Servers/02.Combat/Player/PlayerServer.cs), [CombatServerRuntime](../Assets/01.Scripts/98.Servers/02.Combat/CombatServerRuntime.cs), [PlayerPrediction](../Assets/01.Scripts/02.Combat/Player/View/PlayerPrediction.cs), [DamageResolver](../Assets/01.Scripts/98.Servers/02.Combat/Damage/DamageResolver.cs), [플레이어 프리팹](../Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab) | 서버 이동/공격/회피·피해·생사·부활 규칙 존재. 프리팹의 이동 예측/NetworkTransform·PlayerInputReader와 상태 참조는 연결됐다. 게임 카메라는 [PF_GameCamera](../Assets/02.Prefab/Camera/PF_GameCamera.prefab)와 [CameraConfigSO](../Assets/01.Scripts/00.Common/Data/CameraConfigSO.cs)로 구현됐고 가림 투명화는 `Shinshi/Environment/Lit Occlusion Fade`와 폴리지 그래프를 쓰는 재질에만 적용된다. 정식 게임 씬 배치와 플레이 확인은 남음. 부활 위치는 현재 위치 근처 NavMesh 샘플이며 맵 지정 안전 지점 선택은 TODO. Obstacle은 [TagManager](../ProjectSettings/TagManager.asset)의 레이어 8이며 실제 입력/맵 충돌/시야/부활 위치 수용은 미검증 |
| U06~U08,U12 | [SkillServer](../Assets/01.Scripts/98.Servers/02.Combat/Skill/SkillServer.cs), [CompanionServer](../Assets/01.Scripts/98.Servers/02.Combat/Skill/CompanionServer.cs), [ResonanceLedger](../Assets/01.Scripts/98.Servers/02.Combat/Skill/ResonanceLedger.cs), [부적 보정 인터페이스](../Assets/01.Scripts/02.Combat/Skill/ITalismanModifierResolver.cs) | 12기술·동료·공명·강림 서버 판정이 존재. 부적 resolver가 없으면 Identity이며 실제 45효과 적용은 미구현. 직업은 접속 토큰 교환 응답의 `classId`(API가 캐릭터 행에서 읽음)를 서버 결합이 받아 `PlayerServer`·`SkillServer`를 만든다(EditMode와 백엔드 테스트 통과, 서버 빌드·M2 실행은 미검증). 클라이언트 기술 입력은 [PlayerInputReader](../Assets/01.Scripts/02.Combat/Player/View/PlayerInputReader.cs)·[SkillInputRules](../Assets/01.Scripts/02.Combat/Skill/Rules/SkillInputRules.cs)·[SkillRangeIndicatorView](../Assets/01.Scripts/02.Combat/Skill/View/SkillRangeIndicatorView.cs)로 연결됐다(키 해석·지정 전이·지정점 제한은 EditMode, 범위 판정·표시는 에디터 Play Mode 단독 호출로만 확인, 접속 상태 실제 입력·M2 미검증). 로컬 플레이어 읽기 창구 [LocalPlayerView](../Assets/01.Scripts/02.Combat/Player/View/LocalPlayerView.cs)(`ILocalPlayerView` 구현)와 HUD 연결 [LocalPlayerHudBinder](../Assets/01.Scripts/02.Combat/Player/View/LocalPlayerHudBinder.cs)·[LocalPlayerHudRules](../Assets/01.Scripts/02.Combat/Player/Rules/LocalPlayerHudRules.cs)를 추가했다. 소유 플레이어에서만 동작해 체력·기력(HUD의 HP·MP)·Q/W/E/R 쿨다운(서버 준비 시각 SyncVar와 카탈로그 쿨다운)을 `HudPlayerStatusView`·`HudSkillSlotView`에 넣고, 직업이 바뀌면 칸 기술을 다시 정한다. 기술 아이콘은 `SkillCatalogSO`의 선택 필드이며 비어 있으면 HUD 기본 그림을 둔다(현재 값 없음). 계산은 EditMode, 뷰 반영은 Dev_HUD 씬 Play Mode에서 가짜 상태로만 확인했고 접속 상태 실제 값·M2는 미검증이다. 남은 범위: 공명 게이지와 Z/X/C 강림 칸, 거절 칸 피드백은 HUD 쪽 칸·API가 필요하며(`SkillRejected` 이벤트는 `LocalPlayerView`가 전달), 레벨은 값 출처가 없어 연결하지 않았다. 신령사 동료는 [CompanionSpawnTracker](../Assets/01.Scripts/98.Servers/02.Combat/Skill/CompanionSpawnTracker.cs)·[CompanionNetworkSpawner](../Assets/01.Scripts/98.Servers/02.Combat/Skill/CompanionNetworkSpawner.cs)가 매 틱 활성 동료와 [PF_NetCompanion](../Assets/02.Prefab/Network/NetCompanion/PF_NetCompanion.prefab)의 짝을 맞추고 [CompanionView](../Assets/01.Scripts/02.Combat/Skill/View/CompanionView.cs)가 임시 도형으로 보여 준다. 기술 표현은 남음 |
| U09~U11,U14,U15 | [DungeonSession](../Assets/01.Scripts/98.Servers/01.Network/Session/DungeonSession.cs), [DungeonSessionHost](../Assets/01.Scripts/98.Servers/01.Network/Session/DungeonSessionHost.cs), [LootRoller](../Assets/01.Scripts/98.Servers/01.Network/Session/Reward/LootRoller.cs), [SettlementRules](../Assets/01.Scripts/98.Servers/01.Network/Session/Settlement/SettlementRules.cs) | 일반전 source/웨이브/직업 드롭 원본 연결 TODO. 현재 호스트는 wave 없는 개발용 세션을 열고 보스 종료 훅만 제공한다. 적/보스 AI·맵·회수함은 남음. 파티 경매·단독 구매·분배의 API·던전·Mirror 연동은 구현됐고 실제 서버 경매 실행·UI는 남음. 인원별 배율은 산출/보관만 하며 적 체력/피해 적용 TODO |
| U16,U17 | [ReconnectVault](../Assets/01.Scripts/98.Servers/02.Combat/Player/ReconnectVault.cs), [이벤트 저장소](../Backend/src/Shinshi.Data/Sessions/PostgresGameSessionRepository.cs), [정산/복구 저장소](../Backend/src/Shinshi.Data/Settlement/PostgresSettlementRepository.cs), [유지 서비스](../Backend/src/Shinshi.Api/Sessions/GameSessionMaintenanceService.cs) | 물약 예약/소모/반환·120초 재접속·단일 정산/장애 보정 기반 존재. 사용/회복·포기/귀환 사용자 흐름과 실제 프로세스/API/DB 장애 주입은 남음 |
| U19,U22 | [배포 안내](../deploy/README.md), [게임 서버 안내](../deploy/gameserver/README.md), [던전 systemd 유닛](../deploy/gameserver/shinshi-dungeon@.service), [ServerShutdownController](../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerShutdownController.cs), [Start-M2](../tools/Start-M2.ps1), [로그 규약](../deploy/LOGGING.md) | 던전은 출정 1개 종료 뒤 프로세스를 끝내며 systemd Restart=always로 새 bootId 기동. 도구는 빌드를 만들지 않으며 M2 실행 결과도 아님. 공통 구조화 로그/지표 연결과 배포는 남음 |
| U24,U25,U27 | [경제 원장 모듈](../Backend/src/Shinshi.Data/Ledger/PostgresEconomyLedgerWriter.cs), [아이템 이력](../Backend/src/Shinshi.Data/Ledger/PostgresItemHistoryWriter.cs), [대사 함수](../Backend/src/Shinshi.Data/Ledger/EconomyLedgerReconciler.cs), [AuthBuildPolicy](../Backend/src/Shinshi.Api/Auth/AuthBuildPolicy.cs), [0008 마이그레이션](../Backend/src/Shinshi.Data/Migrations/0008_admin_operations.up.sql), [어드민 API](../Backend/src/Shinshi.Api/Admin/), [우편함 API](../Backend/src/Shinshi.Api/Inbox/InboxController.cs) | 원장/outbox/대사·점검/허용 빌드 검사 기반 존재. U25 P0 어드민 API·감사 로그·우편함·서비스 상태 DB 이관 구현됨(백엔드 자동 테스트 통과(실제 PostgreSQL), CLI·운영 실행 미검증). 일일 대사 호출·서명/게시/런처는 남음 |
| U13,U18,U20,U21,U23,U26 | [GameDB](../Assets/01.Scripts/00.Common/Data/GameDB.cs), [에셋 대장](licenses/ASSET_REGISTRY.md), [런처 계획](design/launcher-and-patch.md) | 관련 SO/계약·일부 씬/자산을 실제 성장/사용자 흐름/초기 지급/런처 구현으로 세지 않는다. 수치/성능/외부 체험과 최종 자산-빌드 대조도 미실행 |

실행 증거와 이번 정적 검수는 구분한다.

| 검증 | 확인한 결과 | 적용 한계 |
|---|---|---|
| Unity Test Runner EditMode | 사용자 제공 결과: 780개 통과 | 이번 문서 작업에서 다시 실행하지 않음. 담당 씬 플레이·PlayMode·서버 빌드·M2/M3 수용을 대신하지 않음 |
| Backend 시험 | 사용자 제공 결과: 396개 통과, 실제 PostgreSQL 18 Docker 사용 | 이번 문서 작업에서 다시 실행하지 않음. [Backend tests](../Backend/tests/Shinshi.Backend.Tests/)의 단위/HTTP/DB 범위이며 Unity↔API 다인 통합·운영 배포를 대신하지 않음 |
| IL2CPP JSON/stripping | [manifest](../Packages/manifest.json)에 Newtonsoft 직접 의존, [link.xml](../Assets/link.xml)에 DTO/Newtonsoft 보존 설정 존재 | 실제 IL2CPP 빌드/왕복 실행 미검증 |
| 서버 빌드·M2/M3·M-Boss·배포·성능·외부 체험 | 미실행 | 성공 수치·추정 통과율·전체 완료로 표시하지 않음 |

2026-10-01 사용자 결정과 실제 구현을 혼동하지 않는다. KCP 암호화는 체험판에 적용하지 않으며, Bootstrap은 KcpTransport를 사용한다. `BackendUnavailable=810`은 [공유 거절 코드](../Assets/01.Scripts/00.Common/Contracts/ContractCodes.cs)와 [Unity enum](../Assets/01.Scripts/00.Common/Types/ERejectReason.cs)에 있다. 정산은 아이템의 계정 귀속과 정기/정련편/티켓/엽전·부적 칸을 분리한다. 장애 취소 보상은 저장된 완료 웨이브 기본 정기 전액과 추가 정기 50% 내림으로 확정되어 정산 코드/SQL에 반영됐다.

남은 결정/설정 불일치는 아래와 같다. 코드·SO를 문서 작업에서 임의로 바꾸지 않는다.

| 항목 | 확정 결정/계획 | 실제 코드/설정과 남은 일 |
|---|---|---|
| 장애 취소 추가 정기 SO | 기본 전액 + 추가 50% 내림 | [SessionConfig 에셋](../Assets/09.Data/Session/SessionConfig.asset)의 `_serviceCancelExtraEssence`는 아직 0(`Undecided`). [GameDBBuildValidator](../Assets/Editor/Build/GameDBBuildValidator.cs)는 비개발 빌드에서 이를 거절한다. 실제 정산 규칙과 설정을 담당자가 맞춘 뒤 빌드 검증 필요 |
| 전체 체험판/실제 출정 연결 | 일반 6웨이브와 보스·직업 선택·45효과·성장/생활/화면을 모두 수용 | M-A는 저장/전투 기반이며 호스트의 개발용 세션·개발 토큰의 기본 직업(무사)·Identity 부적 보정·미적용 배율을 전체 체험판 동작으로 인정하지 않음 |
