// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.UI
{
    // 사망(피격, 낙사 모두)은 내 캐릭터 복제 상태의 생사로 알고, 대기 시각과 남은 공용 횟수도 서버가 같은 상태에 싣는다.
    // 대기 시간, 횟수, 안전 지점 판정은 서버가 하며 여기서는 요청만 보낸다
    /// <summary> 보스 맵의 부활 창을 내 캐릭터의 생사, 부활 대기 시각, 남은 공용 부활 횟수와 부활 요청에 잇는다 </summary>
    public sealed class BossReviveBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossRevivePanelView _view   = null;
        [SerializeField] private UiPopupHub          _popups = null;

        private uint              _boundNetId = 0;
        private PlayerCombatState _combat     = null;
        private PlayerInputState  _input      = null;
        private bool              _requested  = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_view != null)
            {
                _view.ReviveClicked += OnReviveClicked;
                _view.Hide();
            }
        }

        private void OnDisable()
        {
            if (_view != null)
            {
                _view.ReviveClicked -= OnReviveClicked;
            }

            Unbind();
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            if ((!ClientRoles.IsDungeon()) || (!TryBind()))
            {
                _requested = false;
                _view.Hide();
                return;
            }

            EParticipantLifeState life = _combat.LifeState;
            if (life == EParticipantLifeState.Alive)
            {
                _requested = false;
                _view.Hide();
                return;
            }

            if (life == EParticipantLifeState.Spectating)
            {
                _requested = false;
                _view.Show(EReviveViewState.Spectating, 0f, _combat.SharedRevives);
                return;
            }

            // 서버가 대기 시각을 아직 싣지 않은 첫 순간에는 잘못된 초를 보이지 않는다
            double readyAt = _combat.ReviveReadyAt;
            if (!(readyAt > 0.0))
            {
                _view.Hide();
                return;
            }

            float wait = (float)System.Math.Max(0.0, readyAt - NetworkTime.time);
            EReviveViewState state = ((_requested) || (_input.IsWaitingSafePoint)) ? EReviveViewState.Requesting
                : (wait > 0f) ? EReviveViewState.Waiting
                : EReviveViewState.Ready;
            _view.Show(state, wait, _combat.SharedRevives);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 재접속이나 캐릭터 교체로 내 개체가 바뀌면 새 개체로 다시 묶는다
        private bool TryBind()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                Unbind();
                return false;
            }

            if ((local.netId == _boundNetId) && (_combat != null) && (_input != null))
            {
                return true;
            }

            Unbind();
            if ((!local.TryGetComponent(out PlayerCombatState combat)) || (!local.TryGetComponent(out PlayerInputState input)))
            {
                return false;
            }

            _boundNetId = local.netId;
            _combat     = combat;
            _input      = input;
            _requested  = false;
            _input.InputRejected += OnInputRejected;
            return true;
        }

        private void Unbind()
        {
            if (_input != null)
            {
                _input.InputRejected -= OnInputRejected;
            }

            _boundNetId = 0;
            _combat     = null;
            _input      = null;
            _requested  = false;
        }

        private void OnReviveClicked()
        {
            if ((_input == null) || (_requested))
            {
                return;
            }

            _requested = true;
            _input.RequestRevive();
        }

        // 거절은 순번으로 가리지 않는다. 쓰러진 동안 보내는 입력은 부활 요청뿐이다
        private void OnInputRejected(uint sequence, ERejectReason reason)
        {
            if ((!_requested) || (_combat == null) || (_combat.LifeState == EParticipantLifeState.Alive))
            {
                return;
            }

            _requested = false;
            if (_popups == null)
            {
                return;
            }

            string message = RejectText.Describe(reason);
            if (!string.IsNullOrEmpty(message))
            {
                _popups.ShowToast(message);
            }
        }
    }
}
