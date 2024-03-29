﻿using UnityEditor;
using UnityEngine;


namespace AsepriteImporter
{
    [System.Serializable]
	public class AseFileTextureSettings
	{

		[SerializeField] public TextureImporterType textureType = TextureImporterType.Sprite;
		[SerializeField] public int pixelsPerUnit = 16;
        [SerializeField] public bool transparentMask = false;       
        [SerializeField] public Color transparentColor = Color.magenta; 
        [SerializeField] public SpriteMeshType meshType = SpriteMeshType.FullRect;
		[Range(0, 32)] [SerializeField] public uint extrudeEdges = 1;
		[SerializeField] public Vector2 spritePivot = new Vector2(0.5f, 0.5f);
		[SerializeField] public bool generatePhysics = false;

		[SerializeField] public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
		[SerializeField] public FilterMode filterMode = FilterMode.Point;

		[SerializeField] public Vector2Int tileSize = new Vector2Int(16, 16);
		[SerializeField] public Vector2Int tilePadding = new Vector2Int(0, 0);
		[SerializeField] public Vector2Int tileOffset = new Vector2Int(0, 0);

	}
}