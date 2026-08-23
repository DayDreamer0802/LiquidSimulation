using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TowerDefenseTilePalette",
    menuName = "Rouge/Tower Defense Tile Palette")]
public sealed class RougeTowerDefenseTilePalette : ScriptableObject
{
    public const string SharedResourcePath = "Config/TowerDefenseTilePalette";

    [SerializeField] private List<RougeTowerDefenseMap.TileDefinition> tileDefinitions =
        new List<RougeTowerDefenseMap.TileDefinition>();

    private static RougeTowerDefenseTilePalette s_shared;

    public IReadOnlyList<RougeTowerDefenseMap.TileDefinition> TileDefinitions => tileDefinitions;

    public static RougeTowerDefenseTilePalette Shared
    {
        get
        {
            if (s_shared == null)
                s_shared = Resources.Load<RougeTowerDefenseTilePalette>(SharedResourcePath);
            return s_shared;
        }
    }

    private void OnValidate()
    {
        tileDefinitions ??= new List<RougeTowerDefenseMap.TileDefinition>();
        for (int i = 0; i < tileDefinitions.Count; i++)
        {
            RougeTowerDefenseMap.TileDefinition definition = tileDefinitions[i];
            if (definition == null) continue;
            definition.autoTilePrefabs ??= new GameObject[16];
            if (definition.autoTilePrefabs.Length != 16)
                System.Array.Resize(ref definition.autoTilePrefabs, 16);
        }
    }
}
