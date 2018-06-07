using Improbable.Unity;
using Improbable.Unity.Visualizer;
using Improbable.Worker;
using NetworkOptimization;
using RogueFleet.Ship;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.GameLogic.Ship
{
    [WorkerType(WorkerPlatform.UnityClient), DontAutoEnable]
    public class ClientShipPhysics : MonoBehaviour
    {
        [Header("Exponentialy Smoothed Moving Average Error")]

        [Range(0, 100)]
        public uint positionSmoothing = 0;

        [Range(0, 100)]
        public uint rotationSmoothing = 0;

        [Header("Position Error Reduction")]

        public float maxReductionAtDistance = 1f;

        [Range(0, 100)]
        public uint maxPositionReduction = 100;
        
        [Range(0, 100)]
        public uint minPositionReduction = 100;

        [Header("Rotation Error Reduction")]

        [Range(0.01f, 1f)]
        public float maxReductionAtMagnitude = 0.01f;

        [Range(0, 100)]
        public uint maxRotationReduction = 100;

        [Range(0, 100)]
        public uint minRotationReduction = 100;
        
        [Header("Misc.")]

        public bool useRemoteStateBuffer = true;
        public bool showDebugInfo = false;

        public Rigidbody shipRigidbodyPrefab;
        Rigidbody shipExternalRigidbody;

        const byte bufferCount = 5;
        Queue<Vector3> positionBuffer = new Queue<Vector3>(bufferCount);
        Queue<Quaternion> rotationBuffer = new Queue<Quaternion>(bufferCount);
        Queue<Vector3> velocityBuffer = new Queue<Vector3>(bufferCount);
        Queue<Vector3> angularVelocityBuffer = new Queue<Vector3>(bufferCount);
        
        Vector3 positionErrorOffset;
        Vector3 smoothedPosition;
        float biggestPositionError;

        Quaternion rotationErrorOffset;
        Quaternion smoothedRotation;
        float biggestRotationError;

        [Require] ShipPhysics.Reader ShipPhysicsReader;

        void OnEnable()
        {
            ShipPhysicsReader.PositionUpdated.AddAndInvoke(OnPositionUpdated);
            ShipPhysicsReader.RotationUpdated.AddAndInvoke(OnRotationUpdated);
            ShipPhysicsReader.LinearVelocityUpdated.AddAndInvoke(OnVelocityUpdated);
            ShipPhysicsReader.AngularVelocityUpdated.AddAndInvoke(OnAngularVelocityUpdated);
        }

        void Start()//Called only once in the object's lifetime which is desired behavior in this case
        {
            shipExternalRigidbody = Instantiate(shipRigidbodyPrefab, transform.position, transform.rotation);

            var engine = GetComponentInChildren<ShipEngine>();
            
            shipExternalRigidbody.centerOfMass = engine.ShipBody.centerOfMass;
            shipExternalRigidbody.inertiaTensor = engine.ShipBody.inertiaTensor;
            shipExternalRigidbody.inertiaTensorRotation = engine.ShipBody.inertiaTensorRotation;

            engine.ShipBody.isKinematic = true;

            engine.ShipBody = shipExternalRigidbody;
        }

        void OnDisable()
        {
            ShipPhysicsReader.PositionUpdated.Remove(OnPositionUpdated);
            ShipPhysicsReader.RotationUpdated.Remove(OnRotationUpdated);
            ShipPhysicsReader.LinearVelocityUpdated.Remove(OnVelocityUpdated);
            ShipPhysicsReader.AngularVelocityUpdated.Remove(OnAngularVelocityUpdated);
        }

        void OnPositionUpdated(Bytes stateUpdate)
        {
            var decoded = Decode.Vector3f(stateUpdate.BackingArray);
            var position = new Vector3(decoded[0], decoded[1], decoded[2]);

            if (useRemoteStateBuffer)
            {
                if (positionBuffer.Count >= bufferCount)
                {
                    positionBuffer.Dequeue();
                }
                
                positionBuffer.Enqueue(position); 
            }
            else
            {
                shipExternalRigidbody.position = position;
            }
        }

        void OnRotationUpdated(Bytes stateUpdate)
        {
            var decoded = Decode.Quaternion(stateUpdate.BackingArray);
            var rotation = new Quaternion(decoded[0], decoded[1], decoded[2], decoded[3]);

            if (useRemoteStateBuffer)
            {
                if (rotationBuffer.Count >= bufferCount)
                {
                    rotationBuffer.Dequeue();
                }
                
                rotationBuffer.Enqueue(rotation);
            }
            else
            {
                shipExternalRigidbody.rotation = rotation;
            }
        }

        void OnVelocityUpdated(Bytes stateUpdate)
        {
            var decoded = Decode.Velocity(stateUpdate.BackingArray, ShipPhysicsReader.Data.maxLinearVelocity);
            var velocity = new Vector3(decoded[0], decoded[1], decoded[2]);

            if (useRemoteStateBuffer)
            {
                if (velocityBuffer.Count >= bufferCount)
                {
                    velocityBuffer.Dequeue();
                }
                
                velocityBuffer.Enqueue(velocity);
            }
            else
            {
                shipExternalRigidbody.velocity = velocity;
            }
        }

        void OnAngularVelocityUpdated(Bytes stateUpdate)
        {
            var decoded = Decode.Velocity(stateUpdate.BackingArray, ShipPhysicsReader.Data.maxAngularVelocity);
            var velocity = new Vector3(decoded[0], decoded[1], decoded[2]);

            if (useRemoteStateBuffer)
            {
                if (angularVelocityBuffer.Count >= bufferCount)
                {
                    angularVelocityBuffer.Dequeue();
                }
                
                angularVelocityBuffer.Enqueue(velocity); 
            }
            else
            {
                shipExternalRigidbody.angularVelocity = velocity;
            }
        }
        
        void FixedUpdate()
        {
            UpdateRigidbody();
        }

        void Update()
        {
            transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
        }

        void LateUpdate()
        {
            ErrorCorrection();
        }
        
        void UpdateRigidbody()
        {
            if (useRemoteStateBuffer)
            {
                if (positionBuffer.Count > 0)
                {
                    var stateUpdate = positionBuffer.Dequeue();

                    shipExternalRigidbody.position = stateUpdate;
                }

                if (rotationBuffer.Count > 0)
                {
                    var stateUpdate = rotationBuffer.Dequeue();

                    shipExternalRigidbody.rotation = stateUpdate;
                }

                if (velocityBuffer.Count > 0)
                {
                    var stateUpdate = velocityBuffer.Dequeue();

                    shipExternalRigidbody.velocity = stateUpdate;
                }

                if (angularVelocityBuffer.Count > 0)
                {
                    var stateUpdate = angularVelocityBuffer.Dequeue();

                    shipExternalRigidbody.angularVelocity = stateUpdate;
                }
            }
        }

        void ErrorCorrection()
        {
            //Error
            var posError = transform.position - shipExternalRigidbody.position;
            var rotError = transform.rotation * Quaternion.Inverse(shipExternalRigidbody.rotation);

            //Exponentialy smoothed moving average error
            positionErrorOffset += ((1f - (positionSmoothing / 100f)) * (posError - positionErrorOffset));
            rotationErrorOffset *= Quaternion.Slerp(Quaternion.identity, rotError * Quaternion.Inverse(rotationErrorOffset), (1f - (rotationSmoothing / 100f)));

            //Error magnitude
            var posErrorMag = positionErrorOffset.sqrMagnitude;
            var rotErrorMag = 1f - Quaternion.Dot(Quaternion.identity, rotationErrorOffset);//dot product of 1 == same rotation
            
            //Dynamic Error Reduction Ratio
            var posMin = 1f - minPositionReduction / 100f;
            var posMax = 1f - maxPositionReduction / 100f;

            var rotMin = 1f - minRotationReduction / 100f;
            var rotMax = 1f - maxRotationReduction / 100f;
            
            var dynamicPositionErrorReduction = Mathf.Lerp(posMin, posMax, posErrorMag / maxReductionAtDistance);
            var dynamicRotationErrorReduction = Mathf.Lerp(rotMin, rotMax, rotErrorMag / maxReductionAtMagnitude);
            
            if (showDebugInfo)
            {
                if (biggestPositionError < posErrorMag) biggestPositionError = posErrorMag;
                if (biggestRotationError < rotErrorMag) biggestRotationError = rotErrorMag;

                Debug.Log(Time.frameCount + " Positional Error " + posErrorMag + " Reduction " + dynamicPositionErrorReduction + " Largest " + biggestPositionError);
                Debug.Log(Time.frameCount + " Rotational Error " + rotErrorMag + " Reduction " + dynamicRotationErrorReduction + " Largest " + biggestRotationError);
            }

            //Actual Reduction of Error
            positionErrorOffset *= dynamicPositionErrorReduction;
            rotationErrorOffset = Quaternion.Slerp(Quaternion.identity, rotationErrorOffset, dynamicRotationErrorReduction);

            //Apply result
            smoothedPosition = shipExternalRigidbody.position + positionErrorOffset;
            smoothedRotation = shipExternalRigidbody.rotation * rotationErrorOffset;
        }
    }
}