using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class RougeTowerDefenseMapLoader
{
    private RougeCommanderVisualTheme _commanderVisualTheme =
        RougeCommanderVisualThemes.Resolve(
            RougeAutoplayCommanderJson.DefaultCommanderName);
    private bool _commanderThemeBaselineCaptured;
    private Color _baselineArenaBackdropBaseColor;
    private Color _baselineArenaBackdropOuterColor;
    private Color _baselineArenaFrameBaseColor;
    private Color _baselineArenaFramePanelColor;
    private Color _baselineArenaEnergyColor;
    private Color _baselineStartupCrystalColor;
    private Color _baselinePlacedTowerGridColor;

    public RougeCommanderVisualTheme CommanderVisualTheme => _commanderVisualTheme;

    public void ApplyCommanderVisualTheme(RougeCommanderVisualTheme theme)
    {
        CaptureCommanderThemeBaseline();
        _commanderVisualTheme = theme ?? RougeCommanderVisualThemes.Resolve(
            RougeAutoplayCommanderJson.DefaultCommanderName);

        if (_commanderVisualTheme.UsesDefaultPalette)
        {
            // Lan owns the original art direction. Selecting her must be a true
            // no-op, including scene-authored overrides rather than an approximation
            // reconstructed from palette constants.
            arenaBackdropBaseColor = _baselineArenaBackdropBaseColor;
            arenaBackdropOuterColor = _baselineArenaBackdropOuterColor;
            arenaFrameBaseColor = _baselineArenaFrameBaseColor;
            arenaFramePanelColor = _baselineArenaFramePanelColor;
            arenaEnergyColor = _baselineArenaEnergyColor;
            startupCrystalColor = _baselineStartupCrystalColor;
            placedTowerGridColor = _baselinePlacedTowerGridColor;
        }
        else
        {
            arenaBackdropBaseColor = _commanderVisualTheme.MapBackdropBase;
            arenaBackdropOuterColor = _commanderVisualTheme.MapBackdropOuter;
            arenaFrameBaseColor = _commanderVisualTheme.MapFrameBase;
            arenaFramePanelColor = _commanderVisualTheme.MapFramePanel;
            arenaEnergyColor = new Color(
                _commanderVisualTheme.Accent.r * 1.2f,
                _commanderVisualTheme.Accent.g * 1.2f,
                _commanderVisualTheme.Accent.b * 1.2f,
                1f);
            startupCrystalColor = _commanderVisualTheme.ScanColor;
            placedTowerGridColor = new Color(
                _commanderVisualTheme.Accent.r,
                _commanderVisualTheme.Accent.g,
                _commanderVisualTheme.Accent.b,
                0.78f);
        }

        RougeVisualQualityManager.ApplyCommanderVisualTheme(
            _commanderVisualTheme);

        ApplyCommanderThemeToRuntimeMaterials();
        ApplyCommanderThemeToAllNeutralTiles();
        if (_towerFootprintGridOverlay != null &&
            _towerFootprintGridOverlay.mesh != null)
            BuildTowerGridStateMesh(_towerFootprintGridOverlay.mesh,
                _towerFootprintGridOverlay.anchor,
                _towerFootprintGridOverlay.size);
    }

    private void ApplyCommanderThemeToRuntimeMaterials()
    {
        for (int i = 0; i < _runtimeMaterials.Count; i++)
        {
            Material material = _runtimeMaterials[i];
            if (material == null) continue;
            string materialName = material.name ?? string.Empty;

            if (materialName.IndexOf("Tech Arena Backdrop",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetThemeColor(material, "_BaseColor",
                    _commanderVisualTheme.MapBackdropBase);
                SetThemeColor(material, "_OuterColor",
                    _commanderVisualTheme.MapBackdropOuter);
                SetThemeColor(material, "_GridColor",
                    _commanderVisualTheme.MapGrid);
                SetThemeColor(material, "_AccentColor", arenaEnergyColor);
                continue;
            }

            if (materialName.IndexOf("Tech Arena Frame",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                materialName.IndexOf("Tech Arena Energy Seal",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetThemeColor(material, "_BaseColor",
                    _commanderVisualTheme.MapFrameBase);
                SetThemeColor(material, "_PanelColor",
                    _commanderVisualTheme.MapFramePanel);
                SetThemeColor(material, "_AccentColor", arenaEnergyColor);
                continue;
            }

            if (materialName.StartsWith("Runtime Sci-Fi Ground",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetThemeColor(material, "_BaseColor",
                    _commanderVisualTheme.MapGroundBase);
                SetThemeColor(material, "_PanelColor",
                    _commanderVisualTheme.MapGroundPanel);
                SetThemeColor(material, "_AccentColor",
                    _commanderVisualTheme.MapGroundAccent);
                continue;
            }

            if (materialName.StartsWith("Runtime Sci-Fi Placement Pad",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetThemeColor(material, "_BaseColor",
                    _commanderVisualTheme.MapStandardPadBase);
                if (materialName.EndsWith("Tower Place",
                        StringComparison.OrdinalIgnoreCase))
                    SetThemeColor(material, "_AccentColor",
                        _commanderVisualTheme.MapStandardPadAccent);
                continue;
            }

            if (materialName.IndexOf("Tower Place Grid",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetThemeColor(material, "_BaseColor",
                    _commanderVisualTheme.PlacementGridBase);
                SetThemeColor(material, "_LineColor",
                    _commanderVisualTheme.PlacementGridLine);
            }
        }
    }

    private void ApplyCommanderThemeToAllNeutralTiles()
    {
        if (map == null) return;
        foreach (KeyValuePair<Vector2Int, TileVisualState> pair in _tileVisuals)
            ApplyCommanderThemeToTile(pair.Key);
    }

    private void ApplyCommanderThemeToTile(Vector2Int cell)
    {
        if (map == null || !_tileVisuals.TryGetValue(cell,
                out TileVisualState visual) || visual == null)
            return;

        int tileIndex = map.GetTile(cell);
        RougeTowerDefenseMap.TileDefinition definition = map.GetDefinition(tileIndex);
        if (definition == null || definition.blocksNavigation) return;

        RougeTowerPlaceEffect effect = GetEffectiveTowerPlaceEffect(cell);
        bool standardPad = definition.towerPlace &&
                           effect == RougeTowerPlaceEffect.None;
        for (int rendererIndex = 0;
             rendererIndex < visual.renderers.Length; rendererIndex++)
        {
            Renderer renderer = visual.renderers[rendererIndex];
            if (renderer == null || renderer is SpriteRenderer) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0;
                 materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null) continue;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties, materialIndex);
                if (definition.towerPlace)
                {
                    Color padBase = _commanderVisualTheme.UsesDefaultPalette
                        ? new Color(0.07f, 0.135f, 0.19f, 1f)
                        : _commanderVisualTheme.MapStandardPadBase;
                    if (material.HasProperty("_BaseColor"))
                        properties.SetColor("_BaseColor", padBase);
                    if (standardPad && material.HasProperty("_AccentColor"))
                    {
                        Color accent = _commanderVisualTheme.UsesDefaultPalette
                            ? definition.editorColor
                            : _commanderVisualTheme.MapStandardPadAccent;
                        accent.a = 1f;
                        properties.SetColor("_AccentColor", accent);
                    }
                }
                else
                {
                    Color source = definition.editorColor;
                    source.a = 1f;
                    Color groundBase = _commanderVisualTheme.UsesDefaultPalette
                        ? Color.Lerp(new Color(0.16f, 0.25f, 0.32f, 1f),
                            source, 0.58f)
                        : Color.Lerp(_commanderVisualTheme.MapGroundBase,
                            source, 0.34f);
                    Color groundPanel = _commanderVisualTheme.UsesDefaultPalette
                        ? Color.Lerp(groundBase * 0.74f,
                            new Color(0.09f, 0.16f, 0.22f, 1f), 0.42f)
                        : Color.Lerp(groundBase * 0.74f,
                            _commanderVisualTheme.MapGroundPanel, 0.52f);
                    Color groundAccent = _commanderVisualTheme.UsesDefaultPalette
                        ? Color.Lerp(groundBase,
                            new Color(0.05f, 0.62f, 0.72f, 1f), 0.36f)
                        : Color.Lerp(groundBase,
                            _commanderVisualTheme.MapGroundAccent, 0.48f);
                    groundPanel.a = 1f;
                    groundAccent.a = 1f;
                    if (material.HasProperty("_BaseColor"))
                        properties.SetColor("_BaseColor", groundBase);
                    if (material.HasProperty("_PanelColor"))
                        properties.SetColor("_PanelColor", groundPanel);
                    if (material.HasProperty("_AccentColor"))
                        properties.SetColor("_AccentColor", groundAccent);
                }
                renderer.SetPropertyBlock(properties, materialIndex);
            }
        }

        // Special node colours remain gameplay-semantic. Reapply them after the
        // neutral surface tint so pink/cyan themes never make two node types look
        // identical.
        if (definition.towerPlace && effect != RougeTowerPlaceEffect.None)
            ApplyTileEffectColor(cell, effect);
    }

    private static void SetThemeColor(Material material, string property,
        Color color)
    {
        if (material != null && material.HasProperty(property))
            material.SetColor(property, color);
    }

    private void CaptureCommanderThemeBaseline()
    {
        if (_commanderThemeBaselineCaptured) return;
        _commanderThemeBaselineCaptured = true;
        _baselineArenaBackdropBaseColor = arenaBackdropBaseColor;
        _baselineArenaBackdropOuterColor = arenaBackdropOuterColor;
        _baselineArenaFrameBaseColor = arenaFrameBaseColor;
        _baselineArenaFramePanelColor = arenaFramePanelColor;
        _baselineArenaEnergyColor = arenaEnergyColor;
        _baselineStartupCrystalColor = startupCrystalColor;
        _baselinePlacedTowerGridColor = placedTowerGridColor;
    }
}
