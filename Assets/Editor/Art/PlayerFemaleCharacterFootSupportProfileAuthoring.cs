#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

// Unity
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> 실제 원본 메시를 에디터에서 한 번 읽어 발바닥 profile을 만든다 </summary>
    public static class PlayerFemaleCharacterFootSupportProfileAuthoring
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float SUPPORT_CLASS_WEIGHT = 0.5f;
        private const int BITS_PER_BYTE = 8;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 실제 native NOOP 이후 측정한 Avatar별 목표 매핑과 원본 bind 데이터를 저장한다 </summary>
        public static PlayerFemaleCharacterFootSupportProfile Create(Animator animator,
            SkinnedMeshRenderer renderer,
            in PlayerFemaleCharacterFootSupportProfile.GoalMapping leftMapping,
            in PlayerFemaleCharacterFootSupportProfile.GoalMapping rightMapping,
            string sourceFilePath, string profileReportPath)
        {
            if ((animator == null) || (renderer == null) || (renderer.sharedMesh == null)
                || (animator.avatar == null) || (!animator.avatar.isValid) || (!animator.avatar.isHuman)
                || (AssetDatabase.GetAssetPath(animator.avatar) != AssetDatabase.GetAssetPath(renderer.sharedMesh))
                || (string.IsNullOrEmpty(sourceFilePath)) || (!File.Exists(sourceFilePath))
                || (!File.Exists(sourceFilePath + ".meta"))
                || (string.IsNullOrEmpty(profileReportPath)) || (File.Exists(profileReportPath)))
            {
                throw new InvalidOperationException("원본 모델과 Humanoid Avatar, 새 profile 저장 경로가 필요하다");
            }

            Transform[] bones = renderer.bones;
            var bindPoses = new List<Matrix4x4>(bones.Length);
            renderer.sharedMesh.GetBindposes(bindPoses);
            if ((bindPoses.Count != bones.Length)
                || (bones.Length > PlayerFemaleCharacterFootSupportProfile.MAX_BONES))
            {
                throw new InvalidOperationException("원본 bindpose와 bone 순서가 다르다");
            }

            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftToe = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightToe = animator.GetBoneTransform(HumanBodyBones.RightToes);
            var left = new List<PlayerFemaleCharacterFootSupportProfile.SupportPoint>();
            var right = new List<PlayerFemaleCharacterFootSupportProfile.SupportPoint>();
            var influences = new PlayerFemaleCharacterFootSupportProfile.Influence[PlayerFemaleCharacterFootSupportProfile.MAX_INFLUENCES];
            using (Mesh.MeshDataArray array = MeshUtility.AcquireReadOnlyMeshData(renderer.sharedMesh))
            {
                Mesh.MeshData data = array[0];
                if ((!data.HasVertexAttribute(VertexAttribute.BlendWeight))
                    || (!data.HasVertexAttribute(VertexAttribute.BlendIndices))
                    || (data.GetVertexAttributeFormat(VertexAttribute.BlendWeight) != VertexAttributeFormat.Float32))
                {
                    throw new InvalidOperationException("원본 Float32 skin 데이터를 읽을 수 없다");
                }

                int dimension = data.GetVertexAttributeDimension(VertexAttribute.BlendWeight);
                if ((dimension <= 0) || (dimension > PlayerFemaleCharacterFootSupportProfile.MAX_INFLUENCES))
                {
                    throw new InvalidOperationException("원본 skin 영향도가 지원 범위를 넘는다");
                }

                int weightStream = data.GetVertexAttributeStream(VertexAttribute.BlendWeight);
                int indexStream = data.GetVertexAttributeStream(VertexAttribute.BlendIndices);
                NativeArray<byte> weights = data.GetVertexData<byte>(weightStream);
                NativeArray<byte> indices = data.GetVertexData<byte>(indexStream);
                int weightStride = data.GetVertexBufferStride(weightStream);
                int indexStride = data.GetVertexBufferStride(indexStream);
                int weightOffset = data.GetVertexAttributeOffset(VertexAttribute.BlendWeight);
                int indexOffset = data.GetVertexAttributeOffset(VertexAttribute.BlendIndices);
                VertexAttributeFormat format = data.GetVertexAttributeFormat(VertexAttribute.BlendIndices);
                int indexBytes;
                switch (format)
                {
                    case VertexAttributeFormat.UInt8: indexBytes = sizeof(byte); break;
                    case VertexAttributeFormat.UInt16: indexBytes = sizeof(ushort); break;
                    case VertexAttributeFormat.UInt32: indexBytes = sizeof(int); break;
                    default: throw new InvalidOperationException("지원하지 않는 skin bone index 형식이다");
                }

                using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(positions);
                    for (int vertex = 0; vertex < data.vertexCount; ++vertex)
                    {
                        Array.Clear(influences, 0, influences.Length);
                        int count = 0;
                        float leftWeight = 0f;
                        float rightWeight = 0f;
                        for (int slot = 0; slot < dimension; ++slot)
                        {
                            float weight = BitConverter.Int32BitsToSingle(ReadUnsigned(weights,
                                vertex * weightStride + weightOffset + slot * sizeof(float), sizeof(float)));
                            if ((!SecondaryMotionMath.IsFinite(weight)) || (weight < 0f))
                            {
                                throw new InvalidOperationException("원본 skin 가중치가 유효하지 않다");
                            }

                            if (weight == 0f)
                            {
                                continue;
                            }

                            int bone = ReadUnsigned(indices, vertex * indexStride + indexOffset + slot * indexBytes, indexBytes);
                            if ((bone < 0) || (bone >= bones.Length))
                            {
                                throw new InvalidOperationException("원본 skin bone index가 유효하지 않다");
                            }

                            influences[count] = new PlayerFemaleCharacterFootSupportProfile.Influence(bone, weight,
                                bindPoses[bone].MultiplyPoint3x4(positions[vertex]));
                            ++count;
                            if ((bones[bone] == leftFoot) || (bones[bone] == leftToe))
                            {
                                leftWeight += weight;
                            }

                            if ((bones[bone] == rightFoot) || (bones[bone] == rightToe))
                            {
                                rightWeight += weight;
                            }
                        }

                        var point = new PlayerFemaleCharacterFootSupportProfile.SupportPoint(vertex, count,
                            in influences[0], in influences[1], in influences[2], in influences[3]);
                        if (leftWeight >= SUPPORT_CLASS_WEIGHT)
                        {
                            left.Add(point);
                        }

                        if (rightWeight >= SUPPORT_CLASS_WEIGHT)
                        {
                            right.Add(point);
                        }
                    }
                }
            }

            if ((left.Count == 0) || (right.Count == 0)
                || (left.Count > PlayerFemaleCharacterFootSupportProfile.MAX_SUPPORTS)
                || (right.Count > PlayerFemaleCharacterFootSupportProfile.MAX_SUPPORTS))
            {
                throw new InvalidOperationException("원본 발바닥 지원점 개수가 유효하지 않다");
            }

            var paths = new string[bones.Length];
            for (int index = 0; index < paths.Length; ++index)
            {
                paths[index] = AnimationUtility.CalculateTransformPath(bones[index], animator.transform);
            }

            var profile = new PlayerFemaleCharacterFootSupportProfile();
            profile.SetAuthoringData(animator.avatar, renderer.sharedMesh, HashFile(sourceFilePath), HashFile(sourceFilePath + ".meta"), paths,
                in leftMapping, in rightMapping, left.ToArray(), right.ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(profileReportPath));
            File.WriteAllText(profileReportPath, JsonUtility.ToJson(profile, true));
            return profile;
        }

        /// <summary> calibration과 원본이 같음을 영수증에 남길 SHA256을 구한다 </summary>
        public static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary> 검증 영수증의 source hash와 bone 경로를 검사하고 현재 에디터의 실제 참조로 다시 연결한다 </summary>
        public static PlayerFemaleCharacterFootSupportProfile LoadVerifiedData(string profileJsonPath,
            Animator animator, SkinnedMeshRenderer renderer, string sourceFilePath)
        {
            if ((!File.Exists(profileJsonPath)) || (animator == null) || (renderer == null)
                || (renderer.sharedMesh == null) || (animator.avatar == null))
            {
                throw new InvalidOperationException("실제 모델과 Avatar, 보존된 profile 영수증이 필요하다");
            }

            if (AssetDatabase.GetAssetPath(animator.avatar) != AssetDatabase.GetAssetPath(renderer.sharedMesh))
            {
                throw new InvalidOperationException("검증된 같은 원본 모델의 Avatar가 필요하다");
            }

            var saved = JsonUtility.FromJson<PlayerFemaleCharacterFootSupportProfile>(File.ReadAllText(profileJsonPath));
            Transform[] bones = renderer.bones;
            if ((saved == null) || (saved.SourceAssetHash != HashFile(sourceFilePath))
                || (saved.SourceMetadataHash != HashFile(sourceFilePath + ".meta"))
                || (saved.SourceVertexCount != renderer.sharedMesh.vertexCount) || (saved.BoneCount != bones.Length))
            {
                throw new InvalidOperationException("profile 원본 해시나 모델 구조가 달라 다시 authoring해야 한다");
            }

            var paths = new string[bones.Length];
            for (int i = 0; i < bones.Length; ++i)
            {
                paths[i] = AnimationUtility.CalculateTransformPath(bones[i], animator.transform);
                if (paths[i] != saved.GetBonePath(i))
                {
                    throw new InvalidOperationException("profile bone 경로가 달라 다시 authoring해야 한다");
                }
            }

            var left = new PlayerFemaleCharacterFootSupportProfile.SupportPoint[saved.LeftSupportCount];
            var right = new PlayerFemaleCharacterFootSupportProfile.SupportPoint[saved.RightSupportCount];
            for (int i = 0; i < left.Length; ++i)
            {
                left[i] = saved.GetSupportPoint(true, i);
            }

            for (int i = 0; i < right.Length; ++i)
            {
                right[i] = saved.GetSupportPoint(false, i);
            }

            var profile = new PlayerFemaleCharacterFootSupportProfile();
            profile.SetAuthoringData(animator.avatar, renderer.sharedMesh, saved.SourceAssetHash, saved.SourceMetadataHash, paths,
                saved.LeftMapping, saved.RightMapping, left, right);
            return profile;
        }

        private static int ReadUnsigned(NativeArray<byte> bytes, int offset, int length)
        {
            int value = 0;
            for (int index = 0; index < length; ++index)
            {
                value |= bytes[offset + index] << (index * BITS_PER_BYTE);
            }

            return value;
        }
    }
}
#endif
