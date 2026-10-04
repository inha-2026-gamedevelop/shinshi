// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> 로비 미리보기 전용 조립 표현, 서버 판정이나 전투 상태를 갖지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleLookPreviewView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerFemaleLookCatalogSO  _catalog;
        [SerializeField] private RuntimeAnimatorController  _controller;
        [SerializeField] private Transform                  _root;

        private GameObject    _current;
        private bool          _hasLook;
        private CharacterLook _look;
        private bool          _isReady;
        private string        _lastError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool   IsReady   => _isReady;
        public string LastError => _lastError;

        public event Action<bool> ReadyChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            if (_current != null)
            {
                Destroy(_current);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Configure(PlayerFemaleLookCatalogSO catalog, RuntimeAnimatorController controller, Transform root)
        {
            _catalog    = catalog;
            _controller = controller;
            _root       = root;

            // 참조가 바뀌었으니 같은 look이라도 다음 Show는 다시 조립한다
            _hasLook = false;
        }

        /// <summary> 이전 시도가 성공한 같은 조합을 다시 보여 주면 조립을 다시 하지 않는다.
        /// 실패한 시도는 캐시하지 않으므로 같은 look으로 다시 불러도 재시도한다 </summary>
        public void Show(CharacterLook look)
        {
            if ((_isReady) && (_hasLook) && (_look.Equals(look)))
            {
                return;
            }

            _hasLook = true;
            _look    = look;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_current != null)
            {
                Destroy(_current);
                _current = null;
            }

            SetReady(false);
            if ((IsServerBuild) || (_catalog == null) || (_root == null))
            {
                _lastError = "미리보기 참조가 연결되지 않았다";
                return;
            }

            bool success = PlayerFemaleLookAssembler.TryAssemble(_catalog, _look, _root, _controller,
                out GameObject result, out string error);
            _lastError = error;
            if (!success)
            {
                return;
            }

            _current = result;
            SetReady(true);
        }

        private void SetReady(bool value)
        {
            if (_isReady == value)
            {
                return;
            }

            _isReady = value;
            ReadyChanged?.Invoke(value);
        }
    }
}
