// Copyright (c) Meta Platforms, Inc. and affiliates.
using Meta.Utilities.Environment;
using Meta.XR.Samples;
using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Plays vfx when an object hits the water
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class Splash : MonoBehaviour
    {
        [SerializeField] private EffectAsset m_effectAsset;
        [SerializeField] private float m_radius;
        [SerializeField] private bool m_convertFromBoatSpace = false;
        private bool m_isUnderWater = false;

        private void Update()
        {
            var pos = m_convertFromBoatSpace ? BoatController.WorldToBoatSpace(transform.position) : transform.position;
            if (!EnvironmentSystem.Instance)
            {
                return;
            }

            var waterHeight = EnvironmentSystem.Instance.GetOceanHeightIterative(pos, 1);
            if (transform.position.y - m_radius < waterHeight && !m_isUnderWater)
            {
                m_isUnderWater = true;
                var effectPos = pos;
                effectPos.y = waterHeight;
                var rotation = Quaternion.Euler(0, Random.value * 360, 0);
                m_effectAsset?.Play(effectPos, rotation);
            }
            else if (transform.position.y - m_radius > waterHeight && m_isUnderWater)
                m_isUnderWater = false;
        }
        private void OnDrawGizmosSelected()
        {
            var pos = m_convertFromBoatSpace ? BoatController.WorldToBoatSpace(transform.position) : transform.position;
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(pos, m_radius);
        }
    }
}
