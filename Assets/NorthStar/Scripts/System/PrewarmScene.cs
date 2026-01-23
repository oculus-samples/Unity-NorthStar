// Copyright (c) Meta Platforms, Inc. and affiliates.
using System.Collections;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Playables;

namespace NorthStar
{
    /// <summary>
    /// Renderes all objects in scene to prevent hitches
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class PrewarmScene : MonoBehaviour
    {
        [SerializeField] private GameObject[] m_disabledObjectsToWarmUp;

        private bool m_prewarming;
        //private Renderer[] m_renderers;

        private void Start()
        {
            // Attempt to find all renderers in the scene for prewarming
            //m_renderers = FindObjectsOfType<Renderer>(true);

            Prewarm();
        }

        private IEnumerator DeferredDisable()
        {
            foreach (var obj in m_disabledObjectsToWarmUp)
            {
                obj.SetActive(true);
            }

            yield return null;

            foreach (var obj in m_disabledObjectsToWarmUp)
            {
                obj.SetActive(false);
            }
        }

        private void Prewarm()
        {
            _ = StartCoroutine(DeferredDisable());

            var directors = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var director in directors)
            {
                director.RebuildGraph();
            }
        }
    }
}
