// System
using System;

namespace Shinshi.AI
{
    /// <summary> 프롤로그 한 세션의 진행기, 사건이 올 때만 반응하고 월백 30초만 틱에서 확인한다. 서버 세션이 만들고 Dispose로 정리한다 </summary>
    public sealed class PrologueDirector : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IPrologueEventSource _source;
        private readonly IPrologueHost        _host;
        private readonly Func<double>         _clock;
        private readonly bool                 _talismanChecked;
        private readonly ProloguePotionLedger _potions = new ProloguePotionLedger();

        private ForestProgress    _forest           = null;
        private WolbaekEngagement _engagement       = null;
        private EPrologueStage    _stage            = EPrologueStage.Intro;
        private int               _spawnedNormal    = 0;
        private bool              _telegraphSpawned = false;
        private bool              _wolbaekOpened    = false;
        private bool              _disposed         = false;

        /****************************************
        *              Properties
        ****************************************/

        public EPrologueStage Stage       => _stage;
        public int            PotionCount => _potions.Count;

        // 동기화 상태가 읽어 가는 값, 숲이 아니면 아무것도 보여 주지 않는다
        public EForestHint    CurrentHint => (_forest != null) ? _forest.CurrentHint : EForestHint.None;
        public bool           BlocksInput => (_forest != null) && (_forest.BlocksInput);

        // 월백이 열려 교전 중인 동안, 화면 위쪽 목표 문구를 띄우는 기준이다
        public bool           IsWolbaekOpen => (_stage == EPrologueStage.Wolbaek) && (_wolbaekOpened);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> clock은 서버 시각을 돌려준다, talismanChecked는 이 캐릭터가 부적 화면을 이미 본 적이 있는지다 </summary>
        public PrologueDirector(IPrologueEventSource source, IPrologueHost host, Func<double> clock, bool talismanChecked)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _source          = source;
            _host            = host;
            _clock           = clock;
            _talismanChecked = talismanChecked;

            _source.AttackUsed            += OnAttackUsed;
            _source.SkillQUsed            += OnSkillQUsed;
            _source.EnemyKilled           += OnEnemyKilled;
            _source.TelegraphStarted      += OnTelegraphStarted;
            _source.TelegraphEnded        += OnTelegraphEnded;
            _source.PlayerDown            += OnPlayerDown;
            _source.PotionUsed            += OnPotionUsed;
            _source.TalismanApproached    += OnTalismanApproached;
            _source.TalismanConfirmed     += OnTalismanConfirmed;
            _source.WolbaekIntroFinished  += OnWolbaekIntroFinished;
            _source.WolbaekControlStarted += OnWolbaekControlStarted;
            _source.IllustrationFinished  += OnIllustrationFinished;
        }

        /// <summary> 저장된 단계에서 그 구간의 처음부터 시작한다, 일러스트 구간은 클라이언트가 이끌어서 할 일이 없다 </summary>
        public void Start(EPrologueStage stage, int storedWolbaekPotions)
        {
            _stage = stage;

            switch (stage)
            {
                case EPrologueStage.Forest:
                    BeginForest();
                    break;
                case EPrologueStage.Wolbaek:
                    _potions.ResumeWolbaek(storedWolbaekPotions);
                    _engagement    = new WolbaekEngagement();
                    _wolbaekOpened = false;
                    _host.SetPotions(_potions.Count);
                    break;
                default:
                    break;
            }
        }

        /// <summary> 체력이 줄었고 임시 물약이 남았을 때만 참이다, 물약 사용 안내를 띄울 기준이다 </summary>
        public bool ShouldHintPotion(bool isDamaged)
        {
            return (_stage == EPrologueStage.Forest) && (_potions.ShouldHintUse(isDamaged));
        }

        /// <summary> 서버 틱마다 부른다 </summary>
        public void Tick()
        {
            if ((_stage != EPrologueStage.Wolbaek) || (_engagement == null))
            {
                return;
            }

            EWolbaekEnd end = _engagement.Update(_clock());
            if (end != EWolbaekEnd.None)
            {
                FinishWolbaek(end);
            }
        }

        /// <summary> 신시에 도착해 프롤로그가 끝났다, 남은 임시 물약을 제거한다 </summary>
        public void Complete()
        {
            if (_stage == EPrologueStage.Done)
            {
                return;
            }

            _stage = EPrologueStage.Done;
            _potions.Clear();
            _host.SetPotions(0);
            _host.ReportStage(EPrologueStage.Done, 0);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _source.AttackUsed            -= OnAttackUsed;
            _source.SkillQUsed            -= OnSkillQUsed;
            _source.EnemyKilled           -= OnEnemyKilled;
            _source.TelegraphStarted      -= OnTelegraphStarted;
            _source.TelegraphEnded        -= OnTelegraphEnded;
            _source.PlayerDown            -= OnPlayerDown;
            _source.PotionUsed            -= OnPotionUsed;
            _source.TalismanApproached    -= OnTalismanApproached;
            _source.TalismanConfirmed     -= OnTalismanConfirmed;
            _source.WolbaekIntroFinished  -= OnWolbaekIntroFinished;
            _source.WolbaekControlStarted -= OnWolbaekControlStarted;
            _source.IllustrationFinished  -= OnIllustrationFinished;
        }

        private void BeginForest()
        {
            _forest           = new ForestProgress(_talismanChecked);
            _spawnedNormal    = 0;
            _telegraphSpawned = false;

            _potions.StartForest();
            _host.SetPotions(_potions.Count);
            AdvanceForest();
        }

        // 사건마다 한 번씩 부른다. 다음 산귀 소환과 숲 종료를 여기서만 결정한다
        private void AdvanceForest()
        {
            if (_forest == null)
            {
                return;
            }

            if ((_forest.Phase == EForestPhase.FirstFight) && (_spawnedNormal == _forest.FirstFightKills)
                && (_spawnedNormal < ForestProgress.FIRST_FIGHT_ENEMY_COUNT))
            {
                _host.SpawnEnemy(EPrologueEnemyKind.Normal, _spawnedNormal);
                ++_spawnedNormal;
                return;
            }

            if ((_forest.Phase == EForestPhase.SecondFight) && (!_telegraphSpawned))
            {
                _telegraphSpawned = true;
                _host.SpawnEnemy(EPrologueEnemyKind.Telegraph, 0);
                return;
            }

            if (_forest.IsDone)
            {
                FinishForest();
            }
        }

        private void FinishForest()
        {
            _forest        = null;
            _stage         = EPrologueStage.Wolbaek;
            _engagement    = new WolbaekEngagement();
            _wolbaekOpened = false;

            _potions.EnterWolbaek();
            _host.ReportStage(EPrologueStage.Wolbaek, _potions.WolbaekEntryCount);
        }

        private void FinishWolbaek(EWolbaekEnd reason)
        {
            _engagement = null;
            _stage      = EPrologueStage.Ending;

            _host.EndEngagement(reason);
            _host.ReportStage(EPrologueStage.Ending, _potions.WolbaekEntryCount);
        }

        private void OnAttackUsed()
        {
            if (_forest == null)
            {
                return;
            }

            _forest.OnAttackUsed();
            AdvanceForest();
        }

        private void OnSkillQUsed()
        {
            if (_forest == null)
            {
                return;
            }

            _forest.OnSkillQUsed();
            AdvanceForest();
        }

        private void OnEnemyKilled(int spawnIndex)
        {
            if (_forest == null)
            {
                return;
            }

            _forest.OnEnemyKilled();
            AdvanceForest();
        }

        private void OnTelegraphStarted()
        {
            _forest?.OnTelegraphStarted();
        }

        private void OnTelegraphEnded()
        {
            _forest?.OnTelegraphEnded();
        }

        private void OnTalismanApproached()
        {
            _forest?.OnTalismanApproached();
        }

        private void OnTalismanConfirmed()
        {
            if (_forest == null)
            {
                return;
            }

            _forest.OnTalismanConfirmed();
            AdvanceForest();
        }

        private void OnPotionUsed()
        {
            _potions.TryUse();
        }

        private void OnPlayerDown()
        {
            if (PrologueStageRules.RevivesOnDown(_stage))
            {
                _host.ReviveToFull(PrologueStageRules.REVIVE_INVULNERABLE_SECONDS);
                return;
            }

            if ((_stage != EPrologueStage.Wolbaek) || (_engagement == null))
            {
                return;
            }

            EWolbaekEnd end = _engagement.ReportPlayerDown(_clock());
            if (end != EWolbaekEnd.None)
            {
                FinishWolbaek(end);
            }
        }

        // 등장 일러스트가 끝나야 월백을 연다. 같은 구간에서 두 번 열지 않는다
        private void OnWolbaekIntroFinished()
        {
            if ((_stage != EPrologueStage.Wolbaek) || (_engagement == null) || (_wolbaekOpened))
            {
                return;
            }

            _wolbaekOpened = true;
            _host.StartWolbaek();
        }

        // 월백이 열리기 전의 조작 시작 알림은 30초를 시작시키지 않는다
        private void OnWolbaekControlStarted()
        {
            if ((_stage != EPrologueStage.Wolbaek) || (_engagement == null) || (!_wolbaekOpened))
            {
                return;
            }

            _engagement.Begin(_clock());
        }

        private void OnIllustrationFinished()
        {
            if (_stage == EPrologueStage.Intro)
            {
                _stage = EPrologueStage.Forest;
                _host.ReportStage(EPrologueStage.Forest, 0);
                BeginForest();
                return;
            }

            if (_stage == EPrologueStage.Ending)
            {
                Complete();
            }
        }
    }
}
