// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> GameDB의 버전, 모든 카탈로그와 중첩 직렬화 값을 결정적으로 해시한다 </summary>
    public static class GameDBContentHash
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string FORMAT_ID = "Shinshi.GameDB.ContentHash/v1";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> GameDBBootstrap이 초기화한 GameDB에서 계산한다, 서버는 부트 때, 클라이언트는 첫 인증 때 한 번 부른다 </summary>
        public static string Compute()
        {
            if (!GameDB.IsInitialized)
            {
                throw new InvalidOperationException("콘텐츠 해시를 계산하기 전에 GameDB를 초기화해야 한다");
            }

            using (var writer = new ContentHashWriter())
            {
                var traversal = new ContentTraversal(writer);
                FieldInfo[] fields = traversal.GetSerializedFields(typeof(GameDatabaseSO));

                writer.WriteString(FORMAT_ID);
                traversal.WriteObjectHeader(typeof(GameDatabaseSO), fields.Length);
                for (int i = 0; i < fields.Length; ++i)
                {
                    FieldInfo field = fields[i];
                    // 루트의 private 참조를 열거나 Resources를 다시 읽지 않고 공개 GameDB 계약을 쓴다.
                    // 새 루트 필드의 공개 접근자가 빠지면 일부 데이터만 해시하지 않고 부트를 중단한다.
                    PropertyInfo property = typeof(GameDB).GetProperty(field.Name.TrimStart('_'),
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if ((property == null) || (property.PropertyType != field.FieldType))
                    {
                        throw new InvalidOperationException($"GameDB 콘텐츠 필드 접근자가 없다: {field.Name}");
                    }

                    traversal.WriteField(field, property.GetValue(null));
                }

                return writer.Complete();
            }
        }

        /// <summary> 전달한 루트의 콘텐츠를 계산한다. 루트 검증과 초기화는 호출자가 맡는다 </summary>
        public static string Compute(GameDatabaseSO root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (root.GetType() != typeof(GameDatabaseSO))
            {
                throw new NotSupportedException("GameDB 콘텐츠 해시는 GameDatabaseSO 루트만 지원한다");
            }

            using (var writer = new ContentHashWriter())
            {
                var traversal = new ContentTraversal(writer);
                writer.WriteString(FORMAT_ID);
                traversal.WriteValue(root);
                return writer.Complete();
            }
        }

        // 새 필드가 생길 때 수동 필드 목록에서 빠지지 않도록 직렬화 필드만 읽는다.
        // 리플렉션 반환 순서와 metadata token은 사용하지 않고 선언 타입과 필드 이름을 ordinal로 정렬한다.
        // Unity 객체의 이름, instanceID, 캐시와 JsonUtility의 객체 참조 표현은 입력에 넣지 않는다.
        private sealed class ContentTraversal
        {
            /****************************************
            *                Fields
            ****************************************/

            private const int MAX_OBJECT_DEPTH = 128;

            private readonly ContentHashWriter _writer;
            private readonly Dictionary<Type, FieldInfo[]> _fields        = new Dictionary<Type, FieldInfo[]>();
            private readonly List<object> _activeObjects = new List<object>();

            /****************************************
            *                Methods
            ****************************************/

            public ContentTraversal(ContentHashWriter writer)
            {
                _writer = writer;
            }

            public void WriteObjectHeader(Type type, int fieldCount)
            {
                _writer.WriteBoolean(true);
                _writer.WriteString(type.FullName);
                _writer.WriteInt32(fieldCount);
            }

            public void WriteField(FieldInfo field, object value)
            {
                if ((typeof(GameDataSO).IsAssignableFrom(field.FieldType)) && (value == null))
                {
                    throw new InvalidOperationException($"GameDB 콘텐츠의 필수 참조가 없다: {field.Name}");
                }

                _writer.WriteString(field.DeclaringType.FullName);
                _writer.WriteString(field.Name);
                _writer.WriteString(field.FieldType.FullName);
                WriteValue(value);
            }

            public FieldInfo[] GetSerializedFields(Type type)
            {
                if (_fields.TryGetValue(type, out FieldInfo[] cached))
                {
                    return cached;
                }

                var fields = new List<FieldInfo>();
                Type declaringType = type;
                while ((declaringType != null) && (declaringType != typeof(ScriptableObject))
                    && (declaringType != typeof(object)))
                {
                    FieldInfo[] declared = declaringType.GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < declared.Length; ++i)
                    {
                        FieldInfo field = declared[i];
                        if ((field.IsStatic) || (field.IsInitOnly)
                            || (field.IsDefined(typeof(NonSerializedAttribute), false)))
                        {
                            continue;
                        }

                        if (field.IsDefined(typeof(SerializeReference), false))
                        {
                            throw new NotSupportedException($"GameDB 해시는 SerializeReference를 지원하지 않는다: {field.Name}");
                        }

                        if ((field.IsPublic) || (field.IsDefined(typeof(SerializeField), false)))
                        {
                            fields.Add(field);
                        }
                    }

                    declaringType = declaringType.BaseType;
                }

                fields.Sort(CompareFields);
                FieldInfo[] result = fields.ToArray();
                _fields.Add(type, result);
                return result;
            }

            public void WriteValue(object value)
            {
                // 필수 SO가 파괴되거나 빠졌다면 같은 빈 해시로 서버를 띄우지 않는다
                if ((value is UnityEngine.Object unityObject) && (unityObject == null))
                {
                    throw new InvalidOperationException("GameDB 콘텐츠에 파괴된 Unity 에셋 참조가 있다");
                }

                if (value == null)
                {
                    _writer.WriteBoolean(false);
                    return;
                }

                Type type = value.GetType();
                _writer.WriteBoolean(true);
                _writer.WriteString(type.FullName);

                if (type.IsEnum)
                {
                    WriteEnum(value, type);
                    return;
                }

                if (WritePrimitive(value, type))
                {
                    return;
                }

                if (value is Color color)
                {
                    WriteColor(color);
                    return;
                }

                if (value is Gradient gradient)
                {
                    WriteGradient(gradient);
                    return;
                }

                if ((!(value is Array)) && (!IsGameData(type)))
                {
                    throw new NotSupportedException($"GameDB 콘텐츠 해시가 지원하지 않는 타입이다: {type.FullName}");
                }

                EnterObject(value);
                try
                {
                    if (value is Array array)
                    {
                        WriteArray(array);
                        return;
                    }

                    FieldInfo[] fields = GetSerializedFields(type);
                    _writer.WriteInt32(fields.Length);
                    for (int i = 0; i < fields.Length; ++i)
                    {
                        FieldInfo field = fields[i];
                        WriteField(field, field.GetValue(value));
                    }
                }
                finally
                {
                    _activeObjects.RemoveAt(_activeObjects.Count - 1);
                }
            }

            private bool WritePrimitive(object value, Type type)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.String:
                        _writer.WriteString((string)value);
                        return true;
                    case TypeCode.Boolean:
                        _writer.WriteBoolean((bool)value);
                        return true;
                    case TypeCode.Byte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        _writer.WriteUInt64(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Char:
                        _writer.WriteInt64(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.Single:
                        _writer.WriteSingle((float)value);
                        return true;
                    case TypeCode.Double:
                        _writer.WriteDouble((double)value);
                        return true;
                    default:
                        return false;
                }
            }

            private void WriteEnum(object value, Type type)
            {
                Type underlying = Enum.GetUnderlyingType(type);
                if ((underlying == typeof(byte)) || (underlying == typeof(ushort))
                    || (underlying == typeof(uint)) || (underlying == typeof(ulong)))
                {
                    _writer.WriteUInt64(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                    return;
                }

                _writer.WriteInt64(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            private void WriteColor(Color color)
            {
                _writer.WriteSingle(color.r);
                _writer.WriteSingle(color.g);
                _writer.WriteSingle(color.b);
                _writer.WriteSingle(color.a);
            }

            private void WriteGradient(Gradient gradient)
            {
                _writer.WriteInt32((int)gradient.mode);
                _writer.WriteInt32((int)gradient.colorSpace);

                GradientColorKey[] colorKeys = gradient.colorKeys;
                _writer.WriteInt32(colorKeys.Length);
                for (int i = 0; i < colorKeys.Length; ++i)
                {
                    _writer.WriteSingle(colorKeys[i].time);
                    WriteColor(colorKeys[i].color);
                }

                GradientAlphaKey[] alphaKeys = gradient.alphaKeys;
                _writer.WriteInt32(alphaKeys.Length);
                for (int i = 0; i < alphaKeys.Length; ++i)
                {
                    _writer.WriteSingle(alphaKeys[i].time);
                    _writer.WriteSingle(alphaKeys[i].alpha);
                }
            }

            private void WriteArray(Array array)
            {
                if ((array.Rank != 1) || (array.GetLowerBound(0) != 0))
                {
                    throw new NotSupportedException("GameDB 콘텐츠 해시는 0부터 시작하는 1차원 배열만 지원한다");
                }

                // 카탈로그 추첨, 외형 인덱스와 배율 배열의 순서도 콘텐츠 계약이다.
                // ID나 값으로 정렬하면 바뀐 인덱스 의미를 같은 콘텐츠로 오인하므로 저장 순서를 보존한다.
                _writer.WriteInt32(array.Length);
                for (int i = 0; i < array.Length; ++i)
                {
                    WriteValue(array.GetValue(i));
                }
            }

            private void EnterObject(object value)
            {
                if (_activeObjects.Count >= MAX_OBJECT_DEPTH)
                {
                    throw new InvalidOperationException("GameDB 콘텐츠 중첩 깊이가 해시 한도를 넘었다");
                }

                for (int i = 0; i < _activeObjects.Count; ++i)
                {
                    if (ReferenceEquals(_activeObjects[i], value))
                    {
                        throw new InvalidOperationException("GameDB 콘텐츠에 순환 참조가 있다");
                    }
                }

                _activeObjects.Add(value);
            }

            private static bool IsGameData(Type type)
            {
                return (type == typeof(GameDatabaseSO)) || (typeof(GameDataSO).IsAssignableFrom(type))
                    || ((type.Namespace == typeof(GameDatabaseSO).Namespace)
                        && (type.IsDefined(typeof(SerializableAttribute), false)));
            }

            private static int CompareFields(FieldInfo left, FieldInfo right)
            {
                int declaringOrder = StringComparer.Ordinal.Compare(left.DeclaringType.FullName, right.DeclaringType.FullName);
                return (declaringOrder != 0) ? declaringOrder : StringComparer.Ordinal.Compare(left.Name, right.Name);
            }
        }
    }
}
