// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Auth
{
    /// <summary> 던전 입장 교환이 돌려주는 출정 계약. 예약 때 game_sessions에 저장한 값을 그대로 읽는다 </summary>
    public sealed class JoinSortieContract
    {
        /****************************************
        *              Properties
        ****************************************/

        // GameSessionCodes.Mode
        public int     Mode       { get; }
        public string  MapId      { get; }

        // 0012 전에 예약한 출정이나 값을 넣지 않은 예약이면 null이다
        public string? BattleId   { get; }
        public string? BossId     { get; }

        // GameSessionCodes.Difficulty, 보스전이 아니면 NONE
        public int     Difficulty { get; }

        /****************************************
        *                Methods
        ****************************************/

        public JoinSortieContract(int mode, string mapId, string? battleId, string? bossId, int difficulty)
        {
            Mode       = mode;
            MapId      = mapId ?? string.Empty;
            BattleId   = battleId;
            BossId     = bossId;
            Difficulty = difficulty;
        }

        /// <summary> game_sessions의 mode 문자열과 difficulty 열(NULL 가능)에서 만든다 </summary>
        public static JoinSortieContract FromRow(string mode, string mapId, string? battleId, string? bossId,
            short? difficulty)
        {
            int modeCode = mode == Sessions.GameSessionStates.MODE_BOSS
                ? GameSessionCodes.Mode.BOSS : GameSessionCodes.Mode.NORMAL;
            return new JoinSortieContract(modeCode, mapId, battleId, bossId,
                difficulty ?? GameSessionCodes.Difficulty.NONE);
        }
    }
}
