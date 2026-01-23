// Copyright (c) Meta Platforms, Inc. and affiliates.
using Meta.Utilities;
using Meta.XR.Samples;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Moves the player in a pulling motion when a provided pose is performed
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class GrabMovement : MonoBehaviour
    {
        public enum MoveModes
        {
            Linear,
            Snap,
            Direct,
            Disabled
        }
        public enum MovementLockSetting
        {
            Unlocked,
            Locked,
            UnlockedOnSurface
        }
        [SerializeField] private MovementLockSetting m_horizontalMovementSetting;
        [SerializeField] private MovementLockSetting m_verticalMovementSetting;

        public MoveModes MoveMode;
        [SerializeField, Interface(typeof(ISelector))]
        private Object m_leftSelectorObject;
        private ISelector m_leftSelector;
        [SerializeField, Interface(typeof(ISelector))]
        private Object m_rightSelectorObject;
        private ISelector m_rightSelector;

        [SerializeField] private HandGrabInteractor m_leftInteractor, m_rightInteractor;

        [SerializeField] private Transform m_leftAnchor, m_rightAnchor;
        [SerializeField] private PhysicsMaterial m_stationaryMaterial, m_movingMaterial;
        [SerializeField] private float m_movementMultiplier = 5;

        [SerializeField] private float m_groundDetectionRange;
        [SerializeField] private LayerMask m_groundLayer = int.MaxValue;

        private Vector3 m_startPosition = Vector3.zero;
        private Vector3 m_bodyStartPosition = Vector3.zero;
        private bool m_grabbing = false;
        private ConfigurableJoint m_joint;
        private JointDrive m_movingDrive = new();
        private CapsuleCollider m_playerCollider;
        private Rigidbody m_rigidBody;
        private bool m_leftHand = false;
        private bool m_inLocalSpace = false;
        [SerializeField, AutoSet] private MoveableObject m_moveableObject;

        private void Awake()
        {
            m_joint = gameObject.AddComponent<ConfigurableJoint>();
            m_joint.configuredInWorldSpace = true;
            m_joint.swapBodies = true;
            m_joint.autoConfigureConnectedAnchor = false;
            m_joint.targetPosition = transform.position;
            SetJointDrive(new JointDrive());

            m_playerCollider = GetComponent<CapsuleCollider>();
            m_rigidBody = GetComponent<Rigidbody>();
            m_leftSelector = m_leftSelectorObject as ISelector;
            m_rightSelector = m_rightSelectorObject as ISelector;
        }

        private void UpdateSpring()
        {
            m_movingDrive.positionSpring = GlobalSettings.PlayerSettings.PlayerMovementSpring;
            m_movingDrive.positionDamper = GlobalSettings.PlayerSettings.PlayerMovementDamper;
            m_movingDrive.maximumForce = GlobalSettings.PlayerSettings.PlayerMovementMaxForce;
        }

        private void OnEnable()
        {
            m_leftSelector.WhenSelected += StartLeft;
            m_leftSelector.WhenUnselected += EndLeft;
            m_rightSelector.WhenSelected += StartRight;
            m_rightSelector.WhenUnselected += EndRight;

            m_moveableObject.OnRegisterCallback += OnRegister;
            m_moveableObject.OnUnregisterCallback += OnUnRegister;
        }

        private void OnDisable()
        {
            m_leftSelector.WhenSelected -= StartLeft;
            m_leftSelector.WhenUnselected -= EndLeft;
            m_rightSelector.WhenSelected -= StartRight;
            m_rightSelector.WhenUnselected -= EndRight;

            m_moveableObject.OnRegisterCallback -= OnRegister;
            m_moveableObject.OnUnregisterCallback -= OnUnRegister;
        }

        private void Update()
        {
            UpdateSpring();
            if (m_grabbing)
            {
                var handOnSurface = HandOnSurface();
                if ((m_leftHand && (m_leftInteractor.IsGrabbing || !m_leftInteractor.Hand.IsConnected)) || (!m_leftHand && (m_rightInteractor.IsGrabbing || !m_rightInteractor.Hand.IsConnected)))
                {
                    EndGrab();
                    return;
                }

                if (MoveMode is MoveModes.Linear or MoveModes.Direct)
                {
                    var toStart = m_startPosition - (m_leftHand ? m_leftAnchor.localPosition : m_rightAnchor.localPosition);
                    toStart = transform.rotation * toStart;
                    if (m_verticalMovementSetting == MovementLockSetting.Locked || (!handOnSurface && m_verticalMovementSetting == MovementLockSetting.UnlockedOnSurface))
                    {
                        toStart.y = 0;
                        m_rigidBody.useGravity = true;
                    }
                    else
                    {
                        m_rigidBody.useGravity = false;
                    }
                    if (m_horizontalMovementSetting == MovementLockSetting.Locked || (!handOnSurface && m_horizontalMovementSetting == MovementLockSetting.UnlockedOnSurface))
                    {
                        toStart.x = 0;
                        toStart.z = 0;
                    }
                    toStart *= ((m_horizontalMovementSetting == MovementLockSetting.UnlockedOnSurface && handOnSurface) || (m_verticalMovementSetting == MovementLockSetting.UnlockedOnSurface && handOnSurface)) ? 1 : m_movementMultiplier;
                    var targetPosition = m_bodyStartPosition;
                    if (m_moveableObject.Registered)
                    {
                        //targetPosition += m_moveableObject.RegisteredTo.transform.position;
                        targetPosition = m_moveableObject.RegisteredTo.transform.rotation * targetPosition + m_moveableObject.RegisteredTo.transform.position;
                    }

                    targetPosition += toStart;

                    if (MoveMode is MoveModes.Linear)
                    {
                        m_joint.targetPosition = targetPosition;
                    }
                    else
                    {
                        m_rigidBody.MovePosition(targetPosition);
                    }
                }
            }
            else
            {
                //m_joint.targetPosition = transform.position;
                m_rigidBody.useGravity = true;
            }
        }

        private void StartLeft()
        {
            if (m_leftInteractor.IsGrabbing || !m_leftInteractor.Hand.IsConnected) return;
            if (m_grabbing)
            {
                if (!m_leftHand) EndGrab();
                else return;
            }
            m_grabbing = true;
            m_leftHand = true;
            StartGrab();
        }

        private void StartRight()
        {
            if (m_rightInteractor.IsGrabbing || !m_rightInteractor.Hand.IsConnected) return;
            if (m_grabbing)
            {
                if (m_leftHand) EndGrab();
                else return;
            }
            m_grabbing = true;
            m_leftHand = false;
            StartGrab();
        }
        private void EndLeft()
        {
            if (m_leftHand)
                EndGrab();
        }
        private void EndRight()
        {
            if (!m_leftHand)
                EndGrab();
        }

        private bool HandOnSurface()
        {
            var position = m_leftHand ? m_leftAnchor.position : m_rightAnchor.position;
            return Physics.CheckSphere(position, m_groundDetectionRange, m_groundLayer, QueryTriggerInteraction.Ignore);
        }

        private void StartGrab()
        {
            if (!GlobalSettings.PlayerSettings.MovementEnabled)
            {
                return;
            }
            m_startPosition = m_leftHand ? m_leftAnchor.localPosition : m_rightAnchor.localPosition;
            m_bodyStartPosition = transform.position;
            if (m_moveableObject.Registered)
            {
                m_bodyStartPosition -= m_moveableObject.RegisteredTo.transform.position;
                m_bodyStartPosition = Quaternion.Inverse(m_moveableObject.RegisteredTo.transform.rotation) * m_bodyStartPosition;
                m_inLocalSpace = true;
            }
            else
            {
                m_inLocalSpace = false;
            }
            if (MoveMode == MoveModes.Linear)
            {
                m_playerCollider.material = m_movingMaterial;
                m_joint.autoConfigureConnectedAnchor = false;
                m_joint.anchor = Vector3.zero;
                m_joint.connectedAnchor = Vector3.zero;
                SetJointDrive(m_movingDrive);
                m_joint.configuredInWorldSpace = false;
                m_joint.swapBodies = true;
                Debug.Log("Creating Joint");
            }
        }

        private void EndGrab()
        {
            if (!m_grabbing) return;
            m_grabbing = false;
            if (MoveMode == MoveModes.Linear)
            {
                Debug.Log("End Grab");
                m_playerCollider.material = m_stationaryMaterial;
                SetJointDrive(new JointDrive());
            }
            else if (MoveMode == MoveModes.Snap)
            {
                var toStart = m_startPosition - (m_leftHand ? m_leftAnchor.localPosition : m_rightAnchor.localPosition);
                toStart = transform.rotation * toStart;
                toStart.y = 0;

                var targetPosition = m_bodyStartPosition + toStart * m_movementMultiplier;
                if (m_moveableObject.Registered)
                {
                    targetPosition += m_moveableObject.RegisteredTo.transform.position;
                    targetPosition = m_moveableObject.RegisteredTo.transform.rotation * targetPosition;
                }
                //TODO Raycast from player to avoid clipping into walls
                transform.position = targetPosition;
            }
        }

        private void SetJointDrive(JointDrive drive)
        {
            m_joint.xDrive = drive;
            m_joint.yDrive = drive;
            m_joint.zDrive = drive;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(m_leftAnchor.position, m_groundDetectionRange);
            Gizmos.DrawWireSphere(m_rightAnchor.position, m_groundDetectionRange);
        }

        private void OnRegister(ParentedTransform owner)
        {
            if (m_inLocalSpace)
                return;
            m_inLocalSpace = true;
            m_bodyStartPosition -= owner.transform.position;
            m_bodyStartPosition = Quaternion.Inverse(owner.transform.rotation) * m_bodyStartPosition;
        }
        private void OnUnRegister(ParentedTransform owner)
        {
            if (!m_inLocalSpace)
                return;
            m_inLocalSpace = false;
            m_bodyStartPosition += owner.transform.position;
            m_bodyStartPosition = owner.transform.rotation * m_bodyStartPosition;
        }
    }
}
