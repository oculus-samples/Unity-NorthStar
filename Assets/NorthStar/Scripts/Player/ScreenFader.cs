// Copyright (c) Meta Platforms, Inc. and affiliates.
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace NorthStar
{
    /// <summary>
    /// Used to gradually fade the screen in and out when required by game logic and events
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        [SerializeField] private Volume m_ppVolume;
        public float HeadFadeValue;
        public float TeleportFadeValue;
        public float TimedFadeValue;

        private void OnEnable()
        {
            Instance = this;
        }

        private void Update()
        {
            m_ppVolume.weight = Mathf.Clamp01(HeadFadeValue + TeleportFadeValue + TimedFadeValue);
        }

        public Tween DoFadeOut(float duration)
        {
            return DOTween.To(() => TimedFadeValue, x => TimedFadeValue = x, 1.0f, duration);
        }

        public static ScreenFader Instance;
    }
}