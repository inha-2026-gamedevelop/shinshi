// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>경매장 개발 화면의 예시 목록과 확인 동선을 제공한다. 경제 요청을 보내지 않는다</summary>
    public sealed class VillageAuctionPreview : MonoBehaviour
    {
        private const string PREVIEW_TEXT = "UI 미리보기 - 구매와 등록은 실제 재화에 반영되지 않습니다";
        private const int PAGE_SIZE       = 2;

        [SerializeField] private AuctionView _view = null;
        [SerializeField] private UiPopupHub _popup = null;

        private static readonly string[] G_Names = { "희귀 검", "약초", "부적", "희귀 갑옷" };
        private static readonly int[] G_Categories = { 3, 1, 2, 3 };
        private static readonly int[] G_Grades = { 1, 0, 1, 1 };
        private static readonly long[] G_Prices = { 500, 100, 300, 800 };
        private static readonly int[] G_Quantities = { 1, 20, 10, 1 };
        private readonly List<ListRowData> _mine = new List<ListRowData>();
        private readonly List<bool> _cancellable = new List<bool>();
        private readonly List<ListRowData> _history = new List<ListRowData>();
        private readonly List<int> _matches = new List<int>();
        private int _page;
        private int _group = -1;
        private int _sell = -1;

        private void Awake()
        {
            _view.TabChanged += OnTab;
            _view.SearchRequested += Search;
            _view.PageDeltaRequested += Page;
            _view.GroupSelected += Group;
            _view.ListingSelected += Listing;
            _view.SellItemSelected += Sell;
            _view.BuyRequested += Buy;
            _view.RegisterRequested += Register;
            _view.CancelRequested += Cancel;
            _view.AckRequested += Ack;
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }
            _view.TabChanged -= OnTab;
            _view.SearchRequested -= Search;
            _view.PageDeltaRequested -= Page;
            _view.GroupSelected -= Group;
            _view.ListingSelected -= Listing;
            _view.SellItemSelected -= Sell;
            _view.BuyRequested -= Buy;
            _view.RegisterRequested -= Register;
            _view.CancelRequested -= Cancel;
            _view.AckRequested -= Ack;
        }

        public void Prepare()
        {
            _view.SetYeopjeon(12000);
            _view.SetStatus(PREVIEW_TEXT);
            _view.SetSellables(new[] { new ListRowData(1, G_Names[0], "1"), new ListRowData(0, G_Names[1], "20"), new ListRowData(0, G_Names[2], "10") });
            OnTab(_view.Tab);
        }

        private void OnTab(int tab)
        {
            switch (tab)
            {
                case 0:
                    Search(new AuctionSearchInput(string.Empty, -1, -1, -1, 0, 0));
                    break;
                case 1:
                    _view.SetSellTarget(null, 0);
                    break;
                case 2:
                    _view.SetMine(_mine, _cancellable, _history.Count);
                    break;
                case 3:
                    _view.SetHistory(_history, 0, false);
                    break;
            }
            _view.SetStatus(PREVIEW_TEXT);
        }

        private void Search(AuctionSearchInput input)
        {
            _matches.Clear();
            for (int i = 0; i < G_Names.Length; ++i)
            {
                int category = G_Categories[i];
                int grade = G_Grades[i];
                if ((G_Names[i].IndexOf(input.Text, StringComparison.OrdinalIgnoreCase) >= 0)
                    && ((input.Category < 0) || (input.Category == category))
                    && ((input.Grade < 0) || (input.Grade == grade))
                    && ((input.ClassId < 0) || (category != 3) || (input.ClassId == 0)))
                {
                    _matches.Add(i);
                }
            }
            _matches.Sort((left, right) => input.Sort == 2
                ? G_Quantities[left].CompareTo(G_Quantities[right])
                : input.Sort == 1 ? left.CompareTo(right) : G_Prices[left].CompareTo(G_Prices[right]));
            if (input.Order != 0)
            {
                _matches.Reverse();
            }
            _page = 0;
            ShowGroups();
        }

        private void Page(int delta)
        {
            _page = Mathf.Clamp(_page + delta, 0, Mathf.Max(0, (_matches.Count - 1) / PAGE_SIZE));
            ShowGroups();
        }

        private void ShowGroups()
        {
            var rows = new List<ListRowData>();
            int start = _page * PAGE_SIZE;
            for (int i = start; i < Mathf.Min(start + PAGE_SIZE, _matches.Count); ++i)
            {
                int item = _matches[i];
                rows.Add(new ListRowData(G_Grades[item], G_Names[item], G_Prices[item].ToString(), G_Quantities[item].ToString(), "1", G_Prices[item].ToString()));
            }
            _view.SetGroups(rows, _matches.Count, _page, Mathf.Max(1, Mathf.CeilToInt((float)_matches.Count / PAGE_SIZE)));
            _view.SetListings(null, Array.Empty<ListRowData>(), 0);
            _group = -1;
        }

        private void Group(int index)
        {
            int match = (_page * PAGE_SIZE) + index;
            if ((match < 0) || (match >= _matches.Count))
            {
                return;
            }
            _group = _matches[match];
            _view.SetListings(G_Names[_group], new[] { new ListRowData(0, G_Prices[_group].ToString(), G_Quantities[_group].ToString(), "23시간") }, G_Prices[_group]);
        }

        private void Listing(int index)
        {
            if (_group >= 0)
            {
                _view.SetBuyTarget(G_Prices[_group], G_Quantities[_group]);
            }
        }

        private void Sell(int index)
        {
            if ((index < 0) || (index >= G_Names.Length - 1))
            {
                return;
            }
            _sell = index;
            _view.SetSellTarget(G_Names[index], G_Quantities[index]);
        }

        private void Buy(int quantity)
        {
            if (_group < 0)
            {
                return;
            }
            string name = G_Names[_group];
            long total = G_Prices[_group] * quantity;
            _popup.ShowConfirm(EPopupSize.Medium, name + " " + quantity + "개 구매 화면을 확인할까요?\n" + PREVIEW_TEXT, accepted =>
            {
                if (accepted)
                {
                    _history.Add(new ListRowData(0, "구매 (예시)", name, quantity.ToString(), total.ToString(), "0", "0"));
                    _view.SetStatus("구매 확인 완료 - 실제 재화는 차감되지 않았습니다");
                }
            });
        }

        private void Register(int quantity, long price)
        {
            if (_sell < 0)
            {
                return;
            }
            string name = G_Names[_sell];
            _popup.ShowConfirm(EPopupSize.Medium, name + " " + quantity + "개 등록 화면을 확인할까요?\n" + PREVIEW_TEXT, accepted =>
            {
                if (accepted)
                {
                    _mine.Add(new ListRowData(0, name, price.ToString(), quantity.ToString(), "판매 중 (예시)", "24시간"));
                    _cancellable.Add(true);
                    _view.SetStatus("등록 확인 완료 - 내 등록 탭에서 예시 등록을 볼 수 있습니다");
                }
            });
        }

        private void Cancel(int index)
        {
            if ((index < 0) || (index >= _mine.Count))
            {
                return;
            }
            _popup.ShowConfirm(EPopupSize.Medium, "예시 등록을 취소할까요?", accepted =>
            {
                if ((accepted) && (index < _mine.Count))
                {
                    _mine.RemoveAt(index);
                    _cancellable.RemoveAt(index);
                    _view.SetMine(_mine, _cancellable, 0);
                }
            });
        }

        private void Ack() { _view.SetMine(_mine, _cancellable, 0); _view.SetStatus("예시 내역을 확인했습니다"); }
    }
}
