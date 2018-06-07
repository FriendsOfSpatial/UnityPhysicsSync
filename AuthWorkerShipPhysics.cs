using Improbable;
using Improbable.Entity.Component;
using Improbable.Unity;
using Improbable.Unity.Visualizer;
using Improbable.Worker;
using NetworkOptimization;
using RogueFleet.Ship;
using System.Collections;
using UnityEngine;

namespace Assets.GameLogic.Ship
{
    [WorkerType(WorkerPlatform.UnityWorker)]
    public class AuthWorkerShipPhysics : MonoBehaviour
    {
        Rigidbody shipRigidbody;

        Authority authority;

        Vector3 oldPosition, oldVelocity, oldAngularVelocity;
        Quaternion oldRotation;
        float maxLinearVelocity, maxAngularVelocity;

        [Require] Position.Writer PositionWriter;
        [Require] ShipPhysics.Writer ShipPhysicsWriter;

        //TODO if ship is cloaked stop sending updates

        void Awake()
        {
            shipRigidbody = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            shipRigidbody.isKinematic = false;

            Setup();

            authority = ShipPhysicsWriter.Authority;
            ShipPhysicsWriter.AuthorityChanged.Add(OnAuthorityChange);
            
            StartCoroutine(SendPosition());
        }

        void OnDisable()
        {
            StopAllCoroutines();

            ShipPhysicsWriter.AuthorityChanged.Remove(OnAuthorityChange);
        }
        
        void OnAuthorityChange(Authority newAuthority)
        {
            authority = newAuthority;
        }

        void FixedUpdate()
        {
            if (authority == Authority.Authoritative)
            {
                SyncShipPhysics();
            }
        }

        void SyncShipPhysics()
        {
            var shipPhysicsUpdate = new ShipPhysics.Update();
            bool empty = true;

            var currentPosition = shipRigidbody.position;
            if (currentPosition != oldPosition)
            {
                var encodedPosition = Encode.Vector3f(currentPosition.x, currentPosition.y, currentPosition.z);
                shipPhysicsUpdate.SetPosition(Bytes.FromBackingArray(encodedPosition));

                var decodedPosition = Decode.Vector3f(encodedPosition);
                shipRigidbody.position = new Vector3(decodedPosition[0], decodedPosition[1], decodedPosition[2]);

                oldPosition = currentPosition;
                empty = false;
            }

            var currentRotation = shipRigidbody.rotation;
            if (currentRotation != oldRotation)
            {
                var encodedRotation = Encode.Quaternion(currentRotation.x, currentRotation.y, currentRotation.z, currentRotation.w);
                shipPhysicsUpdate.SetRotation(Bytes.FromBackingArray(encodedRotation));

                var decodedRotation = Decode.Quaternion(encodedRotation);
                shipRigidbody.rotation = new Quaternion(decodedRotation[0], decodedRotation[1], decodedRotation[2], decodedRotation[3]);

                oldRotation = currentRotation;
                empty = false;
            }

            var currentVelocity = shipRigidbody.velocity;
            if (currentVelocity != oldVelocity)
            {
                for (int i = 0; i < 3; i++)
                {
                    var component = Mathf.Abs(currentVelocity[i]);
                    if (component > maxLinearVelocity)
                    {
                        maxLinearVelocity = component;
                        shipPhysicsUpdate.SetMaxLinearVelocity(maxLinearVelocity);
                    }
                }

                var encodedLinearVelocity = Encode.Velocity(currentVelocity.x, currentVelocity.y, currentVelocity.z, maxLinearVelocity);
                shipPhysicsUpdate.SetLinearVelocity(Bytes.FromBackingArray(encodedLinearVelocity));

                var decodedLinearVelocity = Decode.Velocity(encodedLinearVelocity, maxLinearVelocity);
                shipRigidbody.velocity = new Vector3(decodedLinearVelocity[0], decodedLinearVelocity[1], decodedLinearVelocity[2]);

                oldVelocity = currentVelocity;
                empty = false;
            }

            var currentAngularVelocity = shipRigidbody.angularVelocity;
            if (currentAngularVelocity != oldAngularVelocity)
            {
                for (int i = 0; i < 3; i++)
                {
                    var component = Mathf.Abs(currentAngularVelocity[i]);
                    if (component > maxAngularVelocity)
                    {
                        maxAngularVelocity = component;
                        shipPhysicsUpdate.SetMaxAngularVelocity(maxAngularVelocity);
                    }
                }

                var encodedAngularVelocity = Encode.Velocity(currentAngularVelocity.x, currentAngularVelocity.y, currentAngularVelocity.z, maxAngularVelocity);
                shipPhysicsUpdate.SetAngularVelocity(Bytes.FromBackingArray(encodedAngularVelocity));

                var decodedAngularVelocity = Decode.Velocity(encodedAngularVelocity, maxAngularVelocity);
                shipRigidbody.angularVelocity = new Vector3(decodedAngularVelocity[0], decodedAngularVelocity[1], decodedAngularVelocity[2]);

                oldAngularVelocity = currentAngularVelocity;
                empty = false;
            }

            if (!empty)
            {
                ShipPhysicsWriter.Send(shipPhysicsUpdate);
            }
        }

        void Setup()
        {
            var decodedPosition = Decode.Vector3f(ShipPhysicsWriter.Data.position.BackingArray);
            shipRigidbody.position = new Vector3(decodedPosition[0], decodedPosition[1], decodedPosition[2]);

            var decodedRotation = Decode.Quaternion(ShipPhysicsWriter.Data.rotation.BackingArray);
            shipRigidbody.rotation = new Quaternion(decodedRotation[0], decodedRotation[1], decodedRotation[2], decodedRotation[3]);

            var decodedLinearVelocity = Decode.Velocity(ShipPhysicsWriter.Data.linearVelocity.BackingArray, ShipPhysicsWriter.Data.maxLinearVelocity);
            shipRigidbody.velocity = new Vector3(decodedLinearVelocity[0], decodedLinearVelocity[1], decodedLinearVelocity[2]);

            var decodedAngularVelocity = Decode.Velocity(ShipPhysicsWriter.Data.angularVelocity.BackingArray, ShipPhysicsWriter.Data.maxAngularVelocity);
            shipRigidbody.angularVelocity = new Vector3(decodedAngularVelocity[0], decodedAngularVelocity[1], decodedAngularVelocity[2]);
            
            maxLinearVelocity = 0f;
            maxAngularVelocity = 0f;
        }
        
        IEnumerator SendPosition()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);

                if (authority == Authority.Authoritative)
                {
                    var currentPosition = shipRigidbody.position;
                    var updatedCoordinates = new Coordinates(currentPosition.x, currentPosition.y, currentPosition.z);

                    var update = new Position.Update();
                    update.SetCoords(updatedCoordinates);
                    PositionWriter.Send(update);
                }
            }
        }

        AbsolutePositionResponse OnAbsolutePosition(AbsolutePositionRequest request, ICommandCallerInfo callerInfo)
        {
            return new AbsolutePositionResponse(new Vector3d(PositionWriter.Data.coords.x, PositionWriter.Data.coords.y, PositionWriter.Data.coords.z));
        }
    }
}