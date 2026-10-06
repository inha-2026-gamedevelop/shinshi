# 캐릭터 커스터마이즈

캐릭터 생성 화면에서 전투 직업, 얼굴, 헤어, 옷을 선택하고 닉네임을 확인해 생성한다. 현재 얼굴은 Cute 1종이고 헤어와 옷은 각각 3종이다. 헤어·옷 선택은 전투 직업과 독립적이다.

| 외형 ID | 얼굴 | 헤어 | 옷 |
|---|---|---|---|
| 1 | Cute | 무사 | 무사 |
| 2 | — | 도술사 | 도술사 |
| 3 | — | 신령사 | 신령사 |

전투 직업 ID는 무사 0, 도술사 1, 신령사 2다. 외형 ID와 혼용하지 않는다. 카탈로그는 `Assets/09.Data/Avatar/PlayerFemaleLookCatalog.asset`, 실제 직업 모델은 `Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_Female{Warrior,Taoist,Spiritcaller}_Cute.prefab`이다.

새 외형은 엔진 비의존 `CharacterAppearance` version 2로 저장한다. 현재는 등록된 ID와 중립 슬라이더·색 값만 허용한다. 기존 version 1의 JSON 10개 필드와 생성 operation request hash는 유지한다. 저장 외형이 null 또는 유효한 version 1이면 표시할 때 Cute와 해당 직업의 기본 헤어·옷으로 해석한다. 모르는 직업과 잘못된 version 2 외형은 거절한다.

마을·던전 입장 토큰을 소비할 때 백엔드가 소유 캐릭터의 닉네임과 외형을 DB에서 읽는다. 서버는 검증된 값을 플레이어 최초 생성 또는 재결합 전에 적용하고, `PlayerCombatState`로 닉네임·외형과 공개 이동 상태를 복제한다. 새 필드가 추가된 네트워크 프로토콜은 version 2다. 클라이언트가 전달한 외형으로 서버 플레이어를 덮어쓰지 않는다.

생성 요청의 operationId는 직업·닉네임·외형 전체가 같은 불변 스냅샷의 모호한 재시도에만 재사용한다. 선택을 바꾸면 새 요청을 만든다. 잘못된 외형이나 조립 실패는 선택 화면에서 안내하고 입장을 차단한다.

시각 조립은 의상 프리팹을 복제하고 선택한 헤어의 스킨과 보조본을 연결한다. 원본 프리팹, Cute SourceMorphs 15메시·전체 54표정 채널, 세 LOD를 보존한다. 시각 인스턴스의 Collider와 root motion은 끈다. 소유 캐릭터는 기존 이동 예측, 다른 캐릭터는 서버 공개 이동 상태로 `Speed`를 구동한다.

공용 컨트롤러는 `Assets/01.Scripts/05.Art/Characters/PlayerFemaleTripo/Animations/AC_FemaleTripo_Locomotion.controller`다. 현재 Speed 기준은 Idle 0, Walk 2.5, Run 5m/s다. Idle 클립은 기존 Walk 주기 평균으로 만든 정지 자세이며 독립 제작한 모션 클립이 아니다. 직업별 발 접지와 전환 품질은 실제 화면에서 별도로 확인한다.

검증은 백엔드의 기존 생성 요청 재생·9조합 저장·입장 DB 권한 테스트, Unity EditMode의 와이어·카탈로그·재시도·조립 테스트, PlayMode의 실제 Idle/Walk/Run·스킨·수명 테스트로 나눈다. Development 빌드의 `DevCustomizationScenario`는 실제 로비 버튼과 생성 탭·옵션을 사용해 선택 화면, 마을의 본인·원격 외형, 재접속, 정상 종료를 검사한다. 자동 검증 코드의 존재만으로 실제 마을 접속이나 릴리즈 빌드를 통과한 것으로 간주하지 않는다. 실행 결과와 미검증 범위는 해당 릴리즈 기록을 기준으로 한다.
