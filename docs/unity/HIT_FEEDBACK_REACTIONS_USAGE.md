# 개발 씬 몬스터 반동과 첫 착지

`Dev_ForestPlateauHitFeedback`의 몬스터 표현 전용이다. 플레이어 애니메이션·공격 VFX·오디오·서버 판정은 변경하지 않는다. 릴리즈 적의 피격/사망 이벤트 연결은 이 작업 범위에 포함하지 않는다.

## 프리팹과 연결

| Resources 프리팹 | 역할 | 소비 경로 |
|---|---|---|
| `Assets/02.Prefab/VFX/HitFeedback/Resources/DevHitReactions/PF_DevMonsterHitRecoil.prefab` | 상체 방향 반동 | `DevHitFeedbackMonster.Init`에서 인스턴스 생성 |
| `Assets/02.Prefab/VFX/HitFeedback/Resources/DevHitReactions/PF_DevCorpseLandingFx.prefab` | 먼지 풀과 혈흔 확산 | `HitFeedbackDirector.Awake` 생성 후 스포너가 몬스터에 전달 |

프리팹 Inspector 값을 바꾼 뒤 플레이를 재시작한다. 씬에 따로 배치할 필요가 없다. Resources 프리팹의 존재가 필요하며 모델마다 반동 프리팹 인스턴스 1개, Director마다 착지 재생기 1개가 생성된다.

반동 도우미는 모델 바깥의 몬스터 루트 자식으로 붙인다. `Init(Animator)`가 Spine/Head/양팔 뼈를 한 번 찾고, 현재 Beast/Vendigo의 CAT 이름과 일반 Spine/Head/LeftArm/RightArm 이름을 지원한다. Humanoid를 요구하지 않는다. 다른 명칭의 리그를 추가할 때는 `AddBone`의 별칭을 검토한다. 존재하지 않는 부위는 생략한다.

`Play(away, critical)`의 `away`는 월드 기준 공격자에서 피격자 쪽 방향이다. 애니메이션 평가 뒤 몸통에 회전을 더하고 머리·팔은 지연해서 따라온다. 다음 Update에서 원래 자세를 복원하므로 반동을 중첩해도 회전이 누적되지 않는다. 몬스터 루트의 위치·회전과 충돌 캡슐은 반동이 바꾸지 않는다. 기존 루트 넉백/찌그러짐은 별도로 유지한다.

`SetLagged(true)` 동안 반동 트윈을 멈추고 false에서 이어간다. `StopAndRestore`, 비활성화, 사망은 원래 뼈 자세로 돌린다. 사체 복제 전에 복원해 래그돌에 임시 뼈 회전이 남지 않는다. 트윈은 초기화 때 만들어 재사용한다.

## 첫 착지 계약

모델에 `DevCorpseLanding.PrepareClone(fx)`를 임시 부착한 뒤 기존 `EnemyCorpse.Launch`로 복제한다. 복제본은 `RagdollLaunched` 알림의 뼈가 자신에게 속하는지 확인한 뒤 각 뼈에 충돌 전달기를 연결한다. 모델 복제/사체 물리의 공용 코드는 변경하지 않는다. 모델 없는 개발 캡슐의 대체 비행도 `HitReactor.Launched`에서 `BeginDirect`로 연결한다.

물리가 시작된 뒤 0.1초부터, 표면 법선 y가 0.55 이상이고 법선 방향 상대 속도가 1.6m/s 이상인 정적 충돌을 착지로 취급한다. 벽, 동적 Rigidbody, CapsuleCollider, CharacterController와 트리거는 제외한다. 이 개발 씬의 정적 지형을 대상으로 하므로 이동 발판과 캡슐 모양 지형에는 별도 정책이 필요하다. 여러 뼈의 동시 충돌과 두 번째 바운스는 재생하지 않는다. 비활성화된 사체는 충돌 전달기가 남아 있어도 재생하지 않는다.

`DevCorpseLandingFx.Play(point, normal, impactSpeed)`의 원점은 실제 접촉 지점, 법선은 지면 바깥쪽, 크기 기준은 월드 미터다. 먼지는 즉시 방출하고, 혈흔은 0.18초 동안 최종 지름의 40%→70%→100% 크기로 찍는다. 기존 [혈흔 풀](HIT_FEEDBACK_BLOOD_USAGE.md)의 지형 밀착 메시·유지/페이드를 재사용하므로 지형 조건에 맞지 않는 얼룩은 생략된다. 혈흔을 꺼도 먼지는 재생한다.

| Inspector | 기본값 | 의미 |
|---|---|---|
| 반동 `_seconds`, `_response` | 0.34초, 반동/작은 되튐 곡선 | 총 시간과 진행 곡선 |
| `_torsoDegrees`, `_headDegrees`, `_armDegrees` | 18°, 10°, 13° | 부위별 회전량 |
| `_headDelay`, `_armDelay`, `_criticalScale` | 0.035초, 0.055초, 1.35 | 부위 지연과 치명타 배율. 지연은 총 시간의 최대 95%로 제한 |
| 착지 `_armDelay`, `_minimumImpactSpeed` | 0.1초, 1.6m/s | 초기 발 접촉 제외와 최소 충격 |
| `_dustCount`, `_dustSize`, `_dustLifetime` | 16개, 0.65~1.25m, 0.25~0.5초 | 먼지 개수·크기·수명 |
| `_dustSpeed`, `_dustColor` | 1~2.6m/s, 낮은 알파의 흙색 | 먼지 이동과 색 |
| `_bloodSize`, `_spreadSeconds` | 2.1m, 0.18초 | 혈흔 지름과 확산 시간 |

착지 충격 속도로 혈흔 크기가 0.75~1.25배 조절된다. 12칸의 먼지/확산 시퀀스를 재사용하며 넘으면 오래된 칸을 교체한다. `StopAll`과 비활성화는 먼지와 미실행 확산 콜백을 취소한다. 이미 찍힌 혈흔은 혈흔 풀의 수명을 따른다. 정리 시 트윈을 Kill하고 사체는 알림 구독을 해제한다.

먼지는 기존 불똥의 부드러운 알파 텍스처를 재사용한 URP Particles/Unlit 투명 재질이다. 새 Blender/생성 이미지 작업은 하지 않았다. 얇은 입자 카드이므로 별도 AO/LOD/피해 Collider를 만들지 않는다. [준비 대상 목록](MAP_PERFORMANCE_PREPARATION.md#개발-씬-준비-대상---몬스터-반동첫-착지)에 프리팹과 의존성을 기록한다.

## 검증 범위

2026-10-26 Unity 6000.4.7f1 PC/DX11 개발 씬에서 확인했다. 자동 시험과 실제 카메라 녹화는 로컬 표현 검증이다. 녹화는 기존 몬스터의 위치/추격/입력을 촬영 중 고정하고 `TakeHit`을 직접 호출했다. 기본 카메라 거리 설정 40m/FOV30을 유지했다. 원본 씬은 저장하지 않는다.

| 주장 | 수준 | 증거 등급 | 실행 수단 | 실제 결과 | 상태 |
|---|---|---:|---|---|---|
| 좌우 반동과 연속 피격 복원 | L2 | 2 | `DevHitReactionTests`, 실제 두 모델 촬영 | 반대 방향 회전, 루트 보존과 자세 복원 확인 | 완료(검증됨) |
| 첫 지면 충돌만 재생 | L2 | 2 | 실제 물리 낙하·벽 접촉·재낙하 시험, 개발 씬 녹화 | 벽 제외, 두 번째 낙하 제외. 실제 두 사체가 각각 한 번 재생 | 완료(검증됨) |
| 비활성화된 사체 정리 | L2 | 2 | 비활성화 뒤 실제 낙하 시험 | 착지 재생 0회 | 완료(검증됨) |
| 히트스톱과 반동 취소 | L2 | 2 | 트윈 시간 정지/재개와 비활성화 시험 | 정지 중 진행 0, 재개 후 회전, 취소 후 원래 자세 | 완료(검증됨) |

신규 PlayMode 시험 6/6, 기존 `EnemyCorpseRagdollTests` 5/5 통과했다. 신규 6개에는 모델 없는 개발 몬스터의 실제 TakeHit→대체 비행→첫 착지 연결도 포함한다. 재컴파일과 신규 코드 콘솔 오류, 프리팹 GUID/의존성을 확인했다.

일반 사용자 입력 전투, 릴리즈 연결, 빌드/M2/M3, Player 성능과 첫 사용 지연/PSO 예산은 미검증이다. 풀 초기화를 셰이더 워밍업 완료로 해석하지 않는다.
