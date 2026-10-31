// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Network
{
    // 프롤로그 표현(PrologueClientPresenter)이 매 프레임 읽는 상태 창구다. 서버가 보낸 마지막 값을 보관하고, 화면이 끝났다는 사건을 서버에 보낸다.
    // 던전 접속이 새로 열리면 서버가 현재 상태를 한 번 다시 보내므로 재접속에도 마지막 값이 복원된다
    /// <summary> 서버의 프롤로그 상태를 읽고 화면 사건을 보내는 클라이언트 창구 </summary>
    public sealed class PrologueClient : IPrologueClientLink
    {
        /****************************************
        *                Fields
        ****************************************/

        private EPrologueStage _stage          = EPrologueStage.Intro;
        private EForestHint    _hint           = EForestHint.None;
        private bool           _isTalismanOpen = false;
        private bool           _isWolbaekOpen  = false;
        private int            _potionCount    = 0;
        private ushort         _reviveSerial   = 0;
        private bool           _hasState       = false;
        private PrologueClientPresenter _presenter = null;

        /****************************************
        *              Properties
        ****************************************/

        public static PrologueClient Instance { get; } = new PrologueClient();

        public EPrologueStage Stage          => _stage;
        public EForestHint    Hint           => _hint;
        public bool           IsTalismanOpen => _isTalismanOpen;
        public bool           IsWolbaekOpen  => _isWolbaekOpen;
        public int            PotionCount    => _potionCount;

        // 서버에서 한 번이라도 상태를 받았는지, 받기 전에는 표시를 시작하지 않는다
        public bool           HasState       => _hasState;

        public event Action Revived;

        // 프롤로그를 끝낸 뒤 마을에서 완료 안내를 한 번 띄울 차례인가. 던전 연결이 끊기고 마을에 다시 접속해도 이어져야 해서 정적이다
        public static bool CompletedNoticePending { get; private set; } = false;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다 </summary>
        public static void RegisterHandlers()
        {
            Instance.Reset();
            NetworkClient.ReplaceHandler<PrologueStateMessage>(Instance.OnState);

            // 신시 도착과 완료 기록 확인(마을의 안내 완료 기록 결과) 뒤에 완료 안내를 띄운다
            TownProgressClient.IntroResultReceived -= OnIntroResult;
            TownProgressClient.IntroResultReceived += OnIntroResult;
        }

        private static void OnIntroResult(IntroCompleteResultMessage message)
        {
            if ((!CompletedNoticePending) || (message.Reason != ERejectReason.None))
            {
                return;
            }

            CompletedNoticePending = false;
            PrologueAssetsSO assets = PrologueAssetsSO.Load();
            PrologueTownNotice.ShowCompletedFromCatalog((assets != null) ? assets.Illustrations : null);
        }

        /// <summary> 마지막으로 받은 상태를 지운다, 새 던전 접속이 열릴 때 부른다 </summary>
        public void Reset()
        {
            _stage          = EPrologueStage.Intro;
            _hint           = EForestHint.None;
            _isTalismanOpen = false;
            _isWolbaekOpen  = false;
            _potionCount    = 0;
            _reviveSerial   = 0;
            _hasState       = false;
        }

        public void SendTalismanConfirmed()
        {
            Send(EPrologueClientEvent.TalismanConfirmed);
        }

        public void SendIllustrationFinished()
        {
            Send(EPrologueClientEvent.IllustrationFinished);
        }

        public void SendWolbaekIntroFinished()
        {
            Send(EPrologueClientEvent.WolbaekIntroFinished);
        }

        private static void Send(EPrologueClientEvent clientEvent)
        {
            if (!NetworkClient.isConnected)
            {
                return;
            }

            NetworkClient.Send(new PrologueEventMessage(clientEvent));
        }

        private void OnState(PrologueStateMessage message)
        {
            _stage          = Enum.IsDefined(typeof(EPrologueStage), (int)message.Stage) ? (EPrologueStage)message.Stage : _stage;
            _hint           = Enum.IsDefined(typeof(EForestHint), (int)message.Hint) ? (EForestHint)message.Hint : EForestHint.None;
            _isTalismanOpen = message.IsTalismanOpen;
            _isWolbaekOpen  = message.IsWolbaekOpen;
            _potionCount    = message.PotionCount;

            // 첫 상태는 재접속에서 이미 올라간 카운터일 수 있어 알림으로 보지 않는다
            bool revived  = (_hasState) && (message.ReviveSerial != _reviveSerial);
            _reviveSerial = message.ReviveSerial;
            _hasState     = true;
            if (_stage == EPrologueStage.Done)
            {
                CompletedNoticePending = true;
            }

            EnsurePresenter();
            if (revived)
            {
                Revived?.Invoke();
            }
        }

        // 서버가 프롤로그 상태를 보내는 것이 프롤로그 출정이라는 신호다. 첫 상태가 오면 이 씬에 표현을 한 번 만든다
        private void EnsurePresenter()
        {
            if (_presenter != null)
            {
                return;
            }

            PrologueAssetsSO assets = PrologueAssetsSO.Load();
            if ((assets == null) || (assets.Illustrations == null))
            {
                UnityEngine.Debug.LogError("[Prologue] Resources의 PrologueAssets가 없어 프롤로그 화면을 만들지 못한다");
                return;
            }

            _presenter = PrologueClientPresenter.Create(this, new PrologueLocalVitals(), assets.Illustrations);
        }
    }
}
