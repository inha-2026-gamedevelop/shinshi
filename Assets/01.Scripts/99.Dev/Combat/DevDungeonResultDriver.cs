// System
using System;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary> 시험 씬에서 플레이어가 죽으면 실패, 모든 구역을 토벌하면 성공으로 던전 결과 창을 띄우는 시험용 연결부 </summary>
    public sealed class DevDungeonResultDriver : MonoBehaviour
    {
        // 실제로는 서버가 출정 종료 사유와 정산 결과를 내려 준다. 서버가 없는 시험 씬에서는 플레이어 사망과 스포너의 구역 처치 판정을 그 대역으로 쓴다.
        // 보상 내역은 시험 씬에 없어 비워 둔다. 귀환은 마을 접속 대신 씬을 다시 불러 처음부터 다시 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly ItemSlotData[] G_NoItems = Array.Empty<ItemSlotData>();

        [Header("참조")]
        [SerializeField] private DevHitFeedbackSpawner _spawner = null;
        [SerializeField] private DevHitFeedbackPlayer  _player  = null;
        [SerializeField] private ResultScreenView      _view    = null;
        [SerializeField] private GameObject            _window  = null; // 결과 창을 감싼 캔버스

        [Header("규칙")]
        [SerializeField] private string _regionName = "숲 고원";
        [SerializeField, Min(0f)] private float _showDelay = 1.5f; // 초, 마지막 몬스터가 쓰러지거나 내가 쓰러지는 연출을 본 뒤 띄운다

        private bool              _isEnded  = false;
        private bool              _isShown  = false;
        private float             _showTime = 0f;
        private ESessionEndReason _reason   = ESessionEndReason.Success;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_spawner == null) || (_player == null) || (_view == null) || (_window == null))
            {
                Debug.LogError("[DevResult] 스포너, 플레이어, 결과 창 중 연결되지 않은 것이 있다", this);
                enabled = false;
                return;
            }

            _player.Died        += OnPlayerDied;
            _view.ReturnClicked += OnReturnClicked;
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.Died -= OnPlayerDied;
            }

            if (_view != null)
            {
                _view.ReturnClicked -= OnReturnClicked;
            }
        }

        private void Update()
        {
            if (_isShown)
            {
                return;
            }

            if ((!_isEnded) && (AreAllZonesCleared()))
            {
                End(ESessionEndReason.Success);
            }

            if ((_isEnded) && (Time.time >= _showTime))
            {
                Show();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnPlayerDied()
        {
            End(ESessionEndReason.AllDead);
        }

        // 먼저 정해진 결과가 우선한다. 마지막 몬스터를 잡은 직후 맞아 죽어도 이미 성공이다
        private void End(ESessionEndReason reason)
        {
            if (_isEnded)
            {
                return;
            }

            _isEnded  = true;
            _reason   = reason;
            _showTime = Time.time + _showDelay;
        }

        private bool AreAllZonesCleared()
        {
            int zoneCount = _spawner.ZoneCount;
            if (zoneCount == 0)
            {
                return false;
            }

            for (int zone = 0; zone < zoneCount; ++zone)
            {
                if (!_spawner.IsZoneCleared(zone))
                {
                    return false;
                }
            }

            return true;
        }

        private void Show()
        {
            _isShown = true;
            _window.SetActive(true);

            _view.SetResult(new ResultScreenData(_reason, _regionName, 0, 0, 0, G_NoItems, G_NoItems));
            _view.SetSaveStatus(ESaveStatus.Saved);
        }

        // 시험 씬은 빌드 설정에 없어 인덱스로 부를 수 없다. 에디터에서는 경로로 플레이 모드 안에서 다시 불러온다
        private void OnReturnClicked()
        {
            Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.name);
#endif
        }
    }
}
