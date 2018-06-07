using Improbable.Unity;
using Improbable.Unity.Visualizer;
using Improbable.Worker;
using NetworkOptimization;
using RogueFleet.Ship;
using UnityEngine;

namespace Assets.GameLogic.Ship
{
    [WorkerType(WorkerPlatform.UnityWorker)]
    public class WorkerShipPhysics : MonoBehaviour
    {
        Rigidbody shipRigidbody;
        Authority authority;
        
        [Require] ShipPhysics.Reader ShipPhysicsReader;

        void Awake()
        {
            shipRigidbody = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            shipRigidbody.isKinematic = false;

            authority = ShipPhysicsReader.Authority;
            ShipPhysicsReader.AuthorityChanged.Add(OnAuthorityChanged);

            ShipPhysicsReader.PositionUpdated.AddAndInvoke(OnPositionUpdated);
            ShipPhysicsReader.RotationUpdated.AddAndInvoke(OnRotationUpdated);
            ShipPhysicsReader.LinearVelocityUpdated.AddAndInvoke(OnVelocityUpdated);
            ShipPhysicsReader.AngularVelocityUpdated.AddAndInvoke(OnAngularVelocityUpdated);
        }
        
        void OnDisable()
        {
            ShipPhysicsReader.AngularVelocityUpdated.Remove(OnAngularVelocityUpdated);
            ShipPhysicsReader.LinearVelocityUpdated.Remove(OnVelocityUpdated);
            ShipPhysicsReader.RotationUpdated.Remove(OnRotationUpdated);
            ShipPhysicsReader.PositionUpdated.Remove(OnPositionUpdated);

            ShipPhysicsReader.AuthorityChanged.Remove(OnAuthorityChanged);
        }

        void OnAuthorityChanged(Authority newAuthority)
        {
            authority = newAuthority;
        }
        
        void OnPositionUpdated(Bytes stateUpdate)
        {
            if (authority == Authority.NotAuthoritative)
            {
                var decoded = Decode.Vector3f(stateUpdate.BackingArray);
                var position = new Vector3(decoded[0], decoded[1], decoded[2]);

                shipRigidbody.position = position;
            }
        }

        void OnRotationUpdated(Bytes stateUpdate)
        {
            if (authority == Authority.NotAuthoritative)
            {
                var decoded = Decode.Quaternion(stateUpdate.BackingArray);
                var rotation = new Quaternion(decoded[0], decoded[1], decoded[2], decoded[3]);

                shipRigidbody.rotation = rotation;
            }
        }

        void OnVelocityUpdated(Bytes stateUpdate)
        {
            if (authority == Authority.NotAuthoritative)
            {
                var decoded = Decode.Velocity(stateUpdate.BackingArray, ShipPhysicsReader.Data.maxLinearVelocity);
                var velocity = new Vector3(decoded[0], decoded[1], decoded[2]);

                shipRigidbody.velocity = velocity;
            }
        }

        void OnAngularVelocityUpdated(Bytes stateUpdate)
        {
            if (authority == Authority.NotAuthoritative)
            {
                var decoded = Decode.Velocity(stateUpdate.BackingArray, ShipPhysicsReader.Data.maxAngularVelocity);
                var velocity = new Vector3(decoded[0], decoded[1], decoded[2]);

                shipRigidbody.angularVelocity = velocity;
            }
        }
    }
}