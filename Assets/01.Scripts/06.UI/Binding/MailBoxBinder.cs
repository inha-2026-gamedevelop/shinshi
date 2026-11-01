// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 우편함 창과 InboxClient를 잇는다. 열 때 목록을 받고, 수령하면 목록과 성장, 생활 스냅숏을 다시 받는다 </summary>
    public sealed class MailBoxBinder : MonoBehaviour
    {
        // 우편은 마을 서버를 거치지 않고 게임 세션 토큰으로 API를 직접 부른다(InboxClient).
        // 수령 결과를 모르면 같은 요청을 한 번 더 보낸다(API가 처음 결과를 돌려준다)

        /****************************************
        *                Fields
        ****************************************/

        private const int    INITIAL_CAPACITY = 16;
        private const int    MAX_RETRIES      = 2;
        private const string LOADING_TEXT     = "우편을 불러오는 중입니다";
        private const string FAILED_TEXT      = "우편을 불러오지 못했습니다";
        private const string CLAIMED_TEXT     = "보상을 받았습니다";
        private const string CLAIMING_TEXT    = "받는 중입니다";

        [SerializeField] private MailBoxView _view = null;

        private readonly List<ListRowData> _rows = new List<ListRowData>(INITIAL_CAPACITY);

        private InboxEntry[] _entries = Array.Empty<InboxEntry>();
        private long         _serverNowMs;
        private int          _page;
        private int          _selected = -1;
        private bool         _wasOpen;
        private bool         _claiming;
        private int          _retries;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            InboxClient.ListReceived   += OnList;
            InboxClient.ClaimCompleted += OnClaim;
            if (_view != null)
            {
                _view.RowSelected        += OnRowSelected;
                _view.ClaimRequested     += OnClaimRequested;
                _view.PageDeltaRequested += OnPageDelta;
            }
        }

        private void OnDisable()
        {
            InboxClient.ListReceived   -= OnList;
            InboxClient.ClaimCompleted -= OnClaim;
            if (_view != null)
            {
                _view.RowSelected        -= OnRowSelected;
                _view.ClaimRequested     -= OnClaimRequested;
                _view.PageDeltaRequested -= OnPageDelta;
            }
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            bool isOpen = _view.IsOpen;
            if ((isOpen) && (!_wasOpen))
            {
                Reload();
            }

            _wasOpen = isOpen;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Reload()
        {
            _view.SetStatus(LOADING_TEXT);
            _page     = 0;
            _selected = -1;
            if (!InboxClient.RequestList())
            {
                _view.SetStatus(string.Empty);
            }
        }

        private void OnList(InboxListResult result)
        {
            if (_view == null)
            {
                return;
            }

            if (!result.Succeeded)
            {
                string text = RejectText.Describe(result.Reason);
                _view.SetStatus(string.IsNullOrEmpty(text) ? FAILED_TEXT : text);
                return;
            }

            _entries     = result.Entries;
            _serverNowMs = result.ServerTimeUnixMs;
            _view.SetStatus(string.Empty);
            ShowPage();
        }

        private void ShowPage()
        {
            int pageCount = MailBoxViewRules.PageCount(_entries.Length, ListPageSize());
            _page = Mathf.Clamp(_page, 0, pageCount - 1);
            MailBoxViewRules.PageRange(_entries.Length, ListPageSize(), _page, out int start, out int count);
            MailBoxViewRules.BuildRows(_entries, _serverNowMs, start, count, _rows);
            _view.SetList(_rows, _page, pageCount);
            _selected = -1;
            _view.ShowDetail(null, null, null, null);
        }

        // 한 쪽에 보이는 줄 수는 씬에 둔 줄 개수다
        private int ListPageSize()
        {
            return Mathf.Max(1, _view.RowCapacity);
        }

        private void OnPageDelta(int delta)
        {
            _page = Mathf.Max(0, _page + delta);
            ShowPage();
        }

        private void OnRowSelected(int rowIndex)
        {
            int index = (_page * ListPageSize()) + rowIndex;
            if ((index < 0) || (index >= _entries.Length))
            {
                return;
            }

            _selected = index;
            InboxEntry entry = _entries[index];
            _view.ShowDetail(MailBoxViewRules.TitleOf(entry.SourceKind), MailBoxViewRules.SenderOf(entry.SourceKind),
                MailBoxViewRules.DateText(entry.CreatedAtUnixMs), MailBoxViewRules.Body(entry));
        }

        private void OnClaimRequested()
        {
            if ((_claiming) || (_selected < 0) || (_selected >= _entries.Length))
            {
                return;
            }

            _retries = 0;
            if (InboxClient.Claim(_entries[_selected].InboxId))
            {
                _claiming = true;
                _view.SetClaimInteractable(false);
                _view.SetStatus(CLAIMING_TEXT);
            }
        }

        private void OnClaim(InboxClaimResult result)
        {
            if (_view == null)
            {
                return;
            }

            if ((result.IsOutcomeUnknown) && (_retries < MAX_RETRIES))
            {
                ++_retries;
                InboxClient.RetryClaim();
                return;
            }

            _claiming = false;
            if (result.Succeeded)
            {
                _view.SetStatus(CLAIMED_TEXT);
                TownGrowthClient.RequestSnapshot();
                TownLifeClient.RequestSnapshot();
                InboxClient.RequestList();
                return;
            }

            string text = MailBoxViewRules.ClaimFailureText(result.Reason, FindEntry(result.InboxId));
            _view.SetStatus(string.IsNullOrEmpty(text) ? FAILED_TEXT : text);
            _view.SetClaimInteractable(true);
        }

        private InboxEntry FindEntry(string inboxId)
        {
            for (int i = 0; i < _entries.Length; ++i)
            {
                if (_entries[i].InboxId == inboxId)
                {
                    return _entries[i];
                }
            }

            return null;
        }
    }
}
