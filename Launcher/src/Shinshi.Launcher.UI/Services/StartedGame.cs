// Project
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Services
{
    /// <summary> 세션 전달에 넘기는 GameProcess의 게임 하나 </summary>
    public sealed class StartedGame : IStartedGame
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly GameProcess _owner;

        /****************************************
        *              Properties
        ****************************************/

        public int ProcessId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public StartedGame(GameProcess owner, int processId)
        {
            _owner    = owner;
            ProcessId = processId;
        }

        public void Kill() { _owner.Kill(ProcessId); }
    }
}
