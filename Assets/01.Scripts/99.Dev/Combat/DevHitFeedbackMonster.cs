// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 정해진 횟수만 맞으면 죽는 제자리 시험용 몬스터. 맞을 때마다 색이 번쩍이고 죽으면 줄어들며 사라진다 </summary>
    public sealed class DevHitFeedbackMonster : MonoBehaviour
    {
        // 판정, 이동, AI는 없다. 피격 연출 확인용 표적이라 체력 대신 맞은 횟수로 죽는다

        /****************************************
        *                Fields
        ****************************************/

        private const string COLOR_PROPERTY = "_BaseColor";

        private const float FLASH_SECONDS = 0.12f;
        private const float DEATH_SECONDS = 0.25f;

        private static readonly Color s_normalColor = new Color(0.55f, 0.2f, 0.2f, 1f);
        private static readonly Color s_flashColor  = Color.white;

        private Renderer             _renderer      = null;
        private MaterialPropertyBlock _block        = null;
        private Vector3              _baseScale     = Vector3.one;
        private int                  _hitsLeft      = 0;
        private float                _flashEndTime  = 0f;
        private float                _deathEndTime  = 0f;
        private bool                 _isDying       = false;

        /// <summary> 죽는 연출이 끝나 사라지기 직전에 한 번 알린다 </summary>
        public event Action<DevHitFeedbackMonster> Died;

        /// <summary> 죽는 연출 중이거나 이미 죽었는지 </summary>
        public bool IsDead => _isDying;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (_isDying)
            {
                float remain = Mathf.Max(0f, _deathEndTime - Time.time) / DEATH_SECONDS;
                transform.localScale = _baseScale * remain;

                if (remain <= 0f)
                {
                    Died?.Invoke(this);
                    Destroy(gameObject);
                }

                return;
            }

            if ((_flashEndTime > 0f) && (Time.time >= _flashEndTime))
            {
                _flashEndTime = 0f;
                ApplyColor(s_normalColor);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 몬스터 렌더러를 잡고 죽기까지 맞아야 하는 횟수를 정한다 </summary>
        public void Init(int hitsToDie)
        {
            _renderer  = GetComponentInChildren<Renderer>();
            _block     = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
            _hitsLeft  = hitsToDie;
            ApplyColor(s_normalColor);
        }

        /// <summary> 한 대 맞는다. 맞은 횟수가 다 차면 죽는 연출을 시작한다 </summary>
        public void TakeHit()
        {
            if (_isDying)
            {
                return;
            }

            --_hitsLeft;
            if (_hitsLeft <= 0)
            {
                _isDying      = true;
                _deathEndTime = Time.time + DEATH_SECONDS;
                ApplyColor(s_flashColor);
                return;
            }

            _flashEndTime = Time.time + FLASH_SECONDS;
            ApplyColor(s_flashColor);
        }

        private void ApplyColor(Color color)
        {
            _block.SetColor(COLOR_PROPERTY, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
