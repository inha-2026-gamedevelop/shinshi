namespace Shinshi.Network
{
    // 던전 호스트는 명단, 웨이브, 부활 횟수를 직접 읽고 전투 개체의 값만 이 창구로 묻는다. 전투 런타임이 구현한다
    /// <summary> 진단용 체크포인트에 실을 전투 값, 서버 메인 스레드에서 30초에 한 번 부른다 </summary>
    public interface ICheckpointSource
    {
        /// <summary> 참가자 전투 개체의 값, 개체가 없으면(시작 전, 지워짐) 거짓 </summary>
        bool TryGetParticipantVitals(int participantId, out CheckpointVitals vitals);

        /// <summary> 보스 체력 비율(0~1)과 페이즈 값, 보스가 없으면 거짓 </summary>
        bool TryGetBossVitals(out float healthRate, out int phase);
    }
}
