// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 조회 결과. Stale이면 입력이 바뀌어 폐기된 조회다 </summary>
    public sealed record ResolutionOutcome(ResolvedAccount? Account, bool Stale);

    /// <summary> 한 대상 입력의 확정 상태. 입력·모드가 바뀌면 선택과 진행 중 요청을 모두 버리고 늦게 온 응답은 세대로 폐기한다 </summary>
    public sealed class AccountResolution
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<string, bool, bool, Task<AccountLookupResult>> _lookup;
        private int _generation;
        private string _key = string.Empty;
        private Task<ResolutionOutcome>? _task;

        /****************************************
        *              Properties
        ****************************************/

        public ResolvedAccount? Selected { get; private set; }
        public IReadOnlyList<ResolvedAccount> Matches { get; private set; } = Array.Empty<ResolvedAccount>();
        public string Error { get; private set; } = string.Empty;
        public bool Busy { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <param name="lookup"> (입력, 삭제 캐릭터 포함, 초대 코드 모드) → 조회 결과 </param>
        public AccountResolution(Func<string, bool, bool, Task<AccountLookupResult>> lookup)
        {
            ArgumentNullException.ThrowIfNull(lookup);
            _lookup = lookup;
        }

        public static string KeyOf(string text, bool includeDeleted, bool inviteMode)
        {
            return (inviteMode ? "I" : "N") + (includeDeleted ? "D" : "-") + "|" + text;
        }

        /// <summary> 입력이 바뀌었을 때. 선택·후보·오류·진행 중 요청을 모두 무효화한다 </summary>
        public void Reset()
        {
            ++_generation;
            _key = string.Empty;
            _task = null;
            Selected = null;
            Matches = Array.Empty<ResolvedAccount>();
            Error = string.Empty;
            Busy = false;
        }

        /// <summary> 현재 입력(과 모드)에 대한 확정 계정을 돌려준다. 같은 입력의 성공만 재사용하고 실패는 매번 다시 조회한다 </summary>
        public async Task<ResolvedAccount?> EnsureAsync(string text, bool includeDeleted, bool inviteMode)
        {
            return (await EnsureOutcomeAsync(text, includeDeleted, inviteMode)).Account;
        }

        /// <summary> EnsureAsync와 같지만 폐기된(오래된) 조회의 완료를 Stale로 구분해 알린다. Stale 결과는 화면 상태에 반영하지 않는다 </summary>
        public Task<ResolutionOutcome> EnsureOutcomeAsync(string text, bool includeDeleted, bool inviteMode)
        {
            string key = KeyOf(text.Trim(), includeDeleted, inviteMode);
            if ((Selected != null) && (key == _key))
            {
                return Task.FromResult(new ResolutionOutcome(Selected, false));
            }
            if ((_task != null) && (key == _key))
            {
                return _task;
            }
            Reset();
            _key = key;
            int generation = _generation;
            Task<ResolutionOutcome> task = ResolveAsync(text, includeDeleted, inviteMode, generation);
            // 동기로 끝난 요청의 완료 Task를 저장하면 다음 입력에서 재사용되므로 진행 중일 때만 보관한다
            _task = task.IsCompleted ? null : task;
            return task;
        }

        /// <summary> 여러 후보 중 사용자가 고른 계정을 확정한다. 현재 후보에 있는 것만 받는다 </summary>
        public bool Choose(ResolvedAccount account)
        {
            if (!Matches.Contains(account))
            {
                return false;
            }
            Selected = account;
            Matches = Array.Empty<ResolvedAccount>();
            Error = string.Empty;
            return true;
        }

        private async Task<ResolutionOutcome> ResolveAsync(string text, bool includeDeleted, bool inviteMode, int generation)
        {
            Busy = true;
            AccountLookupResult result;
            try
            {
                result = await _lookup(text, includeDeleted, inviteMode);
            }
            catch (Exception)
            {
                // 조회 실패는 사용자에게 보이는 오류로 바꾸고 캐시하지 않는다
                result = new AccountLookupResult(Array.Empty<ResolvedAccount>(), "대상을 확인하지 못했습니다. 다시 시도하세요.");
            }
            if (generation != _generation)
            {
                return new ResolutionOutcome(null, true);
            }
            Busy = false;
            _task = null;
            if (!result.IsSuccess)
            {
                _key = string.Empty;
                Error = result.Error;
                return new ResolutionOutcome(null, false);
            }
            if (result.Matches.Count == 0)
            {
                _key = string.Empty;
                Error = inviteMode ? "일치하는 초대 코드가 없습니다."
                    : "'" + text.Trim() + "' 캐릭터를 찾을 수 없습니다." + (includeDeleted ? string.Empty : " 삭제된 캐릭터는 제외됩니다.");
                return new ResolutionOutcome(null, false);
            }
            if (result.Matches.Count == 1)
            {
                Selected = result.Matches[0];
                return new ResolutionOutcome(Selected, false);
            }
            // 키는 유지한다. 사용자가 후보를 고르면 같은 입력의 재확인(EnsureAsync)이 그 선택을 돌려준다
            Matches = result.Matches.ToList();
            return new ResolutionOutcome(null, false);
        }
    }

    /// <summary> 여러 대상 입력의 확정 상태. 전체 성공만 재사용하고 입력이 바뀌면 이전 결과·진행 중 요청을 버린다 </summary>
    public sealed class TargetResolution
    {
        /****************************************
        *                Nested
        ****************************************/

        public sealed record Item(TargetToken Token, ResolvedAccount? Account, string Error);

        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<string, Task<AccountLookupResult>> _lookup;
        private int _generation;
        private string _key = string.Empty;
        private Task<IReadOnlyList<string>?>? _task;
        private IReadOnlyList<string>? _accounts;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<Item> Items { get; private set; } = Array.Empty<Item>();
        public IReadOnlyList<string> Notes { get; private set; } = Array.Empty<string>();
        public bool Busy { get; private set; }
        public int Progress { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public TargetResolution(Func<string, Task<AccountLookupResult>> lookup)
        {
            ArgumentNullException.ThrowIfNull(lookup);
            _lookup = lookup;
        }

        public void Reset()
        {
            ++_generation;
            _key = string.Empty;
            _task = null;
            _accounts = null;
            Items = Array.Empty<Item>();
            Notes = Array.Empty<string>();
            Busy = false;
            Progress = 0;
        }

        /// <summary> 모든 대상의 중복 없는 계정 ID. 하나라도 실패했거나 비었으면 null이며 실패는 캐시하지 않는다 </summary>
        public Task<IReadOnlyList<string>?> EnsureAllAsync(string text, Action? onProgress = null)
        {
            IReadOnlyList<TargetToken> tokens = TargetInput.Parse(text);
            string key = string.Join("\n", tokens.Select(token => token.Text));
            if (tokens.Count == 0)
            {
                Reset();
                Notes = new[] { "대상을 한 명 이상 입력하세요." };
                return Task.FromResult<IReadOnlyList<string>?>(null);
            }
            if ((_accounts != null) && (key == _key))
            {
                return Task.FromResult<IReadOnlyList<string>?>(_accounts);
            }
            if ((_task != null) && (key == _key))
            {
                return _task;
            }
            Reset();
            _key = key;
            Task<IReadOnlyList<string>?> task = ResolveAsync(tokens, _generation, onProgress);
            _task = task.IsCompleted ? null : task;
            return task;
        }

        private async Task<IReadOnlyList<string>?> ResolveAsync(IReadOnlyList<TargetToken> tokens, int generation, Action? onProgress)
        {
            Busy = true;
            List<Item> items = new();
            foreach (TargetToken token in tokens)
            {
                AccountLookupResult lookup;
                try
                {
                    lookup = await _lookup(token.Text);
                }
                catch (Exception)
                {
                    // 한 항목의 조회 실패는 그 항목의 오류로 남기고 나머지는 계속 확인한다
                    lookup = new AccountLookupResult(Array.Empty<ResolvedAccount>(), "확인하지 못함");
                }
                if (generation != _generation)
                {
                    return null;
                }
                if (!lookup.IsSuccess)
                {
                    items.Add(new Item(token, null, lookup.Error));
                }
                else if (lookup.Matches.Count == 0)
                {
                    items.Add(new Item(token, null, "찾을 수 없음"));
                }
                else if (lookup.Matches.Count > 1)
                {
                    items.Add(new Item(token, null, "여러 계정이 일치함. 계정 ID로 입력하세요."));
                }
                else
                {
                    items.Add(new Item(token, lookup.Matches[0], string.Empty));
                }
                ++Progress;
                onProgress?.Invoke();
            }
            Busy = false;
            _task = null;
            Items = items;
            int failed = items.Count(item => item.Account == null);
            string[] unique = items.Where(item => item.Account != null).Select(item => item.Account!.AccountId)
                .Distinct(StringComparer.Ordinal).ToArray();
            List<string> notes = new();
            if (failed > 0)
            {
                notes.Add(failed + "개 대상을 확인하지 못했습니다. 수정한 뒤 다시 확인하세요.");
                Notes = notes;
                _key = string.Empty;
                return null;
            }
            if (unique.Length < items.Count)
            {
                notes.Add("같은 계정을 가리키는 항목은 한 번만 지급합니다. (" + items.Count + "개 입력 → " + unique.Length + "개 계정)");
            }
            Notes = notes;
            _accounts = unique;
            return _accounts;
        }
    }
}
