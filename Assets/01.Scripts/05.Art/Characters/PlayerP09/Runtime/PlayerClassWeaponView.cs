// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 조립된 P09 인스턴스의 손 소켓에 직업 무기를 붙인다. 외형 조립(PlayerFemaleCharacterAppearanceView)이 Attach를 부른다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerClassWeaponView : MonoBehaviour
    {
        // 무기는 표시 전용이라 충돌체를 끄고, 서버 판정 길이나 사거리에 영향을 주지 않는다.
        // 직업 SyncVar가 외형보다 늦게 올 수 있으므로 직업이 바뀔 때마다 지금 붙은 인스턴스에 다시 붙인다

        /****************************************
        *                Fields
        ****************************************/

        [Serializable]
        private struct WeaponEntry
        {
            [SerializeField] private EPlayerClass _playerClass;
            [SerializeField] private GameObject   _prefab;
            [SerializeField] private string       _socketName;

            public EPlayerClass PlayerClass => _playerClass;
            public GameObject   Prefab      => _prefab;
            public string       SocketName  => _socketName;
        }

        [SerializeField] private PlayerCombatState _combat;
        [SerializeField] private WeaponEntry[]     _weapons = Array.Empty<WeaponEntry>();

        private Transform  _characterRoot;
        private GameObject _weapon;

        /****************************************
        *              Properties
        ****************************************/

        public GameObject CurrentWeapon => _weapon;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_combat != null)
            {
                _combat.PlayerClassChanged += OnPlayerClassChanged;
            }
        }

        private void OnDestroy()
        {
            if (_combat != null)
            {
                _combat.PlayerClassChanged -= OnPlayerClassChanged;
            }

            DestroyWeapon();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새로 조립된 인스턴스에 현재 직업의 무기를 붙인다 </summary>
        public void Attach(Transform characterRoot)
        {
            _characterRoot = characterRoot;
            Refresh();
        }

        /// <summary> 인스턴스를 버리기 전에 부른다, 붙어 있던 무기를 지운다 </summary>
        public void Detach()
        {
            DestroyWeapon();
            _characterRoot = null;
        }

        private void OnPlayerClassChanged(EPlayerClass playerClass) { Refresh(); }

        private void Refresh()
        {
            DestroyWeapon();
            if ((_characterRoot == null) || (_combat == null) || (!TryGetEntry(_combat.PlayerClass, out WeaponEntry entry)))
            {
                return;
            }

            Transform socket = FindDeep(_characterRoot, entry.SocketName);
            if (socket == null)
            {
                Debug.LogError($"[ClassWeaponView] 무기 소켓을 찾지 못했다: {entry.SocketName}", this);
                return;
            }

            _weapon = Instantiate(entry.Prefab, socket, false);
            Collider[] colliders = _weapon.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                colliders[i].enabled = false;
            }
        }

        private bool TryGetEntry(EPlayerClass playerClass, out WeaponEntry entry)
        {
            for (int i = 0; i < _weapons.Length; ++i)
            {
                if ((_weapons[i].PlayerClass == playerClass) && (_weapons[i].Prefab != null))
                {
                    entry = _weapons[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private void DestroyWeapon()
        {
            if (_weapon != null)
            {
                Destroy(_weapon);
                _weapon = null;
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; ++i)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
