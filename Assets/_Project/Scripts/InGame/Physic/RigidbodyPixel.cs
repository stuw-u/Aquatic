﻿using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[DisallowMultipleComponent]
public class RigidbodyPixel : MonoBehaviour {

    #region Header
    [Header("Physics Parameters")]
    public float mass = 1f;
    public float bounciness = 1f;
    public float terrainBounciness = 0f;
    public float defaultFloorFriction = 0f;
    public float defaultWallFriction = 0f;

    [Header("Parenting Parameters")]
    public bool canBeParentPlatform = true;
    public bool reorderChild = false;
    public bool interactsWithComplexCollider = false;

    [Header("Collider Type Parameters")]
    public bool collidesOnlyWithTerrain = false;
    public bool isComplexCollider = false;
    public bool applyGenericGravityForceOnLoad = false;
    public bool eliminatesPenetration = false;
    public bool secondExecutionOrder = false;
    public bool clipPermision = false;
    public float clipAmout = 0.25f;
    public bool weakPushCandidate = true;
    public float superPushForce = 0f;

    [Header("Masking")]
    [HideInInspector] public RigidbodyPixel ignoreProjectileOwnerUntilHitWall;

    // Privates
    private bool failedInitialization;
    private List<Bounds2D> sampledCollisions;
    private RigidbodyPixel parentPlatform;
    private bool isParentPlaformConn = false;
    private RigidbodyPixel previousParentPlatform;
    private bool isPrevParentPlaformConn = false;
    private Vector2 pVelDir;
    private float totalVolume = 0f;
    private float subtractedVolume = 0f;
    private List<ForcePixel> forces;

    // Hidden Variables
    [HideInInspector] public BoxCollider2D box;
    [HideInInspector] public Vector2 velocity;
    [HideInInspector] public Vector2 lastVelocity;
    [HideInInspector] public Bounds2D aabb;
    [HideInInspector] public float inverseMass = 0f;
    [HideInInspector] public Vector2 movementDelta;
    [HideInInspector] public MobileChunk mobileChunk;
    [HideInInspector] public Transform aligmentObject;
    [HideInInspector] public bool disableForAFrame;
    [HideInInspector] public Vector2 deltaDischarge;

    [HideInInspector] public bool hadCollisionDown;
    [HideInInspector] public bool hadCollisionLeft;
    [HideInInspector] public bool hadCollisionUp;
    [HideInInspector] public bool hadCollisionRight;

    [Header("Colliding Output")]
    public bool isCollidingDown;
    public bool isCollidingLeft;
    public bool isCollidingUp;
    public bool isCollidingRight;

    public bool isCollidingWallDown;
    public bool isCollidingWallLeft;
    public bool isCollidingWallUp;
    public bool isCollidingWallRight;
    public RigidbodyPixel isInsideComplexObject;

    [Header("Fluid Output")]
    public bool buoyancyEnabled = true;
    public float submergedPercentage = 0f;
    public float volume = 0f;

    Vector2 lastVel;
    bool hasBeenInit;
    #endregion


    #region Monobehaviour
    private void Start () {
        Init();
    }

    public void Init () {
        if(hasBeenInit) {
            return;
        }

        hasBeenInit = true;

        // Adding rigibody to the global rigibody registery
        PhysicsPixel.inst.rbs.Add(this);

        // Getting all necessairy components
        if(box == null)
            box = GetComponent<BoxCollider2D>();
        mobileChunk = GetComponent<MobileChunk>();
        forces = new List<ForcePixel>();
        GetComponents(forces);
        if(GetComponent<Rigidbody2D>()) {
            Debug.Log("There's a rigidbody 2D attached to this object. You can't have multiple rigidbody types on one object");
            failedInitialization = true;
        }
        if(applyGenericGravityForceOnLoad) {
            if(forces.Count <= 0) {
                gameObject.AddComponent<ForcePixel>();
                gameObject.GetComponents(forces);
            }

            forces[0].force = PhysicsPixel.inst.genericGravityForce;
        }

        // Prepare sampled collisions chached array
        sampledCollisions = new List<Bounds2D>();
        if(!isComplexCollider) {
            aabb = GetBoundFromCollider();
        } else {
            aabb = GetBoundFromCollider(mobileChunk.mobileDataChunk.restrictedSize);
        }
    }

    private void OnDestroy () {
        PhysicsPixel.inst.rbs.Remove(this);

        CloseOnWeakCollision();
    }
    #endregion

    #region Simulate Step
    // Apply force components
    public void ApplyForces () {
        // Prepare forces so they can be eliminated if not needed
        foreach(ForcePixel fp in forces) {
            if(!fp.enabled) {
                continue;
            }
            velocity += fp.force * Time.fixedDeltaTime;
            velocity *= (1f - Time.fixedDeltaTime * fp.constantFriction);
        }
    }

    // Apply special buoyancy forces
    public void ApplyBuoyancy () {
        if(!buoyancyEnabled) {
            return;
        }
        velocity += (submergedPercentage * volume) * PhysicsPixel.inst.fluidMassPerUnitDensity * -PhysicsPixel.inst.genericGravityForce * inverseMass * Time.deltaTime;
        velocity *= Mathf.Lerp(1f, (1f - Time.fixedDeltaTime * PhysicsPixel.inst.fluidDrag), submergedPercentage);
    }

    Vector3 transform_position;
    Vector2 box_size;
    Vector2 box_offset;
    public void PreOffThread () { // Meant to be executed in main thread
        transform_position = transform.position;
        box_size = box.size;
        box_offset = box.offset;
    }
    public void PostOffThread () { // Meant to be executed in main thread
        transform.position = transform_position;
    }
    public void SimulateFixedUpdate (bool includePrePost, float fixedDeltaTime) { // Meant to be executed in any thread
        if(failedInitialization) return;

        if(includePrePost) {
            PreOffThread();
        }

        // Apply parent's velocity when leaving the parent
        if(previousParentPlatform != null && parentPlatform == null) {
            velocity += previousParentPlatform.velocity;
        }

        // Calculate inverse mass
        if(mass != 0) {
            inverseMass = 1f / mass;
        } else {
            inverseMass = 0f;
        }

        // Reset collision checks
        isCollidingDown = false;
        isCollidingLeft = false;
        isCollidingUp = false;
        isCollidingRight = false;

        isCollidingWallDown = false;
        isCollidingWallLeft = false;
        isCollidingWallUp = false;
        isCollidingWallRight = false;
        movementDelta = Vector2.zero;

        // Move the rigibody
        lastVel = velocity;
        ApplyExternalCollisionInfo();
        ApplyVelocity(fixedDeltaTime);
        if(mobileChunk != null) {
            mobileChunk.UpdatePositionData(transform_position);
        }

        if(clipPermision) {
            CheckForClipping();
        }

        // Cycle temp. variables
        isPrevParentPlaformConn = isParentPlaformConn;
        previousParentPlatform = parentPlatform;
        isParentPlaformConn = false;
        parentPlatform = null;

        if(includePrePost) {
            PostOffThread();
        }
    }
    public void ApplyExternalCollisionInfo () {
        if(hadCollisionDown) {
            isCollidingDown = true;
            hadCollisionDown = false;
        }
        if(hadCollisionLeft) {
            isCollidingLeft = true;
            hadCollisionLeft = false;
        }
        if(hadCollisionUp) {
            isCollidingUp = true;
            hadCollisionUp = false;
        }
        if(hadCollisionRight) {
            isCollidingRight = true;
            hadCollisionRight = false;
        }
    }
    #endregion

    #region Event Listeners
    public delegate void OnWeakCollisionHandler(RigidbodyPixel target);
    public event OnWeakCollisionHandler OnWeakCollision;

    public void CallOnWeakCollision (RigidbodyPixel target) {
        OnWeakCollision?.Invoke(target);
    }

    void CloseOnWeakCollision () {
        if(OnWeakCollision == null) {
            return;
        }
        foreach(System.Delegate d in OnWeakCollision.GetInvocationList()) {
            OnWeakCollision -= (OnWeakCollisionHandler)d;
        }
    }
    #endregion


    #region External Functions
    /// <summary>
    /// Moves the rigidbody to a certain position while taking into account colliders sorrounding it.
    /// </summary>
    /// <param name="position">The position to move to.</param>
    public void MovePosition (Vector2 position) {
        Vector2 delta = new Vector2(position.x - transform.position.x, position.y - transform.position.y);

        MoveByDelta(delta);
    }

    /// <summary>
    /// Moves the rigidbody by a certain delta while taking into account colliders sorrounding it.
    /// </summary>
    /// <param name="delta">The delta to move by.</param>
    public void MoveByDelta (Vector2 delta) {
        PreOffThread();
        MoveByDeltaInteral(delta, false, Time.fixedDeltaTime);
        PostOffThread();
    }
    #endregion

    #region Internal Functions
    private void ApplyVelocity (float fixedDeltaTime) {
        lastVelocity = velocity;
        Vector2 delta = velocity * fixedDeltaTime;
        if(delta.x > 0) {
            pVelDir.x = 1f;
        } else if(delta.x < 0) {
            pVelDir.x = -1f;
        }
        if(delta.y > 0) {
            pVelDir.y = 1f;
        } else if(delta.y < 0) {
            pVelDir.y = -1f;
        }
        delta += pVelDir * PhysicsPixel.inst.errorHandler;
        delta += deltaDischarge;
        deltaDischarge.Set(0f, 0f);

        if(parentPlatform != null) {
            if(parentPlatform.canBeParentPlatform) {
                delta += parentPlatform.movementDelta;
            }
        }
        MoveByDeltaInteral(delta, true, fixedDeltaTime);
        transform_position -= (Vector3)(pVelDir * PhysicsPixel.inst.errorHandler);
    }

    private void MoveByDeltaInteral (Vector2 delta, bool limitVelocity, float fixedDeltaTime) {
        Bounds2D bounds = GetBoundFromColliderSafe();
        Bounds2D queryBounds = PhysicsPixel.inst.CalculateQueryBounds(bounds, delta);

        totalVolume = box_size.x * box_size.y;
        volume = totalVolume;
        subtractedVolume = totalVolume;

        QueryMinimizeApplyDelta(queryBounds, bounds, delta, limitVelocity, fixedDeltaTime);
        if(!isComplexCollider) {
            aabb = GetBoundFromColliderSafe();
        } else {
            aabb = GetBoundFromColliderSafe(mobileChunk.mobileDataChunk.restrictedSize);
        }

        if(totalVolume > 0f) {
            submergedPercentage = 1f - (subtractedVolume / totalVolume);
        } else {
            submergedPercentage = 0f;
        }
    }
    #endregion

    #region Query and Delta Manipulations
    private void QueryMinimizeApplyDelta (Bounds2D queryBounds, Bounds2D bounds, Vector2 delta, bool limitVelocity, float fixedDeltaTime) {
        Vector2 newDelta = delta;
        Bounds2D cBounds = bounds;

        // Figure out which tiles the query will seek
        Vector2Int queryTileMin = Vector2Int.FloorToInt((queryBounds.min - (Vector2)PhysicsPixel.inst.queryExtension));
        Vector2Int queryTileMax = Vector2Int.FloorToInt((queryBounds.max + (Vector2)PhysicsPixel.inst.queryExtension));

        // Sample the concerned tile size
        sampledCollisions.Clear();
        PhysicsPixel.inst.SampleCollisionInBounds(queryTileMin, queryTileMax, ref sampledCollisions, bounds, ref subtractedVolume);

        // Reduce the delta with the tile found if any
        if(sampledCollisions.Count > 0) {
            #region Reduce Delta Y
            // Figuring out the Y axis first, reduce the delta and apply it
            foreach(Bounds2D b in sampledCollisions) {
                // Reduce the delta
                newDelta.y = PhysicsPixel.inst.MinimizeDeltaY(newDelta.y, b, cBounds);
            }
            transform_position += newDelta.y * Vector3.up;

            // Recalculating current bounds
            cBounds = GetBoundFromColliderSafe();
            #endregion

            #region Reduce Delta X
            // Figuring out the X axis after, reduce the delta and apply it
            foreach(Bounds2D b in sampledCollisions) {
                // Reduce the delta
                newDelta.x = PhysicsPixel.inst.MinimizeDeltaX(newDelta.x, b, cBounds);
            }
            transform_position += newDelta.x * Vector3.right;
            #endregion
        } else {
            transform_position += newDelta.y * Vector3.up;
            transform_position += newDelta.x * Vector3.right;
        }
        movementDelta += newDelta;

        #region Collision Detection
        // Calculating collisions that occured
        isCollidingDown = isCollidingDown || newDelta.y > delta.y;
        isCollidingUp = isCollidingUp || newDelta.y < delta.y;
        isCollidingLeft = isCollidingLeft || newDelta.x > delta.x;
        isCollidingRight = isCollidingRight || newDelta.x < delta.x;

        isCollidingWallDown = isCollidingWallDown || newDelta.y > delta.y;
        isCollidingWallUp = isCollidingWallUp || newDelta.y < delta.y;
        isCollidingWallLeft = isCollidingWallLeft || newDelta.x > delta.x;
        isCollidingWallRight = isCollidingWallRight || newDelta.x < delta.x;

        if(isCollidingDown) {
            velocity.x *= (1f - fixedDeltaTime * defaultFloorFriction);
        }
        if(isCollidingLeft || isCollidingRight) {
            velocity.x *= (1f - fixedDeltaTime * defaultWallFriction);
        }
        if(isCollidingWallDown || isCollidingUp || isCollidingWallLeft || isCollidingWallRight) {
            ignoreProjectileOwnerUntilHitWall = null;
        }

        // Limit the velocity when a wall is it
        if(limitVelocity && !Mathf.Approximately(newDelta.y, delta.y))
            velocity.y = -velocity.y * terrainBounciness;
        if(limitVelocity && !Mathf.Approximately(newDelta.x, delta.x))
            velocity.x = -velocity.x * terrainBounciness;
        #endregion
    }
    #endregion

    #region Cliping
    void CheckForClipping () {
        float e = PhysicsPixel.inst.errorHandler;
        float e5 = 0.03125f;
        
        #region Left
        if(lastVel.x < 0f && isCollidingLeft) {
            Vector2 point0 = new Vector2(
                transform_position.x + box_offset.x - (box_size.x * 0.5f) - e5,
                transform_position.y + box_offset.y - (box_size.y * 0.5f) + e
            );
            Vector2 point1 = new Vector2(
                transform_position.x + box_offset.x - (box_size.x * 0.5f) - e5,
                transform_position.y + box_offset.y - (box_size.y * 0.5f) + clipAmout
            );
            bool solidClip = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0, point1));
            bool solidPoint = PhysicsPixel.inst.IsPointSolid(point1 + Vector2.up * PhysicsPixel.inst.errorHandler);

            if(solidClip && !solidPoint) {
                if(PhysicsPixel.inst.AxisAlignedRaycast(point1, PhysicsPixel.Axis.Down, clipAmout, out Vector2 point)) {
                    float offset = clipAmout - (point1.y - point.y) + 0.03125f;
                    bool noFreeSpace = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0 + Vector2.up * offset, point1 + Vector2.up * (offset + box_size.y - e)));
                    if(!noFreeSpace) {
                        transform_position += Vector3.up * offset;
                        velocity = lastVel;
                    }
                }
            }
        }
        #endregion

        #region Right
        if(lastVel.x > 0f && isCollidingRight) {
            Vector2 point0 = new Vector2(
                transform_position.x + box_offset.x + (box_size.x * 0.5f) + e5,
                transform_position.y + box_offset.y - (box_size.y * 0.5f) + e
            );
            Vector2 point1 = new Vector2(
                transform_position.x + box_offset.x + (box_size.x * 0.5f) + e5,
                transform_position.y + box_offset.y - (box_size.y * 0.5f) + clipAmout
            );
            bool solidClip = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0, point1));
            bool solidPoint = PhysicsPixel.inst.IsPointSolid(point1 + Vector2.up * PhysicsPixel.inst.errorHandler);

            if(solidClip && !solidPoint) {
                if(PhysicsPixel.inst.AxisAlignedRaycast(point1, PhysicsPixel.Axis.Down, clipAmout, out Vector2 point)) {
                    float offset = clipAmout - (point1.y - point.y) + 0.03125f;
                    
                    bool noFreeSpace = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0 + Vector2.up * offset, point1 + Vector2.up * (offset + box_size.y - e)));
                    if(!noFreeSpace) {
                        transform_position += Vector3.up * offset;
                        velocity = lastVel;
                    }
                }
            }
        }
        #endregion

        #region Top Right
        if(lastVel.y > 5f && isCollidingUp) {
            Vector2 point0 = new Vector2(
                transform_position.x + box_offset.x + (box_size.x * 0.5f),
                transform_position.y + box_offset.y + (box_size.y * 0.5f)
            );
            Vector2 point1 = new Vector2(
                transform_position.x + box_offset.x + (box_size.x * 0.5f) - clipAmout,
                transform_position.y + box_offset.y + (box_size.y * 0.5f) + e5
            );
            bool solidClip = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0, point1));
            bool solidPoint = PhysicsPixel.inst.IsPointSolid(point1 + Vector2.left * PhysicsPixel.inst.errorHandler);
            if(solidClip && !solidPoint) {
                if(PhysicsPixel.inst.AxisAlignedRaycast(point1, PhysicsPixel.Axis.Right, clipAmout, out Vector2 point)) {
                    float offset = clipAmout - (point.x - point1.x);
                    bool noFreeSpace = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0 + Vector2.left * offset, point1 + Vector2.left * (offset + box_size.x - e)));
                    if(!noFreeSpace) {
                        transform_position += Vector3.left * offset;
                        velocity = lastVel;
                    }
                }
            }
        }
        #endregion

        #region Top Left
        if(lastVel.y > 5f && isCollidingUp) {
            Vector2 point0 = new Vector2(
                transform_position.x + box_offset.x - (box_size.x * 0.5f),
                transform_position.y + box_offset.y + (box_size.y * 0.5f)
            );
            Vector2 point1 = new Vector2(
                transform_position.x + box_offset.x - (box_size.x * 0.5f) + clipAmout,
                transform_position.y + box_offset.y + (box_size.y * 0.5f) + e5
            );
            bool solidClip = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0, point1));
            bool solidPoint = PhysicsPixel.inst.IsPointSolid(point1 + Vector2.right * PhysicsPixel.inst.errorHandler);
            if(solidClip && !solidPoint) {
                if(PhysicsPixel.inst.AxisAlignedRaycast(point1, PhysicsPixel.Axis.Left, clipAmout, out Vector2 point)) {
                    float offset = clipAmout - (point1.x - point.x);
                    bool noFreeSpace = PhysicsPixel.inst.BoundsCast(new Bounds2D(point0 + Vector2.right * offset, point1 + Vector2.right * (offset + box_size.x - e)));
                    if(!noFreeSpace) {
                        transform_position += Vector3.right * offset;
                        velocity = lastVel;
                    }
                }
            }
        }
        #endregion
    }
    #endregion


    #region Parent Platform Utils
    public void SetParentPlatform (RigidbodyPixel pp, bool fullyConnected) {
        if(pp.canBeParentPlatform) {
            isParentPlaformConn = fullyConnected;
            parentPlatform = pp;
        }
    }

    public bool IsPrevParentingFullyConn () {
        if(previousParentPlatform != null) {
            return isPrevParentPlaformConn;
        }
        return false;
    }

    public bool IsParented () {
        return parentPlatform != null;
    }

    public Vector3 GetParentPosition () {
        if(parentPlatform.aligmentObject != null) {
            return new Vector3(parentPlatform.transform.position.x, parentPlatform.transform.position.y, parentPlatform.aligmentObject.position.z);
        } else {
            return parentPlatform.transform.position;
        }
    }

    public Vector3 GetPosition () {
        if(aligmentObject != null) {
            return new Vector3(transform.position.x, transform.position.y, aligmentObject.position.z);
        } else {
            return parentPlatform.transform.position;
        }
    }
    #endregion

    #region Simple Math Utils
    private bool IsRangeOverlapping (float min1, float max1, float min2, float max2) {
        return !(max1 <= min2 || min1 >= max2);
    }

    private float Min (float a, float b) {
        return a < b ? a : b;
    }

    private float Max (float a, float b) {
        return a > b ? a : b;
    }

    public Bounds2D GetBoundFromCollider () {
        Bounds2D b2 = new Bounds2D((Vector2)transform.position - box.size * 0.5f + box.offset, (Vector2)transform.position + box.size * 0.5f + box.offset);
        PhysicsPixel.DrawBounds(b2, Color.yellow);
        return b2;
    }

    public Bounds2D GetBoundFromColliderFakePosition (Vector3 position) {
        Bounds2D b2 = new Bounds2D(
            (Vector2)position - box.size * 0.5f + box.offset + Vector2.one * PhysicsPixel.inst.errorHandler, 
            (Vector2)position + box.size * 0.5f + box.offset - Vector2.one * PhysicsPixel.inst.errorHandler);
        return b2;
    }

    public Bounds2D GetBoundFromColliderSafe () {
        Bounds2D b2 = new Bounds2D((Vector2)transform_position - box_size * 0.5f + box_offset, (Vector2)transform_position + box_size * 0.5f + box_offset);
        //PhysicsPixel.DrawBounds(b2, Color.yellow);
        return b2;
    }

    public Bounds2D GetBoundFromCollider (Vector2 trueSize) {
        return new Bounds2D(transform.position, (Vector2)transform.position + trueSize);
    }

    public Bounds2D GetBoundFromColliderSafe (Vector2 trueSize) {
        return new Bounds2D(transform_position, (Vector2)transform_position + trueSize);
    }

    public Bounds2D GetBoundFromColliderDelta (Vector2 previsionDelta) {
        return new Bounds2D(
            new Vector2(transform.position.x + previsionDelta.x, transform.position.y + previsionDelta.y) - box.size * 0.5f + box.offset,
            new Vector2(transform.position.x + previsionDelta.x, transform.position.y + previsionDelta.y) + box.size * 0.5f + box.offset);
    }
    #endregion
}
