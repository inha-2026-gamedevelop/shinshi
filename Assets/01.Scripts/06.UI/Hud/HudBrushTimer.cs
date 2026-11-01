// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>피해 숫자와 같은 붓글씨 숫자 이미지로 mm:ss 시간을 그린다. 색은 숫자 이미지에 곱해진다</summary>
    public sealed class HudBrushTimer : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DIGITS_RESOURCE = "T_DamageBrushDigits";
        private const int    DIGIT_COLUMNS   = 5;
        private const float  CELL_WIDTH      = 0.2f;
        private const float  CELL_HEIGHT     = 0.5f;
        private const float  UPPER_ROW_Y     = 0.5f;
        private const int    SECONDS_PER_MIN = 60;
        private const int    MAX_SECONDS     = (99 * SECONDS_PER_MIN) + 59;
        private const int    DECIMAL_BASE    = 10;

        // 분 십의 자리, 분 일의 자리, 초 십의 자리, 초 일의 자리 순서다
        [SerializeField] private RawImage[] _digits = null;
        [SerializeField] private TMP_Text   _colon  = null;

        private Color _color   = Color.white;
        private int   _seconds = -1;
        private bool  _textured;

        /****************************************
        *                Methods
        ****************************************/

        // 씬에는 편집 화면용 숫자가 들어 있다. 실행 중에는 서버 시간을 받기 전까지 숨겨 엉뚱한 시간이 보이지 않게 한다
        private void Awake()
        {
            EnsureTexture();
            SetVisible(false);
        }

        /// <summary>같은 초면 다시 그리지 않는다. 범위를 넘는 값은 99:59로 자른다</summary>
        public void SetSeconds(int seconds)
        {
            int clamped = Mathf.Clamp(seconds, 0, MAX_SECONDS);

            if (clamped == _seconds)
            {
                return;
            }

            _seconds = clamped;
            EnsureTexture();
            SetVisible(true);

            int minutes = clamped / SECONDS_PER_MIN;
            int rest    = clamped % SECONDS_PER_MIN;
            SetDigit(0, minutes / DECIMAL_BASE);
            SetDigit(1, minutes % DECIMAL_BASE);
            SetDigit(2, rest / DECIMAL_BASE);
            SetDigit(3, rest % DECIMAL_BASE);
        }

        public void SetColor(Color color)
        {
            if (color == _color)
            {
                return;
            }

            _color = color;
            ApplyColor();
        }

        private void SetVisible(bool visible)
        {
            if (_digits != null)
            {
                for (int i = 0; i < _digits.Length; ++i)
                {
                    if (_digits[i] != null)
                    {
                        _digits[i].enabled = visible;
                    }
                }
            }

            if (_colon != null)
            {
                _colon.enabled = visible;
            }
        }

        private void SetDigit(int index, int numeral)
        {
            if ((_digits == null) || (index >= _digits.Length) || (_digits[index] == null))
            {
                return;
            }

            float row = (numeral < DIGIT_COLUMNS) ? UPPER_ROW_Y : 0f;
            _digits[index].uvRect = new Rect((numeral % DIGIT_COLUMNS) * CELL_WIDTH, row, CELL_WIDTH, CELL_HEIGHT);
        }

        private void EnsureTexture()
        {
            if (_textured)
            {
                return;
            }

            _textured = true;
            Texture texture = Resources.Load<Texture>(DIGITS_RESOURCE);

            if (_digits != null)
            {
                for (int i = 0; i < _digits.Length; ++i)
                {
                    if (_digits[i] != null)
                    {
                        _digits[i].texture = texture;
                    }
                }
            }

            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_digits != null)
            {
                for (int i = 0; i < _digits.Length; ++i)
                {
                    if (_digits[i] != null)
                    {
                        _digits[i].color = _color;
                    }
                }
            }

            if (_colon != null)
            {
                _colon.color = _color;
            }
        }
    }
}
