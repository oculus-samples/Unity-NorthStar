// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;
using Oculus.Interaction;
using Unity.Mathematics;
using UnityEngine;

namespace NorthStar.DebugUtilities
{
    [MetaCodeSample("NorthStar")]
    public class ResetButton : MonoBehaviour
    {
        [SerializeField, Interface(typeof(IInteractableView))]
        private Object m_interactableView;
        private IInteractableView InteractableView { get; set; }

        private bool m_started;

        [SerializeField]
        private Transform[] m_transforms = new Transform[0];

        private Rigidbody[] m_rigidbodies;
        private Vector3[] m_initialPositions;
        private quaternion[] m_initialRotations;

        protected virtual void Awake()
        {
            InteractableView = m_interactableView as IInteractableView;
        }

        protected virtual void Start()
        {
            this.BeginStart(ref m_started);

            this.AssertField(InteractableView, nameof(InteractableView));

            m_initialPositions = new Vector3[m_transforms.Length];
            m_initialRotations = new quaternion[m_transforms.Length];
            m_rigidbodies = new Rigidbody[m_transforms.Length];

            for (var i = 0; i < m_transforms.Length; i++)
            {
                m_initialPositions[i] = m_transforms[i].position;
                m_initialRotations[i] = m_transforms[i].rotation;
                m_rigidbodies[i] = GetComponent<Rigidbody>();
            }

            this.EndStart(ref m_started);
        }

        protected virtual void OnEnable()
        {
            if (m_started)
            {
                InteractableView.WhenStateChanged += OnStateChange;
            }
        }

        protected virtual void OnDisable()
        {
            if (m_started)
            {
                InteractableView.WhenStateChanged -= OnStateChange;
            }
        }

        private void OnStateChange(InteractableStateChangeArgs args)
        {
            //If button pressed
            if (args.NewState == InteractableState.Select)
            {
                for (var i = 0; i < m_transforms.Length; i++)
                {
                    //Reset to initial state
                    m_transforms[i].position = m_initialPositions[i];
                    m_transforms[i].rotation = m_initialRotations[i];

                    //If transform posesses a ridiged body, reset its values
                    if (m_rigidbodies[i] != null)
                    {
                        m_rigidbodies[i].linearVelocity = Vector3.zero;
                        m_rigidbodies[i].angularVelocity = Vector3.zero;
                        //m_rigidbodies[i].Sleep();
                    }
                }
            }
        }
    }
}