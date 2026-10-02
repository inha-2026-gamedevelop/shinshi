namespace Shinshi.Launcher.Account
{
    /// <summary> 게임 세션 전달 결과. 실패면 시작한 게임은 이미 종료 요청됐고 세션 폐기를 시도했다 </summary>
    public sealed class GameHandoffResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public EGameHandoffError Error { get; }

        // 성공하면 실행 중인 게임
        public IStartedGame?     Game  { get; }

        // 실패 정리 때 서버가 세션 폐기를 받아들였는지. 성공이면 false
        public bool              IsRevoked { get; }

        public bool IsSuccess => Error == EGameHandoffError.None;

        /****************************************
        *                Methods
        ****************************************/

        private GameHandoffResult(EGameHandoffError error, IStartedGame? game, bool isRevoked)
        {
            Error     = error;
            Game      = game;
            IsRevoked = isRevoked;
        }

        internal static GameHandoffResult Success(IStartedGame game) { return new GameHandoffResult(EGameHandoffError.None, game, false); }

        internal static GameHandoffResult Fail(EGameHandoffError error, bool isRevoked)
        {
            return new GameHandoffResult(error, null, isRevoked);
        }
    }
}
