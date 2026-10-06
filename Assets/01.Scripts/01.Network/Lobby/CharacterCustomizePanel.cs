// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 생성 화면의 P09 커스터마이징 행 목록. 각 행은 이전/다음 버튼으로 값을 바꾸고, 선택 상태를 고친 뒤 Changed를 알린다 </summary>
    public sealed class CharacterCustomizePanel : MonoBehaviour
    {
        // 행 오브젝트는 에디터 도구(CharacterCustomizePanelBuilder)가 생성 씬에 만든다. 런타임에는 만들지 않는다.
        // 수염은 남성, 가슴 크기는 여성일 때만 보인다. 표정은 미리보기 전용이라 선택 상태에 넣지 않고 따로 알린다

        /****************************************
        *                Fields
        ****************************************/

        public enum ERow
        {
            Sex,
            Face,
            Hair,
            HairColor,
            EyeColor,
            Skin,
            FacialHair,
            BustSize,
            ArmorHead,
            ArmorChest,
            ArmorArm,
            ArmorWaist,
            ArmorLeg,
            Emotion
        }

        [Serializable]
        public sealed class Row
        {
            [SerializeField] private ERow       _kind     = ERow.Sex;
            [SerializeField] private GameObject _root     = null;
            [SerializeField] private TMP_Text   _value    = null;
            [SerializeField] private Button     _previous = null;
            [SerializeField] private Button     _next     = null;

            public ERow       Kind     => _kind;
            public GameObject Root     => _root;
            public TMP_Text   Value    => _value;
            public Button     Previous => _previous;
            public Button     Next     => _next;

            public Row(ERow kind, GameObject root, TMP_Text value, Button previous, Button next)
            {
                _kind     = kind;
                _root     = root;
                _value    = value;
                _previous = previous;
                _next     = next;
            }
        }

        private const int EMOTION_NONE = 0;

        private static readonly int[] G_ArmorBits = { CharacterAppearanceCatalog.P09_OUTFIT_HEAD, CharacterAppearanceCatalog.P09_OUTFIT_CHEST,
                                                      CharacterAppearanceCatalog.P09_OUTFIT_ARM, CharacterAppearanceCatalog.P09_OUTFIT_WAIST,
                                                      CharacterAppearanceCatalog.P09_OUTFIT_LEG };

        [SerializeField] private Row[] _rows         = Array.Empty<Row>();
        [SerializeField] private int   _emotionCount = 10;

        private CharacterP09Selection _selection;
        private int                   _emotion = EMOTION_NONE;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsConfigured => (_rows != null) && (_rows.Length > 0);
        public int  Emotion      => _emotion;

        public event Action      Changed;
        public event Action<int> EmotionChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            for (int i = 0; i < _rows.Length; ++i)
            {
                Row row = _rows[i];
                if ((row == null) || (row.Previous == null) || (row.Next == null))
                {
                    continue;
                }

                ERow kind = row.Kind;
                row.Previous.onClick.AddListener(() => Step(kind, -1));
                row.Next.onClick.AddListener(() => Step(kind, 1));
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _rows.Length; ++i)
            {
                if ((_rows[i] != null) && (_rows[i].Previous != null) && (_rows[i].Next != null))
                {
                    _rows[i].Previous.onClick.RemoveAllListeners();
                    _rows[i].Next.onClick.RemoveAllListeners();
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(CharacterP09Selection selection)
        {
            _selection = selection;
            Refresh();
        }

        /// <summary> 생성 요청 중에는 값을 바꾸지 못하게 막는다 </summary>
        public void SetInteractable(bool interactable)
        {
            for (int i = 0; i < _rows.Length; ++i)
            {
                if (_rows[i] == null)
                {
                    continue;
                }

                if (_rows[i].Previous != null)
                {
                    _rows[i].Previous.interactable = interactable;
                }

                if (_rows[i].Next != null)
                {
                    _rows[i].Next.interactable = interactable;
                }
            }
        }

        /// <summary> 개발 시나리오가 실제 버튼을 누르도록 버튼을 내준다. direction은 -1 이전, 1 다음 </summary>
        public Button GetStepButton(ERow kind, int direction)
        {
            Row row = FindRow(kind);
            if (row == null)
            {
                return null;
            }

            return (direction < 0) ? row.Previous : row.Next;
        }

        /// <summary> 현재 값. 장비 행은 착용이면 1, 표정 행은 미리보기 표정이다 </summary>
        public int GetValue(ERow kind)
        {
            if (kind == ERow.Emotion)
            {
                return _emotion;
            }

            if (_selection == null)
            {
                return 0;
            }

            switch (kind)
            {
                case ERow.Sex:        return _selection.BodyType;
                case ERow.Face:       return _selection.Get(CharacterP09Selection.EField.Face);
                case ERow.Hair:       return _selection.Get(CharacterP09Selection.EField.Hair);
                case ERow.HairColor:  return _selection.Get(CharacterP09Selection.EField.HairColor);
                case ERow.EyeColor:   return _selection.Get(CharacterP09Selection.EField.EyeColor);
                case ERow.Skin:       return _selection.Get(CharacterP09Selection.EField.Skin);
                case ERow.FacialHair: return _selection.Get(CharacterP09Selection.EField.FacialHair);
                case ERow.BustSize:   return _selection.Get(CharacterP09Selection.EField.BustSize);
                default:              return IsArmorOn(kind) ? 1 : 0;
            }
        }

        private void Step(ERow kind, int direction)
        {
            if (_selection == null)
            {
                return;
            }

            if (kind == ERow.Emotion)
            {
                _emotion = Wrap(_emotion + direction, EMOTION_NONE, _emotionCount);
                Refresh();
                EmotionChanged?.Invoke(_emotion);
                return;
            }

            switch (kind)
            {
                case ERow.Sex:
                    _selection.Select(CharacterP09Selection.EField.BodyType, _selection.IsMale
                        ? CharacterAppearance.BODY_TYPE_FEMALE : CharacterAppearance.BODY_TYPE_MALE);
                    break;
                case ERow.Face:
                    StepField(CharacterP09Selection.EField.Face, direction, CharacterAppearanceCatalog.P09_FACE_MIN,
                        CharacterAppearanceCatalog.P09_FACE_MAX);
                    break;
                case ERow.Hair:
                    StepField(CharacterP09Selection.EField.Hair, direction, CharacterAppearanceCatalog.P09_HAIR_01,
                        CharacterAppearanceCatalog.P09_HAIR_SKINHEAD);
                    break;
                case ERow.HairColor:
                    StepField(CharacterP09Selection.EField.HairColor, direction, CharacterAppearanceCatalog.P09_HAIR_COLOR_MIN,
                        CharacterAppearanceCatalog.P09_HAIR_COLOR_MAX);
                    break;
                case ERow.EyeColor:
                    StepField(CharacterP09Selection.EField.EyeColor, direction, CharacterAppearanceCatalog.P09_EYE_COLOR_MIN,
                        CharacterAppearanceCatalog.P09_EYE_COLOR_MAX);
                    break;
                case ERow.Skin:
                    StepField(CharacterP09Selection.EField.Skin, direction, CharacterAppearanceCatalog.P09_SKIN_MIN,
                        CharacterAppearanceCatalog.P09_SKIN_MAX);
                    break;
                case ERow.FacialHair:
                    StepField(CharacterP09Selection.EField.FacialHair, direction, CharacterAppearanceCatalog.P09_FACIAL_HAIR_NONE,
                        CharacterAppearanceCatalog.P09_FACIAL_HAIR_MAX);
                    break;
                case ERow.BustSize:
                    StepField(CharacterP09Selection.EField.BustSize, direction, CharacterAppearanceCatalog.P09_BUST_MIN,
                        CharacterAppearanceCatalog.P09_BUST_MAX);
                    break;
                default:
                    int outfit = _selection.Get(CharacterP09Selection.EField.Outfit) ^ ArmorBit(kind);
                    _selection.Select(CharacterP09Selection.EField.Outfit, outfit);
                    break;
            }

            Refresh();
            Changed?.Invoke();
        }

        private void StepField(CharacterP09Selection.EField field, int direction, int min, int max)
        {
            _selection.Select(field, Wrap(_selection.Get(field) + direction, min, max));
        }

        private void Refresh()
        {
            if (_selection == null)
            {
                return;
            }

            bool isMale = _selection.IsMale;
            for (int i = 0; i < _rows.Length; ++i)
            {
                Row row = _rows[i];
                if (row == null)
                {
                    continue;
                }

                if (row.Root != null)
                {
                    bool visible = ((row.Kind != ERow.FacialHair) || isMale) && ((row.Kind != ERow.BustSize) || (!isMale));
                    if (row.Root.activeSelf != visible)
                    {
                        row.Root.SetActive(visible);
                    }
                }

                if (row.Value != null)
                {
                    row.Value.text = FormatValue(row.Kind);
                }
            }
        }

        private string FormatValue(ERow kind)
        {
            int value = GetValue(kind);
            switch (kind)
            {
                case ERow.Sex:        return LobbyText.SexName(value);
                case ERow.Hair:       return LobbyText.HairName(value);
                case ERow.FacialHair: return (value == CharacterAppearanceCatalog.P09_FACIAL_HAIR_NONE) ? LobbyText.NONE : value.ToString();
                case ERow.BustSize:   return LobbyText.BustName(value);
                case ERow.Emotion:    return (value == EMOTION_NONE) ? LobbyText.NONE : value.ToString();
                case ERow.Face:
                case ERow.HairColor:
                case ERow.EyeColor:
                case ERow.Skin:       return value.ToString();
                default:              return LobbyText.ArmorState(value != 0);
            }
        }

        private bool IsArmorOn(ERow kind)
        {
            return (_selection.Get(CharacterP09Selection.EField.Outfit) & ArmorBit(kind)) != 0;
        }

        private static int ArmorBit(ERow kind)
        {
            int index = (int)kind - (int)ERow.ArmorHead;
            return ((index >= 0) && (index < G_ArmorBits.Length)) ? G_ArmorBits[index] : 0;
        }

        private Row FindRow(ERow kind)
        {
            for (int i = 0; i < _rows.Length; ++i)
            {
                if ((_rows[i] != null) && (_rows[i].Kind == kind))
                {
                    return _rows[i];
                }
            }

            return null;
        }

        private static int Wrap(int value, int min, int max)
        {
            if (value > max)
            {
                return min;
            }

            return (value < min) ? max : value;
        }

#if UNITY_EDITOR
        /// <summary> 씬 구성 도구 전용 </summary>
        public void EditorSetRows(Row[] rows, int emotionCount)
        {
            _rows         = rows;
            _emotionCount = emotionCount;
        }
#endif
    }
}
