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

        // 프롤로그 출정만 채우는 캐릭터의 저장 단계(PrologueCodes.Stage), 월백 진입 물약 수, 부적 조사 여부. 다른 출정은 0, 0, false다
        public int     PrologueStage           { get; }
        public int     PrologueWolbaekPotions  { get; }
        public bool    PrologueTalismanChecked { get; }

        /****************************************
        *                Methods
        ****************************************/

        public JoinSortieContract(int mode, string mapId, string? battleId, string? bossId, int difficulty,
            int prologueStage = 0, int prologueWolbaekPotions = 0, bool prologueTalismanChecked = false)
        {
            Mode                    = mode;
            MapId                   = mapId ?? string.Empty;
            BattleId                = battleId;
            BossId                  = bossId;
            Difficulty              = difficulty;
            PrologueStage           = prologueStage;
            PrologueWolbaekPotions  = prologueWolbaekPotions;
            PrologueTalismanChecked = prologueTalismanChecked;
        }

        /// <summary> game_sessions의 mode 문자열과 difficulty 열(NULL 가능)에서 만든다. 프롤로그 상태는 프롤로그 출정일 때만 담는다 </summary>
        public static JoinSortieContract FromRow(string mode, string mapId, string? battleId, string? bossId,
            short? difficulty, int prologueStage = 0, int prologueWolbaekPotions = 0, bool prologueTalismanChecked = false)
        {
            int modeCode = mode switch
            {
                Sessions.GameSessionStates.MODE_BOSS     => GameSessionCodes.Mode.BOSS,
                Sessions.GameSessionStates.MODE_PROLOGUE => GameSessionCodes.Mode.PROLOGUE,
                _ => GameSessionCodes.Mode.NORMAL
            };
            bool prologue = modeCode == GameSessionCodes.Mode.PROLOGUE;
            return new JoinSortieContract(modeCode, mapId, battleId, bossId,
                difficulty ?? GameSessionCodes.Difficulty.NONE, prologue ? prologueStage : 0,
                prologue ? prologueWolbaekPotions : 0, prologue && prologueTalismanChecked);
        }
    }
}
