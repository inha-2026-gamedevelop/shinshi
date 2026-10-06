// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> 로비 미리보기 전용 P09 조립 표현, 서버 판정이나 전투 상태를 갖지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleLookPreviewView : ClientView
    {
        // 클래스 이름과 GUID는 생성, 선택 씬 참조를 지키려고 그대로 둔다. 생성 화면은 값을 자주 바꾸므로 이미 만든 인스턴스에
        // 적용기를 다시 돌려 바꾸고, 적용이 실패했을 때만 인스턴스를 버리고 다음 요청에서 전체를 다시 조립한다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _faceEmotionHash = Animator.StringToHash("FaceEmotion");

        [SerializeField] private PlayerCharacterLookCatalogSO _catalog;
        [SerializeField] private Transform                    _root;

        private GameObject       _current;
        private Animator         _animator;
        private bool             _hasLook;
        private CharacterP09Look _look;
        private int              _faceEmotion;
        private bool             _isReady;
        private string           _lastError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool             IsReady   => _isReady;
        public string           LastError => _lastError;
        public CharacterP09Look Look      => _look;

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

        /// <summary> 같은 외형을 이미 보여 주고 있으면 아무것도 하지 않는다. 실패한 시도는 캐시하지 않아 같은 값으로 다시 부르면 재시도한다 </summary>
        public void Show(CharacterP09Look look)
        {
            if ((_isReady) && (_hasLook) && (_look.Equals(look)))
            {
                return;
            }

            _hasLook = true;
            _look    = look;

            if ((_current != null) && (_isReady)
                && (PlayerCharacterLookApplier.TryApply(_current, _catalog, look, out string error)))
            {
                _lastError = string.Empty;
                return;
            }

            Rebuild();
        }

        /// <summary> 생성 화면 표정 미리보기, 0은 무표정이다. 저장하지 않는다 </summary>
        public void SetFaceEmotion(int emotion)
        {
            _faceEmotion = emotion;
            if ((_animator != null) && (_animator.isActiveAndEnabled))
            {
                _animator.SetInteger(_faceEmotionHash, emotion);
            }
        }

        private void Rebuild()
        {
            if (_current != null)
            {
                Destroy(_current);
                _current  = null;
                _animator = null;
            }

            SetReady(false);
            if ((IsServerBuild) || (_catalog == null) || (_root == null))
            {
                _lastError = "미리보기 참조가 연결되지 않았다";
                return;
            }

            if (!PlayerCharacterLookAssembler.TryAssemble(_catalog, _look, _root, out GameObject result, out string error))
            {
                _lastError = error;
                return;
            }

            _current   = result;
            _animator  = result.GetComponent<Animator>();
            _lastError = string.Empty;
            SetFaceEmotion(_faceEmotion);
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
