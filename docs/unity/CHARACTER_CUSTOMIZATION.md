# 캐릭터 커스터마이즈

캐릭터 생성 화면에서 전투 직업, P09 모듈 캐릭터 외형, 닉네임을 정해 생성한다. 외형 선택은 전투 직업과 독립적이다. 2026-10-08 [신규 직업 설계](../design/character-animation-design.md)는 검사·마검사·궁수이며 P09 외형을 유지한다. **현재 구현**은 무사0·도술사1·신령사2와 검사3·마검사4·궁수5를 보존하고 SQL 0016이 0~5를 허용한다. 2026-10-11 사용자 결정으로 API 신규 생성은 `3,4,5`만 허용하며 기본·빈 설정도 동일하다. 명시 설정으로 구직업0~2를 넣어도 생성되지 않는다. 구 진도 자동 변환은 하지 않으며 기존 ID의 읽기 계약은 보존한다. 외형 ID와 직업 ID를 혼용하지 않는다.

## 외형 항목(외형 계약 version 3)

| 항목 | JSON 키 | 값 | 비고 |
|---|---|---|---|
| 성별 | `bodyType` | 0 여성, 1 남성 | |
| 얼굴형 | `faceId` | 1 ~ 3 | |
| 헤어 | `hairId` | 1, 2, 3 민머리 | |
| 헤어 색 | `hairColor` | 1 ~ 9 | 갈색, 파랑, 금색, 초록, 아이보리, 주황, 분홍, 보라, 빨강 |
| 눈 색 | `eyeColor` | 1 ~ 5 | |
| 피부 | `skinTone` | 1 ~ 3 | 기본, 밝게, 어둡게(성별별 재질) |
| 수염 | `facialHair` | 남성 0 ~ 8, 여성 0 | 0은 없음 |
| 가슴 크기 | `bustSize` | 여성 1 ~ 3, 남성 0 | |
| 장비 표시 | `outfitId` | 0 ~ 31 비트 | 1 머리, 2 가슴, 4 팔, 8 허리, 16 다리. 켜면 Armor_003, 끄면 맨몸(머리·허리는 없음) |

- 슬라이더 `face`(22개)·`body`(10개)는 모두 128, `browColor`·`lipColor`는 0이어야 한다.
- 표정은 생성 화면 미리보기 전용이며 저장하지 않는다. 무기·방패는 표시하지 않는다.
- 현재 구현의 직업 기본 외형(모두 여성, 장비 전부 착용, 가슴2): 무사 얼굴1·헤어1·헤어색1·눈1·피부1, 도술사 얼굴2·헤어2·헤어색5·눈4·피부2, 신령사 얼굴3·헤어2·헤어색8·눈3·피부1. 신규 직업 3~5도 `CharacterAppearanceCatalog.CreateDefault`의 직업별 기본값을 사용하며 기존 캐릭터 외형을 직업 변경 이유로 임의 초기화하지 않는다.

## 저장과 호환

- 외형은 엔진 비의존 `CharacterAppearance`로 DB `characters.appearance`(jsonb)에 저장한다. 스키마 변경은 없다.
- 판정은 `CharacterAppearanceCatalog`가 한다. API 생성 검증, 게임 서버 입장, 선택 화면이 같은 함수를 쓴다.
  - 새 캐릭터는 version 3으로 만든다. API는 기존 요청의 재시도를 위해 version 1과 허용된 version 2도 계속 받는다.
  - 표시(`TryResolve`)는 항상 version 3이다. null·version 1·version 2는 직업 기본 version 3으로 보인다. DB 값은 바꾸지 않는다.
  - version 1/2의 JSON 키와 값은 그대로라 기존 생성 operation request hash가 유지된다(백엔드 골든 테스트).
- 생성 요청의 operationId는 직업·닉네임·외형 9개 값이 모두 같은 재시도에만 재사용한다.
- 마을·던전 입장 토큰을 소비할 때 백엔드가 소유 캐릭터의 닉네임과 외형을 DB에서 읽는다. 서버는 검증된 값을 플레이어 생성·재결합 전에 적용하고 `PlayerCombatState`로 복제한다. 클라이언트가 보낸 외형으로 서버 플레이어를 덮어쓰지 않는다.
- Mirror 직렬화는 version 1 44바이트, version 2 46바이트, version 3 48바이트다. 네트워크 프로토콜은 version 4다.

## 모델과 조립

- 모델은 서드파티 P09 Modular Humanoid(`Assets/99.ThirdParty/Model/P09_Modular_Humanoid/`, Git 제외)다. 이 패키지가 없으면 캐릭터가 보이지 않는다.
- 원본 재질은 lilToon 셰이더라 URP Lit 사본을 쓴다: `Assets/01.Scripts/05.Art/Characters/PlayerP09/Materials/`(변환 기록 `README.md`). 원본 HDRP 마스크는 선형으로 다시 임포트한 사본(`Textures/`)을 금속·AO 맵으로 쓴다. 헤어·눈·수염은 원본에 AO 데이터가 없다.
- 프리팹은 원본 `P09_Human`의 변형 `Assets/02.Prefab/Characters/PlayerP09/PF_PlayerP09.prefab`이다. 재질만 사본으로 바꾸고 무기는 끈다.
- 구 카탈로그는 `Assets/09.Data/Avatar/PlayerCharacterLookCatalog.asset`, 출시 `PF_NetPlayer` 참조는 `PlayerCharacterLookCatalog_Classes.asset`이다. `PlayerCharacterLookApplier`가 부위 오브젝트를 켜고 끄며 색 재질 슬롯을 바꾼다. 필요한 부위가 하나라도 없으면 아무것도 바꾸지 않고 실패한다.
- 위 에셋은 에디터 도구 `PlayerP09AssetBuilder.BuildAll`(재질·프리팹·클립·컨트롤러·카탈로그)과 `PlayerP09SceneWiring.WireAll`(생성·선택 씬, PF_NetPlayer 연결)로 다시 만들 수 있다.
- 생성 화면 왼쪽의 외형 패널(`CharacterCustomizePanel`)이 항목별 이전/다음 버튼을 제공한다. 이전 Tripo 외형 탭은 씬에서 꺼 두었다.

## 애니메이션

출시 `PF_NetPlayer`는 `PlayerCombatAnimationProfile_Classes`와 Classes 외형 카탈로그를 참조한다. 신규 세 직업의 기본 공격·QWER 배정과 `AN_Release_*` 구간 클립 17개가 있으며 실제 재생·타격시각·리타깃·무기 소켓·서버 타임라인 수용은 [직업 이관 게이트](../05-implementation-roadmap.md#직업-개편-구현이관-검증-게이트2026-10-07)로 확인한다. 아래 CombatGirls 13클립·기본 컨트롤러 설명은 구 경로 기록이다.

- 클립은 서드파티 CombatGirls Sword 팩(`Assets/99.ThirdParty/Animation/CombatGirlsCharacterPack/`, Git 제외)을 `.anim`으로 복사한 것이다: `Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/Clips/`. 애니메이션 이벤트를 지우고, 루트 XZ 이동·회전은 굽지 않고 애니메이터 루트 모션을 꺼 제자리에서 재생한다.
- 컨트롤러 `AC_PlayerP09`: `Speed` 블렌드(Idle 0, Walk 2.5, Run 5m/s), 행동 trigger(공격 3단, 기술 7종, 회피, 피격), `Alive`(사망), 표정 레이어.
- `PlayerCharacterCombatAnimationView`가 서버 공개 상태로 행동 모션을 재생한다. 기본 공격·회피는 `PlayerCombatState`의 표현 전용 순번(`AttackSequence`, `DodgeSequence`), 기술은 시전 번호와 단계, 피격은 체력 감소, 사망은 생사 상태를 쓴다. 판단은 SyncVar hook이 아니라 메시지 처리가 끝난 뒤의 `Update`에서 한다.
- 구 경로의 기술별 클립 배정과 재생 속도는 `Assets/09.Data/Avatar/PlayerCombatAnimationProfile.asset`, 배정표는 `Animations/README.md`다. 구도술사·구신령사는 전용 시전 모션이 없어 검 모션을 임시로 쓴다. 출시 Classes 프로필은 이 구 임시 배정과 별도로 신규 행동을 연결한다. 연결 사실을 부적 변형·타격 연출의 실제 수용으로 해석하지 않는다. 현재 컨트롤러에는 일어서기 클립이 없다.

## 검증 상태

아래 기록은 P09 외형과 구직업의 현재 구현 증거다. 2026-10-07 신규 직업 설계·클립/코드/데이터 이관의 실행 검증으로 확대하지 않는다.

- 백엔드 생성 요청 재생·v3 저장·입장 전달 테스트, Unity EditMode 계약·직렬화·선택 상태·적용기·행동 규칙 테스트가 있다.
- 개발 빌드의 `DevCustomizationScenario`는 실제 로비 버튼과 외형 패널 버튼으로 생성, 선택, 마을의 본인·원격 외형(9개 값), 재접속을 검사한다. `-dev-combat-autopilot`은 소유 플레이어가 기본 공격·회피·기술 입력을 반복해 보낸다.
- 2026-10-06 로컬 API·DB·마을 서버에서 생성 → 선택 → 입장 → 재접속(1클라이언트)과 두 클라이언트의 원격 외형, 기본 공격·회피 모션 동기화를 확인했다.
- 구현됨·실행 미검증: 기술 모션의 네트워크 동기화(마을은 기술이 거절돼 던전에서 확인 필요), 피격·사망 동기화, 서로 다른 PC와 지연 환경, 캐릭터 여러 명일 때 성능(P09는 LOD 없음), 근거리 조명별 화질.
