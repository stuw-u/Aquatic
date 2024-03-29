﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;

public class EntityRegionManager : MonoBehaviour {

    #region Header and Monobehaviour
    // Public Variables
    public static EntityRegionManager inst;

    // Privates
    StringBuilder sb;
    float outOfBoundsCounter = 0f;

    // Data
    Dictionary<Vector2Int, EntityRegion> entityRegions;
    Queue<EntityRegion> unusedEntityRegions;

    public List<MobileChunk> outOfBoundsMobileChunks;
    public List<Entity> outOfBoundsEntities;

    private void Awake () {
        if(inst == null) {
            inst = this;
        }

        entityRegions = new Dictionary<Vector2Int, EntityRegion>();
        unusedEntityRegions = new Queue<EntityRegion>();
        outOfBoundsMobileChunks = new List<MobileChunk>();
        outOfBoundsEntities = new List<Entity>();
    }

    private void Update () {
        outOfBoundsCounter += Time.unscaledDeltaTime;

        if(outOfBoundsCounter >= TerrainManager.inst.outOfBoundsRefreshInterval) {
            outOfBoundsCounter = 0f;

            CheckForOutOfBounds();
        }

        foreach(KeyValuePair<Vector2Int, EntityRegion> kvp in entityRegions) {
            Bounds b = new Bounds() {
                min = (Vector2)(kvp.Value.regionPosition * TerrainManager.inst.chunksPerRegionSide * TerrainManager.inst.chunkSize),
                max = (Vector2)((kvp.Value.regionPosition + Vector2Int.one) * TerrainManager.inst.chunksPerRegionSide * TerrainManager.inst.chunkSize)
            };

            PhysicsPixel.DrawBounds(b, Color.magenta);
        }
    }
    #endregion

    #region Region Pool
    void LoadRegion (Vector2Int regionPosition) {
        EntityRegion uEntityRegion;
        if(unusedEntityRegions.Count > 0) {
            uEntityRegion = unusedEntityRegions.Dequeue();
        } else {
            uEntityRegion = new EntityRegion();
        }

        uEntityRegion.Clear();
        uEntityRegion.LoadRegion(regionPosition);
        entityRegions.Add(regionPosition, uEntityRegion);

        uEntityRegion.ChangeSubRegionPositions();
        uEntityRegion.LoadAllEntitiesChunks();
    }

    void SetRegionAsUnused (EntityRegion entityRegion) {
        entityRegion.SaveRegion();
        entityRegions.Remove(entityRegion.regionPosition);
        unusedEntityRegions.Enqueue(entityRegion);

        entityRegion.UnloadAllMobileChunksAndEntities();
    }
    #endregion

    #region Events
    public void CheckForOutOfBounds () {
        for(int i = outOfBoundsMobileChunks.Count - 1; i >= 0; i--) {
            if(TerrainManager.inst.IsMobileChunkInLoadedChunks(outOfBoundsMobileChunks[i])) {
                outOfBoundsMobileChunks[i].gameObject.SetActive(true);
                outOfBoundsMobileChunks.RemoveAt(i);
            }
        }
        for(int i = outOfBoundsEntities.Count - 1; i >= 0; i--) {
            if(TerrainManager.inst.IsEntityInLoadedChunks(outOfBoundsEntities[i])) {
                outOfBoundsEntities[i].gameObject.SetActive(true);
                outOfBoundsEntities.RemoveAt(i);
            }
        }
    }

    public void CheckForAutosaves () {
        foreach(KeyValuePair<Vector2Int, EntityRegion> kvp in entityRegions) {
            if(Time.time - kvp.Value.timeOfLastAutosave > GameManager.autoSaveTimeLimit) {
                kvp.Value.timeOfLastAutosave = Time.time;
                WorldSaving.inst.SaveRegionFile(kvp.Value);
            }
        }
    }

    public void SaveAllRegions () {
        foreach(KeyValuePair<Vector2Int, EntityRegion> kvp in entityRegions) {
            kvp.Value.timeOfLastAutosave = Time.time;
            WorldSaving.inst.SaveRegionFile(kvp.Value);
        }
    }

    public void LoadRegionAtChunk (Vector2Int chunkPosition) {
        Vector2Int regionPosition = Vector2Int.FloorToInt((Vector2)chunkPosition / TerrainManager.inst.chunksPerRegionSide);

        if(!entityRegions.ContainsKey(regionPosition)) {
            LoadRegion(regionPosition);
        }
    }

    public void UnloadRegionAtChunk (Vector2Int chunkPosition) {
        Vector2Int regionPosition = Vector2Int.FloorToInt((Vector2)chunkPosition / TerrainManager.inst.chunksPerRegionSide);

        if(entityRegions.ContainsKey(regionPosition)) {
            if(!entityRegions[regionPosition].IsAnySubRegionLoaded()) {
                SetRegionAsUnused(entityRegions[regionPosition]);
            }
        }
    }
    #endregion


    #region Data Management (Add, Move and Remove entities)
    public void AddMobileChunk (MobileChunk mobileChunk) {
        Vector2Int regionPos = TerrainManager.inst.WorldToRegion(mobileChunk.position);
        Vector2Int chunkPos = TerrainManager.inst.WorldToChunk(mobileChunk.position);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to add a mobile chunk to a none-loaded region.");
            return;
        }

        if(entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Contains(mobileChunk.uid)) {
            Debug.Log("Trying to add a mobile chunk in a region where it already exist.");
            return;
        }

        entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Add(mobileChunk.uid);
    }

    public void AddEntity (Entity entity) {
        Vector2Int regionPos = TerrainManager.inst.WorldToRegion(entity.entityData.position);
        Vector2Int chunkPos = TerrainManager.inst.WorldToChunk(entity.entityData.position);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to add a entity to a none-loaded region.");
            return;
        }

        if(entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.ContainsKey(entity.entityData.uid)) {
            Debug.Log("Trying to add a entity in a region where it already exist.");
            return;
        }

        //Debug.Log("Adding Entity: " + entity.entityData.uid);
        entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.Add(entity.entityData.uid, new EntityUIDAssetPair(entity.entityData.uid, entity.asset.globalID));
    }

    public void RemoveMobileChunk (MobileChunk mobileChunk) {
        Vector2Int regionPos = TerrainManager.inst.WorldToRegion(mobileChunk.position);
        Vector2Int chunkPos = TerrainManager.inst.WorldToChunk(mobileChunk.position);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to remove a mobile chunk to a none-loaded region.");
            return;
        }

        if(!entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Contains(mobileChunk.uid)) {
            Debug.Log("Trying to remove a mobile chunk in a region where it isn't even present.");
            return;
        }
        
        entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Remove(mobileChunk.uid);
    }

    public void RemoveEntity (Entity entity) {
        Vector2Int regionPos = TerrainManager.inst.WorldToRegion(entity.entityData.position);
        Vector2Int chunkPos = TerrainManager.inst.WorldToChunk(entity.entityData.position);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to remove a entity to a none-loaded region.");
            return;
        }

        if(!entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.ContainsKey(entity.entityData.uid)) {
            Debug.Log("Trying to remove a entity in a region where it isn't even present.");
            return;
        }
        
        entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.Remove(entity.entityData.uid);
    }

    public bool MoveMobileChunk (MobileChunk mobileChunk, Vector3 previousPosition) {

        Vector2Int regionPos = TerrainManager.inst.WorldToRegion(previousPosition);
        Vector2Int chunkPos = TerrainManager.inst.WorldToChunk(previousPosition);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to remove a mobile chunk to a none-loaded region.");
            return false;
        }

        if(!entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Contains(mobileChunk.uid)) {
            Debug.Log("Trying to remove a mobile chunk in a region where it isn't even present.");
        } else {
            entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Remove(mobileChunk.uid);
        }

        regionPos = TerrainManager.inst.WorldToRegion(mobileChunk.position);
        chunkPos = TerrainManager.inst.WorldToChunk(mobileChunk.position);
        
        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to add a mobile chunk to a none-loaded region.");

            regionPos = TerrainManager.inst.WorldToRegion(previousPosition);
            chunkPos = TerrainManager.inst.WorldToChunk(previousPosition);
            entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Add(mobileChunk.uid);
            return false;
        }

        if(entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Contains(mobileChunk.uid)) {
            Debug.Log("Trying to add a mobile chunk in a region where it already is.");
        } else {
            entityRegions[regionPos].GetSubRegion(chunkPos).mobileChunkUIDs.Add(mobileChunk.uid);
        }

        return true;
    }

    public bool MoveEntity (Entity entity, Vector3 previousPosition) {

        Vector2Int regionPos = TerrainManager.inst.WorldToRegion(previousPosition);
        Vector2Int chunkPos = TerrainManager.inst.WorldToChunk(previousPosition);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to remove a entity to a none-loaded region.");
            return false;
        }

        if(!entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.ContainsKey(entity.entityData.uid)) {
            Debug.Log("Trying to remove a entity in a region where it isn't even present.");
        } else {
            entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.Remove(entity.entityData.uid);
        }

        regionPos = TerrainManager.inst.WorldToRegion(entity.entityData.position);
        chunkPos = TerrainManager.inst.WorldToChunk(entity.entityData.position);

        if(!entityRegions.ContainsKey(regionPos)) {
            Debug.Log("Trying to add a entity to a none-loaded region.");

            regionPos = TerrainManager.inst.WorldToRegion(previousPosition);
            chunkPos = TerrainManager.inst.WorldToChunk(previousPosition);
            entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.Add(entity.entityData.uid, new EntityUIDAssetPair(entity.entityData.uid, entity.asset.globalID));
            return false;
        }

        if(entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.ContainsKey(entity.entityData.uid)) {
            Debug.Log("Trying to add a entity in a region where it already is.");
        } else {
            entityRegions[regionPos].GetSubRegion(chunkPos).entitiesUIDs.Add(entity.entityData.uid, new EntityUIDAssetPair(entity.entityData.uid, entity.asset.globalID));
        }

        return true;
    }
    #endregion
}

public class EntityRegion {
    public Vector2Int regionPosition;
    public SubEntityRegion[][] subRegions;
    public float timeOfLastAutosave;

    public EntityRegion () {
        subRegions = new SubEntityRegion[TerrainManager.inst.chunksPerRegionSide][];

        for(int x = 0; x < TerrainManager.inst.chunksPerRegionSide; x++) {
            subRegions[x] = new SubEntityRegion[TerrainManager.inst.chunksPerRegionSide];
            for(int y = 0; y < TerrainManager.inst.chunksPerRegionSide; y++) {
                subRegions[x][y] = new SubEntityRegion();
            }
        }
    }

    public bool IsAnySubRegionLoaded () {
        for(int x = 0; x < TerrainManager.inst.chunksPerRegionSide; x++) {
            for(int y = 0; y < TerrainManager.inst.chunksPerRegionSide; y++) {
                Vector2Int chunkPosition = new Vector2Int(
                    x + regionPosition.x * TerrainManager.inst.chunksPerRegionSide,
                    y + regionPosition.y * TerrainManager.inst.chunksPerRegionSide);
                
                if(TerrainManager.inst.chunks.ContainsKey(Hash.longFrom2D(chunkPosition))) {
                    return true;
                }
            }
        }
        return false;
    }

    public void Clear () {
        timeOfLastAutosave = 0f;
        for(int x = 0; x < TerrainManager.inst.chunksPerRegionSide; x++) {
            for(int y = 0; y < TerrainManager.inst.chunksPerRegionSide; y++) {
                subRegions[x][y].Clear();
            }
        }
    }

    #region Files & Serialization
    public void LoadRegion (Vector2Int regionPosition) {
        this.regionPosition = regionPosition;
        if(!WorldSaving.inst.LoadRegionFile(this, GameManager.inst.currentDataLoadMode)) {
            //Debug.LogError("Region failed to load. What are we gonna do?");
        }
    }

    public void SaveRegion () {
        WorldSaving.inst.SaveRegionFile(this);
    }
    #endregion

    #region Load/Unload Events
    public void LoadAllEntitiesChunks () {
        for(int x = 0; x < TerrainManager.inst.chunksPerRegionSide; x++) {
            for(int y = 0; y < TerrainManager.inst.chunksPerRegionSide; y++) {
                for(int i = subRegions[x][y].mobileChunkUIDs.Count - 1; i >= 0; i--) {
                    int uid = subRegions[x][y].mobileChunkUIDs[i];
                    subRegions[x][y].mobileChunkUIDs.RemoveAt(i);
                    if(VisualChunkManager.inst.mobileChunkPool.ContainsKey(uid)) {
                        continue;
                    }
                    TerrainManager.inst.LoadMobileChunkFromUID(uid);
                }
                foreach(KeyValuePair<int, EntityUIDAssetPair> kvp in subRegions[x][y].entitiesUIDs.ToList()) {
                    int uid = kvp.Key;
                    int gid = kvp.Value.gid;
                    subRegions[x][y].entitiesUIDs.Remove(uid);
                    if(EntityManager.inst.entitiesByUID.ContainsKey(uid)) {
                        continue;
                    }
                    EntityManager.inst.LoadFromUID(uid, gid); // Should read it to the subRegion Edit: What does that mean?
                }
            }
        }
    }

    public void UnloadAllMobileChunksAndEntities () {
        for(int x = 0; x < TerrainManager.inst.chunksPerRegionSide; x++) {
            for(int y = 0; y < TerrainManager.inst.chunksPerRegionSide; y++) {
                for(int i = 0; i < subRegions[x][y].mobileChunkUIDs.Count; i++) {
                    if(VisualChunkManager.inst.mobileChunkPool.ContainsKey(subRegions[x][y].mobileChunkUIDs[i])) {
                        VisualChunkManager.inst.UnloadMobileChunk(VisualChunkManager.inst.mobileChunkPool[subRegions[x][y].mobileChunkUIDs[i]]);
                    }
                }
                foreach(KeyValuePair<int, EntityUIDAssetPair> kvp in subRegions[x][y].entitiesUIDs) {
                    if(EntityManager.inst.entitiesByUID.ContainsKey(kvp.Key)) {
                        EntityManager.inst.UnloadEntity(EntityManager.inst.entitiesByUID[kvp.Key]);
                    }
                }
            }
        }
    }
    #endregion

    #region Edit Sub Regions
    public void ChangeSubRegionPositions () {
        for(int x = 0; x < TerrainManager.inst.chunksPerRegionSide; x++) {
            for(int y = 0; y < TerrainManager.inst.chunksPerRegionSide; y++) {
                subRegions[x][y].chunkPosition = (regionPosition * TerrainManager.inst.chunksPerRegionSide) + new Vector2Int(x, y);
            }
        }
    }
    
    public SubEntityRegion GetSubRegion (Vector2Int chunkPos) {
        Vector2Int regionChunkPos = regionPosition * TerrainManager.inst.chunksPerRegionSide;
        return subRegions[chunkPos.x - regionChunkPos.x][chunkPos.y - regionChunkPos.y];
    }
    #endregion
}

public class SubEntityRegion {
    public Vector2Int chunkPosition;
    public List<int> mobileChunkUIDs;
    public Dictionary<int, EntityUIDAssetPair> entitiesUIDs;

    public SubEntityRegion () {
        mobileChunkUIDs = new List<int>();
        entitiesUIDs = new Dictionary<int, EntityUIDAssetPair>();
    }

    public void Clear () {
        mobileChunkUIDs.Clear();
        entitiesUIDs.Clear();
    }
}

public struct EntityUIDAssetPair {
    public int uid;
    public int gid;

    public EntityUIDAssetPair (int uid, int gid) {
        this.uid = uid;
        this.gid = gid;
    }
}
