# 《Red vs Blue》指挥官包协议：core v2 / locale v1

本文与当前 `Assets/Rouge/RougeAutoplayCommanderConfig.cs` 对齐。指挥官不是一份单文件角色卡，而是一个由**核心逻辑 JSON**和一个或多个**本地化 JSON**组成的 Resources 包。

权威顺序：运行时校验器 > `CommanderConfig.schema.json` > 本文。Schema 根节点校验 core；`$defs.localeDocument` 描述 locale。

## 1. 文件结构与选择规则

```text
Assets/Rouge/Resources/Commanders/<commanderId>/
├─ commander.json
├─ Locales/
│  ├─ zh-CN.json
│  └─ <other-locale>.json
└─ Portraits/
   ├─ base_calm.png
   └─ <optional-variants>.png
```

对应 Resources 路径：

- core：`Commanders/<id>/commander`
- locale：`Commanders/<id>/Locales/<locale>`
- 基础立绘：`Commanders/<id>/Portraits/base_calm`

`RougeTowerDefenseMapLoader.commanderConfigName` 默认 `lan`，接受 `lan` 或 `lan.json`。输入会去首尾空白、移除末尾 `.json`、转为小写，再检查 `^[a-z0-9_-]{1,48}$`。无效名称退回 `lan`。`commanderLocaleOverride` 留空时使用 core 的 `defaultLocale`。

角色目录名、core 的 `commanderId`、locale 的 `commanderId` 必须完全一致；locale 文件名必须与其 `locale` 完全一致。指定的非默认 locale 缺失时会尝试包内 `defaultLocale`。自定义角色包整体无效时会尝试完整的 `lan` 包；最后才使用代码内安全回退。

包名可以按关卡拆分，例如 `lan_level01`。如果对应目录不存在，仍走上述安静回退，并把原因写入 Console。

## 2. 两份协议和大小上限

### core：`commander.json`

- `schemaVersion`：整数 `2`
- `protocol`：`red-vs-blue.commander/2`
- UTF-8 最大 `256 KiB`

### locale：`Locales/<locale>.json`

- `schemaVersion`：整数 `1`
- `protocol`：`red-vs-blue.commander-locale/1`
- UTF-8 最大 `512 KiB`

旧版 `schemaVersion: 1` / `red-vs-blue.commander/1` 单文件不再适用。

## 3. 严格 JSON 形状

运行时先做反射驱动的严格形状预检，再调用 Unity `JsonUtility`：

- 每个公开 DTO 字段都必须出现，不能省略。
- 未声明字段、重复键、`null`、错误类型和尾随内容会被拒绝。
- 对象必须是对象，数组必须是数组，布尔必须是 `true/false`，数字必须使用 JSON 数字语法。
- JSON 不支持注释、尾随逗号、`NaN` 或 `Infinity`。
- 标准 JSONC 模板可以含 `//`，但它不是运行时文件。

字符串禁止 `<` 和 `>`。所有创作文本禁止控制字符；只有 `locale.outcomes.defeat` 三档文本允许 CR/LF，正式 JSON 中仍要写成 `\n` 或 `\r\n` 转义。

## 4. 数字：有限越界收敛与硬拒绝

运行时先对 core 中可调数值做规范化，再做范围校验：

- **有限但越界**：收敛到合法边界，并在报告中记录 warning。
- **非有限浮点值**：不会被修复，后续校验硬拒绝。`NaN`/`Infinity` 本身也不是合法 JSON。
- 整数越界：收敛到合法范围。
- 结构错误、非法 ID、缺失类别、文本错误、重复塔 ID等不会自动修复。

作者仍应直接输出合法范围内的数值，不要把收敛当作角色机制。Schema 对这些字段使用 `x-runtime-normalization` 描述运行时行为；推荐范围以本文为准。

三项为引擎权威值。只要输入是有限数，加载时都会强制改为：

| 路径 | 权威值 |
| --- | ---: |
| `talent.costMultiplier` | `1` |
| `strategy.capitalActionIntervalSeconds` | `0.65` |
| `strategy.emergencyActionIntervalSeconds` | `0.24` |

它们保证价格权限和资本动作 APM 公平。角色不能通过配置获得额外金币、折扣、塔属性或更短动作间隔。

## 5. core v2 顶层

所有字段必需，且不得增加其他字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `schemaVersion` | integer | 固定 `2` |
| `protocol` | string | 固定 `red-vs-blue.commander/2` |
| `commanderId` | string | `^[a-z0-9_-]{1,48}$` |
| `defaultLocale` | string | 2–24 字符；字母、数字、`-`；不能以 `-` 开头/结尾 |
| `visuals` | object | 立绘目录选择 |
| `talent` | object | 引擎权威公平参数 |
| `personality` | object | 有界关注和行为倾向 |
| `strategy` | object | 决策数值 |
| `dialogue` | object | 好感、触发、情绪和 42 条规则 |

### 5.1 visuals

`portraitResourceFolder` 必须是字符串：

- `""`：直接使用内置 `Commanders/lan/Portraits/base_calm`。
- 非空：必须精确等于 `Commanders/<commanderId>/Portraits`。

基础文件固定为 `base_calm.png`。运行时也会按约定尝试下列可选文件，它们不是 JSON 字段：

- 情绪：`base_focused.png`、`base_tense.png`、`base_critical.png`
- 单击：`click_calm.png`、`click_focused.png`、`click_tense.png`、`click_critical.png`
- 连点：`rapid_click_calm.png`、`rapid_click_focused.png`、`rapid_click_tense.png`、`rapid_click_critical.png`
- 失败：`defeat.png`

某变体缺失时回退到该角色的 `base_calm`；该图也缺失时回退到岚的 `base_calm`。所有变体应保持同尺寸、同裁切、同人物位置和同透明轮廓，只改变小幅表情。失败后立绘不可点击。

### 5.2 talent

只有 `costMultiplier`；它必须是数字并由引擎强制为 `1`。天赋名称和说明属于 locale。

### 5.3 personality.concerns

四项均收敛到 `[0.75, 1.35]`，`1` 为中性：

| 字段 | 含义 |
| --- | --- |
| `crowd` | 普通怪群压力关注 |
| `elite` | 精英、重甲目标关注 |
| `boss` | 首领压力与备战关注 |
| `urgent` | 接近主塔的紧急压力关注 |

### 5.4 personality.biases

九项均收敛到 `[0.75, 1.25]`，`1` 为中性：

| 字段 | 含义 |
| --- | --- |
| `save` | 攒钱、等待更优投资 |
| `build` | 建造相对升级的倾向 |
| `controlTower` | 控场塔倾向 |
| `focusedTower` | 单体/攻坚塔倾向 |
| `areaTower` | 范围/清群塔倾向 |
| `defense` | 主塔近端防守倾向 |
| `specialTile` | 特殊格利用倾向 |
| `upgrade` | 升级已有塔倾向 |
| `redeploy` | 出售并重新部署倾向 |

这些是现有决策器的偏好权重，不是新的动作语言。它们不能单独表达“先冰冻再点燃”“固定走冰塔破甲分支”或“围绕充能塔构筑”等多步玩法。

## 6. strategy

### 6.1 核心字段

| 字段 | 收敛/约束 | 岚值 | 含义 |
| --- | --- | ---: | --- |
| `buildOrder` | 至少 1 个合法、无重复塔 ID；不自动修复 | 8 塔完整序列 | 候选遍历顺序，不是强制出塔脚本 |
| `openingTowerCount` | integer `[1,8]` | 3 | 开局结构基线塔数 |
| `expansionIntervalSeconds` | `[5,180]` | 38 | 期望塔数随时间扩张间隔 |
| `capitalActionIntervalSeconds` | 强制 `0.65` | 0.65 | 普通资本动作间隔 |
| `emergencyActionIntervalSeconds` | 强制 `0.24` | 0.24 | 紧急资本动作间隔 |
| `strategyHoldSeconds` | `[0,30]` | 2.5 | 低优先级模式保持时间 |
| `waveForecastSeconds` | `[1,120]` | 18 | 波次预测窗口 |
| `bossPreparationLeadSeconds` | `[5,600]` | 150 | 首领备战提前量 |
| `saleCooldownSeconds` | `[0,300]` | 45 | 两次出售间隔 |
| `minimumTowerAgeBeforeSaleSeconds` | `[0,300]` | 35 | 塔进入出售候选前年龄 |
| `personalityRegretBudget` | `[0,0.15]` | 0.08 | 人格偏好允许的客观质量损失 |
| `bossRegretBudget` | `[0, personalityRegretBudget]` | 0.035 | 首领阶段更严格的遗憾预算 |
| `maximumPreferenceShift` | `[0,0.08]` | 0.04 | 人格对评分的最大偏移 |

`bossRegretBudget` 在 `personalityRegretBudget` 规范化后，再收敛到其上限。

允许的标准塔 ID：

`MachineGun`, `Ice`, `Cannon`, `Flame`, `Laser`, `RocketBarrage`, `OrbitSphere`, `PiercingLaser`

运行时枚举解析不区分大小写，但必须能精确对应标准塔枚举且不可重复；为避免工具差异，生成文件应使用上面的标准拼写。`ChargeTower` 和 `ReinforcementTower` 不属于 buildOrder。

### 6.2 skills

四项都收敛到 `[0,1]`：

| 字段 | 含义 |
| --- | --- |
| `mapReading` | 地图、路线和特殊格信息利用程度 |
| `threatReading` | 怪群、精英、首领和紧急压力识别程度 |
| `crisisResponse` | 主塔与近端危机反应程度 |
| `adaptation` | 根据战况调整阵容的程度 |

### 6.3 thresholds

| 字段 | 收敛范围 | 岚值 |
| --- | --- | ---: |
| `emergencyMainTowerHealthRatio` | `[0,1]` | 0.45 |
| `emergencyUrgentPressureMinimum` | `[0,100]` | 3 |
| `emergencyUrgentPressureFraction` | `[0,1]` | 0.2 |
| `emergencyImminentPressure` | `[0,200]` | 16 |
| `prepareBossProgress` | `[0,1]` | 0.32 |
| `economyMaximumActiveEnemies` | integer `[0,64]` | 4 |
| `economyMaximumIncomingPressure` | `[0,200]` | 5 |
| `economyMinimumNextWaveSeconds` | `[0,120]` | 6 |
| `economyMinimumMainTowerHealthRatio` | `[0,1]` | 0.78 |
| `highUrgentPressure` | `[0,100]` | 5 |
| `highPeakPressure` | `[0,200]` | 14 |
| `mediumUrgentPressure` | `[0, highUrgentPressure]` | 2 |
| `mediumActiveEnemies` | integer `[1,128]` | 16 |
| `mediumIncomingPressure` | `[0,200]` | 9 |
| `highBossPreparation` | `[0,1]` | 0.72 |
| `mediumBossPreparation` | `[0, highBossPreparation]` | 0.28 |
| `criticalCrisisHealthRatio` | `[0,1]` | 0.35 |
| `lowCrisisHealthRatio` | `[criticalCrisisHealthRatio,1]` | 0.7 |
| `redeployMinimumExtraTowers` | integer `[0,8]` | 2 |
| `redeployMinimumHealthRatio` | `[0,1]` | 0.72 |
| `redeployMaximumUrgentPressure` | `[0,100]` | 1.5 |
| `redeployMaximumActiveEnemies` | integer `[0,128]` | 18 |
| `redeployMaximumBossPreparation` | `[0,1]` | 0.18 |
| `immediateDefenseHealthRatio` | `[0,1]` | 0.9 |
| `immediateDefenseUrgentPressure` | `[0,100]` | 1 |
| `immediateDefenseActiveEnemies` | integer `[0,128]` | 10 |
| `valuableSpecialTileScore` | `[0,500]` | 105 |
| `coverageUrgentPressure` | `[0,100]` | 2 |
| `coverageActiveEnemies` | integer `[0,128]` | 12 |
| `coverageHealthRatio` | `[0,1]` | 0.7 |

有依赖上限的字段按表中前项规范化后的值再收敛。

### 6.4 modePriorities

必须有 `opening`、`economy`、`hold`、`prepareBoss`、`bossFight`、`emergency` 六项。运行时把任意整数组合规范化为 `1..6` 的唯一排列：先保留字段顺序中已经合法且未重复的值；对越界或重复项，按字段顺序分配最接近期望值的空闲优先级（同距离先较小值）。最终数字越大，模式越能抢占当前模式。

## 7. core dialogue

### 7.1 好感与普通对白节奏

| 字段 | 收敛规则 | 岚值 |
| --- | --- | ---: |
| `startingAffinity` | integer `[0,100]` | 15 |
| `familiarThreshold` | integer `[1,99]` | 30 |
| `closeThreshold` | integer `[familiarThreshold+1,100]` | 70 |
| `intervalMinimumSeconds` | `[2,120]` | 14 |
| `intervalMaximumSeconds` | `[intervalMinimumSeconds,180]` | 22 |
| `preemptionCooldownSeconds` | `[0,60]` | 7 |
| `recentHistorySize` | integer `[0,32]` | 4 |

当前档位语义：低于 familiar 为 `Distant`，达到 familiar 但低于 close 为 `Familiar`，达到 close 为 `Close`。显示名称属于 locale。

### 7.2 dialogue.thresholds

| 字段 | 收敛规则 | 岚值 |
| --- | --- | ---: |
| `baseCriticalHealthRatio` | `[0,1]` | 0.25 |
| `baseLowHealthRatio` | `[baseCriticalHealthRatio,1]` | 0.5 |
| `urgentPressureMinimum` | `[0,100]` | 2 |
| `urgentPressureFraction` | `[0,1]` | 0.18 |
| `hardConcernMinimum` | `[0,100]` | 2 |
| `hardVersusCrowdFactor` | `[0,2]` | 0.42 |
| `crowdEnemyCount` | integer `[0,128]` | 8 |
| `crowdConcernMinimum` | `[0,200]` | 6 |
| `flowObservationWindowSeconds` | `[2,30]` | 8 |
| `lowKillSpawnRatio` | `[0.1,1.2]` | 0.8 |
| `nearBaseDistanceCells` | `[0.5,7.5]` | 3；此距离内为满危机，随后到 8 格线性衰减为 0 |
| `nearBaseSustainSeconds` | `[0.5,10]` | 1.5 |
| `economyObservationWindowSeconds` | `[10,120]` | 30 |
| `lowIncomeSpendRatio` | `[0.1,1.2]` | 0.75 |

后六项是隐藏的情绪传感器：用击杀/生成速率、近主塔滞留和长期收支判断防线是否失控，不直接显示给玩家。金币信号只造成轻微影响。

### 7.3 dialogue.triggers

| 字段 | 收敛规则 | 岚值 |
| --- | --- | ---: |
| `lateFirstTakeoverMinutes` | 恰好 4 个有限数字；最终 `[1,1440]` 严格递增，步长至少 0.01 | `[3,6,9,12]` |
| `mainTowerBurstWindowSeconds` | `[0.5,10]` | 4 |
| `mainTowerBurstHealthLossPercent` | `[1,50]` | 10 |
| `mainTowerHitDialogueChance` | `[0,1]` | 0.3 |
| `mainTowerHitDialogueCooldownSeconds` | `[0,120]` | 8 |
| `mainTowerBurstDialogueCooldownSeconds` | `[0,120]` | 12 |
| `towerBuildDialogueChance` | `[0,1]` | 0.3 |
| `towerBuildDialogueCooldownSeconds` | `[5,180]` | 28 |
| `towerUpgradeDialogueChance` | `[0,1]` | 0.28 |
| `towerUpgradeDialogueCooldownSeconds` | `[5,180]` | 32 |
| `pressureReliefMinimumHighSeconds` | `[1,60]` | 6 |
| `pressureReliefConfirmLowSeconds` | `[0.5,10]` | 2 |
| `pressureReliefDialogueCooldownSeconds` | `[5,180]` | 30 |
| `bossHealthWarningRatio` | `[0.05,0.95]` | 0.5 |
| `bossHealthCriticalRatio` | `[0.01, warning-0.0001]` | 0.25 |
| `portraitClickDialogueCooldownSeconds` | `[0.1,30]` | 0.35 |
| `portraitRapidClickCount` | integer `[3,12]` | 5 |
| `portraitRapidClickWindowSeconds` | `[0.5,5]` | 2 |
| `portraitRapidClickDialogueCooldownSeconds` | `[0.5,60]` | 1.5 |

`lateFirstTakeoverMinutes` 必须是长度 4 的数组；长度错误是结构/语义错误，不会补齐。全部有限时，运行时按数组顺序收敛为严格递增；含非有限值最终会被拒绝。

事件语义：

- 第一次接管：高压接管优先于晚接管最高档，晚接管优先于普通首次接管。
- 主塔：本局首次实际损血触发 `BaseFirstDamage`；后续普通损血按概率和冷却触发 `BaseDamaged`；滑动窗口累计损失达到百分比触发 `BaseBurstDamage`。
- 建塔和升级：只在动作成功后分别抽取概率，并各用独立冷却。
- 压力解除：先连续高压达到 minimum，再让隐藏张力连续回落达到 confirm，最后检查独立冷却。
- 首领血线：每个首领首次越过 warning/critical 各一次；同帧跨两线只播更严重的 quarter。
- 点击：普通/连点使用 `Time.unscaledTime`；手动点击以最高优先级抢占显示，仅保留短防刷。失败状态不能点击。

### 7.4 dialogue.emotions

| 字段 | 收敛规则 | 岚值 |
| --- | --- | ---: |
| `focusedTensionThreshold` | `[0.05,0.75]` | 0.3 |
| `tenseTensionThreshold` | `[focused+0.0001,0.9]` | 0.58 |
| `criticalTensionThreshold` | `[tense+0.0001,0.98]` | 0.82 |
| `transitionConfirmSeconds` | `[0.5,10]` | 2 |
| `transitionDialogueCooldownSeconds` | `[3,60]` | 8 |
| `calmIntervalMultiplier` | `[0.8,1.2]` | 1.12 |
| `focusedIntervalMultiplier` | `[0.8,calm]` | 1.03 |
| `tenseIntervalMultiplier` | `[0.8,focused]` | 0.94 |
| `criticalIntervalMultiplier` | `[0.8,tense]` | 0.86 |

三个张力阈值最终严格递增，四个间隔倍率最终非递增。降压采用迟滞并逐级回落；情绪只改变情绪转换对白、点击类别和普通对白间隔，不直接改写战术动作。

### 7.5 dialogue.sets：42 条规则

core 数组每项必须严格只有：

| 字段 | 约束 |
| --- | --- |
| `category` | 下表固定类别之一 |
| `priority` | integer，运行时收敛到 `[1,20]` |
| `battleState` | boolean |

每类恰好一次，不能缺失、未知或重复。`priority` 是对白抢占优先级；`battleState` 表示可被新战况替换的持续状态对白，不赋予战术能力。

## 8. locale v1 顶层

所有字段必需且不得添加其他字段：

| 字段 | 说明 |
| --- | --- |
| `schemaVersion` | 固定 integer `1` |
| `protocol` | 固定 `red-vs-blue.commander-locale/1` |
| `commanderId` | 必须与 core 一致 |
| `locale` | 2–24 字符安全 locale ID |
| `identity` | 名称、身份、背景与语气 |
| `talent` | 天赋显示名称和说明 |
| `personality` | 思考风格和决策原则文本 |
| `strategy` | 六个模式显示名 |
| `dialogue` | 三档显示名和 42 个本地化文本集 |
| `outcomes` | 三档失败对白 |

### 8.1 identity / talent / personality

| 路径 | 约束 |
| --- | --- |
| `identity.displayName` | 1–48 字符 |
| `identity.role` | 1–96 字符 |
| `identity.personaLabel` | 1–120 字符 |
| `identity.background` | 1–1200 字符 |
| `identity.speakingStyle` | 1–500 字符 |
| `identity.personalityTraits` | 1–12 项，每项 1–40 字符 |
| `talent.name` | 1–64 字符 |
| `talent.description` | 1–300 字符 |
| `personality.thinkingStyle` | 1–800 字符 |
| `personality.decisionPrinciples` | 1–16 项，每项 1–180 字符 |

所有字符串必须非空白并通过字符安全检查。描述字段供 UI、作者和未来工具使用，不会被当作代码执行。

### 8.2 strategy.modeLabels

必须有 `opening`、`economy`、`hold`、`prepareBoss`、`bossFight`、`emergency` 六个非空字符串，每项最多 32 字符。它们只改变 HUD 文案。

### 8.3 dialogue 和 category 关联

`distantLabel`、`familiarLabel`、`closeLabel` 各为 1–24 字符。

locale 的 `dialogue.sets` 每项严格只有：

- `category`
- `distant`
- `familiar`
- `close`

当前协议没有单独的 `textSetId` 字段；`category` 就是 core 规则和 locale 文本集的一对一关联键。两边都必须各有完整 42 类。

每档必须有 1–64 条，每条最多 180 字符，不允许换行。三档不能是相同集合，即使只改顺序也会被视为相同并拒绝。重复台词只产生 warning，不会单独拒绝。

| category | 场景 | 岚的建议 battleState |
| --- | --- | --- |
| `TakeoverFirst` | 普通首次接管 | false |
| `TakeoverQuickReturn` | 短时间再次交回 | false |
| `TakeoverFrequentToggle` | 频繁切换托管 | false |
| `TakeoverReturn` | 普通重新接管 | false |
| `TakeoverHighPressure` | 高压状态接管 | false |
| `TakeoverLateTier1` | 首次接管达到第 1 分钟阈值 | false |
| `TakeoverLateTier2` | 首次接管达到第 2 分钟阈值 | false |
| `TakeoverLateTier3` | 首次接管达到第 3 分钟阈值 | false |
| `TakeoverLateTier4` | 首次接管达到第 4 分钟阈值 | false |
| `ReleaseFirst` | 首次退出接管 | false |
| `Calm` | 平静战况 | true |
| `Crowd` | 怪群压力 | true |
| `Hard` | 精英/重甲压力 | true |
| `BossArrival` | 首领刚出现 | true |
| `Boss` | 首领战持续状态 | true |
| `BossHealthHalf` | 首领越过 warning 血线 | false |
| `BossHealthQuarter` | 首领越过 critical 血线 | false |
| `Urgent` | 近端紧急压力 | true |
| `BaseLow` | 主塔低血量 | true |
| `BaseCritical` | 主塔濒危 | true |
| `BaseFirstDamage` | 主塔首次损血 | false |
| `BaseDamaged` | 普通概率受伤反馈 | false |
| `BaseBurstDamage` | 时间窗累计重创 | false |
| `BuildTower` | 成功建塔反馈 | false |
| `UpgradeTower` | 成功升级反馈 | false |
| `PressureRelieved` | 持续高压后确认低压 | false |
| `EmotionToCalm` | 情绪转 Calm | false |
| `EmotionToFocused` | 情绪转 Focused | false |
| `EmotionToTense` | 情绪转 Tense | false |
| `EmotionToCritical` | 情绪转 Critical | false |
| `PortraitClickCalm` | Calm 普通点击 | false |
| `PortraitClickFocused` | Focused 普通点击 | false |
| `PortraitClickTense` | Tense 普通点击 | false |
| `PortraitClickCritical` | Critical 普通点击 | false |
| `PortraitRapidClickCalm` | Calm 短时连点 | false |
| `PortraitRapidClickFocused` | Focused 短时连点 | false |
| `PortraitRapidClickTense` | Tense 短时连点 | false |
| `PortraitRapidClickCritical` | Critical 短时连点 | false |
| `Saving` | 为目标攒钱 | false |
| `GreatTile` | 使用高价值特殊格 | false |
| `Branch` | 选择升级分支 | false |
| `Discount` | 成本节省表现 | false |

`battleState` 在协议上可配置；表中是内置岚的取值，不是运行时隐藏常量。

### 8.4 outcomes.defeat

必须严格包含 `distant`、`familiar`、`close` 三个数组。每档 1–64 条，每条最多 180 字符，三组必须真正不同。只有这里允许 `\n`/`\r\n`。运行时按当前好感档取得整组候选文本；失败表现层选定并固定本次台词，且失败后不再允许点击立绘。

## 9. 六维雷达投影

雷达不是 JSON 字段。角色选择页从已规范化的 `concerns` / `biases` 计算六个整数，顺时针顺序为：

`存钱, 控场, 攻坚, 铺塔, 纯伤, 清群`

三对严格相加为 50：索引 `(0,3)`、`(1,4)`、`(2,5)`。中性值均为 25。

先把偏好转换为有符号信号：

```text
SignedBias(x) = clamp((x - 1) / 0.25, -1, 1)
SignedConcern(x) = clamp((x - 1) / (x >= 1 ? 0.35 : 0.25), -1, 1)
```

再计算：

```text
saving       = SignedBias(save)
expansion    = average(SignedBias(build), SignedBias(upgrade))
utility      = SignedBias(controlTower)
directDamage = average(SignedBias(focusedTower), SignedBias(areaTower))
hunting      = average(SignedBias(focusedTower), SignedConcern(elite), SignedConcern(boss))
crowd        = average(SignedBias(areaTower), SignedConcern(crowd))
```

每一对通过：

```text
first  = round(25 + clamp(firstSignal - secondSignal, -1, 1) * 25)，再 clamp 到 0..50
second = 50 - first
```

因此雷达只是便于人理解的三组互斥投影，不是胜率、逻辑能力或总强度评分，也不能替代详细配置。

## 10. Burst 与安全边界

当前链路是：

`自然语言设定 → 大模型生成 core + locale JSON → 严格形状检查 → 数值规范化与语义校验 → managed 指挥官定义 → 现有决策器 → 必要的纯数值 Burst Job`

JSON、字符串、字典、立绘和角色背景都留在 managed C#。Burst 不解析 JSON，不执行自然语言，不持有 Sprite 或任意脚本。

配置可以改变现有偏好、阈值和表现，但不能：

- 增加动作、塔、分支、技能、表达式或任意规则节点；
- 修改塔/关卡权威数据；
- 联网、访问任意文件、启动进程、反射或动态编译；
- 改变基础价格、资源、伤害或动作频率权威值。

## 11. 校验顺序

1. 检查目录、文件名以及 core/locale ID 对应。
2. 检查 core 256 KiB、locale 512 KiB 上限。
3. 严格解析两份 JSON：无注释、重复键、未知字段、缺失字段或错误类型。
4. 检查协议版本、ID、默认语言和 locale 文件名。
5. 确保所有数字有限；记录有限越界收敛 warning；三项权威字段强制标准值。
6. 检查 buildOrder 合法、非空、无重复。
7. 检查 core 与 locale 各自 42 个 category 完整、唯一，并按 category 对应。
8. 检查文本长度、安全字符、三档差分和失败文本。
9. 解析 `base_calm`；自定义缺失则回退内置岚。
10. 进入 Play Mode 检查加载页、角色选择、雷达、确认进入关卡、接管/放权、受伤、建造/升级反馈、情绪、点击和失败流程。

`CommanderConfig.schema.json` 负责静态结构和可直接表达的约束；运行时仍负责文件大小、跨文档对应、数值收敛、42 类完整唯一以及三档集合差异。
