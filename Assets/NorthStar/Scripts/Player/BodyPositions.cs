// Copyright (c) Meta Platforms, Inc. and affiliates.
using System;
using System.Collections;
using Meta.XR.Samples;
using Oculus.Interaction.Input;
using Oculus.Movement.AnimationRigging;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace NorthStar
{
    /// <summary>
    /// Manages logic and visuals relating to body tracking
    /// 
    /// A custom leg IK solution is used here to support seated and standing play with an offset rig
    /// </summary>
    [MetaCodeSample("NorthStar")]
    [DefaultExecutionOrder(9999)] // Must be higher than FakeMovement to keep player upright
    public class BodyPositions : MonoBehaviour
    {
        public bool BodyTrackingActive => OVRPlugin.bodyTrackingEnabled && m_body is not null && m_body.GetBodyTrackingCalibrationStatus() != OVRPlugin.BodyTrackingCalibrationState.Invalid;

        [SerializeField] private Animator m_animator;
        [SerializeField] public Transform Head;

        [SerializeField, Range(-1, 1)] private float m_breakCutoff = .5f;
        [SerializeField, Min(0)] private float m_distanceCutoff = 1.0f;
        [field: SerializeField] public Transform[] WristAnchors { get; private set; }
        [field: SerializeField] public SyntheticHand[] SyntheticHands { get; private set; }
        [field: SerializeField] public Transform[] HandVisuals { get; private set; }
        [field: SerializeField] public Transform CameraRig { get; private set; }

        public static BodyPositions Instance { get; private set; }

        public enum BodySide
        {
            Left,
            Right
        }

        public enum LegState
        {
            Planted,
            Stepping
        }

        [Serializable]
        public class LocomotionSettings
        {
            public float StepThreshold = 0.3f;
            public float StepDuration = 0.5f;
            public float StepHeight = 0.2f;
            public float StepPlantedPause = 0.25f;
            public float ExclusionRadius = 0.2f;
            public AnimationCurve StepHeightCurve;
        }

        [Serializable]
        public class LegSettings
        {
            public BodySide Side;
            public TwoBoneIKConstraint Constraint;
            [NonSerialized] public LegState State;
            [NonSerialized] public float StepInterp;
            [NonSerialized] public Vector3 TargetPositionOffset;
            [NonSerialized] public Quaternion TargetRotationOffset;
            [NonSerialized] public Vector3 LastPlantedPosition;
            [NonSerialized] public Quaternion LastPlantedRotation;
        }

        [SerializeField] private LegSettings m_leftLegSettings;
        [SerializeField] private LegSettings m_rightLegSettings;
        [SerializeField] private LocomotionSettings m_locomotionSettings;

        private const float SHOULDER_SPAN_RATIO = .25f;
        private const float SHOULDER_HEIGHT_RATIO = 0.875f;
        private float m_elbowToWristLength;

        private Transform m_leftShoulderBone, m_rightShoulderBone;
        private OVRBody m_body;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            m_body = GetComponentInChildren<OVRBody>(); // Attempt to get the OVRBody for more tracking info
            m_leftShoulderBone = m_animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            m_rightShoulderBone = m_animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            m_elbowToWristLength = Vector3.Distance(m_animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position, m_animator.GetBoneTransform(HumanBodyBones.LeftHand).position);

            SetupLeg(m_leftLegSettings);
            SetupLeg(m_rightLegSettings);

            OVRManager.HMDMounted += OVRManager_HMDMounted;
        }

        private void OnDestroy()
        {
            OVRManager.HMDMounted -= OVRManager_HMDMounted;
        }

        private IEnumerator ResetTracking()
        {
            yield return new WaitForSeconds(1.0f);
            m_body.enabled = false;
            yield return null;
            m_body.enabled = true;
            yield return null;
            FindFirstObjectByType<RetargetingAnimationConstraint>()?.RegenerateData();
        }

        private void OVRManager_HMDMounted()
        {
            // force body tracking to restart, since sleep mode will sometimes break it
            if (m_body && GlobalSettings.PlayerSettings.ResetBodyTrackingOnWake)
            {
                // Dont reset if this is the first frame - headset has been freshly initialised
                // and this can cause a hitch
                if (Time.timeSinceLevelLoad <= 0.0f) return;

                _ = StartCoroutine(ResetTracking());
            }
        }

        public void ResetLegPositions()
        {
            ResetLeg(m_leftLegSettings);
            ResetLeg(m_rightLegSettings);
        }

        private void ResetLeg(LegSettings settings)
        {
            settings.State = LegState.Planted;
            GetTargetPositionAndRotation(settings, out settings.LastPlantedPosition, out settings.LastPlantedRotation);
        }

        private void SetupLeg(LegSettings settings)
        {
            var constraintData = settings.Constraint.data;
            settings.TargetPositionOffset = constraintData.tip.position - constraintData.target.position;
            settings.TargetRotationOffset = Quaternion.Inverse(transform.rotation) * constraintData.tip.rotation;
            ResetLeg(settings);
        }

        private void GetTargetPositionAndRotation(LegSettings settings, out Vector3 position, out Quaternion rotation)
        {
            var groundLevel = transform.TransformPoint(Vector3.zero).y;
            var constraintData = settings.Constraint.data;
            var legTarget = constraintData.target.position;
            var hipPos = constraintData.root.position;
            legTarget.x = hipPos.x;
            legTarget.y = groundLevel + settings.TargetPositionOffset.y;
            legTarget.z = hipPos.z;

            position = legTarget;

            var hipsForward = Vector3.Scale(constraintData.root.parent.up, new Vector3(1, 0, 1)).normalized;
            rotation = Quaternion.LookRotation(hipsForward, Vector3.up);

            // Transform into parent space
            if (transform.parent != null)
            {
                position = transform.parent.InverseTransformPoint(position);
                rotation = Quaternion.Inverse(transform.parent.rotation) * rotation;
            }
        }

        private void UpdateTargetPositionAndRotation(LegSettings legSettings, Vector3 position, Quaternion rotation)
        {
            var constraintData = legSettings.Constraint.data;

            if (transform.parent)
            {
                constraintData.target.position = transform.parent.TransformPoint(position);
                constraintData.target.rotation = transform.parent.rotation * rotation;
            }
            else
            {
                constraintData.target.position = position;
                constraintData.target.rotation = rotation;
            }
        }

        private void UpdateLeg(LegSettings legSettings, LegSettings otherLegSettings, float dt)
        {
            var constraintData = legSettings.Constraint.data;

            if (legSettings.State == LegState.Planted)
            {
                UpdateTargetPositionAndRotation(legSettings, legSettings.LastPlantedPosition, legSettings.LastPlantedRotation * legSettings.TargetRotationOffset);

                GetTargetPositionAndRotation(legSettings, out var targetPosition, out var _);

                var distance = Vector3.Distance(legSettings.LastPlantedPosition, targetPosition);
                if (distance > m_locomotionSettings.StepThreshold && otherLegSettings.State == LegState.Planted)
                {
                    legSettings.State = LegState.Stepping;
                    legSettings.StepInterp = 0;
                }
            }
            else if (legSettings.State == LegState.Stepping)
            {
                GetTargetPositionAndRotation(legSettings, out var targetPosition, out var targetRotation);

                legSettings.StepInterp = Mathf.Clamp01(legSettings.StepInterp + dt / m_locomotionSettings.StepDuration);

                if (legSettings.StepInterp >= 1)
                {
                    legSettings.LastPlantedPosition = targetPosition;
                    legSettings.LastPlantedRotation = targetRotation;
                    legSettings.StepInterp = 0;
                    legSettings.State = LegState.Planted;

                    UpdateTargetPositionAndRotation(legSettings, legSettings.LastPlantedPosition, legSettings.LastPlantedRotation * legSettings.TargetRotationOffset);
                }
                else
                {
                    var position = Vector3.Lerp(legSettings.LastPlantedPosition, targetPosition, legSettings.StepInterp);
                    position += Vector3.up * m_locomotionSettings.StepHeight * m_locomotionSettings.StepHeightCurve.Evaluate(legSettings.StepInterp);

                    var rotation = Quaternion.Slerp(legSettings.LastPlantedRotation, targetRotation, legSettings.StepInterp);

                    UpdateTargetPositionAndRotation(legSettings, position, rotation * legSettings.TargetRotationOffset);
                }
            }

            var hipForward = constraintData.root.up * (legSettings.Side == BodySide.Left ? -1 : 1);
            var footForward = constraintData.tip.up * (legSettings.Side == BodySide.Left ? -1 : 1);
            var hintForward = Vector3.Lerp(hipForward, footForward, 0.5f).normalized;
            constraintData.hint.position = Vector3.Lerp(constraintData.root.position, constraintData.tip.position, 0.5f) + hintForward * 1.0f;
        }

        private void FixedUpdate()
        {
            UpdateLeg(m_leftLegSettings, m_rightLegSettings, Time.fixedDeltaTime);
            UpdateLeg(m_rightLegSettings, m_leftLegSettings, Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            FixHorizon();
        }

        private void FixHorizon()
        {
            var reorientStrength = GlobalSettings.PlayerSettings.ReorientStrength;
            if (reorientStrength > 0 && BoatController.Instance != null)
            {
                var boatRotation = BoatController.Instance.MovementSource.CurrentRotation;
                CameraRig.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(boatRotation), GlobalSettings.PlayerSettings.ReorientStrength) * CameraRig.parent.localRotation;
            }
            else
            {
                CameraRig.localRotation = Quaternion.identity;
            }
        }

        private Vector3 EstimateShoulderPosition(bool right)
        {
            var toSide = Vector3.Scale(Head.right * (right ? 1 : -1), new Vector3(1, 0, 1)).normalized;
            var heightInUnits = GlobalSettings.PlayerSettings.Height / 100;
            var neckRoot = Head.TransformPoint(new Vector3(0, -heightInUnits * (1 - SHOULDER_HEIGHT_RATIO), 0));
            return neckRoot + toSide * (heightInUnits * (SHOULDER_SPAN_RATIO / 2));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(EstimateShoulderPosition(false), 0.05f);
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(EstimateShoulderPosition(true), 0.05f);
        }

        public Vector3 GetShoulderPosition(bool right)
        {
            return BodyTrackingActive ? (right ? m_rightShoulderBone : m_leftShoulderBone).position : EstimateShoulderPosition(right);
        }

        public static Transform GetLeftHand()
        {
            return Instance.SyntheticHands[0].transform;
        }

        public static Transform GetRightHand()
        {
            return Instance.SyntheticHands[1].transform;
        }

        public Vector3 GetRelativeHandAngles(HumanBodyBones bodyBone)
        {
            var hand = m_animator.GetBoneTransform(bodyBone);
            var elbow = m_animator.GetBoneTransform(bodyBone == HumanBodyBones.LeftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);

            return GetRelativeHandAngles(elbow, hand);
        }

        public Vector3 GetRelativeHandAngles(Transform elbow, Transform hand)
        {
            var twist = Vector3.SignedAngle(elbow.up, hand.up, elbow.right);
            var upDown = Vector3.SignedAngle(elbow.right, hand.right, hand.up);
            var sideToSide = Vector3.SignedAngle(elbow.up, hand.up, elbow.forward);

            return new Vector3(twist, upDown, sideToSide);
        }

        public bool IsHandWithinLimits(HumanBodyBones bodyBone)
        {
            if (bodyBone is not HumanBodyBones.LeftHand and not HumanBodyBones.RightHand) return false;
            Transform anchor;
            Transform hand;
            if (HumanBodyBones.LeftHand == bodyBone)
            {
                anchor = WristAnchors[0];
                hand = SyntheticHands[0].transform;
            }
            else
            {
                anchor = WristAnchors[1];
                hand = SyntheticHands[1].transform;
            }

            if (Vector3.Dot(anchor.forward, hand.forward) < m_breakCutoff)
                return false;
            if (Vector3.Dot(anchor.up, hand.up) < m_breakCutoff)
                return false;
            if (Vector3.Dot(anchor.right, hand.right) < m_breakCutoff)
                return false;
            if (Vector3.Distance(anchor.position, hand.position) > m_distanceCutoff)
                return false;

            if (!BodyTrackingActive) return true;

            var handBone = m_animator.GetBoneTransform(bodyBone);
            var elbow = m_animator.GetBoneTransform(bodyBone == HumanBodyBones.LeftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);

            var elbowToWristLength = Vector3.Distance(handBone.position, elbow.position) / m_animator.transform.localScale.x;
            var stretch = elbowToWristLength - m_elbowToWristLength;

            return stretch < GlobalSettings.PlayerSettings.ArmStretchLimit;
        }
    }
}
