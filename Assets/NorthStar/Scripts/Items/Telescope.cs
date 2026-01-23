// Copyright (c) Meta Platforms, Inc. and affiliates.
using Meta.Utilities;
using Meta.Utilities.ViewportRenderer;
using Meta.XR.Samples;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace NorthStar
{
    /// <summary>
    /// Interactable object that can be extended and looked through by the player
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class Telescope : Holsterable
    {
        [Header("Eye Anchors"), SerializeField]
        private Rigidbody m_leftEyeRigidbody;
        [SerializeField]
        private Rigidbody m_rightEyeRigidbody;

        [Space(5), SerializeField]
        private Transform m_telescopeCamera;

        [SerializeField]
        private ViewportRenderer m_telescopeRenderer;

        [Space(5), SerializeField]
        private Vector3 m_cameraOffset;


        [System.Serializable]
        private struct Half
        {
            public Transform Transform;
            public Rigidbody Rigidbody;
        }

        [Header("Joint components"), SerializeField]
        private Half m_telescopeFront;
        [SerializeField]
        private Half m_telescopeEnd;

        [Space(5), SerializeField]
        private ConfigurableJoint m_joint;
        [Space(5), SerializeField]
        private ConfigurableJoint m_eyeJoint;

        [Header("Eye piece"), SerializeField]
        private MeshRenderer m_eyePieceMeshRenderer;

        [System.Serializable]
        private struct Part
        {
            public Transform Transform;
            [Space(5)]
            public Vector3 LocalPositionClosed;
            public Vector3 LocalPositionOpen;
        }

        [Space(10), SerializeField]
        private Part[] m_parts;

        private bool m_enabled = false;

        private bool m_rightEyeUsed;

        // These are the minimum and maximum distances the main joint for the telescope can have
        private const float CLOSEDDISTANCE = 0.1f; //TODO: derive these two values from the joint's parameters
        private const float OPENEDDISTANCE = 0.35f;

        [Header("Eye transition"), SerializeField, Tooltip("How close the eye piece has to be before it is considered for locking")]
        private float m_maximumDistance = 0.13f;
        [SerializeField, Tooltip("The fraction of m_maximumDistance that is used to transition into the locked eye state")]
        private float m_dampeningRatio = 0.45f;
        [Space(5), SerializeField, Tooltip("The amount of drag applied to the end rigidbody when fully locked to the eye")]
        private float m_lockedDrag = 100;
        [SerializeField, Tooltip("The amount of drag applied to the end rigidbody based on its proximity to the eye")]
        private AnimationCurve m_dragCurve;
        [Space(5), SerializeField, Tooltip("The amount of spring applied to the end joint when fully locked to the eye")]
        private float m_lockedSpring = 300;
        [SerializeField, Tooltip("The amount of spring applied to the eye joint based on its proximity to the eye")]
        private AnimationCurve m_springTransitionCurve;

        [Header("Camera Dampening"), SerializeField]
        private AnimationCurve m_rotationDampening;

        private Vector3 m_velocity;

        public TMP_Text AngularDampiningReadout;
        public TMP_Text LinearDampiningReadout;

        [SerializeField] // For debug
        private bool m_forceRender;

        private RenderTexture m_telescopeRenderTexture;

        protected override void Start()
        {
            base.Start();
            m_telescopeRenderer.enabled = m_enabled;

            m_joint.targetPosition = Vector3.right * (m_enabled ? -m_joint.linearLimit.limit : m_joint.linearLimit.limit);

            m_eyeJoint.connectedBody = m_leftEyeRigidbody;
            SetEyeStability(0);

            m_telescopeRenderer.MarkForPrewarm();
        }

        private void Update()
        {
            //Get the distance between the two grab points to determine how extended the telescope should be
            var val = Vector3.Distance(m_telescopeFront.Transform.position, m_telescopeEnd.Transform.position).Map(CLOSEDDISTANCE, OPENEDDISTANCE, 0f, 1f);

            UpdateState(val > 0.5f || m_forceRender);

            if (m_enabled)
            {
                //Get the closer eye and use that as the prefered eye for looking out of
                var distanceLeft = Vector3.Distance(m_telescopeFront.Transform.position, m_leftEyeRigidbody.position);
                var distanceRight = Vector3.Distance(m_telescopeFront.Transform.position, m_rightEyeRigidbody.position);

                Vector3 eyePosition;
                if (distanceLeft < distanceRight)
                {
                    eyePosition = m_leftEyeRigidbody.position;

                    if (m_rightEyeUsed)
                    {
                        m_eyeJoint.connectedBody = m_leftEyeRigidbody; //Only trigger these once per swap incase there is unexpected overhead
                    }
                    m_rightEyeUsed = false;
                }
                else
                {
                    eyePosition = m_rightEyeRigidbody.position;

                    if (!m_rightEyeUsed)
                    {
                        m_eyeJoint.connectedBody = m_rightEyeRigidbody; //Only trigger these once per swap incase there is unexpected overhead
                    }
                    m_rightEyeUsed = true;
                }

                MoveCamera(eyePosition);

                //Set stability and eye snapping
                if (IsGrabbed)
                {
                    var stability = Vector3.Distance(m_telescopeFront.Transform.position, eyePosition).ClampedMap(0f, m_maximumDistance, 1, 0);

                    if (stability < 0)
                    {
                        stability = 0;
                    }

                    SetEyeStability(stability);
                }
                else
                {
                    SetEyeStability(0);
                }
            }

            for (var i = 0; i < m_parts.Length; i++)
            {
                m_parts[i].Transform.localPosition = Vector3.Lerp(m_parts[i].LocalPositionClosed, m_parts[i].LocalPositionOpen, val);
            }
        }

        /// <summary>
        /// Determines whether the telescope should be extended or collapsed, and consequently if its features should be enabled.
        /// </summary>
        /// <param name="value">Extends the telescope when true</param>
        public void UpdateState(bool value)
        {
            if (value && !IsGrabbed && !m_forceRender) //Force state false if not grabbed
            {
                value = false;
            }

            m_joint.targetPosition = Vector3.right * (value ? -m_joint.linearLimit.limit : m_joint.linearLimit.limit);

            if (!value)
            {
                SetEyeStability(0);
            }

            if (m_telescopeRenderer != null)
            {
                m_telescopeRenderer.enabled = value;
            }

            m_enabled = value;
        }

        /// <summary>
        /// Determines where the camera should be relative to the telescope and the dominant eye
        /// </summary>
        /// <param name="eyePosition">The position of the dominant eye in worldspace</param>
        private void MoveCamera(Vector3 eyePosition)
        {
            //Offset the camera based on the telescope position and the characters eye, giving it some paralax / depth based on where you look through
            var targetPos = m_telescopeFront.Transform.position + m_telescopeEnd.Transform.position - eyePosition + m_telescopeEnd.Transform.TransformDirection(m_cameraOffset);
            m_telescopeCamera.position = Vector3.SmoothDamp(m_telescopeCamera.position, targetPos, ref m_velocity, 0.5f);

            //Debug logging to check dampening strength
            if (LinearDampiningReadout != null)
            {
                LinearDampiningReadout.text = m_velocity.ToString(".000");
            }

            var angle = Quaternion.Angle(m_telescopeCamera.rotation, m_telescopeFront.Transform.rotation);
            //Add a dampening based on how far the angle is, the smaller the angle, the less it should contribute to the rotation
            var dampenedAngle = Time.deltaTime * m_rotationDampening.Evaluate(angle);

            if (dampenedAngle <= 0.001f) //TODO: Determine if this is redudant / not causing the loss of dampening
            {
                dampenedAngle = 0.001f;
            }

            //Debug logging to check dampening strength
            if (AngularDampiningReadout != null)
            {
                AngularDampiningReadout.text = dampenedAngle.ToString("0.0000");
            }

            var outputRotation = Quaternion.RotateTowards(m_telescopeCamera.rotation, m_telescopeFront.Transform.rotation, dampenedAngle);
            //Force the euler angle along the z axis to be exact, as the visual lag behind in that axis
            outputRotation.eulerAngles = new Vector3(outputRotation.eulerAngles.x, outputRotation.eulerAngles.y, m_telescopeFront.Transform.eulerAngles.z);

            m_telescopeCamera.rotation = outputRotation;
        }

        /// <summary>
        /// Transitions the joint on the telescope's eyepiece from free to locked
        /// </summary>
        /// <param name="strength">
        /// Value between 0-<see cref="m_dampeningRatio"/> has the joint's springs ramps increase according to <see cref="m_springTransitionCurve"/><br/>
        /// Value greater than <see cref="m_dampeningRatio"/> is a locked joint
        /// </param>
        private void SetEyeStability(float strength)
        {
            var limitContainer = m_eyeJoint.linearLimit;
            var springContainer = m_eyeJoint.linearLimitSpring;

            if (strength < 0.01f)
            {
                m_eyeJoint.xMotion = ConfigurableJointMotion.Free;
                m_eyeJoint.yMotion = ConfigurableJointMotion.Free;
                m_eyeJoint.zMotion = ConfigurableJointMotion.Free;

                m_telescopeEnd.Rigidbody.linearDamping = 0;
            }
            else if (strength < m_dampeningRatio)
            {
                //springContainer.spring = stabilityCurve.Evaluate(strength / DAMPENINGRATIO);// strength.ClampedMap(0, 0.25f, 100, 500);

                springContainer.spring = m_springTransitionCurve.Evaluate(strength / m_dampeningRatio);// strength.ClampedMap(0, DAMPENINGRATIO, 300, 2500);// strength.ClampedMap(0, 0.25f, 100, 500);
                limitContainer.limit = strength.ClampedMap(0, m_dampeningRatio, m_maximumDistance, 0);

                //if (limit.limit == 0)
                {
                    m_eyeJoint.xMotion = ConfigurableJointMotion.Free;
                    m_eyeJoint.yMotion = ConfigurableJointMotion.Limited;
                    m_eyeJoint.zMotion = ConfigurableJointMotion.Limited;
                }
                m_telescopeEnd.Rigidbody.linearDamping = m_dragCurve.Evaluate(strength / m_dampeningRatio);
            }
            else
            {
                springContainer.spring = m_lockedSpring;
                //var container = m_eyeJoint.linearLimitSpring;
                //container.spring = 300;// strength.ClampedMap(0, 0.25f, 100, 500);
                //m_eyeJoint.linearLimitSpring = container;

                limitContainer.limit = 0.01f;

                //if (limit.limit == 0.01f)
                {
                    m_eyeJoint.xMotion = ConfigurableJointMotion.Limited;
                    m_eyeJoint.yMotion = ConfigurableJointMotion.Locked;
                    m_eyeJoint.zMotion = ConfigurableJointMotion.Locked;
                }
                m_telescopeEnd.Rigidbody.linearDamping = m_lockedDrag;
            }

            m_eyeJoint.linearLimitSpring = springContainer;
        }

        protected override void OnValidate()
        {
            base.OnValidate();

            m_leftEyeRigidbody = GameObject.Find("LeftEyeRigidbody")?.GetComponent<Rigidbody>();
            m_rightEyeRigidbody = GameObject.Find("RightEyeRigidbody")?.GetComponent<Rigidbody>();


            if (m_telescopeEnd.Transform != null && m_telescopeEnd.Rigidbody == null)
            {
                m_telescopeEnd.Rigidbody = m_telescopeEnd.Transform.GetComponent<Rigidbody>();
            }

            if (m_telescopeFront.Transform != null && m_telescopeFront.Rigidbody == null)
            {
                m_telescopeFront.Rigidbody = m_telescopeFront.Transform.GetComponent<Rigidbody>();
            }
        }
    }
}
