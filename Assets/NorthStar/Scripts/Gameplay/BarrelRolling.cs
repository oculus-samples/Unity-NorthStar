// Copyright (c) Meta Platforms, Inc. and affiliates.
using System;
using System.Collections;
using Meta.XR.Samples;
//using System.Security;
//using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

namespace NorthStar
{
    /// <summary>
    /// handles the barrel rolling interactions
    /// </summary>
    [MetaCodeSample("NorthStar")]
    public class BarrelRolling : MonoBehaviour
    {
        public Vector3 StartPosition { get => m_startPosition; set => m_startPosition = value; }
        [SerializeField]
        private Vector3 m_startPosition = new(1f, 0, 0);
        [SerializeField] private AnimationCurve m_startToEndForceCurve;
        public Vector3 EndPosition { get => m_endPosition; set => m_endPosition = value; }
        [SerializeField]
        private Vector3 m_endPosition = new(1f, 0, 0);
        [SerializeField] private Rigidbody m_barrelRigidBody;
        [Range(0f, 100f)]
        [SerializeField] private float m_barrelMaxForce = 5.0f;
        private Vector3 m_forceToAdd;
        [Tooltip("Adding a slight vertical offset to the position where force is added will make the barrel roll better rather than trying to slide it along the ground")]
        [SerializeField] private Vector3 m_barrelForcePositionOffset = new(0f, 0.4f, 0f);
        [SerializeField] private bool m_flipForceDirection = false;
        [SerializeField] private bool m_moveBarrelToStartPos = true;
        [Tooltip("To avoid a snap/drop when switching between the animated barrel and a physics version, we will ramp up to full gravity over a few frames")]
        [SerializeField] private bool m_useGravityWarmup = true;
        [Range(0.01f, 1f)]
        [SerializeField] private float m_gravityWarumpTime = 0.5f;

        [Header("Start Speed will give velocity in units/sec from start to end points")]
        [Range(0f, 10f)]
        [SerializeField] private float m_startSpeed = 3f;
        [SerializeField] private bool m_useStartSpeed = true;

        [Header("Optional: If set will adopt this transforms position and rotation on Awake")]
        [SerializeField] private Transform m_startTransformRef;

        [Header("Gizmo Options")]
        [SerializeField] private bool m_drawGizmos = true;
        [SerializeField] private Mesh m_barrelGizmoMesh;

        [Header("End Event Options")]
        [Tooltip("If selected, an event will be fired when the barrel is further from the start than the total start to end distance")]
        [SerializeField] private bool m_useFinishEvent = false;
        private bool m_hasFinished = false;
        public UnityEvent OnFinishEvent;

        [Header("Debug Options")]
        [Tooltip("In editor, have a minimum level of force applied regardless of the force curve")]
        [Range(0f, 100f)]
        [SerializeField] private float m_debugMinForce = 2f;

        [SerializeField] private bool m_rotateOtherObjectsToMatch = true;
        [SerializeField] private GameObject[] m_objectsToRotate;

        [Range(0f, 1f)]
        [SerializeField] private float m_interactionSuccessfulDistance = 0.65f;
        [SerializeField] private UnityEvent m_onInteractionSuccessfulEvent;
        private bool m_hasTriggeredInteractionSuccessfulEvents = false;


        private void Awake()
        {
            if (m_startTransformRef != null)
            {
                m_barrelRigidBody.transform.position = m_startTransformRef.position;
                m_barrelRigidBody.transform.rotation = m_startTransformRef.rotation;
            }
            if (m_useStartSpeed)
            {
                SetStartVelocity();
            }
            if (m_moveBarrelToStartPos)
            {
                if (m_startTransformRef == null)
                {
                    m_barrelRigidBody.position = m_startPosition;
                }
                else
                {
                    m_barrelRigidBody.position = m_startTransformRef.position;
                    m_barrelRigidBody.rotation = m_startTransformRef.rotation;
                }
            }
            if (m_useGravityWarmup)
            {
                _ = StartCoroutine(GravityWarmup());
            }
        }

        private IEnumerator GravityWarmup()
        {
            var startTime = Time.time;
            m_barrelRigidBody.useGravity = false;
            while (Time.time <= startTime + m_gravityWarumpTime)
            {
                var gravityForceToAdd = Physics.gravity * Mathf.Lerp(0, 1, (Time.time - startTime) / m_gravityWarumpTime);
                m_barrelRigidBody.AddForce(gravityForceToAdd, ForceMode.Acceleration);
                yield return new WaitForFixedUpdate();
            }
            m_barrelRigidBody.useGravity = true;
        }

        private void SetStartVelocity()
        {
            //todo, optioon to make this start velocity ramp up over a few frames rather than being instantaneous so it feels more natuaral
            var velocityToSet = (EndPosition - StartPosition).normalized * m_startSpeed;
            m_barrelRigidBody.linearVelocity = velocityToSet;
        }

        private void FixedUpdate()
        {
            if (m_hasFinished)
            {
                //This should disable itself when complete, but if for some reason it hasn't been set to we don't need to do the other work in here anyway
                return;
            }
            //Evaluate curve to figure out how much force should be applied
            //This assumes the barrel can only move on a straight line between the two points!
            //This should also be moved into a coroutine (or make this script turn itself off once the movement has finished)
            var distanceBetweenPoints = Vector3.Distance(m_startPosition, m_endPosition);
            var progressDist = Vector3.Distance(m_startPosition, m_barrelRigidBody.position);
            var evaluationValue = progressDist / distanceBetweenPoints;
            if (!m_hasTriggeredInteractionSuccessfulEvents && evaluationValue >= m_interactionSuccessfulDistance)
            {
                m_onInteractionSuccessfulEvent.Invoke();
                m_hasTriggeredInteractionSuccessfulEvents = true;
            }
            if (!m_hasFinished && evaluationValue >= 1)
            {
                if (m_rotateOtherObjectsToMatch)
                {
                    foreach (var go in m_objectsToRotate)
                    {
                        go.transform.rotation = m_barrelRigidBody.transform.rotation;
                    }
                }
                if (m_useFinishEvent)
                {
                    OnFinishEvent.Invoke();
                }
                m_hasFinished = true;
            }
            var evaluatedMultiplier = m_barrelMaxForce * m_startToEndForceCurve.Evaluate(evaluationValue);
            //Editor only override to allow playing of sequences without needing to interact with the barrel in VR
#if UNITY_EDITOR
            if (evaluatedMultiplier <= m_debugMinForce)
            {
                evaluatedMultiplier = m_debugMinForce;
            }
#endif
            m_forceToAdd = !m_flipForceDirection
                ? (m_endPosition - m_startPosition).normalized * evaluatedMultiplier
                : (m_startPosition - m_endPosition).normalized * evaluatedMultiplier;
            m_barrelRigidBody.AddForceAtPosition(m_forceToAdd, m_barrelRigidBody.position + m_barrelForcePositionOffset);
        }

        //This function just exists for debugging purposes, used in test scene to set up ping ponging of the barrel
        public void FlipDirection()
        {
            m_flipForceDirection = !m_flipForceDirection;
        }

        private void OnDrawGizmosSelected()
        {
            if (m_drawGizmos)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireMesh(m_barrelGizmoMesh, m_startPosition, m_barrelRigidBody.transform.rotation, m_barrelRigidBody.transform.localScale);
                Gizmos.color = Color.red;
                Gizmos.DrawWireMesh(m_barrelGizmoMesh, m_endPosition, m_barrelRigidBody.transform.rotation, m_barrelRigidBody.transform.localScale);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(m_startPosition, m_endPosition);
            }
        }


    }
}
