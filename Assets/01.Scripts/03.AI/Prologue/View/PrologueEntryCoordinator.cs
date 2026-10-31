// System
using System;

// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.AI
{
    /// <summary> 캐릭터 생성 직후 프롤로그를 어떻게 시작할지 정하고 선택창을 띄우는 로비용 도우미, 로비 쪽은 Begin 한 줄만 부르면 된다 </summary>
    public sealed class PrologueEntryCoordinator : IDisposable
    {
        // 계정에서 처음이면 선택창 없이 바로 프롤로그를 시작한다(이야기 일러스트만 건너뛸 수 있다).
        // 이미 한 번 마쳤다면 진행/건너뛰기 선택창을 띄우고 고른 쪽 콜백만 부른다. 완료 기록과 기본 지급품은 건너뛰기 콜백 쪽(서버)이 맡는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly PrologueChoiceView _view;

        private Action _startPrologue = null;
        private Action _skipToTown    = null;
        private bool   _disposed      = false;

        /****************************************
        *                Methods
        ****************************************/

        private PrologueEntryCoordinator(PrologueChoiceView view)
        {
            _view         = view;
            _view.Chosen += OnChosen;
        }

        /// <summary> 선택창을 만들어 둔다. font는 프롤로그 카탈로그의 글꼴을 넘기면 된다 </summary>
        public static PrologueEntryCoordinator Create(TMP_FontAsset font)
        {
            PrologueChoiceView view = PrologueChoiceView.Create();
            view.SetFont(font);
            return new PrologueEntryCoordinator(view);
        }

        /// <summary> 카탈로그의 글꼴로 선택창을 만들어 둔다 </summary>
        public static PrologueEntryCoordinator CreateFromCatalog(PrologueIllustrationCatalogSO catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            return Create(catalog.Font);
        }

        /// <summary> 계정 완료 여부로 진입을 정한다. 최초면 선택창 없이 startPrologue를 바로 부르고, 아니면 선택창을 띄운다 </summary>
        public void Begin(bool accountCompleted, Action startPrologue, Action skipToTown)
        {
            if (startPrologue == null)
            {
                throw new ArgumentNullException(nameof(startPrologue));
            }

            if (skipToTown == null)
            {
                throw new ArgumentNullException(nameof(skipToTown));
            }

            if (PrologueEntryRules.Decide(accountCompleted) == EPrologueEntry.PlayRequired)
            {
                startPrologue();
                return;
            }

            _startPrologue = startPrologue;
            _skipToTown    = skipToTown;
            _view.Show();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed     = true;
            _view.Chosen -= OnChosen;

            if (_view != null)
            {
                UnityEngine.Object.Destroy(_view.gameObject);
            }
        }

        private void OnChosen(EPrologueChoice choice)
        {
            _view.Close();

            Action action = (choice == EPrologueChoice.Play) ? _startPrologue : _skipToTown;
            _startPrologue = null;
            _skipToTown    = null;
            action?.Invoke();
        }
    }
}
