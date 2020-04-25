﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour {
    public static GameManager inst;

    public PlayerController[] allPlayers;
    private EngineModes _engineMode = EngineModes.Edit;
    private GameModes _gameMode;

    public delegate void EngineModeChangeHandler();
    public event EngineModeChangeHandler OnChangeEngineMode;

    public EngineModes engineMode {
        get => _engineMode;
        set {
            ChunkLoader.inst.UnloadAll(value == EngineModes.Edit);
            if(value == EngineModes.Edit) {
                TerrainManager.inst.CompleteEntitySave();
            }
            _engineMode = value;
            if(_engineMode == EngineModes.Play) {
                WorldSaving.inst.ClearPlayFolders();
            }

            WorldSaving.inst.OnReloadEngine();
            ChunkLoader.inst.ReloadAll();

            OnChangeEngineMode?.Invoke();
        }
    }

    public GameModes gameMode {
        get => _gameMode;
        set => _gameMode = value;
    }

    public DataLoadMode currentDataLoadMode {
        private set => currentDataLoadMode = value;
        get {
            if(engineMode == EngineModes.Play) {
                return DataLoadMode.TryReadonly;
            }
            return DataLoadMode.Default;
        }
    }

    private void Awake () {
        inst = this;
    }

    public PlayerController GetNearestPlayer (Vector2 position) {
        int nearestPlayerIndex = -1;
        float smallestDistance = float.PositiveInfinity;
        for(int i = 0; i < allPlayers.Length; i++) {
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
}

public enum EngineModes {
    Play,
    Edit
}

public enum GameModes {
    PlayOnly,
    EditorAllowed
}
