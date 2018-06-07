### Resources
https://gafferongames.com/categories/networked-physics/

## AuthWorkerShipPhysics.cs
Every fixed update, if authoritative, position, rotation, linear & angular velocity is encoded and an update is sent to the ShipPhysics component. The Position component is updated less frequently.


## WorkerShipPhysics.cs
As new updates arrives physic states are decoded and applied to the local gameobject (if not auth). Extrapolation naturally happen based on last state received.


## ClientShipPhysics.cs
Physics state updates are decoded and buffered (to reduce jitter) and applied to an external rigidbody (separate from the player gameobject). The player controls apply forces to the player gameobject directly and any deviation from server is smoothly coorected.
