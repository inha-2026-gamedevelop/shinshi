# 캐릭터·애니메이션 개편 — 검사·마검사·궁수

> 기준일: 2026-10-08. 사용자 확정: 검사·마검사·궁수(세 번째 직업). 후속 요청으로 서버 전투·부적·저장 계약과 별도 애니메이션 생성기를 구현했다. 아래 모션 배정은 초기안이며 실제 생성에서 19후보 중 궁수 기본/Q/R 3개만 정적 시간 예산을 통과했다. 나머지 16개는 보류다. 기존 게임 프리팹 연결·실제 재생 검증은 미적용이며 [구현 로드맵](../05-implementation-roadmap.md#2026-10-08-세-직업-구현과-검증)에 검증 범위를 기록한다.

[게임 디자인](../02-game-design.md) · [전투 규칙](combat-design.md) · [부적 목록](talisman-catalog.md) · [P09 외형 계약](../unity/CHARACTER_CUSTOMIZATION.md) · [설치·권리 대장](../licenses/ASSET_REGISTRY.md).

## 1. 조사 근거와 적용 범위

실제 경로는 `Assets/99.ThirdParty/Animation/`이다. 버전은 `.meta`의 AssetOrigin, 모션 유형은 실제 FBX 이름·폴더·clipAnimations, Humanoid는 importer의 `animationType: 3`에서 확인했다. 궁수 애니메이션은 Humanoid454/Generic8이며 모든 파일이 Humanoid인 것은 아니다. Generic8은 Walk/Inplace의 8방향 `_Root.FBX`다. 설치·리타게팅 설정과 P09 실제 재생 성공은 별개다.

| 패키지 | 설치 버전·productId | 애니메이션 FBX | 이번 역할 |
|---|---|---:|---|
| CombatGirlsCharacterPack | 1.3.1·345454 | 36(전투/이동 29+감정 7) | 검사 검격·이동·회피 주 후보 |
| Magical-Knight_Set | 1.23·180708 | 239(T-pose 1+원본 119+Inplace 119) | 마검사 검격·마법·이동·회피 주 후보 |
| Succubus_Anim_Set | 1.21·179271 | 151(T-pose 1+원본 75+Inplace 75) | 보스/후속 표현 조사 후보·궁수 주 모션 아님 |
| Stylish Archer Assets Pack | 1.4·249931 | 462(전체 FBX466 중 모델4 제외) | 궁수 사격·차지·회피사격 후보 |

원본/Inplace·콤보 전체/분절·Z 변형을 새로운 기술 수로 합산하지 않는다. 공급자 설명 PDF는 [Magical-Knight](../../Assets/99.ThirdParty/Animation/Magical-Knight_Set/Animation%20Asset%20Description.pdf)와 [Succubus](../../Assets/99.ThirdParty/Animation/Succubus_Anim_Set/Animation%20Asset%20Description.pdf)의 각 1쪽이다. Root Motion/In-Place 제공을 설명할 뿐 기술별 베기 방향·효과·타격 순간은 설명하지 않는다. `AttackEnergy`를 빔/폭발/검풍, `S_Sp_Skill`을 횡/종베기로 확인한 사실처럼 쓰지 않는다.

현재 P09는 CombatGirls에서 만든 `.anim` 사본 13개와 `AC_PlayerP09`를 사용한다. [현재 배정표](../../Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/README.md)는 도술사/신령사에 검 모션을 임시 배정한 구현 설명이다. 이 문서의 마검사 모션으로 이미 교체됐다는 뜻이 아니다. 신규 세 직업은 P09 남녀·기존 외형 저장 계약을 유지한다. 공급자 몸·의상·세계관을 자동 채택하지 않는다.

## 2. 전투 방향

검사는 검격 방향·접근·후속 타격을 조절하는 근접 직업이다. 대검 고정·지면 내려찍기·전방 방어 자세가 필요한 구 무사 규칙을 강제하지 않는다. 기본 3단 모션과 Q 넓게 베기/W 돌진 베기/E 집중 베기/R 연환참을 쓴다.

마검사는 근접 검 기본 공격에 술식을 실어 범위를 바꾼다. Q 마력검격/W 자기 위치 화염검진/E 근접 봉인검격/R 방향 뇌광검파로 제어와 중거리 견제를 섞는다. 부적 없는 Q/E는 근접이다. 별도 마나·마력 스택·공명·강림을 추가하지 않고 공통 기력만 쓴다. 검인·정기·부적 표현으로 세계관에 맞춘다.

궁수는 직접 조준하는 원거리 직업으로 Q 속사/W 후퇴사격/E 관통시위/R 산개사격과 무료 원거리 기본 공격을 쓴다. 대표 E의 6효과와 나머지 각3효과를 [궁수 기준안](archer-design.md)에 정의한다. 공통 기력을 쓰고 동료·공명·강림·비행은 추가하지 않는다. 구 신령사의 4기술·15효과·동료/강림은 [구설계](legacy-class-design.md)에 보존한다. 세 직업의 설계 합계는 **12기술/45효과**이며 실제 이관 완료와 구분한다.

## 3. 클립 후보 배정

접두부 아래의 실제 공급자 파일을 가리킨다. 사본 생성/Controller 연결은 후속 구현이다. 표에서 각 파일에 같은 셀의 접두부/하위 폴더를 적용한다.

- `CG`: `Assets/99.ThirdParty/Animation/CombatGirlsCharacterPack/Sword_Girl/Animations/`
- `MK`: `Assets/99.ThirdParty/Animation/Magical-Knight_Set/Animation/Humanoid/Inplace/`

| 행동·기술 | 실제 파일 후보 | 재생 뒤 결정할 것 |
|---|---|---|
| 검사 기본 1/2/3 | CG `Normal/S_Attack_1.fbx`, `Normal/S_Attack_2.fbx`, `Normal/S_Attack_3.fbx` | 각 1요청 1타·전방 궤적·연결/리셋 |
| KS01 넓게 베기 | CG `Special/S_Sp_Skill_1.fbx`(현재 사본 4.0초) | **시간 예산 초과·주 후보 재검토 필수**, 120도 전방 1타 검 궤적 |
| KS01 연속/회전 핵심 | CG `Normal/S_Attack_1.fbx`+`Normal/S_Attack_2.fbx` / `Special/S_Sp_Skill_2.fbx` | **예산 초과·배정 보류**, 2회차 연결/원형 궤적 지원 여부 |
| KS02 돌진 베기 | CG `Special/S_QuickShift_F.fbx`+`Normal/S_Attack_3.fbx` | **전체 순차 재생 예산 초과·배정 보류**, 서버 이동/검격·루트 중복 없음 |
| KS03 집중 베기 | CG `Special/S_Sp_Skill_3.fbx`(현재 사본 4.4초) | **시간 예산 초과·주 후보 재검토 필수**, 80도 전방 1타 |
| KS04 연환참 | CG `Normal/S_Attack_2.fbx`+`Normal/S_Attack_3.fbx` | **예산 초과·배정 보류**, 후속 간격·연결 구간 재설계 필요 |
| 검사 이동·회피·피격·사망 | CG `Normal/S_Idle.fbx`, `Normal/S_Walk.fbx`, `Normal/S_Run.fbx`, `Normal/S_Evade.fbx`, `Normal/S_Hit_L.fbx`, `Normal/S_Hit_R.fbx`, `Normal/S_Die.fbx` | 속도/회피 방향·접지·원샷 종료 |
| 마검사 기본 1/2/3 | MK `AttackSword01_inplace.fbx`, `AttackSword02_inplace.fbx`, `AttackSword03_inplace.fbx` | 1타씩·근접 검/몸 방향·접지 |
| MK01 마력검격 | MK `AttackSword04_inplace.fbx` | 100도 전방 검격·검인/VFX 소켓 |
| MK01 삼갈래 핵심 | MK `AttackEnergy01_inplace.fbx` | 근접 교체·3갈래 방출 시점 |
| MK02 화염검진·단일/잔불 | MK `AttackEnergy03_inplace.fbx` | 자기 위치 시전·독립된 장판/폭발 VFX |
| MK03 봉인검격 | MK `AttackSword05_inplace.fbx` 또는 `AttackEnergy02_inplace.fbx` | 근접 판정 가독성·당김 시각 |
| MK04 뇌광검파·연속 핵심 | MK `AttackEnergy06_inplace.fbx` | 정면 방출·두 번째 방출, Z 변형 자동 대체 금지 |
| 마검사 이동·회피·피격·사망·기상 | MK `idle_inplace.fbx`, `move_walk_front_inplace.fbx`, `move_run_front_inplace.fbx`, `avoid_front_inplace.fbx`, `hit_body_front_inplace.fbx`, `dead01_inplace.fbx`, `down_rise_inplace.fbx` | 최초 후보·다른 방향으로 확장, 접지·원샷/기상 종료 |

마검사 이동/회피/피격/사망은 MK의 `move_`/`avoid_`/`hit_`/`dead` 계열에서 고른다. 8방향·시작/정지·구르기/step·기상이 있지만 최종 파일 배정은 재생 뒤 정한다. `AttackEnergy09_Start/Loop/End`와 `Buff01`은 지속시전/강화 확장 후보이며 4기술에 자동 추가하지 않는다. 공중 공격이 있어도 점프·비행 입력을 추가하지 않는다. 검사 팩에는 이름 기준 전용 방어·점프·기상이 확인되지 않았고 새 검사에 방어/반격을 배정하지 않은 이유다. 부활의 기상 연출은 이동/입력 복구와 별도로 해결한다.

검풍·검진·속박·번개는 **추가 제작/선정할 VFX**다. 동명 FBX에 이 효과가 포함됐다고 주장하지 않는다. 정확한 원형/긴 준비/짧은 연격이 후보와 맞지 않으면 다른 검격 후보 또는 행동 수치를 다시 정한다. 재생 확인 전 표의 매핑은 채택 완료가 아니다. Succubus의 `idle_inplace`는 이미 [월백 대기 모션](../licenses/ASSET_REGISTRY.md#월백-대기-모션-파생물--2026-10-06)에 쓰인다. 궁수 주 모션은 Grruzam 팩을 사용하며 Succubus를 자동 채택하지 않는다.

### 궁수 후보와 정적 확인

[궁수 모션 표](archer-design.md#4-보유-모션과-표현-후보)가 파일 후보·메타 프레임 범위의 기준이다. 단발/속사/차지/회피사격을 활용하고 알파벳 Skill_A~G를 특정 효과라고 단정하지 않는다. R 산개는 단발 동작에 정기 화살 분기를 붙이는 표현 초기안이며 5발 그립이 있는 모션을 확인한 것은 아니다.

전체 FBX466 중 rig 경고가 있는 meta446개·animation import 경고가 있는 meta413개를 확인했다. rig 경고는 복사한 Avatar 뼈 길이 불일치다. 현재 콘솔 오류나 실패 횟수가 아니라 공급자 임포트 기록이다. `.meta`의 프레임 범위만 확인했고 초 단위 실제 클립 길이·발사 프레임과 P09 그립은 미측정이다. [궁수 정적 길이/시간 예산](archer-design.md#정적-길이와-시간-예산)은 헤더30fps로 계산했으며 초과 후보는 정식 배정 보류다. Loop를 가진 발사/차지 후보의 회차/종료와 Generic 이동 후보의 대체/변환은 프로젝트 사본에서 검증한다. 원본은 수정하지 않는다.

### 확인된 사본 길이와 시간 예산

기존 `PlayerP09/Animations/Clips/AN_P09_*.anim`의 `m_StartTime`/`m_StopTime`·30fps 직렬화 값을 정적으로 읽었다. 이는 현재 사본의 전체 길이이며 검이 맞는 프레임을 실제 재생한 증거가 아니다. 준비 시간만 전체 길이와 비교하지 않고 최초 준비+후속 간격+공통 후딜로 행동 예산을 계산했다. 콤보는 두 전체 클립을 순차 재생하는 보수적 상한이다.

| 후보·행동 | 사본 전체 길이 | 설계 행동 예산 | 전체 재생에 필요한 속도 | 판정 |
|---|---:|---:|---:|---|
| S_Attack_1/2/3 기본 공격 | 1.4/1.4/2.1667초 | 0.9초 간격 | 약 1.56/1.56/2.41배 | 3배는 생성기 상한이며 수용 기준은 재생 뒤 정함·0.2초 타격/연결 미확인 |
| S_Sp_Skill_1 KS01 기본 | 4.0초 | 0.2+0.2=0.4초 | 10배 | 초과·현재 초기값으로 전체 클립 채택 금지 |
| S_Sp_Skill_2 KS01 회전 | 2.4초 | 0.2+0.2=0.4초 | 6배 | 초과·재검토 필수 |
| S_Sp_Skill_3 KS03 | 4.4초 | 0.6+0.2=0.8초 | 5.5배 | 초과·현재 초기값으로 전체 클립 채택 금지 |
| Attack_1+2 KS01 연속 | 2.8초 | 0.2+0.15+0.2=0.55초 | 약 5.09배 | 초과·클립 선택/부분 구간/타격 간격 재설계 필요 |
| Attack_2+3 KS04 연환참 | 3.5667초 | 0.3+0.2+0.2=0.7초 | 약 5.10배 | 초과·연결/회차 재설계 필요 |
| QuickShift_F+Attack_3 KS02 | 1.0+2.1667초 | 0.3+0.2=0.5초 | 약 6.33배 | 전체 순차 재생 불가, 이동과 공격 구간 재검토 필수 |

현재 생성기는 0.5~3배로 제한하고 지연이 0.5초를 넘으면 다른 기술 클립으로 대체한다. 기존 표에서도 Warrior E의 `S_Sp_Skill_3`는 `S_Attack_3`, R의 `S_Sp_Skill_1`은 `S_QuickShift_F`로 바뀌었다. 이 자동 대체는 의도한 검 궤적을 보장하지 않으므로 새 개편에서 사용하지 않는다. **초과 후보는 임시 타격 수치와 함께 정식 배정 보류**이며, 실제 검 궤적 구간을 측정해 다른 클립/부분 구간/준비·후속·후딜 중 무엇을 바꿀지 정한 뒤 양쪽 문서를 갱신해야 한다. 부분 잘라내기가 자연스럽다는 가정도 금지한다. Magical-Knight의 실제 임포트 길이·검/방출 시점은 아직 측정하지 않았고 같은 게이트를 적용한다.

## 4. 표현·타이밍 적용 계약

- 서버는 이동·공격·비용·쿨다운·제어·부적 회차를 판정한다. Animator/Animation Event가 피해·무적·자원을 생성하지 않는다. 클라이언트는 확정 시전/타격 회차·조준·위치를 표현하고 Gameplay 루트를 애니메이션으로 이동시키지 않는다.
- 공급자 FBX를 보존하고 프로젝트 사본에서 이벤트·Loop·Root를 조정한다. CG의 `SwitchSocket` 39개 이벤트는 공급자 무기 전환용이다. P09 소켓·검 표시는 프로젝트 계약으로 연결하고 공급자 이벤트를 타격 근거로 쓰지 않는다. 기존 P09 사본은 이벤트 제거 상태다.
- MK/Succubus importer는 비 T-pose에서 Loop와 XZ/Y bake가 켜져 있다. 공격·피격·사망·시전 시작/끝은 사본에서 원샷으로 설정하고 지속 Loop는 명시한 상태에서만 반복한다. 파일명으로 제자리 재생을 보장하지 않는다. `applyRootMotion=false`·이동/회전 잔류·루트 불변을 실제 재생으로 확인한다.
- 준비·후속·후딜은 [전투 기준안](combat-design.md) 초기값이다. 클립 길이/검 궤적 시점을 측정하고 시전 단계를 연결한다. 현재 생성기의 speed 0.5~3 제한/다른 클립 자동 대체가 후보를 바꿔도 신규 배정 완료로 보지 않는다. 과도한 속도 변형보다 클립/타이밍 재설계를 우선한다.
- 서버가 취소한 후속 회차는 표현도 취소한다. 지연 이벤트로 다시 보여주지 않는다. 이미 만든 장판/투사체 수명·종료를 맞춘다. 화염검진 4초는 캐릭터 동작을 4초 Loop한다는 뜻이 아니다.
- P09 주손/양손 그립·활 시위/화살 발사점·검 길이·휴대/전투 소켓·무기 표시와 남녀 체형의 손목/어깨/발바닥·갑옷 관통을 검증한다. 한손검/대검은 파일명으로 확정하지 않고 실제 그립을 재생 확인한 뒤 정한다. 검 표시가 서버 판정 길이를 자동 변경하지 않는다.

## 5. 구 구현·저장 데이터 이관

현재 [`EPlayerClass`](../../Assets/01.Scripts/00.Common/Types/EPlayerClass.cs), [`PlayerConfigSO`](../../Assets/01.Scripts/00.Common/Data/PlayerConfigSO.cs), [`SkillCatalogSO`](../../Assets/01.Scripts/00.Common/Data/SkillCatalogSO.cs), [`TalismanCatalogSO`](../../Assets/01.Scripts/00.Common/Data/TalismanCatalogSO.cs)는 구 직업·12기술/45효과다. 도술사의 9m·각도 0 기본 공격은 서버에서 원거리로 판정한다. 검 모션만 교체해 마검사 근접 공격이 구현됐다고 보고할 수 없다.

| 구 코드·설계 | 신규 문서 대상 | 차이·이관 판단 |
|---|---|---|
| Warrior 0·T01/T02 | 검사 KS01/KS02 | 유사 규칙 후보 대응. 이름·타이밍·그립/수치 확인 뒤 변환표 작성 |
| Warrior 0·T03/T04 | 검사 KS03/KS04 | 내려찍기→전방 검격, 방어 반격→연환참. 효과 의미 변경·자동 동등 변환 금지 |
| Sorcerer 1·T05~T08 | 마검사 MK01~MK04 | 근접 기본/Q/E·자기 위치 W·방향 R. 원거리/지점 판정·진도와 구분 |
| Summoner 2·T09~T12·강림 | 궁수 AR01~AR04로의 별도 이관 정책 필요 | 캐릭터·카드·등록 레벨·실패 보정·조합 보존. 임의 변환/삭제 금지 |

구 직업 진도를 새 직업으로 변환하는 이관은 승인·구현되지 않았다. 후속 구현은 구 ID를 보존하고 Swordsman=3/Mageknight=4/Archer=5, 신규 기술 12개와 T13~T24 효과 45개를 추가했다. KS/MK/AR은 계속 문서용 코드다. 등록 진도 변환·보상 정책은 별도로 확정해야 한다.

후속 구현 전에 아래를 함께 설계·검증한다.

1. 직업 enum·공통 DTO·SQL `class_id IN(0,1,2)`, 기술 12개·부적 45개 고정 검증과 신규 생성/선택 공개 목록. 궁수의 이관 전 공개 차단과 구 캐릭터 접근/보존 정책을 함께 정한다.
2. [`growth-content.json`](../../Backend/src/Shinshi.Data/Content/growth-content.json), 카드 ID·`account_items`·보상 회수함·등록 레벨/실패 보정·조합·초기 지급/기존 장비 무기 표시. [`0011`](../../Backend/src/Shinshi.Data/Migrations/0011_growth_progression.up.sql)의 effect ID 형식 `^T[0-9]{2}-[1-3][A-Z]$`와 `(character_id,skill_id,tier)` 키를 점검한다. 문서 ID를 DB에 직접 쓰지 않는다.
3. 열린 출정 `loadout_snapshot`·`loadout_revision`·캐릭터 잠금. 출정 중 변경을 거절하고 구 출정을 완료/정산한 뒤 전환한다. 구버전/이관 서버가 같은 출정을 처리하지 않게 한다.
4. 신규 `contentHash`와 필요 시 protocolVersion으로 구클라/서버 혼합을 차단한다. 해시 검사는 저장 데이터 이관을 대신하지 않는다. 멱등 재시도·진도/비용/보상 중복 없음·백업/롤백을 실제 DB에서 확인한다. 정책/구현 확정 전에 새 카탈로그를 실제 서버에 배포하지 않는다.

신규 설정·서버 판정·모션 사본/프로필·부적 resolver·UI·저장 이관은 **후속 구현**이다. 이번 문서 개편은 기존 코드·자산·SQL을 변경하지 않는다.

## 6. 적용과 완료 게이트

| 게이트 | 필요한 증거 | 현재 상태 |
|---|---|---|
| 문서 L1 | 신규 12기술/45효과·직업별 15·수치·후보 경로·문서 링크·구 구현과의 구분 | 이번 검증 대상 |
| 모션 L2 | P09 남녀 후보 재생·검 궤적/접지/그립/무기/방향·Loop/Root·사망/부활, 기본 공격과 기술의 시각 구분·궁수 활/화살·발사/차지·후퇴·루프 종료 캡처 | 미실행·위 초과 후보 배정 보류 |
| 전투 L2 | 검사/마검사 근접 기본/Q/E·돌진 벽·장판8회/폭발·궁수 관통/산개/W충돌·후속 취소·부적 대표 조합·보스 이동 금지 | 서버 규칙 구현·EditMode 검증, 실제 플레이/상태이상 연동 검증 대기 |
| 이관 | 실제 DB 카드/진도/조합·중복 요청·출정 잠금·롤백·구버전 차단 | 미실행·정책 결정 필요 |
| 통합 L3 | 전용 서버+2클라 M2·서로 다른 PC 지연 M3·본인/원격 타격/취소/사망·재접속 | 미실행 |
| 최종 체험판 | 검사·마검사·궁수의 솔로·동일/혼합 4인·성능·빌드 | 세 직업 설계됨·신규 전투 미실행 |

설계 문서와 게임 실행 규칙을 구분한다. 팩 설치/Humanoid 설정과 과거 CombatGirls 원격 기본 공격 확인은 신규 Magical-Knight 모션·직업 전투의 실행 증거가 아니다.
