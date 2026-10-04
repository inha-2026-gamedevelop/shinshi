// System
using System;

namespace Shinshi.Network
{
    /// <summary> 진단용 체크포인트 한 장의 값, 던전 호스트가 하나를 만들어 30초마다 다시 채운다 </summary>
    public sealed class SessionCheckpointState
    {
        // 경제 복구에는 쓰지 않는다(03 기술 설계 4장). 프로세스 소실 뒤 무엇이 남아 있었는지 운영자가 보는 기록이다

        /****************************************
        *                Fields
        ****************************************/

        private readonly CheckpointParticipant[] _participants;

        /****************************************
        *              Properties
        ****************************************/

        public double ServerTime       { get; set; }
        public double ElapsedSeconds   { get; set; }
        public int    StartingCount    { get; set; }
        public long   KillSequence     { get; set; }

        // 보스전 남은 공용 부활, 일반전은 -1
        public int    RemainingRevives { get; set; }

        public bool   HasWave          { get; set; }
        public int    WaveIndex        { get; set; }
        public int    WaveCount        { get; set; }
        public int    RemainingInWave  { get; set; }
        public double WaveElapsed      { get; set; }

        public bool   HasBoss          { get; set; }
        public float  BossHealthRate   { get; set; }
        public int    BossPhase        { get; set; }

        public int    ParticipantCount { get; private set; }
        public int    Capacity         => _participants.Length;

        /****************************************
        *                Methods
        ****************************************/

        public SessionCheckpointState(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "참가자 상한은 1 이상이다");
            }

            _participants = new CheckpointParticipant[capacity];
        }

        /// <summary> 참가자 값을 비운다, 다시 채우기 전에 부른다 </summary>
        public void ClearParticipants()
        {
            Array.Clear(_participants, 0, ParticipantCount);
            ParticipantCount = 0;
        }

        /// <summary> 참가자 한 명을 더한다, 상한이면 거짓 </summary>
        public bool TryAdd(in CheckpointParticipant participant)
        {
            if (ParticipantCount >= _participants.Length)
            {
                return false;
            }

            _participants[ParticipantCount] = participant;
            ++ParticipantCount;
            return true;
        }

        public ref readonly CheckpointParticipant GetAt(int index)
        {
            if ((index < 0) || (index >= ParticipantCount))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _participants[index];
        }
    }
}
