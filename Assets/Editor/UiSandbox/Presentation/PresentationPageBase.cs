using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary>
    /// Presentation 화면의 공통 뼈대. 씬에 View가 있으면 그것을 쓰고 없으면 시험 전용 배치를 만들어 붙인다.
    /// 구독한 이벤트와 시험 배치는 Detach에서 모두 해제하고 파괴한다
    /// </summary>
    public abstract class PresentationPageBase : IUiSandboxPage
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string FIXTURE_HELP     = "씬에 없는 View라 시험 전용 배치를 만들어 붙였다. 위치와 모양은 운영 화면의 배치가 아니며 View의 공개 API 동작만 확인한다";
        private const string SCENE_HELP       = "복제 씬에 있는 실제 View에 값을 적용한다";
        private const string UNWIRED_HELP     = "시험 배치에서 연결하지 않은 필드 (null로 둠): ";
        private const string PLAY_REQUIRED    = "버튼 클릭과 지연 응답은 Play Mode에서만 동작한다";
        private const string LAST_RESPONSE    = "마지막 모의 응답";
        private const string NO_RESPONSE      = "없음";
        private const string NOT_STARTED_TEXT = "시작되지 않음 (Play Mode가 아니거나 이미 요청이 진행 중)";
        private const string DETACH_REASON    = "화면 전환";

        private readonly List<PresentationFixture> _fixtures = new List<PresentationFixture>();

        private UiSandboxContext _context;
        private string           _refusal      = string.Empty;
        private string           _lastResponse = NO_RESPONSE;
        private int              _generation;
        private bool             _dirty;

        /****************************************
        *              Properties
        ****************************************/

        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string SourceScenePath { get; }
        public abstract string Requirement { get; }

        public virtual bool HasView
        {
            get { return true; }
        }

        public bool UsesFixture
        {
            get { return _fixtures.Count > 0; }
        }

        public IReadOnlyList<PresentationFixture> Fixtures
        {
            get { return _fixtures; }
        }

        public bool IsAttached
        {
            get { return _context != null; }
        }

        public string LastResponse
        {
            get { return _lastResponse; }
        }

        /// <summary> 안전하지 않은 씬이라 View 연결을 거절한 사유. 거절하지 않았으면 빈 문자열 </summary>
        public string RefusalReason
        {
            get { return _refusal; }
        }

        public bool IsRefused
        {
            get { return !string.IsNullOrEmpty(_refusal); }
        }

        protected UiSandboxContext Context
        {
            get { return _context; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Attach(UiSandboxContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Detach();
            _context      = context;
            _lastResponse = NO_RESPONSE;
            _refusal      = string.Empty;
            ++_generation;

            Seed(context.Scenario);

            // View 화면은 샌드박스 씬이나 시험 범위가 아니면 아예 연결하지 않는다. 원본 씬 객체에 값을 쓰거나 이벤트를 걸지 않기 위해서다
            if ((HasView) && (!PresentationFixtureFactory.CanCreate(out string reason)))
            {
                Refuse(reason);
                return;
            }

            try
            {
                OnAttach(context);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Refuse("연결 중 예외: " + exception.GetType().Name + " (View 필드 연결을 확인한다)");
            }
        }

        public void Apply(UiSandboxContext context)
        {
            if (context == null)
            {
                return;
            }

            if ((_context != context) || (HasDeadFixture()) || (NeedsReattach(context)))
            {
                Attach(context);
            }

            if (IsRefused)
            {
                return;
            }

            ApplyGuarded(context);
        }

        public void DrawControls(UiSandboxContext context)
        {
            if (context == null)
            {
                return;
            }

            // Play 진입이나 씬 교체로 시험 배치가 사라졌으면 다시 만든다
            if ((_context != context) || (HasDeadFixture()) || (NeedsReattach(context)))
            {
                Attach(context);
            }

            if (IsRefused)
            {
                EditorGUILayout.HelpBox(_refusal, MessageType.Error);
                return;
            }

            DrawProvenance(context);
            _dirty = false;

            OnDrawControls(context);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(LAST_RESPONSE, _lastResponse);

            if (_dirty)
            {
                ApplyGuarded(context);
                _dirty = false;
            }
        }

        public void Detach()
        {
            ++_generation;

            if ((_context != null) && (!_context.IsDisposed) && (_context.IsBusy))
            {
                _context.CancelPending(DETACH_REASON);
            }

            OnDetach();

            bool hadFixtures = _fixtures.Count > 0;
            for (int i = 0; i < _fixtures.Count; ++i)
            {
                _fixtures[i].Destroy();
            }

            _fixtures.Clear();
            _context = null;
            _refusal = string.Empty;

            if (hadFixtures)
            {
                PresentationFixtureFactory.ReleaseCanvasIfUnused();
            }
        }

        private bool HasDeadFixture()
        {
            for (int i = 0; i < _fixtures.Count; ++i)
            {
                if (!_fixtures[i].IsAlive)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> Play 전환처럼 같은 컨텍스트라도 다시 붙여야 하는 상태 변화가 있는지. 기본은 없음 </summary>
        protected virtual bool NeedsReattach(UiSandboxContext context)
        {
            return false;
        }

        /// <summary> 비어 있는 시나리오 키만 채운다. 사용자가 고친 값은 덮어쓰지 않는다 </summary>
        protected abstract void Seed(UiSandboxScenarioSO scenario);

        /// <summary> View를 찾거나 만들고 이벤트를 구독한다 </summary>
        protected abstract void OnAttach(UiSandboxContext context);

        protected abstract void OnApply(UiSandboxContext context);

        protected abstract void OnDrawControls(UiSandboxContext context);

        /// <summary> 구독한 이벤트를 해제한다. 시험 배치 파괴는 이 뒤에 기반 클래스가 한다 </summary>
        protected abstract void OnDetach();

        /// <summary>
        /// 씬에서 View를 찾고 없으면 시험 배치를 만든다. 씬의 View를 우선한다.
        /// 도구가 만든 샌드박스 씬이나 명시적 시험 범위가 아니면 거절하고 null을 돌려준다
        /// </summary>
        protected T BindView<T>(UiSandboxContext context, string label, PresentationFixtureOptions options = null)
            where T : Component
        {
            T found = context.Find<T>();
            if (found != null)
            {
                if (PresentationFixtureFactory.IsHostScene(found.gameObject.scene))
                {
                    return found;
                }

                Refuse(PresentationFixtureFactory.REFUSE_REASON);
                return null;
            }

            return CreateFixture<T>(label, options);
        }

        /// <summary> 씬의 View를 쓰지 않고 시험 전용 배치를 강제로 만든다. 서버로 나가는 입력 칸이 이미 연결된 View를 피할 때 쓴다 </summary>
        protected T CreateFixture<T>(string label, PresentationFixtureOptions options = null)
            where T : Component
        {
            if (!PresentationFixtureFactory.CanCreate(out string reason))
            {
                Refuse(reason);
                return null;
            }

            PresentationFixture fixture = PresentationFixtureFactory.Create<T>(label, options);
            _fixtures.Add(fixture);
            return fixture.View as T;
        }

        /// <summary> 자동 연결로 만들 수 없는 구조를 직접 짠 시험 배치로 붙인다. 씬의 View는 찾지 않으며 Detach에서 함께 파괴된다 </summary>
        protected T CreateScaffold<T>(string label, PresentationFixtureOptions options, Func<Transform, T> build)
            where T : Component
        {
            if (!PresentationFixtureFactory.CanCreate(out string reason))
            {
                Refuse(reason);
                return null;
            }

            PresentationFixture fixture = PresentationFixtureFactory.CreateScaffold(label, options, host => build(host));
            _fixtures.Add(fixture);
            return fixture.View as T;
        }

        private void Refuse(string reason)
        {
            _refusal = reason;
            if ((_context != null) && (!_context.IsDisposed))
            {
                _context.SetStatus(Title + ": " + reason);
            }
        }

        /// <summary> 모의 요청을 보내고 결과를 마지막 응답 줄에 남긴다. Detach 뒤에 도착한 응답은 버린다 </summary>
        protected bool RunRequest(UiSandboxContext context, string operation, Action onSuccess, Action<string> onFailure = null)
        {
            int generation = _generation;

            bool started = context.Request(operation,
                () =>
                {
                    if (generation != _generation)
                    {
                        return;
                    }

                    _lastResponse = operation + ": 성공 (모의 응답)";
                    if (onSuccess != null)
                    {
                        onSuccess();
                    }
                },
                message =>
                {
                    if (generation != _generation)
                    {
                        return;
                    }

                    _lastResponse = operation + ": 실패 - " + message;
                    if (onFailure != null)
                    {
                        onFailure(message);
                    }
                });

            _lastResponse = started ? (operation + ": 요청 중") : (operation + ": " + NOT_STARTED_TEXT);
            return started;
        }

        protected void MarkDirty()
        {
            _dirty = true;
        }

        /****************************************
        *            Draw Helpers
        ****************************************/

        protected static void DrawHeader(string text)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        protected int DrawInt(UiSandboxScenarioSO scenario, string key, string label, int min, int max)
        {
            int current = scenario.GetInt(key);
            int next    = Mathf.Clamp(EditorGUILayout.IntField(label, current), min, max);
            if (next != current)
            {
                scenario.SetInt(key, next);
                _dirty = true;
            }

            return next;
        }

        protected float DrawFloat(UiSandboxScenarioSO scenario, string key, string label, float min, float max)
        {
            float current = scenario.GetFloat(key);
            float next    = Mathf.Clamp(EditorGUILayout.FloatField(label, current), min, max);
            if (!Mathf.Approximately(next, current))
            {
                scenario.SetFloat(key, next);
                _dirty = true;
            }

            return next;
        }

        protected bool DrawBool(UiSandboxScenarioSO scenario, string key, string label)
        {
            bool current = scenario.GetBool(key);
            bool next    = EditorGUILayout.Toggle(label, current);
            if (next != current)
            {
                scenario.SetBool(key, next);
                _dirty = true;
            }

            return next;
        }

        protected string DrawString(UiSandboxScenarioSO scenario, string key, string label)
        {
            string current = scenario.GetString(key);
            string next    = EditorGUILayout.TextField(label, current);
            if (!string.Equals(next, current, StringComparison.Ordinal))
            {
                scenario.SetString(key, next);
                _dirty = true;
            }

            return next;
        }

        /// <summary> 열거형은 정수로 저장한다. 정의되지 않은 값도 그대로 시험할 수 있게 값을 막지 않는다 </summary>
        protected TEnum DrawEnum<TEnum>(UiSandboxScenarioSO scenario, string key, string label, TEnum fallback)
            where TEnum : struct, Enum
        {
            int current = scenario.GetInt(key, Convert.ToInt32(fallback));
            Enum selected = EditorGUILayout.EnumPopup(label, (Enum)Enum.ToObject(typeof(TEnum), current));
            int next = Convert.ToInt32(selected);
            if (next != current)
            {
                scenario.SetInt(key, next);
                _dirty = true;
            }

            return (TEnum)Enum.ToObject(typeof(TEnum), next);
        }

        /// <summary> 씬의 View 필드가 연결되지 않은 경우 창 전체가 멈추지 않도록 예외를 상태 줄에 남긴다 </summary>
        private void ApplyGuarded(UiSandboxContext context)
        {
            try
            {
                OnApply(context);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                context.SetStatus(Title + " 적용 중 예외: " + exception.GetType().Name + " (View 필드 연결을 확인한다)");
            }
        }

        private void DrawProvenance(UiSandboxContext context)
        {
            if (UsesFixture)
            {
                EditorGUILayout.HelpBox(FIXTURE_HELP, MessageType.Warning);

                StringBuilder labels = new StringBuilder();
                List<string> unwired = new List<string>();
                for (int i = 0; i < _fixtures.Count; ++i)
                {
                    if (i > 0)
                    {
                        labels.Append(", ");
                    }

                    labels.Append(_fixtures[i].Label);
                    unwired.AddRange(_fixtures[i].UnwiredFields);
                }

                EditorGUILayout.LabelField("시험 배치", labels.ToString());
                if (unwired.Count > 0)
                {
                    EditorGUILayout.HelpBox(UNWIRED_HELP + string.Join(", ", unwired), MessageType.None);
                }
            }
            else if (HasView)
            {
                EditorGUILayout.HelpBox(SCENE_HELP, MessageType.Info);
            }

            if (!context.IsPlaying)
            {
                EditorGUILayout.HelpBox(PLAY_REQUIRED, MessageType.None);
            }
        }
    }
}
