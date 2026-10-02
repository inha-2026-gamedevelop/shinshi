# 마을 HUD 개발 씬

대상은 `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity`이다. 원본 `Dev_HUD.unity`와 `Assets/09.Data/UI/Layout_HUD.json`은 별개이며, JSON을 다시 적용하면 수동 배치에 영향을 줄 수 있다.

## 이동용 NavMesh

`Navigation` 루트의 NavMeshSurface는 Humanoid, Walkable, Physics Colliders, Volume 수집을 사용한다. 포함 레이어는 Default와 Obstacle(mask 257)이다. 수집 범위는 center `(16.203926, 8.5442295, 5.126976)`, size `(154.71117, 28.383587, 141.47272)`이며 기존 활성 Collider 범위에 여유를 둔 값이다.

`PF_NetPlayer`와 `Test_Player`의 씬 인스턴스에는 자식까지 적용하는 Ignore From Build NavMeshModifier를 붙여 플레이어가 장애물로 베이크되지 않게 했다. 원본 프리팹은 수정하지 않았다. 환경 Collider나 배치를 바꾸면 Navigation의 NavMesh를 다시 베이크하고 아래 파일과 함께 씬을 저장한다.

- 데이터: `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView/NavMesh-Navigation.asset`
- 씬과 데이터, 데이터 폴더를 각자의 `.meta`와 함께 보관한다.

2026-10-03 에디터 검증에서 베이크된 정점 5259개·삼각형 2393개를 확인했다. 중앙 출발점 주변 7곳의 완전 경로와 Play Mode의 MovePathRules/분리된 PathFollower 도달을 확인했다. 전체 구역 연결성, 소유된 네트워크 플레이어 입력·이동, M2/M3 및 Player 빌드는 미검증이다.

## 씬 플레이어의 스폰 상태

Play Mode에서 미스폰 `PF_NetPlayer` 씬 인스턴스가 비활성화되는 것은 Mirror의 씬 NetworkIdentity 준비 처리다. 오브젝트는 남아 있으며 NavMesh가 삭제한 것이 아니다. 실제 플레이어는 인증된 서버의 스폰과 소유권 부여가 필요하다.

검증 때 기존 Mirror NetworkConnection 생성자의 Time.time 접근으로 PF_NetPlayer 직렬화 예외가 남아 있었다. Mirror 원본은 이번 작업에서 수정하지 않았다.
