using System;
using System.Collections.Generic;
using UnityEngine;

public enum RougeLevelEventEffectType
{
    [InspectorName("解锁精英生成")] UnlockEliteSpawns = 0,
    [InspectorName("精英概率倍率")] EliteChanceMultiplier = 1,
    [InspectorName("敌人生成速率倍率")] EnemySpawnRateMultiplier = 2,
    [InspectorName("击杀金币倍率")] KillGoldMultiplier = 3,
    [InspectorName("敌人生命倍率")] EnemyHealthMultiplier = 4,
    [InspectorName("敌人移动速度倍率")] EnemyMoveSpeedMultiplier = 5,
    [InspectorName("精英生命倍率")] EliteHealthMultiplier = 6,
    [InspectorName("精英移动速度倍率")] EliteMoveSpeedMultiplier = 7,
    [InspectorName("塔楼伤害倍率")] TowerDamageMultiplier = 8,
    [InspectorName("塔楼攻速倍率")] TowerAttackSpeedMultiplier = 9,
    [InspectorName("立即获得金币")] GrantGold = 10,
    [InspectorName("修复主塔固定生命")] RepairMainTowerFlat = 11,
    [InspectorName("修复主塔最大生命百分比")] RepairMainTowerPercent = 12,
    [InspectorName("立即触发一轮刷怪")] TriggerImmediateWave = 13
}

public enum RougeLevelEventTone
{
    [InspectorName("情报")] Information = 0,
    [InspectorName("危险")] Danger = 1,
    [InspectorName("增益")] Opportunity = 2,
    [InspectorName("混合")] Mixed = 3
}

[Serializable]
public sealed class RougeLevelEventEffect
{
    public RougeLevelEventEffectType type;
    [Tooltip("倍率类填写倍率，例如 1.5；百分比修复填写 0.2；立即金币填写数量。")]
    public float value = 1f;
}

[Serializable]
public sealed class RougeLevelEventDefinition
{
    [Tooltip("关卡内唯一 ID，时间线通过它引用事件。")]
    public string eventId = "event";
    public string title = "战场事件";
    [TextArea(1, 3)] public string description = "战场状态发生变化。";
    public RougeLevelEventTone tone = RougeLevelEventTone.Information;
    [Tooltip("-1：倍率效果永久、立即效果只执行一次；其他正数：持续秒数。")]
    public float durationSeconds = 30f;
    public List<RougeLevelEventEffect> effects = new List<RougeLevelEventEffect>();
}

[Serializable]
public sealed class RougeLevelEventTrigger
{
    [Min(0f)] public float triggerMinute;
    [Tooltip("到点后从这些事件 ID 中随机选择一个。只填一个时固定触发。")]
    public List<string> candidateEventIds = new List<string>();
}
