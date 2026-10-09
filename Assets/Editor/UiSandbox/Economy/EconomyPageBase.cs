// System
using System;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 경제 화면 공통 뼈대. 연결 세션을 세어 Detach 뒤에 도착한 응답을 버리고, 요청은 오직 UiSandboxContext.Request로만 보낸다 </summary>
    public abstract class EconomyPageBase : IUiSandboxPage
    {
        /****************************************
        *                Fields
        ****************************************/

        private UiSandboxContext _context;
        private int              _session;
        private string           _fixtureNote = string.Empty;

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

        public bool IsAttached
        {
            get { return _context != null; }
        }

        /// <summary> Attach와 Detach마다 올라가는 번호. 이전 세션에서 보낸 응답을 구별하는 데 쓴다 </summary>
        public int Session
        {
            get { return _session; }
        }

        /// <summary> 시험 배치를 쓰고 있으면 그 설명, 아니면 빈 문자열 </summary>
        public string FixtureNote
        {
            get { return _fixtureNote; }
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

            if (_context != null)
            {
                Detach();
            }

            _context     = context;
            _fixtureNote = string.Empty;
            ++_session;
            SeedScenario(context.Scenario);
            OnAttach(context);
        }

        public void Apply(UiSandboxContext context)
        {
            if ((context == null) || (_context == null))
            {
                return;
            }

            SeedScenario(context.Scenario);
            OnApply(context);
        }

        public void DrawControls(UiSandboxContext context)
        {
            if ((context == null) || (_context == null))
            {
                EditorGUILayout.HelpBox("연결되지 않은 화면입니다", MessageType.Info);
                return;
            }

            SeedScenario(context.Scenario);
            if (_fixtureNote.Length > 0)
            {
                EditorGUILayout.HelpBox(_fixtureNote, MessageType.Info);
            }

            if (!context.IsPlaying)
            {
                EditorGUILayout.HelpBox("요청 단추는 Play Mode에서만 쓸 수 있습니다. 편집 값은 지금도 바꿀 수 있습니다", MessageType.Warning);
            }

            OnDrawControls(context);
        }

        public void Detach()
        {
            if (_context == null)
            {
                return;
            }

            ++_session;
            _context.CancelPending("화면 전환");
            OnDetach();
            _context = null;
        }

        protected abstract void SeedScenario(UiSandboxScenarioSO scenario);

        protected abstract void OnAttach(UiSandboxContext context);

        protected abstract void OnApply(UiSandboxContext context);

        protected abstract void OnDrawControls(UiSandboxContext context);

        protected abstract void OnDetach();

        protected void SetFixtureNote(string note)
        {
            _fixtureNote = note ?? string.Empty;
        }

        /// <summary> 가짜 요청을 보낸다. 이 세션이 끝난 뒤 도착한 응답은 콜백을 부르지 않고 버린다 </summary>
        protected bool Send(string operation, Action onSuccess, Action<string> onFailure)
        {
            if (_context == null)
            {
                return false;
            }

            int session = _session;
            return _context.Request(operation,
                () =>
                {
                    if (session == _session)
                    {
                        onSuccess?.Invoke();
                    }
                },
                reason =>
                {
                    if (session == _session)
                    {
                        onFailure?.Invoke(reason);
                    }
                });
        }

        protected void ReportFailure(string operation, string reason)
        {
            if (_context != null)
            {
                _context.SetStatus($"{operation} 실패: {reason}");
            }
        }

        protected void ReportSuccess(string operation)
        {
            if (_context != null)
            {
                _context.SetStatus($"{operation} 성공");
            }
        }

        /// <summary> Play Mode가 아니거나 다른 요청이 진행 중이면 눌리지 않는 요청 단추 </summary>
        protected bool DrawRequestButton(UiSandboxContext context, string label)
        {
            bool enabled = (context.IsPlaying) && (!context.IsBusy);
            using (new EditorGUI.DisabledScope(!enabled))
            {
                return GUILayout.Button(label);
            }
        }
    }
}
