# 《Red vs Blue》指挥官包：给大模型的生成说明

你要为 Unity 游戏《Red vs Blue》生成一个**数据驱动的指挥官包**。这不是 C# 代码生成任务，也不是运行时大模型提示词。游戏只会读取两份严格 JSON，把其中有限的参数交给已经写好的托管决策器；Burst 仍只处理纯数值战场数据。

当前协议由两份文件组成：

- 核心逻辑：`commander.json`，`schemaVersion: 2`，`protocol: "red-vs-blue.commander/2"`。
- 本地化：`Locales/<locale>.json`，`schemaVersion: 1`，`protocol: "red-vs-blue.commander-locale/1"`。

不要再生成旧版 `red-vs-blue.commander/1` 单文件。身份文案、思考风格、策略显示名、对白和失败台词都在 locale 文件里；可参与决策的数值和对白触发规则在 core 文件里。

## 必须生成的目录

把 `<id>` 换成 `commanderId`，把 `<locale>` 换成本地化文件内的 `locale`：

```text
Assets/Rouge/Resources/Commanders/<id>/
├─ commander.json
├─ Locales/
│  └─ <locale>.json
└─ Portraits/
   ├─ base_calm.png        # 可选；唯一必要的自定义基础立绘
   └─ <variant>.png        # 可选；见立绘命名与回退规则
```

例：角色 ID 为 `mira`、默认语言为 `zh-CN`，则输出：

- `Assets/Rouge/Resources/Commanders/mira/commander.json`
- `Assets/Rouge/Resources/Commanders/mira/Locales/zh-CN.json`

`commanderId`、角色目录名以及两份 JSON 内的 `commanderId` 必须完全一致。`defaultLocale` 必须对应包内默认 locale 文件名；locale 文件的 `locale` 必须与文件名完全一致。

若你有文件写入能力，直接创建上述两份文件。若只能聊天输出，请依次给出清晰标注路径的两个 JSON 代码块，不能把它们合并成一个对象。每个代码块内部只能是 JSON。

## 应同时提供给模型的参考资料

1. 本文件：生成任务和输出纪律。
2. `CommanderConfigSpec.zh-CN.md`：字段语义、范围、收敛与安全规则。
3. `CommanderConfig.schema.json`：根节点是 core v2；`$defs.localeDocument` 是 locale v1。
4. `CommanderConfig.template.jsonc`：同时展示两份文件的注释模板；它是说明包装器，不能直接导入。
5. `CommanderAI.GameRules.zh-CN.md`：基本胜负、金币、地图格、塔楼和特殊机制。
6. `CommanderAI.TowerReference.json`：供决策理解的塔楼、分支与参数快照；不是可修改的权威平衡表。
7. `CommanderAI.Level0Reference.json`：默认关卡的地图与事件快照，帮助模型理解关卡结构。
8. 可选：内置 `lan/commander.json` 与 `lan/Locales/zh-CN.json`，作为已通过运行时校验的完整范例。

发生冲突时，以 `Assets/Rouge/RougeAutoplayCommanderConfig.cs` 的运行时校验器为最终裁决。

## JSON 输出纪律

- 正式文件必须是 UTF-8 **纯 JSON**。JSON 不支持注释。
- `CommanderConfig.template.jsonc` 可以写 `//` 注释，因为它只供阅读；不要把注释、包装层或尾随逗号复制进正式文件。
- 所有 DTO 字段都必须出现；不能依赖 C# 默认值。
- 任何对象都不能出现未知字段；重复键、错误类型、`null`、尾随内容都会被拒绝。
- 不得输出 `NaN`、`Infinity`、`-Infinity`。它们本身就不是合法 JSON；解析后形成的非有限浮点值也会被运行时硬拒绝。
- core 最大 256 KiB；单个 locale 最大 512 KiB。
- 文本禁止 `<`、`>` 和控制字符。只有三档失败对白允许用转义后的 `\n` 或 `\r\n` 换行。
- 不得输出 C#、表达式、条件语言、URL、绝对路径、任意文件访问、联网请求或动态代码字段。

## MapLoader 如何选择角色

`RougeTowerDefenseMapLoader` 的 `commanderConfigName` 默认是 `lan`。这里输入的是**角色目录名**，可写 `lan`、`mira`、`lan_level01`；末尾 `.json` 会被去掉，并统一转为小写。合法字符仅为小写字母、数字、`-`、`_`，长度 1–48。

`commanderLocaleOverride` 留空时使用 core 的 `defaultLocale`。请求的自定义角色缺失或被拒绝时，运行时尝试完整加载内置 `lan` 包；仍失败才使用代码内安全回退。该过程只写 Unity Console，不需要给玩家弹窗。

## 立绘规则

core 中只有一个字段：`visuals.portraitResourceFolder`。

- 自定义立绘时，它必须精确等于 `Commanders/<commanderId>/Portraits`。
- 留空字符串 `""` 表示直接使用内置岚的默认立绘。
- 基础立绘固定为 `base_calm.png`。可选情绪图为 `base_focused.png`、`base_tense.png`、`base_critical.png`。
- 可选点击图为 `click_<emotion>.png`和 `rapid_click_<emotion>.png`，其中 `<emotion>` 是 `calm | focused | tense | critical`；失败图为 `defeat.png`。
- 变体缺失时会回退到该角色的 `base_calm`；该图也缺失时回退到岚的 `base_calm`。不需要在 JSON 中声明变体文件名。
- 所有变体必须与 `base_calm` 保持相同像素尺寸、人物位置、裁切和透明轮廓；只做小幅表情差分，否则点击切图会跳动或出现模糊边。
- 失败状态使用 `defeat.png`，此时立绘不接受点击。

## 公平性与数字收敛

所有数字都应主动写在规范推荐范围内。运行时对**有限但越界**的可调数字进行收敛，并把警告写入加载报告；它不会因为一个有限数值稍微越界就丢掉整个角色。非有限值仍然硬拒绝。

三项是引擎权威值：即使输入其他有限数字，也会被强制改回：

- `talent.costMultiplier = 1`
- `strategy.capitalActionIntervalSeconds = 0.65`
- `strategy.emergencyActionIntervalSeconds = 0.24`

不要故意依赖收敛来设计角色。范围、跨字段顺序和优先级排列的具体收敛方式见完整规范。

好感和情绪都只影响表现：好感选择三档文本；情绪选择情绪对白、点击对白以及普通对白间隔。它们不改变建造、升级、伤害、资源、APM 或策略评分。

## 固定塔型 ID

`strategy.buildOrder` 只能包含下面 8 个标准塔 ID 的非空、无重复子集，大小写可被枚举解析，但建议严格按此拼写：

`MachineGun`, `Ice`, `Cannon`, `Flame`, `Laser`, `RocketBarrage`, `OrbitSphere`, `PiercingLaser`

不要写中文塔名、数字 ID、`ChargeTower`、`ReinforcementTower` 或自创塔型。此字段只是候选遍历顺序，不等于完整的多步战术脚本；当前协议也没有可自定义的升级分支、冰霜格、燃烧与冰冻联动、充能塔布局等 playbook 字段。

## 42 个规则与 42 个文本集

core 的 `dialogue.sets` 放触发元数据，每项只有：

- `category`
- `priority`（加载后为 1–20）
- `battleState`

locale 的 `dialogue.sets` 放文本，每项只有：

- `category`
- `distant`
- `familiar`
- `close`

当前实现没有名为 `textSetId` 的字段；两边相同的 `category` 就是规则与文本集的一对一关联键，也就是当前协议实际承担 textSetId 作用的字段。两份数组都必须各包含以下 42 类，每类恰好一次：

```text
TakeoverFirst, TakeoverQuickReturn, TakeoverFrequentToggle, TakeoverReturn,
TakeoverHighPressure, TakeoverLateTier1, TakeoverLateTier2, TakeoverLateTier3,
TakeoverLateTier4, ReleaseFirst, Calm, Crowd, Hard, BossArrival, Boss,
BossHealthHalf, BossHealthQuarter, Urgent, BaseLow, BaseCritical,
BaseFirstDamage, BaseDamaged, BaseBurstDamage, BuildTower, UpgradeTower,
PressureRelieved, EmotionToCalm, EmotionToFocused, EmotionToTense,
EmotionToCritical, PortraitClickCalm, PortraitClickFocused, PortraitClickTense,
PortraitClickCritical, PortraitRapidClickCalm, PortraitRapidClickFocused,
PortraitRapidClickTense, PortraitRapidClickCritical, Saving, GreatTile, Branch,
Discount
```

每个 locale 文本集的三档数组都要有 1–64 条、每条最多 180 字符，并且三组不能是同一批文本的重排：

- `distant`：生疏，正式或保持距离。
- `familiar`：熟悉，自然协作。
- `close`：亲近，更信任、更柔和，可以适度调侃。

失败文本位于 locale 的 `outcomes.defeat`，也必须有上述三档非空差分。失败演出中角色不可点击；这不是 JSON 字段，而是引擎行为。

普通点击和短时连点需要同时体现**当前情绪 × 好感档**。高压时生疏角色可以不耐烦，但不要辱骂玩家。点击对白是最高优先级，仅保留很短的防刷冷却。

## 六维雷达不是输入字段

角色选择页显示六个 0–50 数值，但 JSON 中**绝对不要添加 radar 字段**。它是从 `concerns` 和 `biases` 计算出来的可视化投影，不是综合战斗力：

- 存钱 ↔ 铺塔，二者之和 50。
- 控场减益 ↔ 直接伤害，二者之和 50。
- 清群 ↔ 攻坚（精英/首领），二者之和 50。

中性为每项 25。六项不可能同时拉满。具体公式见完整规范。

## 生成前自检

1. 输出的是两个文件，不是 v1 单文件，也不是 JSONC 包装对象。
2. core 的版本/协议为 `2`/`red-vs-blue.commander/2`；locale 为 `1`/`red-vs-blue.commander-locale/1`。
3. 目录名、两份 `commanderId`、`defaultLocale` 和 locale 文件名完全对应。
4. 所有对象字段完整、类型正确、无未知字段、无重复键、无注释。
5. 所有数字有限；主动满足规范范围和跨字段关系；三项引擎权威值写标准值。
6. buildOrder 至少一项，塔 ID 合法且不重复。
7. core 与 locale 各有完整 42 类，按 `category` 一对一对应。
8. 每类三档对白和三档失败对白都非空且真正不同。
9. 文本长度与字符安全合格；只有失败对白使用转义换行。
10. 不添加雷达、脚本、塔参数、关卡参数或协议未声明的特殊玩法字段。

通过以上检查后再交付两份 JSON。
