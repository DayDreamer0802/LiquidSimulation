using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class RougeTowerDefenseMapLoader
{
    private const string ArenaBackdropResource = "Shaders/RougeTechArenaBackdrop";
    private const string ArenaBackdropShaderName = "Rouge/Tech Arena Backdrop";
    private const string ArenaFrameResource = "Shaders/RougeTechArenaFrame";
    private const string ArenaFrameShaderName = "Rouge/Tech Arena Frame";

    [Header("Arena Presentation")]
    [SerializeField] private bool buildArenaPresentation = true;
    [SerializeField, Min(512f)] private float arenaBackdropSize = 2048f;
    [SerializeField, Range(0.12f, 0.3f)] private float arenaRailWidthInCells = 0.22f;
    [SerializeField, Range(0.4f, 3f)] private float arenaFoundationHeight = 1.35f;
    [SerializeField] private Color arenaBackdropBaseColor = new Color(0.025f, 0.06f, 0.095f, 1f);
    [SerializeField] private Color arenaBackdropOuterColor = new Color(0.009f, 0.023f, 0.042f, 1f);
    [SerializeField] private Color arenaFrameBaseColor = new Color(0.035f, 0.075f, 0.11f, 1f);
    [SerializeField] private Color arenaFramePanelColor = new Color(0.07f, 0.14f, 0.19f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color arenaEnergyColor =
        new Color(0.08f, 0.82f, 1.2f, 1f);

    private void BuildArenaPresentation()
    {
        if (!buildArenaPresentation || map == null || _runtimeRoot == null) return;

        bool[,] occupiedCells = BuildArenaOccupancyMask();
        GetArenaPresentationBounds(occupiedCells, out Vector2 center, out Vector2 occupiedSize);
        float backdropSize = Mathf.Max(arenaBackdropSize,
            Mathf.Max(occupiedSize.x, occupiedSize.y) +
            Mathf.Max(256f, map.CellSize * 16f));
        Transform presentationRoot = CreateRoot("Arena Presentation");

        Material backdropMaterial = CreateArenaMaterial(
            ArenaBackdropResource, ArenaBackdropShaderName, "Runtime Tech Arena Backdrop");
        Material frameMaterial = CreateArenaMaterial(
            ArenaFrameResource, ArenaFrameShaderName, "Runtime Tech Arena Frame");
        if (backdropMaterial == null || frameMaterial == null) return;

        ConfigureBackdropMaterial(backdropMaterial, center, backdropSize);
        ConfigureFrameMaterial(frameMaterial);
        Material energyMaterial = new Material(frameMaterial)
        {
            name = "Runtime Tech Arena Energy Seal",
            hideFlags = HideFlags.DontSave,
            enableInstancing = true
        };
        energyMaterial.SetFloat("_AccentOnly", 1f);
        _runtimeMaterials.Add(energyMaterial);
        CreateBackdrop(presentationRoot, backdropMaterial, center, backdropSize);
        CreateFoundationRuns(presentationRoot, frameMaterial, occupiedCells);
        CreateContourFrame(presentationRoot, frameMaterial, energyMaterial, occupiedCells);
    }

    private Material CreateArenaMaterial(string resourcePath, string shaderName, string materialName)
    {
        Shader shader = Resources.Load<Shader>(resourcePath);
        if (shader == null) shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning($"Arena presentation shader '{shaderName}' was not found.", this);
            return null;
        }

        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
            enableInstancing = true
        };
        _runtimeMaterials.Add(material);
        return material;
    }

    private void ConfigureBackdropMaterial(Material material, Vector2 center, float backdropSize)
    {
        material.SetColor("_BaseColor", arenaBackdropBaseColor);
        material.SetColor("_OuterColor", arenaBackdropOuterColor);
        material.SetColor("_GridColor", _commanderVisualTheme.MapGrid);
        material.SetColor("_AccentColor", arenaEnergyColor);
        material.SetVector("_ArenaCenter", new Vector4(center.x, center.y, 0f, 0f));
        material.SetFloat("_BackdropHalfSize", Mathf.Max(256f, backdropSize * 0.5f));
        material.SetFloat("_GridSize", Mathf.Max(0.025f, map.CellSize));
        material.SetFloat("_LineIntensity", 0.72f);
        material.SetFloat("_AnimationSpeed", 0.16f);
    }

    private void ConfigureFrameMaterial(Material material)
    {
        material.SetColor("_BaseColor", arenaFrameBaseColor);
        material.SetColor("_PanelColor", arenaFramePanelColor);
        material.SetColor("_AccentColor", arenaEnergyColor);
        material.SetFloat("_PanelSize", Mathf.Max(0.05f, map.CellSize));
        material.SetFloat("_EmissionStrength", 1.65f);
        material.SetFloat("_AccentOnly", 0f);
    }

    private void CreateBackdrop(Transform parent, Material material, Vector2 center,
        float backdropSize)
    {
        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Plane);
        backdrop.name = "Subtle Circuit Backdrop";
        backdrop.transform.SetParent(parent, false);
        backdrop.transform.position = new Vector3(center.x, -1.72f, center.y);
        float safeSize = Mathf.Max(512f, backdropSize);
        backdrop.transform.localScale = new Vector3(safeSize * 0.1f, 1f, safeSize * 0.1f);
        ConfigurePresentationRenderer(backdrop, material);
    }

    private bool[,] BuildArenaOccupancyMask()
    {
        bool[,] occupiedCells = new bool[map.Width, map.Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int tileIndex = map.GetTile(new Vector2Int(x, y));
                occupiedCells[x, y] = tileIndex != 0 && map.GetDefinition(tileIndex) != null;
            }
        }
        return occupiedCells;
    }

    private void GetArenaPresentationBounds(bool[,] occupiedCells, out Vector2 center,
        out Vector2 occupiedSize)
    {
        int minX = map.Width;
        int minY = map.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!occupiedCells[x, y]) continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            minX = 0;
            minY = 0;
            maxX = Mathf.Max(0, map.Width - 1);
            maxY = Mathf.Max(0, map.Height - 1);
        }

        occupiedSize = new Vector2(
            (maxX - minX + 1) * map.CellSize,
            (maxY - minY + 1) * map.CellSize);
        center = new Vector2(
            map.Origin.x + (minX + maxX + 1) * map.CellSize * 0.5f,
            map.Origin.y + (minY + maxY + 1) * map.CellSize * 0.5f);
    }

    private static bool IsArenaCellOccupied(bool[,] occupiedCells, int x, int y)
    {
        return x >= 0 && y >= 0 && x < occupiedCells.GetLength(0) &&
               y < occupiedCells.GetLength(1) && occupiedCells[x, y];
    }

    private void CreateFoundationRuns(Transform parent, Material material, bool[,] occupiedCells)
    {
        float designScale = map.CellSize / 8f;
        float height = Mathf.Max(0.05f, arenaFoundationHeight * designScale);
        float cellSize = map.CellSize;
        bool[,] usedCells = new bool[map.Width, map.Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!occupiedCells[x, y] || usedCells[x, y]) continue;

                int rectangleWidth = 1;
                while (x + rectangleWidth < map.Width &&
                       occupiedCells[x + rectangleWidth, y] &&
                       !usedCells[x + rectangleWidth, y]) rectangleWidth++;

                int rectangleHeight = 1;
                bool canGrow = true;
                while (y + rectangleHeight < map.Height && canGrow)
                {
                    for (int testX = x; testX < x + rectangleWidth; testX++)
                    {
                        if (occupiedCells[testX, y + rectangleHeight] &&
                            !usedCells[testX, y + rectangleHeight]) continue;
                        canGrow = false;
                        break;
                    }
                    if (canGrow) rectangleHeight++;
                }

                for (int markY = y; markY < y + rectangleHeight; markY++)
                {
                    for (int markX = x; markX < x + rectangleWidth; markX++)
                        usedCells[markX, markY] = true;
                }

                float centerX = map.Origin.x +
                    (x + rectangleWidth * 0.5f) * cellSize;
                float centerZ = map.Origin.y +
                    (y + rectangleHeight * 0.5f) * cellSize;
                GameObject foundation = CreateFramePiece(
                    $"Foundation [{x},{y}] {rectangleWidth}x{rectangleHeight}",
                    parent,
                    material,
                    new Vector3(centerX, -0.08f - height * 0.5f, centerZ),
                    new Vector3(rectangleWidth * cellSize, height,
                        rectangleHeight * cellSize));
                foundation.GetComponent<Renderer>().receiveShadows = true;
            }
        }
    }

    private void CreateContourFrame(Transform parent, Material armorMaterial,
        Material energyMaterial, bool[,] occupiedCells)
    {
        float railWidth = Mathf.Max(0.025f,
            map.CellSize * arenaRailWidthInCells);
        float railHeight = Mathf.Max(0.012f, railWidth * 0.23f);
        float energyWidth = Mathf.Max(0.008f, map.CellSize * 0.032f);
        float energyHeight = Mathf.Max(0.005f, energyWidth * 0.58f);

        CreateHorizontalContourRuns(parent, armorMaterial, energyMaterial,
            occupiedCells, 1, 1f, "North",
            railWidth, railHeight, energyWidth, energyHeight);
        CreateHorizontalContourRuns(parent, armorMaterial, energyMaterial,
            occupiedCells, -1, -1f, "South",
            railWidth, railHeight, energyWidth, energyHeight);
        CreateVerticalContourRuns(parent, armorMaterial, energyMaterial,
            occupiedCells, 1, 1f, "East",
            railWidth, railHeight, energyWidth, energyHeight);
        CreateVerticalContourRuns(parent, armorMaterial, energyMaterial,
            occupiedCells, -1, -1f, "West",
            railWidth, railHeight, energyWidth, energyHeight);
    }

    private void CreateHorizontalContourRuns(Transform parent, Material armorMaterial,
        Material energyMaterial, bool[,] occupiedCells, int neighborOffset,
        float outwardSign, string edgeName, float railWidth, float railHeight,
        float energyWidth, float energyHeight)
    {
        float cellSize = map.CellSize;
        for (int y = 0; y < map.Height; y++)
        {
            int x = 0;
            while (x < map.Width)
            {
                while (x < map.Width && (!occupiedCells[x, y] ||
                       IsArenaCellOccupied(occupiedCells, x, y + neighborOffset))) x++;
                if (x >= map.Width) break;
                int startX = x;
                while (x < map.Width && occupiedCells[x, y] &&
                       !IsArenaCellOccupied(occupiedCells, x, y + neighborOffset)) x++;
                int cellCount = x - startX;
                float runLength = cellCount * cellSize;
                float centerX = map.Origin.x + (startX + cellCount * 0.5f) * cellSize;
                float boundaryZ = map.Origin.y +
                    (y + (outwardSign > 0f ? 1f : 0f)) * cellSize;

                CreateFramePiece($"{edgeName} Armor [{startX},{y}] {cellCount}",
                    parent, armorMaterial,
                    new Vector3(centerX, 0.015f, boundaryZ + outwardSign * railWidth * 0.5f),
                    new Vector3(runLength + railWidth, railHeight, railWidth));
                CreateFramePiece($"{edgeName} Energy Seal [{startX},{y}] {cellCount}",
                    parent, energyMaterial,
                    new Vector3(centerX, 0.22f,
                        boundaryZ + outwardSign * energyWidth * 0.72f),
                    new Vector3(runLength + energyWidth, energyHeight, energyWidth));
            }
        }
    }

    private void CreateVerticalContourRuns(Transform parent, Material armorMaterial,
        Material energyMaterial, bool[,] occupiedCells, int neighborOffset,
        float outwardSign, string edgeName, float railWidth, float railHeight,
        float energyWidth, float energyHeight)
    {
        float cellSize = map.CellSize;
        for (int x = 0; x < map.Width; x++)
        {
            int y = 0;
            while (y < map.Height)
            {
                while (y < map.Height && (!occupiedCells[x, y] ||
                       IsArenaCellOccupied(occupiedCells, x + neighborOffset, y))) y++;
                if (y >= map.Height) break;
                int startY = y;
                while (y < map.Height && occupiedCells[x, y] &&
                       !IsArenaCellOccupied(occupiedCells, x + neighborOffset, y)) y++;
                int cellCount = y - startY;
                float runLength = cellCount * cellSize;
                float centerZ = map.Origin.y + (startY + cellCount * 0.5f) * cellSize;
                float boundaryX = map.Origin.x +
                    (x + (outwardSign > 0f ? 1f : 0f)) * cellSize;

                CreateFramePiece($"{edgeName} Armor [{x},{startY}] {cellCount}",
                    parent, armorMaterial,
                    new Vector3(boundaryX + outwardSign * railWidth * 0.5f, 0.015f, centerZ),
                    new Vector3(railWidth, railHeight, runLength + railWidth));
                CreateFramePiece($"{edgeName} Energy Seal [{x},{startY}] {cellCount}",
                    parent, energyMaterial,
                    new Vector3(boundaryX + outwardSign * energyWidth * 0.72f,
                        0.22f, centerZ),
                    new Vector3(energyWidth, energyHeight, runLength + energyWidth));
            }
        }
    }

    private GameObject CreateFramePiece(string name, Transform parent, Material material,
        Vector3 position, Vector3 scale)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        piece.transform.SetParent(parent, false);
        piece.transform.position = position;
        piece.transform.localScale = scale;
        ConfigurePresentationRenderer(piece, material);
        return piece;
    }

    private Renderer ConfigurePresentationRenderer(GameObject instance, Material material)
    {
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        instance.layer = ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : gameObject.layer;
        Collider collider = instance.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        Renderer renderer = instance.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }
}
