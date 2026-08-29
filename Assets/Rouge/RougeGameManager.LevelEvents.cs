using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private sealed class ActiveTowerDefenseLevelEvent
    {
        public RougeLevelEventDefinition Definition;
        public float EndTime;
    }

    private readonly List<RougeLevelEventTrigger> _towerDefenseLevelEventTimeline =
        new List<RougeLevelEventTrigger>();
    private readonly List<ActiveTowerDefenseLevelEvent> _towerDefenseActiveLevelEvents =
        new List<ActiveTowerDefenseLevelEvent>();
    private System.Random _towerDefenseLevelEventRandom;
    private int _towerDefenseNextLevelEventIndex;
    private bool _towerDefenseHasPreviousLevelEventTone;
    private RougeLevelEventTone _towerDefensePreviousLevelEventTone;
    private bool _towerDefenseLevelEventsControlEliteUnlock;
    private bool _towerDefenseLevelEventElitesUnlocked;
    private float _towerDefenseLevelEventEliteChanceMultiplier = 1f;
    private float _towerDefenseLevelEventSpawnRateMultiplier = 1f;
    private float _towerDefenseLevelEventKillGoldMultiplier = 1f;
    private float _towerDefenseLevelEventEnemyHealthMultiplier = 1f;
    private float _towerDefenseLevelEventEnemyMoveSpeedMultiplier = 1f;
    private float _towerDefenseLevelEventEliteHealthMultiplier = 1f;
    private float _towerDefenseLevelEventEliteMoveSpeedMultiplier = 1f;
    private float _towerDefenseLevelEventTowerDamageMultiplier = 1f;
    private float _towerDefenseLevelEventTowerAttackSpeedMultiplier = 1f;
    private float _towerDefenseLevelEventGoldFraction;

    private Canvas _towerDefenseLevelEventCanvas;
    private CanvasGroup _towerDefenseLevelEventBannerGroup;
    private Image _towerDefenseLevelEventBannerAccent;
    private Text _towerDefenseLevelEventBannerTitle;
    private Text _towerDefenseLevelEventBannerDescription;
    private float _towerDefenseLevelEventBannerRemaining;
    private CanvasGroup _towerDefenseBossWarningGroup;
    private Text _towerDefenseBossWarningTitle;
    private Text _towerDefenseBossWarningDetail;
    private float _towerDefenseBossWarningRemaining;

    private void InitializeTowerDefenseLevelEvents()
    {
        _towerDefenseLevelEventTimeline.Clear();
        _towerDefenseActiveLevelEvents.Clear();
        _towerDefenseNextLevelEventIndex = 0;
        _towerDefenseHasPreviousLevelEventTone = false;
        _towerDefenseLevelEventGoldFraction = 0f;
        _towerDefenseLevelEventRandom = new System.Random(
            unchecked(TowerDefenseFixedRandomSeed ^ 0x51E7A11 ^
                      Environment.TickCount ^ GetInstanceID() * 7919));

        IReadOnlyList<RougeLevelEventTrigger> configuredTimeline =
            _towerDefenseLevel != null ? _towerDefenseLevel.LevelEventTimeline : null;
        for (int i = 0; configuredTimeline != null &&
             i < configuredTimeline.Count; i++)
        {
            if (configuredTimeline[i] != null)
                _towerDefenseLevelEventTimeline.Add(configuredTimeline[i]);
        }
        _towerDefenseLevelEventTimeline.Sort((left, right) =>
            left.triggerMinute.CompareTo(right.triggerMinute));

        _towerDefenseLevelEventsControlEliteUnlock = false;
        for (int i = 0; i < _towerDefenseLevelEventTimeline.Count; i++)
        {
            RougeLevelEventTrigger trigger = _towerDefenseLevelEventTimeline[i];
            for (int candidate = 0; trigger.candidateEventIds != null &&
                 candidate < trigger.candidateEventIds.Count; candidate++)
            {
                RougeLevelEventDefinition definition =
                    FindTowerDefenseLevelEventDefinition(
                        trigger.candidateEventIds[candidate]);
                if (DefinitionContainsTowerDefenseLevelEventEffect(definition,
                        RougeLevelEventEffectType.UnlockEliteSpawns))
                    _towerDefenseLevelEventsControlEliteUnlock = true;
            }
        }
        RecalculateTowerDefenseLevelEventModifiers();
    }

    private void DisposeTowerDefenseLevelEvents()
    {
        _towerDefenseLevelEventTimeline.Clear();
        _towerDefenseActiveLevelEvents.Clear();
        _towerDefenseLevelEventRandom = null;
        if (_towerDefenseLevelEventCanvas != null)
            Destroy(_towerDefenseLevelEventCanvas.gameObject);
        _towerDefenseLevelEventCanvas = null;
        _towerDefenseLevelEventBannerGroup = null;
        _towerDefenseLevelEventBannerAccent = null;
        _towerDefenseLevelEventBannerTitle = null;
        _towerDefenseLevelEventBannerDescription = null;
        _towerDefenseBossWarningGroup = null;
        _towerDefenseBossWarningTitle = null;
        _towerDefenseBossWarningDetail = null;
        _towerDefenseLevelEventBannerRemaining = 0f;
        _towerDefenseBossWarningRemaining = 0f;
    }

    private void UpdateTowerDefenseLevelEvents()
    {
        UpdateTowerDefenseLevelEventUi(Time.unscaledDeltaTime);
        bool modifiersChanged = false;
        for (int i = _towerDefenseActiveLevelEvents.Count - 1; i >= 0; i--)
        {
            ActiveTowerDefenseLevelEvent active =
                _towerDefenseActiveLevelEvents[i];
            if (active.EndTime < 0f || _survivalTime < active.EndTime) continue;
            _towerDefenseActiveLevelEvents.RemoveAt(i);
            modifiersChanged = true;
        }

        while (_towerDefenseNextLevelEventIndex <
               _towerDefenseLevelEventTimeline.Count)
        {
            RougeLevelEventTrigger trigger = _towerDefenseLevelEventTimeline[
                _towerDefenseNextLevelEventIndex];
            if (_survivalTime + 0.0001f <
                Mathf.Max(0f, trigger.triggerMinute) * 60f) break;
            _towerDefenseNextLevelEventIndex++;
            RougeLevelEventDefinition selected =
                SelectTowerDefenseLevelEvent(trigger);
            if (selected != null)
            {
                ActivateTowerDefenseLevelEvent(selected);
                modifiersChanged = true;
            }
        }

        if (modifiersChanged)
            RecalculateTowerDefenseLevelEventModifiers();
    }

    private RougeLevelEventDefinition SelectTowerDefenseLevelEvent(
        RougeLevelEventTrigger trigger)
    {
        if (trigger?.candidateEventIds == null ||
            trigger.candidateEventIds.Count == 0) return null;
        int start = _towerDefenseLevelEventRandom != null
            ? _towerDefenseLevelEventRandom.Next(trigger.candidateEventIds.Count)
            : 0;
        RougeLevelEventDefinition fallback = null;
        for (int offset = 0; offset < trigger.candidateEventIds.Count; offset++)
        {
            int index = (start + offset) % trigger.candidateEventIds.Count;
            RougeLevelEventDefinition definition =
                FindTowerDefenseLevelEventDefinition(
                    trigger.candidateEventIds[index]);
            if (definition == null) continue;
            fallback ??= definition;
            // Avoid chaining two pure danger events when this time slot also offers
            // a buff/mixed beat. Randomness stays, but a run cannot roll an entire
            // timeline of stacked punishment.
            if (_towerDefenseHasPreviousLevelEventTone &&
                _towerDefensePreviousLevelEventTone == RougeLevelEventTone.Danger &&
                definition.tone == RougeLevelEventTone.Danger)
                continue;
            _towerDefensePreviousLevelEventTone = definition.tone;
            _towerDefenseHasPreviousLevelEventTone = true;
            return definition;
        }
        if (fallback != null)
        {
            _towerDefensePreviousLevelEventTone = fallback.tone;
            _towerDefenseHasPreviousLevelEventTone = true;
        }
        return fallback;
    }

    private RougeLevelEventDefinition FindTowerDefenseLevelEventDefinition(
        string eventId)
    {
        IReadOnlyList<RougeLevelEventDefinition> definitions =
            _towerDefenseLevel != null
                ? _towerDefenseLevel.LevelEventDefinitions
                : null;
        for (int i = 0; definitions != null && i < definitions.Count; i++)
        {
            RougeLevelEventDefinition definition = definitions[i];
            if (definition != null && string.Equals(definition.eventId, eventId,
                    StringComparison.OrdinalIgnoreCase))
                return definition;
        }
        return null;
    }

    private static bool DefinitionContainsTowerDefenseLevelEventEffect(
        RougeLevelEventDefinition definition, RougeLevelEventEffectType type)
    {
        for (int i = 0; definition?.effects != null &&
             i < definition.effects.Count; i++)
        {
            if (definition.effects[i] != null &&
                definition.effects[i].type == type) return true;
        }
        return false;
    }

    private void ActivateTowerDefenseLevelEvent(
        RougeLevelEventDefinition definition)
    {
        bool hasPersistentEffect = false;
        for (int i = 0; definition.effects != null &&
             i < definition.effects.Count; i++)
        {
            RougeLevelEventEffect effect = definition.effects[i];
            if (effect == null) continue;
            if (IsInstantTowerDefenseLevelEventEffect(effect.type))
                ApplyInstantTowerDefenseLevelEventEffect(effect);
            else
                hasPersistentEffect = true;
        }
        if (hasPersistentEffect)
        {
            _towerDefenseActiveLevelEvents.Add(
                new ActiveTowerDefenseLevelEvent
                {
                    Definition = definition,
                    EndTime = definition.durationSeconds < 0f
                        ? -1f
                        : _survivalTime + Mathf.Max(0.1f,
                            definition.durationSeconds)
                });
        }
        ShowTowerDefenseLevelEventBanner(definition);
    }

    private static bool IsInstantTowerDefenseLevelEventEffect(
        RougeLevelEventEffectType type)
    {
        return type == RougeLevelEventEffectType.GrantGold ||
               type == RougeLevelEventEffectType.RepairMainTowerFlat ||
               type == RougeLevelEventEffectType.RepairMainTowerPercent ||
               type == RougeLevelEventEffectType.TriggerImmediateWave;
    }

    private void ApplyInstantTowerDefenseLevelEventEffect(
        RougeLevelEventEffect effect)
    {
        switch (effect.type)
        {
            case RougeLevelEventEffectType.GrantGold:
                int gold = Mathf.Max(0, Mathf.RoundToInt(effect.value));
                _towerDefenseGold = AddGoldWithoutOverflow(_towerDefenseGold, gold);
                _towerDefenseGoldEarnedTotal = AddGoldWithoutOverflow(
                    _towerDefenseGoldEarnedTotal, gold);
                break;
            case RougeLevelEventEffectType.RepairMainTowerFlat:
                mainTower?.Repair(Mathf.Max(0f, effect.value));
                break;
            case RougeLevelEventEffectType.RepairMainTowerPercent:
                if (mainTower != null)
                    mainTower.Repair(mainTower.maxHealth *
                        Mathf.Clamp01(effect.value));
                break;
            case RougeLevelEventEffectType.TriggerImmediateWave:
                TriggerAllTowerDefenseSpawnPointsOnce();
                break;
        }
        RefreshTowerDefenseUi(true);
    }

    private void RecalculateTowerDefenseLevelEventModifiers()
    {
        _towerDefenseLevelEventElitesUnlocked =
            !_towerDefenseLevelEventsControlEliteUnlock;
        _towerDefenseLevelEventEliteChanceMultiplier = 1f;
        _towerDefenseLevelEventSpawnRateMultiplier = 1f;
        _towerDefenseLevelEventKillGoldMultiplier = 1f;
        _towerDefenseLevelEventEnemyHealthMultiplier = 1f;
        _towerDefenseLevelEventEnemyMoveSpeedMultiplier = 1f;
        _towerDefenseLevelEventEliteHealthMultiplier = 1f;
        _towerDefenseLevelEventEliteMoveSpeedMultiplier = 1f;
        _towerDefenseLevelEventTowerDamageMultiplier = 1f;
        _towerDefenseLevelEventTowerAttackSpeedMultiplier = 1f;

        for (int i = 0; i < _towerDefenseActiveLevelEvents.Count; i++)
        {
            RougeLevelEventDefinition definition =
                _towerDefenseActiveLevelEvents[i].Definition;
            for (int effectIndex = 0; definition?.effects != null &&
                 effectIndex < definition.effects.Count; effectIndex++)
            {
                RougeLevelEventEffect effect = definition.effects[effectIndex];
                if (effect == null) continue;
                float multiplier = Mathf.Max(0.01f, effect.value);
                switch (effect.type)
                {
                    case RougeLevelEventEffectType.UnlockEliteSpawns:
                        _towerDefenseLevelEventElitesUnlocked = true;
                        break;
                    case RougeLevelEventEffectType.EliteChanceMultiplier:
                        _towerDefenseLevelEventEliteChanceMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.EnemySpawnRateMultiplier:
                        _towerDefenseLevelEventSpawnRateMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.KillGoldMultiplier:
                        _towerDefenseLevelEventKillGoldMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.EnemyHealthMultiplier:
                        _towerDefenseLevelEventEnemyHealthMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.EnemyMoveSpeedMultiplier:
                        _towerDefenseLevelEventEnemyMoveSpeedMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.EliteHealthMultiplier:
                        _towerDefenseLevelEventEliteHealthMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.EliteMoveSpeedMultiplier:
                        _towerDefenseLevelEventEliteMoveSpeedMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.TowerDamageMultiplier:
                        _towerDefenseLevelEventTowerDamageMultiplier *= multiplier;
                        break;
                    case RougeLevelEventEffectType.TowerAttackSpeedMultiplier:
                        _towerDefenseLevelEventTowerAttackSpeedMultiplier *= multiplier;
                        break;
                }
            }
        }

        float levelGold = _towerDefenseLevel != null
            ? _towerDefenseLevel.TowerGoldCostMultiplier
            : 1f;
        float levelDamage = _towerDefenseLevel != null
            ? _towerDefenseLevel.TowerDamageMultiplier
            : 1f;
        float levelAttackSpeed = _towerDefenseLevel != null
            ? _towerDefenseLevel.TowerAttackSpeedMultiplier
            : 1f;
        TowerDefenseVisuals.SetRuntimeLevelModifiers(levelGold,
            levelDamage * _towerDefenseLevelEventTowerDamageMultiplier,
            levelAttackSpeed *
            _towerDefenseLevelEventTowerAttackSpeedMultiplier);
        InvalidateTowerDefenseAutoplayPriorCache();
    }

    private int ApplyTowerDefenseLevelEventGoldMultiplier(int earned)
    {
        if (earned <= 0) return 0;
        float scaled = earned *
            Mathf.Max(0f, _towerDefenseLevelEventKillGoldMultiplier) +
            _towerDefenseLevelEventGoldFraction;
        int whole = Mathf.Max(0, Mathf.FloorToInt(scaled));
        _towerDefenseLevelEventGoldFraction = Mathf.Clamp01(scaled - whole);
        return whole;
    }

    private void BuildTowerDefenseLevelEventUi()
    {
        DisposeTowerDefenseLevelEventUiOnly();
        GameObject canvasObject = new GameObject("Tower Defense Event Canvas");
        _towerDefenseLevelEventCanvas = canvasObject.AddComponent<Canvas>();
        _towerDefenseLevelEventCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _towerDefenseLevelEventCanvas.sortingOrder = 70;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        RougeTowerDefenseUiLayout.ConfigureCanvasScaler(scaler);

        Image banner = CreateUiImage("Level Event Banner", canvasObject.transform,
            new Color(0.006f, 0.035f, 0.06f, 0.94f));
        RectTransform bannerRect = banner.rectTransform;
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0f, -148f);
        bannerRect.sizeDelta = new Vector2(650f, 92f);
        _towerDefenseLevelEventBannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        _towerDefenseLevelEventBannerGroup.alpha = 0f;
        _towerDefenseLevelEventBannerGroup.blocksRaycasts = false;
        _towerDefenseLevelEventBannerAccent = CreateUiImage("Accent", banner.transform,
            new Color(0.1f, 0.82f, 1f, 1f));
        RectTransform accentRect = _towerDefenseLevelEventBannerAccent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.sizeDelta = new Vector2(6f, 0f);
        _towerDefenseLevelEventBannerTitle = CreateUiText("Title", banner.transform,
            25, TextAnchor.MiddleLeft);
        _towerDefenseLevelEventBannerTitle.fontStyle = FontStyle.Bold;
        RectTransform titleRect = _towerDefenseLevelEventBannerTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(22f, -8f);
        titleRect.sizeDelta = new Vector2(-38f, 34f);
        _towerDefenseLevelEventBannerDescription = CreateUiText("Description",
            banner.transform, 17, TextAnchor.UpperLeft);
        StretchRect(_towerDefenseLevelEventBannerDescription.rectTransform,
            22f, 43f, 16f, 8f);

        Image warning = CreateUiImage("Boss Warning", canvasObject.transform,
            new Color(0.24f, 0f, 0.015f, 0.38f));
        StretchRect(warning.rectTransform, 0f, 0f, 0f, 0f);
        _towerDefenseBossWarningGroup = warning.gameObject.AddComponent<CanvasGroup>();
        _towerDefenseBossWarningGroup.alpha = 0f;
        _towerDefenseBossWarningGroup.blocksRaycasts = false;
        _towerDefenseBossWarningTitle = CreateUiText("Warning", warning.transform,
            68, TextAnchor.MiddleCenter);
        _towerDefenseBossWarningTitle.text = "WARNING";
        _towerDefenseBossWarningTitle.fontStyle = FontStyle.Bold;
        _towerDefenseBossWarningTitle.color = new Color(1f, 0.12f, 0.08f, 1f);
        RectTransform warningTitleRect = _towerDefenseBossWarningTitle.rectTransform;
        warningTitleRect.anchorMin = new Vector2(0f, 0.5f);
        warningTitleRect.anchorMax = new Vector2(1f, 0.5f);
        warningTitleRect.pivot = new Vector2(0.5f, 0.5f);
        warningTitleRect.anchoredPosition = new Vector2(0f, 26f);
        warningTitleRect.sizeDelta = new Vector2(-80f, 92f);
        _towerDefenseBossWarningDetail = CreateUiText("Warning Detail",
            warning.transform, 25, TextAnchor.MiddleCenter);
        RectTransform detailRect = _towerDefenseBossWarningDetail.rectTransform;
        detailRect.anchorMin = new Vector2(0f, 0.5f);
        detailRect.anchorMax = new Vector2(1f, 0.5f);
        detailRect.pivot = new Vector2(0.5f, 0.5f);
        detailRect.anchoredPosition = new Vector2(0f, -45f);
        detailRect.sizeDelta = new Vector2(-80f, 44f);
    }

    private void DisposeTowerDefenseLevelEventUiOnly()
    {
        if (_towerDefenseLevelEventCanvas != null)
            Destroy(_towerDefenseLevelEventCanvas.gameObject);
        _towerDefenseLevelEventCanvas = null;
    }

    private void ShowTowerDefenseLevelEventBanner(
        RougeLevelEventDefinition definition)
    {
        if (_towerDefenseLevelEventBannerGroup == null || definition == null)
            return;
        Color color = GetTowerDefenseLevelEventToneColor(definition.tone);
        if (_towerDefenseLevelEventBannerAccent != null)
            _towerDefenseLevelEventBannerAccent.color = color;
        if (_towerDefenseLevelEventBannerTitle != null)
        {
            _towerDefenseLevelEventBannerTitle.text = definition.title;
            _towerDefenseLevelEventBannerTitle.color =
                Color.Lerp(color, Color.white, 0.38f);
        }
        if (_towerDefenseLevelEventBannerDescription != null)
        {
            string duration = definition.durationSeconds < 0f
                ? string.Empty
                : $"  ·  持续 {definition.durationSeconds:0.#} 秒";
            _towerDefenseLevelEventBannerDescription.text =
                definition.description + duration;
        }
        _towerDefenseLevelEventBannerRemaining = 4.2f;
        _towerDefenseLevelEventBannerGroup.alpha = 1f;
    }

    private static Color GetTowerDefenseLevelEventToneColor(
        RougeLevelEventTone tone)
    {
        switch (tone)
        {
            case RougeLevelEventTone.Danger:
                return new Color(1f, 0.2f, 0.08f, 1f);
            case RougeLevelEventTone.Opportunity:
                return new Color(0.22f, 1f, 0.54f, 1f);
            case RougeLevelEventTone.Mixed:
                return new Color(1f, 0.68f, 0.12f, 1f);
            default:
                return new Color(0.1f, 0.82f, 1f, 1f);
        }
    }

    private void ShowTowerDefenseBossWarning(string bossName)
    {
        if (_towerDefenseBossWarningGroup == null) return;
        _towerDefenseBossWarningRemaining = 1.35f;
        _towerDefenseBossWarningGroup.alpha = 1f;
        if (_towerDefenseBossWarningDetail != null)
            _towerDefenseBossWarningDetail.text =
                $"检测到 {bossName} 高速接近 · 冲击区域即将形成";
    }

    private void UpdateTowerDefenseLevelEventUi(float unscaledDt)
    {
        if (_towerDefenseLevelEventBannerGroup != null)
        {
            _towerDefenseLevelEventBannerRemaining = Mathf.Max(0f,
                _towerDefenseLevelEventBannerRemaining - Mathf.Max(0f, unscaledDt));
            float alpha = Mathf.Clamp01(
                _towerDefenseLevelEventBannerRemaining / 0.45f);
            _towerDefenseLevelEventBannerGroup.alpha = alpha;
        }
        if (_towerDefenseBossWarningGroup != null)
        {
            _towerDefenseBossWarningRemaining = Mathf.Max(0f,
                _towerDefenseBossWarningRemaining - Mathf.Max(0f, unscaledDt));
            float fade = Mathf.Clamp01(_towerDefenseBossWarningRemaining / 0.22f);
            float pulse = 0.62f +
                Mathf.Sin(Time.unscaledTime * 16f) * 0.18f;
            _towerDefenseBossWarningGroup.alpha = fade * pulse;
            if (_towerDefenseBossWarningTitle != null)
                _towerDefenseBossWarningTitle.rectTransform.localScale =
                    Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 14f) * 0.025f);
        }
    }
}
