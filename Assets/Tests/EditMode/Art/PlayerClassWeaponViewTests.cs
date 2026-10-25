// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public sealed class PlayerClassWeaponViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string NET_PLAYER_PATH = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string CHARACTER_PATH  = "Assets/02.Prefab/Characters/PlayerP09/PF_PlayerP09.prefab";

        private GameObject _netPlayer;
        private GameObject _character;

        /****************************************
        *              Unity Event
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            if (_character != null)
            {
                Object.DestroyImmediate(_character);
            }

            if (_netPlayer != null)
            {
                Object.DestroyImmediate(_netPlayer);
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(EPlayerClass.Mageknight, "Weapon_Target_Hand_R", "PF_P09Weapon_MageknightSword")]
        [TestCase(EPlayerClass.Archer, "Weapon_Target_Hand_L", "PF_P09Weapon_ArcherBow")]
        public void Attach_ClassWithWeapon_PutsWeaponOnHandSocket(EPlayerClass playerClass, string socket, string weapon)
        {
            PlayerClassWeaponView view = Build(playerClass);

            view.Attach(_character.transform);

            Assert.That(view.CurrentWeapon, Is.Not.Null);
            Assert.That(view.CurrentWeapon.name, Does.StartWith(weapon));
            Assert.That(view.CurrentWeapon.transform.parent.name, Is.EqualTo(socket));
            Assert.That(view.CurrentWeapon.GetComponentInChildren<MeshRenderer>().sharedMaterial, Is.Not.Null);
        }

        [Test]
        public void Attach_ClassWithoutWeapon_AddsNothing()
        {
            PlayerClassWeaponView view = Build(EPlayerClass.Swordsman);

            view.Attach(_character.transform);

            Assert.That(view.CurrentWeapon, Is.Null);
        }

        [Test]
        public void NetPlayer_AppearanceView_ReferencesWeaponView()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NET_PLAYER_PATH);
            var appearance = prefab.GetComponentInChildren<PlayerFemaleCharacterAppearanceView>(true);
            var view = prefab.GetComponentInChildren<PlayerClassWeaponView>(true);

            Assert.That(view, Is.Not.Null);
            Assert.That(new SerializedObject(appearance).FindProperty("_weaponView").objectReferenceValue, Is.EqualTo(view));
            Assert.That(new SerializedObject(view).FindProperty("_combat").objectReferenceValue, Is.Not.Null);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private PlayerClassWeaponView Build(EPlayerClass playerClass)
        {
            _netPlayer = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NET_PLAYER_PATH));
            _character = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CHARACTER_PATH));

            // 직업은 직렬화하지 않는 SyncVar이고 오프라인에서는 서버 전용 설정자를 부를 수 없어 필드에 바로 넣는다
            FieldInfo field = typeof(PlayerCombatState).GetField("_playerClass", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(_netPlayer.GetComponent<PlayerCombatState>(), playerClass);

            return _netPlayer.GetComponentInChildren<PlayerClassWeaponView>(true);
        }
    }
}
