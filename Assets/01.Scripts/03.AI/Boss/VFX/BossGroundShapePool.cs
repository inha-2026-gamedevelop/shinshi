// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 임시 도형을 필요할 때 만들어 빌려준다. 프리팹이나 재질 에셋을 쓰지 않으려고 기본 스프라이트 셰이더로 재질을 직접 만든다
    // 최종 VFX가 들어오면 이 풀을 부르는 쪽(Acquire/Release)은 그대로 두고 만드는 방법만 바꾸면 된다
    /// <summary> 바닥 도형 풀, 상한에 닿으면 null을 돌려준다 </summary>
    public sealed class BossGroundShapePool : IDisposable
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string ROOT_NAME   = "Boss Ground Shapes";
        private const string SHAPE_NAME  = "BossGroundShape";
        private const string SHADER_NAME = "Sprites/Default";

        /****************************************
        *                Fields
        ****************************************/

        private readonly Stack<BossGroundShapeVFX>   _free   = new Stack<BossGroundShapeVFX>();
        private readonly HashSet<BossGroundShapeVFX> _leased = new HashSet<BossGroundShapeVFX>();
        private readonly int      _maxCount;
        private GameObject        _root;
        private Material          _material;
        private int               _createdCount;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 도형을 만들 재질이 있다 </summary>
        public bool IsAvailable => _material != null;

        public int LeasedCount => _leased.Count;

        public int CreatedCount => _createdCount;

        public int MaxCount => _maxCount;

        /****************************************
        *                Methods
        ****************************************/

        public BossGroundShapePool(int maxCount)
        {
            _maxCount = Mathf.Max(1, maxCount);

            Shader shader = Shader.Find(SHADER_NAME);
            if (shader != null)
            {
                _material = new Material(shader) { name = "BossGroundShape Runtime Material" };
            }
        }

        /// <summary> 하나를 빌린다, 재질이 없거나 상한에 닿았으면 null이라 호출자는 표현을 생략할 수 있어야 한다 </summary>
        public BossGroundShapeVFX Acquire()
        {
            if (_material == null)
            {
                return null;
            }

            BossGroundShapeVFX shape = null;
            while ((shape == null) && (_free.Count > 0))
            {
                shape = _free.Pop();
            }

            if (shape == null)
            {
                if (_createdCount >= _maxCount)
                {
                    return null;
                }

                shape = Create();
            }

            shape.gameObject.SetActive(true);
            _leased.Add(shape);
            return shape;
        }

        /// <summary> 화면을 지우고 돌려받는다, 빌리지 않은 것이나 두 번째 반환은 무시한다 </summary>
        public void Release(BossGroundShapeVFX shape)
        {
            if ((shape == null) || (!_leased.Remove(shape)))
            {
                return;
            }

            shape.Hide();
            shape.gameObject.SetActive(false);
            _free.Push(shape);
        }

        /// <summary> 빌려준 도형을 모두 돌려받는다 </summary>
        public void ReleaseAll()
        {
            BossGroundShapeVFX[] leased = new BossGroundShapeVFX[_leased.Count];
            _leased.CopyTo(leased);
            for (int i = 0; i < leased.Length; ++i)
            {
                Release(leased[i]);
            }
        }

        public void Dispose()
        {
            _free.Clear();
            _leased.Clear();

            if (_root != null)
            {
                DestroyObject(_root);
                _root = null;
            }

            if (_material != null)
            {
                DestroyObject(_material);
                _material = null;
            }
        }

        private BossGroundShapeVFX Create()
        {
            if (_root == null)
            {
                _root = new GameObject(ROOT_NAME);
            }

            GameObject go = new GameObject(SHAPE_NAME, typeof(MeshFilter), typeof(MeshRenderer), typeof(BossGroundShapeVFX));
            go.transform.SetParent(_root.transform, false);
            BossGroundShapeVFX shape = go.GetComponent<BossGroundShapeVFX>();
            shape.Initialize(_material);
            go.SetActive(false);
            ++_createdCount;
            return shape;
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
