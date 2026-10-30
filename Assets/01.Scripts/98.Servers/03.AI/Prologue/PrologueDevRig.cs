#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.AI
{
    /// <summary> Dev_Prologue 씬을 단독으로 Play하면 서버 없이 가짜 세션으로 프롤로그 전체를 끝까지 돌려 보는 시험 장치 </summary>
    public sealed class PrologueDevRig : MonoBehaviour, IPrologueEventSource, IPrologueHost
    {
        // 실제 서버 세션 대신 이 장치가 사건 수신과 서버 지시를 모두 맡는다. 서버 판정은 없으므로 이 결과는 서버 연결의 증거가 아니다.
        // 씬 파일에는 이 컴포넌트를 직렬화하지 않고 씬 이름을 보고 스스로 만들어진다. 서버 어셈블리 타입이 씬에 남지 않게 하려는 것이다.
        // 일러스트 문구는 내용이 정해지지 않아 모두 임시 문구다. 부적 조사와 월백 등장 자막만 기획 확정 문구다

        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_NAME      = "Dev_Prologue";
        private const string RIG_OBJECT_NAME = "PrologueDevRig";
        private const string OS_FONT_NAME    = "Malgun Gothic";
        private const int    GUI_FONT_SIZE   = 16;
        private const float  PANEL_X         = 16f;
        private const float  PANEL_Y         = 16f;
        private const float  PANEL_WIDTH     = 340f;
        private const int    LOG_LIMIT       = 14;

        private static readonly IllustrationPage[] INTRO_PAGES =
        {
            new IllustrationPage(null, "[임시] 도입 1장 문구"),
            new IllustrationPage(null, "[임시] 도입 2장 문구"),
            new IllustrationPage(null, "[임시] 도입 3장 문구"),
            new IllustrationPage(null, "[임시] 도입 4장 문구")
        };

        private static readonly IllustrationPage[] TALISMAN_PAGES =
        {
            new IllustrationPage(null, "신시의 부적과 닮은 문양이다.")
        };

        private static readonly IllustrationPage[] APPEAR_PAGES =
        {
            new IllustrationPage(null, "그 부적...... 아직도 인간에게 전해지고 있었구나.")
        };

        private static readonly IllustrationPage[] ENDING_PAGES =
        {
            new IllustrationPage(null, "[임시] 마무리 1장 문구"),
            new IllustrationPage(null, "[임시] 마무리 2장 문구")
        };

        private enum EViewPurpose
        {
            None,
            Intro,
            Talisman,
            Appearance,
            Ending
        }

        private readonly List<string> _log = new List<string>();

        private PrologueDirector _director        = null;
        private IllustrationView _view            = null;
        private GUIStyle         _labelStyle      = null;
        private GUIStyle         _buttonStyle     = null;
        private EViewPurpose     _purpose         = EViewPurpose.None;
        private bool             _talismanChecked = false;
        private int              _potionStock     = 0;
        private int              _lastSpawn       = 0;

        /****************************************
        *              Events
        ****************************************/

        public event Action      AttackUsed;
        public event Action      SkillQUsed;
        public event Action<int> EnemyKilled;
        public event Action      TelegraphStarted;
        public event Action      TelegraphEnded;
        public event Action      PlayerDown;
        public event Action      PotionUsed;
        public event Action      TalismanConfirmed;
        public event Action      WolbaekControlStarted;
        public event Action      IllustrationFinished;

        /****************************************
        *              Unity Event
        ****************************************/

        // 씬이 열린 직후 한 번 본다. 개발 씬이 아니면 아무것도 하지 않는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (SceneManager.GetActiveScene().name != SCENE_NAME)
            {
                return;
            }

            new GameObject(RIG_OBJECT_NAME).AddComponent<PrologueDevRig>();
        }

        private void Start()
        {
            _view = IllustrationView.Create();
            PrologueDevFont.Apply(_view);
            _view.Finished += OnViewFinished;

            Restart(false);
        }

        private void Update()
        {
            if (_director == null)
            {
                return;
            }

            _director.Tick();

            // 숲에서 부적을 아직 못 봤다면 조사 화면을 띄운다
            if ((_director.BlocksInput) && (!_view.IsShowing))
            {
                ShowPages(EViewPurpose.Talisman, TALISMAN_PAGES, true, false);
            }
        }

        private void OnDestroy()
        {
            if (_director != null)
            {
                _director.Dispose();
            }

            if (_view != null)
            {
                _view.Finished -= OnViewFinished;
            }
        }

        private void OnGUI()
        {
            if (_director == null)
            {
                return;
            }

            EnsureStyles();

            GUILayout.BeginArea(new Rect(PANEL_X, PANEL_Y, PANEL_WIDTH, Screen.height - (PANEL_Y * 2f)), GUI.skin.box);
            GUILayout.Label($"단계: {_director.Stage}   물약: {_director.PotionCount}", _labelStyle);
            GUILayout.Label($"안내: {_director.CurrentHint}   입력 차단: {_director.BlocksInput}", _labelStyle);

            if (Button("기본 공격 사용")) { AttackUsed?.Invoke(); }
            if (Button("Q 사용"))         { SkillQUsed?.Invoke(); }
            if (Button("현재 적 처치"))   { EnemyKilled?.Invoke(_lastSpawn); }
            if (Button("예고 시작"))      { TelegraphStarted?.Invoke(); }
            if (Button("예고 끝"))        { TelegraphEnded?.Invoke(); }
            if (Button("물약 사용"))      { PotionUsed?.Invoke(); }
            if (Button("체력 0"))         { PlayerDown?.Invoke(); }
            if (Button("처음부터 (부적 미확인)")) { Restart(false); }
            if (Button("처음부터 (부적 확인함)")) { Restart(true); }

            GUILayout.Space(8f);
            for (int i = 0; i < _log.Count; ++i)
            {
                GUILayout.Label(_log[i], _labelStyle);
            }

            GUILayout.EndArea();
        }

        /****************************************
        *               Host
        ****************************************/

        public void SpawnEnemy(EPrologueEnemyKind kind, int spawnIndex)
        {
            _lastSpawn = spawnIndex;
            Log($"소환 지시: {kind} #{spawnIndex}");
        }

        public void SetPotions(int count)
        {
            _potionStock = count;
            Log($"물약 재고: {count}");
        }

        public void ReviveToFull(float invulnerableSeconds)
        {
            Log($"체력 회복 + 무적 {invulnerableSeconds}초");
        }

        public void EndEngagement(EWolbaekEnd reason)
        {
            Log($"월백 교전 종료: {reason}");
        }

        public void ReportStage(EPrologueStage stage, int wolbaekEntryPotions)
        {
            Log($"단계 저장: {stage} (월백 진입 물약 {wolbaekEntryPotions})");

            switch (stage)
            {
                case EPrologueStage.Wolbaek:
                    ShowPages(EViewPurpose.Appearance, APPEAR_PAGES, false, true);
                    break;
                case EPrologueStage.Ending:
                    ShowPages(EViewPurpose.Ending, ENDING_PAGES, false, true);
                    break;
                default:
                    break;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 처음부터 다시 시작한다. 진행기는 부적 확인 여부를 만들 때 받으므로 새로 만든다
        private void Restart(bool talismanChecked)
        {
            if (_director != null)
            {
                _director.Dispose();
            }

            _view.Close();
            _log.Clear();
            _talismanChecked = talismanChecked;
            _purpose         = EViewPurpose.None;

            _director = new PrologueDirector(this, this, () => Time.timeAsDouble, _talismanChecked);
            _director.Start(EPrologueStage.Intro, 0);
            ShowPages(EViewPurpose.Intro, INTRO_PAGES, false, true);
        }

        private void ShowPages(EViewPurpose purpose, IReadOnlyList<IllustrationPage> pages, bool startRevealed, bool allowSkip)
        {
            _purpose = purpose;
            _view.Show(pages, startRevealed, allowSkip);
        }

        // 화면을 내린 뒤 목적에 맞는 사건을 일으킨다. 월백 등장은 건너뛰어도 교전은 이어지므로 같은 사건이다
        private void OnViewFinished()
        {
            EViewPurpose finished = _purpose;
            _purpose = EViewPurpose.None;
            _view.Close();

            switch (finished)
            {
                case EViewPurpose.Intro:
                case EViewPurpose.Ending:
                    IllustrationFinished?.Invoke();
                    break;
                case EViewPurpose.Talisman:
                    TalismanConfirmed?.Invoke();
                    break;
                case EViewPurpose.Appearance:
                    WolbaekControlStarted?.Invoke();
                    break;
                default:
                    break;
            }
        }

        private void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > LOG_LIMIT)
            {
                _log.RemoveAt(0);
            }
        }

        private bool Button(string label)
        {
            return GUILayout.Button(label, _buttonStyle);
        }

        // IMGUI 기본 글꼴은 한글이 깨져서 OS 한글 글꼴을 쓴다
        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            Font font = Font.CreateDynamicFontFromOSFont(OS_FONT_NAME, GUI_FONT_SIZE);

            _labelStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = GUI_FONT_SIZE, wordWrap = true };
            _labelStyle.normal.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = GUI_FONT_SIZE };
        }
    }
}
#endif
