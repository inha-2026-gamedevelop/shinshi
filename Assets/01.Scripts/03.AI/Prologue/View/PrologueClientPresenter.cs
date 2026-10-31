// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 서버에서 받은 프롤로그 상태에 맞춰 일러스트, 안내 문구, 알림을 띄우고 내리는 클라이언트 연결부, 빌드에도 들어간다 </summary>
    public sealed class PrologueClientPresenter : ClientView
    {
        // 상태는 서버가 정하고 이쪽은 보여 주기만 한다. 화면이 끝났다는 것만 서버에 알린다(조사 화면 닫음, 일러스트 구간 종료, 월백 등장 종료).
        // 한 단계에서 일러스트를 두 번 띄우지 않으려고 단계가 바뀔 때만 다시 띄울 수 있게 한다. 서버가 단계를 바꾸기 전에
        // 클라이언트가 먼저 끝났다고 알려도, 다음 상태가 올 때까지 같은 일러스트를 다시 띄우지 않는다.
        // 연결하는 쪽은 Create 한 번만 부르면 된다. 서버 빌드에서는 아무것도 만들지 않는다

        /****************************************
        *                Types
        ****************************************/

        private enum EPurpose
        {
            None,
            Intro,
            Talisman,
            Appearance,
            Ending
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float  RESUME_NOTICE_SECONDS = 5f;
        private const float  MOVING_NOTICE_SECONDS = 5f;
        private const string ROOT_OBJECT_NAME      = "PrologueClientPresenter";

        private IPrologueClientLink           _link;
        private IPrologueLocalVitals          _vitals;
        private PrologueIllustrationCatalogSO _catalog;
        private IllustrationView              _illustration;
        private PrologueHintView              _hint;

        private EPurpose       _purpose          = EPurpose.None;
        private EPrologueStage _lastStage        = EPrologueStage.Intro;
        private bool           _hasLastStage     = false;
        private bool           _started          = false;
        private bool           _stageActionDone  = false;
        private bool           _talismanShown    = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShowingIllustration => (_illustration != null) && (_illustration.IsShowing);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 연결부를 만든다. vitals가 없으면 물약 안내는 띄우지 않는다. 서버 빌드에서는 만들기만 하고 아무것도 그리지 않는다 </summary>
        public static PrologueClientPresenter Create(IPrologueClientLink link, IPrologueLocalVitals vitals,
            PrologueIllustrationCatalogSO catalog)
        {
            if (link == null)
            {
                throw new ArgumentNullException(nameof(link));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var root      = new GameObject(ROOT_OBJECT_NAME);
            var presenter = root.AddComponent<PrologueClientPresenter>();
            presenter.Bind(link, vitals, catalog);
            return presenter;
        }

        /// <summary> 마을 도착과 완료 기록 확인 뒤에 마을 쪽에서 부른다. 이 연결부가 없는 씬에서도 쓸 수 있게 따로 두지 않고 도우미를 쓴다 </summary>
        public void ShowCompletedNotice()
        {
            if (_hint != null)
            {
                _hint.ShowNotice(PrologueHintTexts.PROLOGUE_COMPLETED, PrologueTownNotice.SECONDS);
            }
        }

        /// <summary> 일러스트 자막을 흰 글자·검은 테두리로 바꾼다, 시험 장치만 부른다 </summary>
        public void UseOutlinedSubtitle()
        {
            if (_illustration != null)
            {
                _illustration.UseOutlinedSubtitle();
            }
        }

        private void Bind(IPrologueClientLink link, IPrologueLocalVitals vitals, PrologueIllustrationCatalogSO catalog)
        {
            // 서버 빌드이면 Awake에서 이미 꺼졌다. 화면을 만들지 않는다
            if (!enabled)
            {
                return;
            }

            _link    = link;
            _vitals  = vitals;
            _catalog = catalog;

            _illustration = IllustrationView.Create();
            _illustration.transform.SetParent(transform, false);
            _illustration.SetFont(catalog.Font);
            _illustration.SetTransitionMaterial(catalog.InkWipeMaterial);
            _illustration.Finished += OnIllustrationFinished;

            _hint = PrologueHintView.Create();
            _hint.transform.SetParent(transform, false);
            _hint.SetFont(catalog.Font);

            _link.Revived += OnRevived;
        }

        private void Update()
        {
            if (_link == null)
            {
                return;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (_link != null)
            {
                _link.Revived -= OnRevived;
            }

            if (_illustration != null)
            {
                _illustration.Finished -= OnIllustrationFinished;
            }
        }

        private void Refresh()
        {
            EPrologueStage stage = _link.Stage;

            // 처음 받은 상태가 숲이나 월백이면 중단했던 구간의 재개다. 도입이나 마무리는 새로 시작한 것과 구분되지 않아 알리지 않는다
            if (!_started)
            {
                _started = true;
                if ((stage == EPrologueStage.Forest) || (stage == EPrologueStage.Wolbaek))
                {
                    _hint.ShowNotice(PrologueHintTexts.ResumeNotice(stage), RESUME_NOTICE_SECONDS);
                }
            }

            if ((!_hasLastStage) || (stage != _lastStage))
            {
                _hasLastStage    = true;
                _lastStage       = stage;
                _stageActionDone = false;
                _talismanShown   = false;
            }

            ShowIllustrationIfNeeded(stage);
            RefreshGuide(stage);
        }

        private void ShowIllustrationIfNeeded(EPrologueStage stage)
        {
            bool showing = _illustration.IsShowing;

            switch (stage)
            {
                case EPrologueStage.Intro:
                    if ((!_stageActionDone) && (!showing))
                    {
                        Begin(EPurpose.Intro, _catalog.IntroPages, false, true, PrologueHintTexts.SKIP_INTRO_LABEL);
                    }

                    break;
                case EPrologueStage.Forest:
                    // 조사 화면은 서버가 부적에 닿았다고 알린 동안만 한 번 띄운다. 닫으면 서버가 이 값을 거둔다
                    if (_link.IsTalismanOpen)
                    {
                        if ((!_talismanShown) && (!showing))
                        {
                            _talismanShown = true;
                            Begin(EPurpose.Talisman, _catalog.TalismanPages, true, false, null);
                        }
                    }
                    else
                    {
                        _talismanShown = false;
                    }

                    break;
                case EPrologueStage.Wolbaek:
                    if ((!_stageActionDone) && (!_link.IsWolbaekOpen) && (!showing))
                    {
                        Begin(EPurpose.Appearance, _catalog.AppearancePages, false, true, PrologueHintTexts.SKIP_APPEARANCE_LABEL);
                    }

                    break;
                case EPrologueStage.Ending:
                    if ((!_stageActionDone) && (!showing))
                    {
                        Begin(EPurpose.Ending, _catalog.EndingPages, false, true, PrologueHintTexts.SKIP_ENDING_LABEL);
                    }

                    break;
                default:
                    break;
            }
        }

        private void Begin(EPurpose purpose, IllustrationPage[] pages, bool startRevealed, bool allowSkip, string skipLabel)
        {
            _purpose         = purpose;
            _stageActionDone = true;
            _illustration.Show(pages, startRevealed, allowSkip, skipLabel);
        }

        // 일러스트가 떠 있는 동안은 안내를 숨긴다. 월백 안내와 숲 안내는 상태에 맞춰 규칙이 정한다
        private void RefreshGuide(EPrologueStage stage)
        {
            if (_illustration.IsShowing)
            {
                _hint.SetObjective(string.Empty);
                _hint.SetHint(string.Empty);
                return;
            }

            bool damaged     = (_vitals != null) && (_vitals.IsDamaged);
            bool potionHint  = PrologueGuideRules.PotionHintActive(stage, _link.PotionCount, damaged);
            PrologueGuideLines lines = PrologueGuideRules.Resolve(stage, _link.Hint, _link.IsWolbaekOpen, potionHint);

            _hint.SetObjective(lines.Objective);
            _hint.SetHint(lines.Instruction);
        }

        private void OnIllustrationFinished()
        {
            EPurpose finished = _purpose;
            _purpose = EPurpose.None;
            _illustration.Close();

            switch (finished)
            {
                case EPurpose.Intro:
                    _link.SendIllustrationFinished();
                    break;
                case EPurpose.Talisman:
                    _link.SendTalismanConfirmed();
                    break;
                case EPurpose.Appearance:
                    _link.SendWolbaekIntroFinished();
                    break;
                case EPurpose.Ending:
                    _hint.ShowNotice(PrologueHintTexts.MOVING_TO_TOWN, MOVING_NOTICE_SECONDS);
                    _link.SendIllustrationFinished();
                    break;
                default:
                    break;
            }
        }

        // 숲의 회복만 알린다. 월백 교전에서는 회복 처리가 없다
        private void OnRevived()
        {
            if (_link.Stage != EPrologueStage.Forest)
            {
                return;
            }

            _hint.ShowNotice(PrologueHintTexts.REVIVE_NOTICE, PrologueStageRules.REVIVE_INVULNERABLE_SECONDS);
        }
    }
}
