// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 몸체(BossServer)가 패턴 알림을 이 형태로 바꿔 상태(BossState)에 넘기고, 상태는 같은 형태로 클라이언트 수신부에 넘긴다.
    // 한 계약을 양쪽에 써서 서버에서 만든 값이 그대로 표현 진입점(BossTelegraphPresenter)의 입력이 되게 한다.
    // 시각은 모두 서버 시각(서버 NetworkTime.time 기준 틱 시각이나 FSM 단계 경계)이고 받은 순간의 시각으로 바꾸지 않는다.
    // 시전 번호가 키다. 투사체와 장판은 시전당 한 발, 한 장판이라 시전 번호를 그대로 투사체 번호, 장판 번호로 쓴다
    /// <summary> 보스 공격 표현에 필요한 서버 확정 사건, 무엇을 언제 어디서만 담고 표현 값은 담지 않는다 </summary>
    public interface IBossEventListener
    {
        /// <summary> 예고 시작 </summary>
        void OnTelegraphStarted(in BossTelegraphStart start);

        /// <summary> 추적 중 조준 갱신, length는 부채꼴이면 반경, 직사각형이면 길이이고 돌진과 투사체는 벽 앞까지의 길이다 </summary>
        void OnAimChanged(int castId, Vector3 direction, float length, double serverTime);

        /// <summary> 방향 고정 </summary>
        void OnDirectionLocked(int castId, Vector3 direction, float length);

        /// <summary> 부채꼴, 원형, 직사각형의 발동, 적중 여부와 상관없이 온다 </summary>
        void OnStruck(int castId, double serverTime);

        /// <summary> 돌진 시작, startServerTime부터 speed로 가고 최대 distance에서 멈춘다 </summary>
        void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime);

        /// <summary> 돌진 정지 </summary>
        void OnDashStopped(int castId, Vector3 position, bool stoppedByWall);

        /// <summary> 회복까지 정상 종료 </summary>
        void OnFinished(int castId);

        /// <summary> 중단, 발동 뒤에 온 중단은 표현이 무시한다 </summary>
        void OnCancelled(int castId);

        /// <summary> 투사체 발사, 같은 번호의 예고는 발사와 함께 끝난다 </summary>
        void OnProjectileLaunched(in BossProjectileStart start);

        /// <summary> 투사체 소멸 </summary>
        void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position);

        /// <summary> 장판 활성, 같은 번호의 예고는 활성과 함께 끝난다 </summary>
        void OnAreaActivated(in BossAreaStart start);

        /// <summary> 장판 타격 회차 하나가 끝났다 </summary>
        void OnAreaTicked(int areaId, int hitIndex, double roundServerTime);

        /// <summary> 장판 소멸 </summary>
        void OnAreaEnded(int areaId, EBossAreaFate fate);
    }
}
