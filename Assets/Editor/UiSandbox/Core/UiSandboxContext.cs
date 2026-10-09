using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 샌드박스 씬 한 개와 그 시나리오 사본을 묶은 실행 문맥. 화면은 이 문맥으로 View를 찾고 가짜 요청을 보낸다. 네트워크 요청은 하지 않는다 </summary>
    public sealed class UiSandboxContext : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_HISTORY = 50;

        private sealed class PendingRequest
        {
            public UiSandboxRequestRecord Record;
            public double DueAt;
            public int Generation;
            public UiSandboxResponsePolicy Policy;
            public Action OnSuccess;
            public Action<string> OnFailure;
        }

        private readonly Dictionary<Type, Array> _cache = new Dictionary<Type, Array>();
        private readonly List<UiSandboxRequestRecord> _history = new List<UiSandboxRequestRecord>();
        private readonly Func<double> _clock;
        private readonly Func<bool> _isPlayingProvider;
        private readonly Func<string> _unsafeReasonProvider;

        private UiSandboxScenarioSO _baseline = null;
        private UiSandboxScenarioSO _scenario = null;
        private GameObject[] _roots = null;
        private PendingRequest _pending = null;
        private string _status = "대기 중";
        private int _generation = 0;
        private int _nextRequestId = 1;
        private bool _updateSubscribed = false;
        private bool _disposed = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 이 문맥이 소유한 변경 가능한 시나리오 사본. 원본 템플릿은 바뀌지 않는다 </summary>
        public UiSandboxScenarioSO Scenario
        {
            get { return _scenario; }
        }

        public string Status
        {
            get { return _status; }
        }

        public bool IsBusy
        {
            get { return _pending != null; }
        }

        public bool IsPlaying
        {
            get { return _isPlayingProvider(); }
        }

        public bool IsDisposed
        {
            get { return _disposed; }
        }

        /// <summary> 지금 요청을 막아야 하는 사유. 안전하면 null이다 </summary>
        public string UnsafeReason
        {
            get { return _unsafeReasonProvider(); }
        }

        /// <summary> 진행 중인 요청 이름. 없으면 빈 문자열 </summary>
        public string PendingOperation
        {
            get { return (_pending != null) ? _pending.Record.Operation : string.Empty; }
        }

        /// <summary> 요청 이력. 오래된 것부터 최대 50건이다 </summary>
        public IReadOnlyList<UiSandboxRequestRecord> History
        {
            get { return _history; }
        }

        public IReadOnlyList<GameObject> Roots
        {
            get { return _roots; }
        }

        /****************************************
        *               Lifecycle
        ****************************************/

        public UiSandboxContext(UiSandboxScenarioSO template, GameObject[] roots)
            : this(template, roots, null, null)
        {
        }

        /// <summary> 시계와 Play 상태 제공자를 바꿀 수 있는 생성자. 테스트가 결정적인 시간으로 요청을 검증할 때 쓴다 </summary>
        public UiSandboxContext(UiSandboxScenarioSO template, GameObject[] roots, Func<double> clock, Func<bool> isPlayingProvider)
            : this(template, roots, clock, isPlayingProvider, null)
        {
        }

        /// <summary> 요청 안전 판정을 바꿀 수 있는 생성자. 제공자는 요청을 막아야 하면 사유를, 안전하면 null을 돌려준다. null이면 Mirror 접속 여부만 본다 </summary>
        public UiSandboxContext(UiSandboxScenarioSO template, GameObject[] roots, Func<double> clock, Func<bool> isPlayingProvider,
            Func<string> unsafeReasonProvider)
        {
            _clock             = clock ?? DefaultClock;
            _isPlayingProvider = isPlayingProvider ?? DefaultIsPlaying;
            _unsafeReasonProvider = unsafeReasonProvider ?? DefaultUnsafeReason;
            _baseline          = CloneOrEmpty(template);
            _scenario          = _baseline.Clone();
            _roots             = (roots != null) ? (GameObject[])roots.Clone() : Array.Empty<GameObject>();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary> 진행 중인 요청을 취소하고 소유한 시나리오 사본만 파괴한다. 씬 오브젝트와 원본 템플릿은 건드리지 않는다 </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CancelPending("컨텍스트 폐기");
            _disposed = true;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _cache.Clear();
            _roots = Array.Empty<GameObject>();

            DestroyOwned(ref _scenario);
            DestroyOwned(ref _baseline);
        }

        /****************************************
        *             Scene Lookup
        ****************************************/

        /// <summary> 샌드박스 씬 루트 아래에서 첫 컴포넌트를 찾는다. 비활성 오브젝트도 포함한다 </summary>
        public T Find<T>() where T : Component
        {
            T[] all = FindAll<T>();
            return (all.Length > 0) ? all[0] : null;
        }

        /// <summary> 샌드박스 씬 루트 아래의 모든 컴포넌트를 찾는다. 결과는 읽기 전용으로 쓴다 </summary>
        public T[] FindAll<T>() where T : Component
        {
            if (_disposed)
            {
                return Array.Empty<T>();
            }

            Type type = typeof(T);
            if ((_cache.TryGetValue(type, out Array cached)) && (AllAlive((T[])cached)))
            {
                return (T[])cached;
            }

            List<T> found = new List<T>();
            for (int i = 0; i < _roots.Length; ++i)
            {
                if (_roots[i] == null)
                {
                    continue;
                }

                found.AddRange(_roots[i].GetComponentsInChildren<T>(true));
            }

            T[] result = found.ToArray();

            // 비어 있는 결과는 저장하지 않는다. 실행 중에 만들어지는 오브젝트를 다음 호출에서 찾을 수 있어야 한다
            if (result.Length > 0)
            {
                _cache[type] = result;
            }
            else
            {
                _cache.Remove(type);
            }

            return result;
        }

        public void InvalidateCache()
        {
            _cache.Clear();
        }

        /// <summary> 다른 씬 루트로 다시 연결한다. 진행 중이던 요청은 새 화면에 적용되지 않도록 취소한다 </summary>
        public void Rebind(GameObject[] roots)
        {
            if (_disposed)
            {
                return;
            }

            CancelPending("씬 다시 연결");
            _roots = (roots != null) ? (GameObject[])roots.Clone() : Array.Empty<GameObject>();
            _cache.Clear();
        }

        /****************************************
        *              Scenario
        ****************************************/

        public void SetStatus(string message)
        {
            _status = message ?? string.Empty;
        }

        /// <summary> 프리셋 값으로 시나리오를 통째로 교체한다. 진행 중인 요청은 취소된다 </summary>
        public void LoadScenario(UiSandboxScenarioSO source)
        {
            if ((_disposed) || (source == null))
            {
                return;
            }

            CancelPending("시나리오 교체");
            ++_generation;
            _baseline.CopyFrom(source);
            _scenario.CopyFrom(_baseline);
            _status = $"시나리오 '{source.name}'을 불러왔다";
        }

        /// <summary> 작업 중 시나리오 값을 JSON으로 저장한다. 도메인 리로드 전에 창이 보관한다 </summary>
        public string CaptureWorkingJson()
        {
            return _disposed ? string.Empty : _scenario.ToJson();
        }

        /// <summary> 마지막으로 불러온 기준 값을 JSON으로 저장한다. 리셋이 리로드 뒤에도 같은 기준으로 돌아가게 한다 </summary>
        public string CaptureBaselineJson()
        {
            return _disposed ? string.Empty : _baseline.ToJson();
        }

        /// <summary> 보관해 둔 JSON으로 기준 값과 작업 값을 복원한다. 작업 JSON이 깨졌으면 아무것도 바꾸지 않고 false를 돌려준다 </summary>
        public bool RestoreSnapshot(string workingJson, string baselineJson)
        {
            if (_disposed)
            {
                return false;
            }

            UiSandboxScenarioSO probe = CreateInstance();
            try
            {
                if (!probe.TryLoadJson(workingJson))
                {
                    return false;
                }

                CancelPending("시나리오 복원");
                ++_generation;

                if (!_baseline.TryLoadJson(baselineJson))
                {
                    _baseline.CopyFrom(probe);
                }

                _scenario.CopyFrom(probe);
                _status = "편집 중이던 시나리오 값을 복원했다";
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static UiSandboxScenarioSO CreateInstance()
        {
            UiSandboxScenarioSO instance = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        /// <summary> 시나리오를 마지막으로 불러온 값으로 되돌리고 진행 중인 요청을 취소한다 </summary>
        public void ResetScenario()
        {
            if (_disposed)
            {
                return;
            }

            CancelPending("리셋");
            ++_generation;
            _scenario.CopyFrom(_baseline);
            _status = "시나리오를 처음 값으로 되돌렸다";
        }

        /****************************************
        *               Request
        ****************************************/

        /// <summary> 가짜 서버 요청을 시작한다. 응답 정책은 시작 순간의 값으로 고정되고 완료는 에디터 업데이트에서 처리한다. 시작하지 못하면 false이고 이유는 Status에 남는다 </summary>
        public bool Request(string operation, Action onSuccess, Action<string> onFailure = null)
        {
            if (_disposed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(operation))
            {
                SetStatus("요청 이름이 비어 있어 시작하지 않았다");
                return false;
            }

            UiSandboxResponsePolicy policy = UiSandboxResponsePolicy.FromScenario(_scenario);
            double now = _clock();

            string unsafeReason = _unsafeReasonProvider();
            if (unsafeReason != null)
            {
                AddHistory(operation, policy, now, EUiSandboxRequestState.Blocked, unsafeReason);
                SetStatus(unsafeReason);
                return false;
            }

            if (!IsPlaying)
            {
                string instruction = $"'{operation}' 요청은 Play Mode에서만 실행한다. 테스트 씬을 연 뒤 Play를 누르고 다시 시도한다";
                AddHistory(operation, policy, now, EUiSandboxRequestState.Blocked, instruction);
                SetStatus(instruction);
                return false;
            }

            if (_pending != null)
            {
                string blocked = $"'{_pending.Record.Operation}' 요청이 진행 중이라 '{operation}' 요청을 막았다";
                AddHistory(operation, policy, now, EUiSandboxRequestState.Blocked, blocked);
                SetStatus(blocked);
                return false;
            }

            UiSandboxRequestRecord record = AddHistory(operation, policy, now, EUiSandboxRequestState.Pending,
                $"{policy} 응답 대기");
            _pending = new PendingRequest
            {
                Record     = record,
                Policy     = policy,
                DueAt      = now + policy.DelaySeconds,
                Generation = _generation,
                OnSuccess  = onSuccess,
                OnFailure  = onFailure
            };

            SubscribeUpdate();
            SetStatus($"'{operation}' 요청 중 ({policy})");
            return true;
        }

        /// <summary> 지정한 시각까지 시간이 흘렀다고 보고 완료 가능한 요청을 처리한다. 에디터 업데이트가 부르고 테스트도 직접 부를 수 있다 </summary>
        public void Tick(double now)
        {
            if (_pending == null)
            {
                return;
            }

            if (!IsPlaying)
            {
                CancelPending("Play 종료");
                return;
            }

            string unsafeReason = _unsafeReasonProvider();
            if (unsafeReason != null)
            {
                CancelPending(unsafeReason);
                return;
            }

            if (_pending.Generation != _generation)
            {
                CancelPending("시나리오가 바뀌어 이전 응답을 버렸다");
                return;
            }

            if (now < _pending.DueAt)
            {
                return;
            }

            Complete(now);
        }

        /// <summary> 진행 중인 요청을 콜백 호출 없이 취소한다. 취소한 요청이 있었으면 true </summary>
        public bool CancelPending(string reason)
        {
            if (_pending == null)
            {
                return false;
            }

            PendingRequest pending = _pending;
            ReleasePending();

            pending.Record.State       = EUiSandboxRequestState.Canceled;
            pending.Record.CompletedAt = _clock();
            pending.Record.Message     = $"취소됨: {reason}";
            _status                    = $"'{pending.Record.Operation}' 요청 취소 ({reason})";
            return true;
        }

        public void ClearHistory()
        {
            _history.Clear();
        }

        private void Complete(double now)
        {
            PendingRequest pending = _pending;
            Action onSuccess       = pending.OnSuccess;
            Action<string> onFail  = pending.OnFailure;

            // 콜백이 새 요청을 시작할 수 있도록 먼저 비우고, 구독도 이 시점에 끊는다
            ReleasePending();

            UiSandboxRequestRecord record = pending.Record;
            record.CompletedAt = now;

            if (pending.Policy.IsSuccess)
            {
                record.State   = EUiSandboxRequestState.Succeeded;
                record.Message = "성공";
                _status        = $"'{record.Operation}' 성공";
                Invoke(onSuccess, record);
                return;
            }

            string failure = pending.Policy.ResolveFailureMessage();
            record.State   = EUiSandboxRequestState.Failed;
            record.Message = $"{pending.Policy.Mode}: {failure}";
            _status        = $"'{record.Operation}' 실패 - {failure}";
            Invoke(onFail, failure, record);
        }

        private void Invoke(Action callback, UiSandboxRequestRecord record)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _status = $"'{record.Operation}' 성공 처리 중 예외: {exception.GetType().Name}";
            }
        }

        private void Invoke(Action<string> callback, string message, UiSandboxRequestRecord record)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _status = $"'{record.Operation}' 실패 처리 중 예외: {exception.GetType().Name}";
            }
        }

        private void ReleasePending()
        {
            if (_pending != null)
            {
                _pending.OnSuccess = null;
                _pending.OnFailure = null;
                _pending           = null;
            }

            UnsubscribeUpdate();
        }

        private UiSandboxRequestRecord AddHistory(string operation, UiSandboxResponsePolicy policy, double now,
            EUiSandboxRequestState state, string message)
        {
            UiSandboxRequestRecord record = new UiSandboxRequestRecord(_nextRequestId, operation, policy.Mode,
                policy.DelaySeconds, now, state, message);
            ++_nextRequestId;
            _history.Add(record);

            if (_history.Count > MAX_HISTORY)
            {
                _history.RemoveAt(0);
            }

            return record;
        }

        private void SubscribeUpdate()
        {
            if (_updateSubscribed)
            {
                return;
            }

            _updateSubscribed        = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private void UnsubscribeUpdate()
        {
            if (!_updateSubscribed)
            {
                return;
            }

            _updateSubscribed        = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            Tick(_clock());
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            CancelPending($"Play 상태 전환 ({change})");
        }

        private static bool AllAlive<T>(T[] components) where T : Component
        {
            for (int i = 0; i < components.Length; ++i)
            {
                if ((UnityEngine.Object)components[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static UiSandboxScenarioSO CloneOrEmpty(UiSandboxScenarioSO template)
        {
            if (template != null)
            {
                return template.Clone();
            }

            UiSandboxScenarioSO empty = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            empty.name      = "UiSandboxScenario (Empty)";
            empty.hideFlags = HideFlags.HideAndDontSave;
            return empty;
        }

        private static void DestroyOwned(ref UiSandboxScenarioSO owned)
        {
            if (owned != null)
            {
                UnityEngine.Object.DestroyImmediate(owned);
            }

            owned = null;
        }

        private static double DefaultClock()
        {
            return EditorApplication.timeSinceStartup;
        }

        private static bool DefaultIsPlaying()
        {
            return Application.isPlaying;
        }

        private static string DefaultUnsafeReason()
        {
            return UiSandboxSafetyPolicy.IsNetworkActive(out string reason) ? reason : null;
        }
    }
}
