// Copyright (c) Meta Platforms, Inc. and affiliates.
using System.Collections.Generic;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;

namespace NorthStar
{
    /// <summary>
    /// A place for the player to teleport. Handles teleportation hotspot effects and links to other valid teleport locations
    /// </summary>
    [MetaCodeSample("NorthStar")]
    [SelectionBase]
    public class TeleportWaypoint : MonoBehaviour
    {
        public enum State
        {
            /// <summary>
            /// Not in line of sight.
            /// All elements should be hidden.
            /// </summary>
            Hidden,

            /// <summary>
            /// In line of sight.
            /// Play pin animation, no other visual elements. 
            /// </summary>
            Visible,

            /// <summary>
            /// Open palm pointing at waypoint.
            /// Stop pin animation, play selection animation, show player silhouette, show dotted line.
            /// </summary>
            Selected,

            /// <summary>
            /// Clenching fist pointed at waypoint.
            /// Same as selected, but we transition the line from 0 to 1 with the lock timer.
            /// </summary>
            Locked,
        }

        public bool RotatePlayer;
        [field: SerializeField] public Transform LosCheckTarget { get; private set; }
        [field: SerializeField] public List<TeleportWaypoint> Connections { get; private set; } = new List<TeleportWaypoint>();

        [field: SerializeField] public float AutoConnectionDistanceLimit { get; private set; } = 5;

        [field: SerializeField] public ParticleSystem PinParticles { get; private set; }
        [field: SerializeField] public SoundPlayer VisibleSound { get; private set; }
        [field: SerializeField] public ParticleSystem SelectionParticles { get; private set; }

        [field: SerializeField] public GameObject PlayerSilhouette { get; private set; }

        [field: SerializeField] public GameObject PinAppearObject { get; private set; }
        [field: SerializeField] public GameObject PinHoldObject { get; private set; }

        [field: SerializeField] public float PlayerBoundsRadius { get; private set; } = float.PositiveInfinity;
        [field: SerializeField] public float ExtraScreenFadeTime { get; private set; } = 0;

        private bool m_tooClose;
        public bool TooClose
        {
            get => m_tooClose;
            set
            {
                if (m_tooClose == value) return;
                m_tooClose = value;
                UpdateAppearance();
            }
        }

        public UnityEvent OnWarp;

        private State m_currentState;

        public State CurrentState
        {
            get => m_currentState;

            set
            {
                m_currentState = value;
                UpdateAppearance();
            }
        }


        private void Start()
        {
            UpdateAppearance();
        }

        private void UpdateAppearance()
        {
            switch (m_currentState)
            {
                case State.Hidden:
                    PinParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    PinParticles.gameObject.SetActive(false);

                    SelectionParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    SelectionParticles.gameObject.SetActive(false);

                    PlayerSilhouette.SetActive(false);

                    break;

                case State.Visible:
                    PinParticles.gameObject.SetActive(true);
                    if (VisibleSound is not null)
                    {
                        VisibleSound.Stop();
                        VisibleSound.Play();
                    }

                    PinHoldObject.SetActive(!TooClose);
                    PinAppearObject.SetActive(!TooClose);
                    if (!PinParticles.isPlaying) PinParticles.Play();

                    SelectionParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    SelectionParticles.gameObject.SetActive(false);

                    PlayerSilhouette.SetActive(false);

                    break;

                case State.Selected:
                case State.Locked:
                    PinParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    PinParticles.gameObject.SetActive(false);

                    SelectionParticles.gameObject.SetActive(true);
                    if (!SelectionParticles.isPlaying) SelectionParticles.Play();

                    PlayerSilhouette.SetActive(true);

                    break;
            }
        }

        private void Update()
        {
            var forward = transform.position - Camera.main.transform.position;
            var sqrDistance = forward.sqrMagnitude;

            if (!RotatePlayer)
            {
                forward.y = 0;
                forward.Normalize();
                transform.rotation = Quaternion.LookRotation(forward);
            }

            var sqrMinDistance = GlobalSettings.PlayerSettings.TeleporterToCloseDespawnDistance * GlobalSettings.PlayerSettings.TeleporterToCloseDespawnDistance;
            TooClose = sqrDistance < sqrMinDistance;
        }

        public void OnShow()
        {
            CurrentState = State.Visible;
        }

        public void OnHide()
        {
            CurrentState = State.Hidden;
        }

        public void OnSelect()
        {
            CurrentState = State.Selected;
        }

        public void OnUnselect()
        {
            CurrentState = State.Visible;
        }

        public void OnLock()
        {
            CurrentState = State.Locked;
        }

        public void ShowConnections()
        {
            foreach (var target in Connections)
            {
                target.OnShow();
            }
        }
        public void HideConnections()
        {
            foreach (var target in Connections)
            {
                target.OnHide();
            }
        }
        private void OnDrawGizmosSelected()
        {
            DrawGizmos();
            DrawRadiusGizmo();
        }

        protected virtual void DrawRadiusGizmo()
        {
            Gizmos.color = Color.green;
            if (PlayerBoundsRadius != float.PositiveInfinity)
                Gizmos.DrawWireSphere(transform.position, PlayerBoundsRadius);
        }

        protected virtual void DrawGizmos()
        {
            Gizmos.color = Color.red;
            foreach (var t in Connections)
            {
                if (t is not null)
                {
                    Gizmos.DrawLine(transform.position, t.transform.position);
                }
            }
        }

        public virtual TeleportWaypoint DoWarp(Vector3 offset, Transform target, Transform head)
        {
            OnWarp.Invoke();
            target.transform.position = transform.position - (GlobalSettings.PlayerSettings.TeleportCenteredOnHead ? offset : Vector3.zero);
            if (RotatePlayer)
            {
                var lastHeadPos = head.position;
                target.transform.rotation = transform.rotation * (GlobalSettings.PlayerSettings.TeleportRotationRotatesHead ? Quaternion.Euler(0, -head.localEulerAngles.y, 0) : Quaternion.identity);
                var posChange = head.position - lastHeadPos;
                if (GlobalSettings.PlayerSettings.TeleportCenteredOnHead)
                    target.transform.position -= posChange;
            }

            OnHide();

            return this;
        }

        public virtual bool LosCheck(Vector3 from)
        {
            return true;
        }

        [ContextMenu("Build Connections")]
        public void BuildLosConnections()
        {
            if (Connections.Count != 0)
            {
                Debug.LogWarning("Teleporter has existing connections remove them before building", this);
                return;
            }
            foreach (var t in FindObjectsByType<TeleportWaypoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == this || Vector3.Distance(t.transform.position, transform.position) > AutoConnectionDistanceLimit)
                    continue;
                if (!Physics.Linecast(LosCheckTarget.transform.position, t.LosCheckTarget.transform.position, GlobalSettings.PlayerSettings.TeleporterLayers, queryTriggerInteraction: QueryTriggerInteraction.Ignore))
                    Connections.Add(t);
            }
        }

        [ContextMenu("Remove From Connections")]
        public void RemoveFromConnections()
        {
            foreach (var t in FindObjectsByType<TeleportWaypoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                _ = t.Connections.Remove(this);
            }
        }

        [ContextMenu("Snap to floor")]
        public void SnapToFloor()
        {
            if (Physics.Raycast(transform.position, Vector3.down, out var hit))
            {
                transform.position = hit.point;
            }
        }
    }
}
