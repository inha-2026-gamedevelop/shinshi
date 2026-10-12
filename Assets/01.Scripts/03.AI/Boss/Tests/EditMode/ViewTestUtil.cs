// System
using System.Reflection;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Tests
{
    // EditMode에서는 AddComponent가 Awake를 부르지 않는다. ClientView의 시작 처리를 시험이 직접 부른다
    internal static class ViewTestUtil
    {
        private static readonly MethodInfo G_Awake = typeof(ClientView).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary> 한 표시 뷰의 시작 처리를 부른다 </summary>
        public static void Awake(ClientView view)
        {
            G_Awake.Invoke(view, null);
        }

        /// <summary> 오브젝트에 붙은 T 형식의 표시 뷰마다 시작 처리를 부른다 </summary>
        public static void AwakeAll<T>(GameObject root) where T : ClientView
        {
            T[] views = root.GetComponents<T>();
            for (int i = 0; i < views.Length; ++i)
            {
                Awake(views[i]);
            }
        }
    }
}
