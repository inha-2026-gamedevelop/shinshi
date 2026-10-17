// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 몸체(BossServer)가 패턴 알림을 이 형태로 바꿔 상태(BossState)에 넘기고, 상태는 같은 형태로 클라이언트 수신부에 넘긴다.
    // 한 계약을 양쪽에 써서 서버에서 만든 값이 그대로 표현 진입점(BossTelegraphPresenter)의 입력이 되게 한다.
    // 시각은 모두 서버 시각(서버 NetworkTime.time 기준 틱 시각이나 FSM 단계 경계)이고 받은 순간의 시각으로 바꾸지 않는다.
    // 시전 번호가 키다. 투사체와 장판은 시전당 한 발, 한 장판이라 시전 번호를 그대로 투사체 번호, 장판 번호로 쓴다.
    // 2페이즈의 빔, 별형 빔, 반월 연쇄 구역은 한 시전이 여럿을 가질 수 있어 서버가 발급한 빔, 구조, 구역 번호가 키다
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

        /// <summary> 돌진 정지, stopServerTime은 서버가 정지를 판정한 틱 시각이나 실행 끝 경계다 </summary>
        void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime);

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

        /// <summary> 반월 연쇄 폭발의 여우불 이동 시작, 구역 예고는 따로 온다 </summary>
        void OnSweepStarted(in BossSweepStart start);

        /// <summary> 반월 연쇄의 구역 예고 하나, CastId는 구역 번호이고 GroupId는 시전 번호다 </summary>
        void OnSweepZoneScheduled(in BossTelegraphStart zone);

        /// <summary> 구역 폭발 확정, blastServerTime은 예약 시각이다 </summary>
        void OnSweepZoneBlasted(int zoneId, double blastServerTime);

        /// <summary> 반월 연쇄 중단, 아직 폭발하지 않은 같은 시전의 구역만 지운다 </summary>
        void OnSweepCancelled(int castId);

        /// <summary> 빔 한 줄의 예고 시작, 빔 번호가 키다 </summary>
        void OnBeamTelegraphStarted(in BossBeamStart start);

        /// <summary> 활성 전 빔 취소, castId는 그 빔을 낸 시전 번호다 </summary>
        void OnBeamCancelled(int castId, int beamId);

        /// <summary> 빔 활성, 활성 순간이 첫 타격이다 </summary>
        void OnBeamActivated(int beamId, double activateServerTime);

        /// <summary> 빔 타격 회차 하나가 끝났다 </summary>
        void OnBeamTicked(int beamId, int hitIndex, double roundServerTime);

        /// <summary> 빔 소멸 </summary>
        void OnBeamEnded(int beamId, EBossAreaFate fate);

        /// <summary> 별형 빔 구조 하나의 예고 시작, 구조 번호가 키다 </summary>
        void OnStarTelegraphStarted(in BossStarStart start);

        /// <summary> 활성 전 별형 빔 취소, castId는 그 구조를 낸 시전 번호다 </summary>
        void OnStarCancelled(int castId, int starId);

        /// <summary> 별형 빔 활성, 이 서버 시각부터 회전한다 </summary>
        void OnStarActivated(int starId, double activateServerTime);

        /// <summary> 별형 빔 타격 회차 하나가 끝났다 </summary>
        void OnStarTicked(int starId, int hitIndex, double roundServerTime);

        /// <summary> 별형 빔 소멸 </summary>
        void OnStarEnded(int starId, EBossAreaFate fate);

        /// <summary> 50% 결계 위험 구역 예고 시작, 이 값의 시각들로 예고와 활성 단계를 재현한다 </summary>
        void OnBarrierStarted(in BossBarrierStart start);

        /// <summary> 결계 위험 구역 활성, 이 서버 시각부터 피해가 난다 </summary>
        void OnBarrierActivated(int barrierId, double activateServerTime);

        /// <summary> 결계 전개 시퀀스 완료, 위험 구역은 정리될 때까지 남고 2페이즈 인계는 서버가 이어서 한다 </summary>
        void OnBarrierCompleted(int barrierId, double completeServerTime);

        /// <summary> 결계 위험 구역 정리, 사망과 전투 종료가 부른다 </summary>
        void OnBarrierCleared(int barrierId);
    }
}
