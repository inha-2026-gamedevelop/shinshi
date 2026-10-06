// System
using System.Collections;
using System.Diagnostics;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

using Debug = UnityEngine.Debug;

namespace Shinshi.Network
{
    /// <summary> 받은 게임 세션의 남은 시간이 갱신 시점 안으로 들어오면 API로 회전해 새 토큰으로 바꾼다, 씬에 두지 않고 스스로 만든다 </summary>
    public sealed class GameSessionRefresher : MonoBehaviour
    {
        // 로비, 캐릭터 선택, 마을 어디서든 같은 세션을 쓰므로 DontDestroyOnLoad로 하나만 둔다.
        // API 호출과 LauncherSession 변경은 모두 메인 스레드(코루틴)에서 한다. 요청을 보낼 때 세션 번호를 잡고,
        // 응답을 넣을 때 그 번호로 대조해 그사이 리셋이나 재로그인이 있었으면 늦은 응답을 버린다.
        // 회전은 이전 토큰을 같은 트랜잭션에서 폐기한다. 응답을 잃으면 이전 토큰이 이미 죽었을 수 있어 다음 재시도는
        // TOKEN_REVOKED로 끝나고 런처 재실행을 안내한다(회전 응답 유실 복구는 두지 않는다)

        /****************************************
        *                Fields
        ****************************************/

        // 갱신 시점을 매 프레임 계산하지 않으려는 검사 간격
        private const float CHECK_INTERVAL_SECONDS = 1f;
        private const int   NO_ATTEMPT             = -1;

        private readonly LobbyApiClient _api = new LobbyApiClient();

        private float _nextCheckTime  = 0f;
        private bool  _isRefreshing   = false;

        // 이 세션 번호로는 더 갱신하지 않는다. 재시도를 다 썼거나 다시 보내도 같은 거절이 오는 경우다
        private int   _stoppedAttempt = NO_ATTEMPT;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStart()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode)
            {
                return;
            }

            var root = new GameObject(nameof(GameSessionRefresher));
            DontDestroyOnLoad(root);
            root.AddComponent<GameSessionRefresher>();
#endif
        }

        private void Update()
        {
            if ((_isRefreshing) || (Time.unscaledTime < _nextCheckTime))
            {
                return;
            }

            _nextCheckTime = Time.unscaledTime + CHECK_INTERVAL_SECONDS;
            if (!LauncherSession.HasSession)
            {
                return;
            }

            int attempt = LauncherSession.SessionAttempt;
            if (attempt == _stoppedAttempt)
            {
                return;
            }

            if (!LauncherSessionRules.ShouldRefresh(LauncherSession.GetRemainingMs()))
            {
                return;
            }

            _isRefreshing = true;
            StartCoroutine(CoRefresh(attempt));
        }

        private void OnDestroy()
        {
            _api.Abort();
        }

        /****************************************
        *                Methods
        ****************************************/

        private IEnumerator CoRefresh(int attempt)
        {
            int retryIndex = 0;
            while (true)
            {
                string token = LauncherSession.GetGameSessionToken();
                if ((attempt != LauncherSession.SessionAttempt) || string.IsNullOrEmpty(token))
                {
                    break;
                }

                LobbyApiReply<GameSessionIssueResponse> reply = null;
                yield return _api.SendEmptyPost<GameSessionIssueResponse>(LauncherSession.ApiBaseUrl, token,
                    ApiRoutes.AUTH_GAME_SESSION_REFRESH, value => reply = value);
                if (reply == null)
                {
                    break;
                }

                EGameSessionRefreshOutcome outcome = LauncherSessionRules.ClassifyRefreshReply(reply.StatusCode, reply.ReasonCode,
                    reply.Value);
                if (outcome == EGameSessionRefreshOutcome.Succeeded)
                {
                    if (!LauncherSession.ReplaceToken(attempt, reply.Value, Stopwatch.GetTimestamp()))
                    {
                        Debug.LogWarning("[Session] 늦게 도착한 게임 세션 갱신 응답을 버린다");
                    }

                    break;
                }

                if (outcome == EGameSessionRefreshOutcome.SessionEnded)
                {
                    ELauncherSessionFailure reason = (reply.ReasonCode == ContractCodes.RejectReason.TOKEN_EXPIRED)
                        ? ELauncherSessionFailure.Expired
                        : ELauncherSessionFailure.Revoked;
                    if (LauncherSession.EndSession(attempt, reason))
                    {
                        Debug.LogWarning($"[Session] 게임 세션이 끝나 런처로 다시 실행해야 한다 ({reason})");
                    }

                    break;
                }

                if ((outcome == EGameSessionRefreshOutcome.Stopped)
                    || (!LauncherSessionRules.TryGetRefreshRetryDelay(retryIndex, out float delaySeconds)))
                {
                    // 토큰이 아직 살아 있으면 만료까지 그대로 쓴다. 상태 코드와 사유만 남기고 응답 본문은 남기지 않는다
                    Debug.LogWarning($"[Session] 게임 세션을 갱신하지 못했다, status={reply.StatusCode}, reason={reply.ReasonCode}");
                    _stoppedAttempt = attempt;
                    break;
                }

                ++retryIndex;
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            _isRefreshing = false;
        }
    }
}
