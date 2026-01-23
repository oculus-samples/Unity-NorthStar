// Copyright (c) Meta Platforms, Inc. and affiliates.
using System;
using Meta.XR.Samples;
using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Stores pairs of physics materials used to generate effects when collisions occur
    /// Physics materials are used for convenience as all colliders already have them meaning no extra data is required
    /// </summary>
    [MetaCodeSample("NorthStar")]
    [CreateAssetMenu(menuName = "Data/Collision Material Pairs")]
    public class CollisionMaterialPairs : ScriptableObject
    {
        private const string FILEPATH = "CollisionMaterialPairs";

        [Serializable]
        private class CollisionPair
        {
            public PhysicsMaterial A, B;
            public EffectAsset Effect;
            public AnimationCurve VolumeCurve;
        }
        [SerializeField] private EffectAsset m_defaultEffect;
        [SerializeField] private AnimationCurve m_velocityInstensityCurve;
        [SerializeField] private CollisionPair[] m_collisionPairs;

        public EffectAsset GetEffectLocal(PhysicsMaterial a, PhysicsMaterial b, out AnimationCurve curve)
        {
            foreach (var pair in m_collisionPairs)
            {
                if (pair.A == a && pair.B == b)
                {
                    curve = pair.VolumeCurve;
                    return pair.Effect;
                }
                if (pair.B == a && pair.A == b)
                {
                    curve = pair.VolumeCurve;
                    return pair.Effect;
                }
            }

            curve = m_velocityInstensityCurve;
            return m_defaultEffect;
        }

        public float GetIntensityLocal(float velocity)
        {
            return m_velocityInstensityCurve.Evaluate(velocity);
        }

        private static CollisionMaterialPairs s_instance;

        public static CollisionMaterialPairs Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = Resources.Load(FILEPATH) as CollisionMaterialPairs;
                }
                return s_instance;
            }
        }
        public static EffectAsset GetEffect(PhysicsMaterial a, PhysicsMaterial b, out AnimationCurve curve)
        {
            return Instance.GetEffectLocal(a, b, out curve);
        }
        public static float GetIntensity(float velocity)
        {
            return Instance.GetIntensityLocal(velocity);
        }
    }
}
