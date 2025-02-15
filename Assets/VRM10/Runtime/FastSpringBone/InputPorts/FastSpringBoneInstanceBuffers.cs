using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UniVRM10.FastSpringBones.Blittables;

namespace UniVRM10.FastSpringBones.System
{
    /// <summary>
    /// ひとつのVRMに紐づくFastSpringBoneに関連したバッファを保持するクラス
    /// </summary>
    public class FastSpringBoneBuffer : IDisposable
    {
        public NativeArray<BlittableSpring> Springs { get; }
        public NativeArray<BlittableJoint> Joints { get; }
        public NativeArray<BlittableCollider> Colliders { get; }
        public NativeArray<BlittableLogic> Logics { get; }
        public NativeArray<BlittableTransform> BlittableTransforms { get; }
        public Transform[] Transforms { get; }
        public bool IsDisposed { get; private set; }

        public FastSpringBoneBuffer(IReadOnlyList<FastSpringBoneSpring> springs)
        {
            Profiler.BeginSample("FastSpringBone.ConstructBuffers");
            
            // Transformの列挙
            Profiler.BeginSample("FastSpringBone.ConstructBuffers.ConstructTransformBuffer");
            var transformHashSet = new HashSet<Transform>();
            foreach (var spring in springs)
            {
                foreach (var joint in spring.joints)
                {
                    transformHashSet.Add(joint.Transform);
                    if (joint.Transform.parent != null) transformHashSet.Add(joint.Transform.parent);
                }
                foreach (var collider in spring.colliders)
                {
                    transformHashSet.Add(collider.Transform);
                }

                if (spring.center != null) transformHashSet.Add(spring.center);
            }
            var transforms = transformHashSet.ToArray();
            var transformIndexDictionary = transforms.Select((trs, index) => (trs, index))
                .ToDictionary(tuple => tuple.trs, tuple => tuple.index);
            Profiler.EndSample();

            // 各種bufferの構築
            Profiler.BeginSample("FastSpringBone.ConstructBuffers.ConstructBuffers");
            var blittableColliders = new List<BlittableCollider>();
            var blittableJoints = new List<BlittableJoint>();
            var blittableSprings = new List<BlittableSpring>();
            var blittableLogics = new List<BlittableLogic>();

            foreach (var spring in springs)
            {
                var blittableSpring = new BlittableSpring
                {
                    colliderSpan = new BlittableSpan
                    {
                        startIndex = blittableColliders.Count,
                        count = spring.colliders.Length,
                    },
                    logicSpan = new BlittableSpan
                    {
                        startIndex = blittableJoints.Count,
                        count = spring.joints.Length - 1,
                    },
                    centerTransformIndex = spring.center ? transformIndexDictionary[spring.center] : -1
                };
                blittableSprings.Add(blittableSpring);

                blittableColliders.AddRange(spring.colliders.Select(collider =>
                {
                    var blittable = collider.Collider;
                    blittable.transformIndex = transformIndexDictionary[collider.Transform];
                    return blittable;
                }));
                blittableJoints.AddRange(spring.joints.Take(spring.joints.Length - 1).Select(joint =>
                {
                    var blittable = joint.Joint;
                    return blittable;
                }));

                for (var i = 0; i < spring.joints.Length - 1; ++i)
                {
                    var joint = spring.joints[i];
                    var tailJoint = spring.joints[i + 1];
                    var localPosition = tailJoint.Transform.localPosition;
                    var scale = tailJoint.Transform.lossyScale;
                    var localChildPosition =
                        new Vector3(
                            localPosition.x * scale.x,
                            localPosition.y * scale.y,
                            localPosition.z * scale.z
                        );

                    var worldChildPosition = joint.Transform.TransformPoint(localChildPosition);
                    var currentTail = spring.center != null
                        ? spring.center.InverseTransformPoint(worldChildPosition)
                        : worldChildPosition;
                    var parent = joint.Transform.parent;
                    blittableLogics.Add(new BlittableLogic
                    {
                        headTransformIndex = transformIndexDictionary[joint.Transform],
                        parentTransformIndex = parent != null ? transformIndexDictionary[parent] : -1,
                        currentTail = currentTail,
                        prevTail = currentTail,
                        localRotation = Quaternion.identity,
                        boneAxis = localChildPosition.normalized,
                        length = localChildPosition.magnitude
                    });
                }
            }
            Profiler.EndSample();

            // 各種bufferの初期化
            Profiler.BeginSample("FastSpringBone.ConstructBuffers.ConstructNativeArrays");
            Springs = new NativeArray<BlittableSpring>(blittableSprings.ToArray(), Allocator.Persistent);

            Joints = new NativeArray<BlittableJoint>(blittableJoints.ToArray(), Allocator.Persistent);
            Colliders = new NativeArray<BlittableCollider>(blittableColliders.ToArray(), Allocator.Persistent);
            Logics = new NativeArray<BlittableLogic>(blittableLogics.ToArray(), Allocator.Persistent);

            BlittableTransforms = new NativeArray<BlittableTransform>(transforms.Length, Allocator.Persistent);
            Transforms = transforms.ToArray();
            Profiler.EndSample();

            Profiler.EndSample();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Springs.Dispose();
            Joints.Dispose();
            BlittableTransforms.Dispose();
            Colliders.Dispose();
            Logics.Dispose();
        }
    }
}