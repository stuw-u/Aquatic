﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour {
    private static GameManager _instance;

    public const float autoSaveTimeLimit = 10f;

    public static GameManager inst {
        get {
            if(_instance == null) {
                _instance = new GameObject("GameManager").AddComponent<GameManager>();
                DontDestroyOnLoad(_instance);
                _instance.Init();
            }

            return _instance;
        }
    }

    public List<PlayerController> allPlayers;
    private EngineModes _engineMode = EngineModes.Edit;
    private GameModes _gameMode;

    public delegate void EngineModeChangeHandler();
    public event EngineModeChangeHandler OnChangeEngineMode;

    public delegate void OnCloseWorldHandler();
    public event OnCloseWorldHandler OnCloseWorld;

    private void Init () {
        allPlayers = new List<PlayerController>();
        Application.quitting += () => OnCloseWorld?.Invoke();
        SceneManager.sceneUnloaded += (s) => {
            if(s.name == "Main") {
                OnCloseWorld?.Invoke();
            }
        };
        OnCloseWorld += () => {
            CompleteSave();
            allPlayers.Clear();
        };
    }

    #region Events
    #endregion

    #region Getters Setters
    public EngineModes engineMode {
        get => _engineMode;
        set {
            if(NetworkAssistant.inst.IsServer) {
                ChunkLoader.inst.UnloadAll(_engineMode == EngineModes.Edit);
                if(value == EngineModes.Edit) {
                    CompleteEntitySave();
                }
            }
            _engineMode = value;
            if(NetworkAssistant.inst.IsServer) {
                if(_engineMode == EngineModes.Play) {
                    WorldSaving.inst.ClearPlayFolders();
                }

                WorldSaving.inst.OnReloadEngine();
                ChunkLoader.inst.LoadAll();
            }
            SetPlayersFlyMode(_engineMode == EngineModes.Edit);

            OnChangeEngineMode?.Invoke();
        }
    }

    public GameModes gameMode {
        get => _gameMode;
        set => _gameMode = value;
    }

    public DataLoadMode currentDataLoadMode {
        get {
            if(engineMode == EngineModes.Play) {
                return DataLoadMode.DefaultElseReadonly;
            } else if(engineMode == EngineModes.Edit) {
                return DataLoadMode.Readonly;
            } else {
                return DataLoadMode.Readonly;
            }
            
        }
    }

    public DataSaveMode currentDataSaveMode {
        get {
            if(engineMode == EngineModes.Play) {
                return DataSaveMode.Default;
            } else if(engineMode == EngineModes.Edit) {
                return DataSaveMode.Readonly;
            } else {
                return DataSaveMode.Default;
            }
        }
    }
    #endregion

    #region Players
    public PlayerController GetNearestPlayer (Vector2 position) {
        int nearestPlayerIndex = -1;
        float smallestDistance = float.PositiveInfinity;
        for(int i = 0; i < allPlayers.Count; i++) {
            float dist = ((Vector2)allPlayers[i].transform.position - position).sqrMagnitude;
            if(dist < smallestDistance) {
                smallestDistance = dist;
                nearestPlayerIndex = i;
            }
        }

        if(nearestPlayerIndex == -1) {
            return null;
        }
        return allPlayers[nearestPlayerIndex];
    }

    void SetPlayersFlyMode (bool flyMode) {
        foreach(PlayerController pc in allPlayers) {
            if(pc.isControlledLocally) {
                pc.status.isFlying = flyMode;
            }
        }
    }
    #endregion

    #region Saving
    public void AutoSaves () {
        DataSaveMode dataSaveMode = currentDataSaveMode;

        foreach(KeyValuePair<long, DataChunk> kvp in TerrainManager.inst.chunks) {
            if(Time.time - kvp.Value.timeOfLastAutosave > autoSaveTimeLimit) {
                kvp.Value.timeOfLastAutosave = Time.time;

                TerrainManager.inst.StartNewChunkJobAt(
                    kvp.Value.chunkPosition,
                    JobState.Saving,
                    currentDataSaveMode == DataSaveMode.Readonly,
                    () => { // JOB
                        WorldSaving.inst.SaveChunk(kvp.Value, dataSaveMode);
                    },
                    () => { // CALLBACK

                    },
                    () => { // CALLBACK IF CANCELLED

                    },
                    runImidialty: false
                );

                //WorldSaving.inst.SaveChunk(kvp.Value, currentDataLoadMode == DataLoadMode.TryReadonly);
            }
        }
        foreach(KeyValuePair<int, MobileChunk> kvp in VisualChunkManager.inst.mobileChunkPool) {
            if(Time.time - kvp.Value.timeOfLastAutosave > autoSaveTimeLimit) {
                kvp.Value.timeOfLastAutosave = Time.time;
                WorldSaving.inst.SaveMobileChunk(kvp.Value.mobileDataChunk);
            }
        }
        foreach(KeyValuePair<int, Entity> kvp in EntityManager.inst.entitiesByUID) {
            if(Time.time - kvp.Value.entityData.timeOfLastAutosave > autoSaveTimeLimit) {
                kvp.Value.entityData.timeOfLastAutosave = Time.time;
                EntityManager.inst.SaveEntity(kvp.Value, false);
            }
        }
        foreach(PlayerController pc in allPlayers) {
            if(Time.time - pc.timeOfLastAutosave > autoSaveTimeLimit) {
                pc.timeOfLastAutosave = Time.time;
                WorldSaving.inst.SavePlayer(pc.status, 0);
            }
        }
        EntityRegionManager.inst.CheckForAutosaves();
    }

    public void CompleteSave () {
        if(TerrainManager.inst == null) {
            return;
        }
        foreach(KeyValuePair<long, DataChunk> kvp in TerrainManager.inst.chunks) {
            WorldSaving.inst.SaveChunk(kvp.Value, currentDataSaveMode);
        }
        foreach(KeyValuePair<int, MobileChunk> kvp in VisualChunkManager.inst.mobileChunkPool) {
            WorldSaving.inst.SaveMobileChunk(kvp.Value.mobileDataChunk);
        }
        foreach(KeyValuePair<int, Entity> kvp in EntityManager.inst.entitiesByUID) {
            EntityManager.inst.SaveEntity(kvp.Value, true);
        }
        foreach(PlayerController pc in allPlayers) {
            WorldSaving.inst.SavePlayer(pc.status, 0);
        }
        EntityRegionManager.inst.SaveAllRegions();
    }

    public void CompleteEntitySave () {
        foreach(KeyValuePair<int, Entity> kvp in EntityManager.inst.entitiesByUID) {
            EntityManager.inst.SaveEntity(kvp.Value, true);
        }
        foreach(KeyValuePair<int, MobileChunk> kvp in VisualChunkManager.inst.mobileChunkPool) {
            WorldSaving.inst.SaveMobileChunk(kvp.Value.mobileDataChunk);
        }
    }
    #endregion
}

public enum EngineModes {
    Play,
    Edit
}

public enum GameModes {
    PlayOnly,
    EditorAllowed
}
