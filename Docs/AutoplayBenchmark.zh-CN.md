# Autoplay 资本决策与回归基准

本轮保留 Commander 配置和 JSON。最终资本入口为 `SelectAutoplayCapitalAction`，Build、Upgrade、Support、Charge、Hold 使用同一客观资本评分。免费升级也进入比较。

## 决策约束

1. 根据 Present 压力、即时主塔突破、近期主塔受伤、主塔血量、在场 Boss 的到达时间和有真实近端敌人的 lane gap 更新 Safety Shield。预测压力不会单独触发 Emergency。
2. 在可执行且通过 Safety 的动作中得到 objective best。不可负担的动作只有在允许短期等待时才能转为 Hold；Hold 的客观分数计入等待成本，SaveBias 不参与客观评分。
3. 使用现有 personalityRegretBudget / bossRegretBudget 构造 `ObjectiveScore >= best * (1 - epsilon)` 集合；Emergency 和 Objective Baseline 的 epsilon 为 0。
4. Commander concerns / biases 只影响集合内的最终排序。风格提议如被 Safety 拒绝，直接回到 objective best。Emergency 不允许 Hold、Sell、Redeploy，以及无法覆盖当前威胁的购买。Charge 的随机效果只在执行时揭示，第一版不能证明其必然带来即时战斗改善，因此也被拒绝；没有安全可执行动作时返回 None。

旧 `TrySelectAutoplayRolloutCapitalAction` 代码保留，标记为禁止调用，不再进入默认路径。新的可选 `SelectAutoplayGatedRolloutTieBreaker` 默认关闭；只读取已经过 Safety 和 epsilon 的前 2–4 项，采用 Commander 的 waveForecastSeconds、深度 1（上限要求为 2）、4 个节点和 2 ms 预算。预算耗尽返回传入的 baseline。现有 forward model 不能公平处理 Charge/Hold，因此前四项包含它们时也返回 baseline。未经胜率验收不要打开开关。

普通完整分析按资本间隔启动（当前约 0.65 s）；没有购买成功时也受同样的分析间隔约束。0.25 s 传感器只读已有模拟密度格、血量和 Boss handle，不复制敌人数组。Emergency 使用现有约 0.24 s 的资本间隔。四组敌人数组复制及串行 reduction 仅在完整分析时运行；batch-local reduction / fixed-point 并行化留给第二版。

静态 prior 为每种塔缓存排序后的可建格。每次动态分析选择前 24 个空位，并从最多 4 个不同热点各补至多 8 个可覆盖热点的空位。Burst 调度和托管建造评分使用同一稀疏列表，每种塔最多 56 格；已占格被跳过，空列表不调度空间评分。地图、效果或 prior revision 改变后缓存重建。

## 运行

Unity 菜单：`Rouge > Tower Defense > Autoplay Benchmark`。

- `Validate Policy Scenarios`（Play 模式）：500 组混合动作的 regret 约束、Objective 模式隔离、五类资本动作、Emergency veto 和稀疏 cell 索引检查；批量基准首局自动执行。
- `Smoke - 1 Paired Seed`：Objective、岚、桃桃各一局，只验证流程，不能代表胜率。
- `Run 100 Paired Seeds`：先跑 Objective 的 100 个种子，再以相同种子跑岚、桃桃。
- `Stop And Save Partial Report`：退出并保存已完成局的数据。

命令行（不要加 `-quit`；Play 模式结束后工具自行退出）：

```powershell
& 'F:\UnityHub\Editor\Unity.exe' -batchmode -noaudio `
  -projectPath 'F:\NewRouge\RedVsBlue' `
  -executeMethod RougeAutoplayBenchmark.RunFromCommandLine `
  -autoplayRuns 100 -autoplaySeed 1337 `
  -autoplayAcceptDrop 0.05 `
  -autoplayMaxSeconds 1800 -autoplayMaxWallSeconds 3600 `
  -autoplayOutput Reports/Autoplay/acceptance.json `
  -logFile Temp/autoplay-acceptance.log
```

工具复制关卡和地图再修改 GameplaySeed，关闭副本的角色选择和开场动画（batch mode 不支持该动画的 WaitForEndOfFrame）。不保存原关卡或 Commander JSON 的修改。运行前应保存已打开的场景；正常完成会清理临时副本。意外中断留下的 `Assets/__AutoplayBenchmark*` 仅为临时副本。

## 指标与验收

每局写入 JSON 和 CSV；JSON 同时包含各模式汇总及实际执行的动作分布。

| 指标 | 定义 |
| --- | --- |
| WinRate | 正式胜利局数 / 全部运行局数；超时单列，并阻止验收 |
| AvgCoreHP | 结束时主塔剩余 HP，包含失败局 |
| GoldWaste | 结束时未花费金币的代理指标，并不等于每一枚余款都是浪费 |
| style divergence | 最终选择不同于同状态 objective best 的决策比例 |
| shield intervention | 风格提议遭 Safety veto 并回退 objective best 的次数 |
| decision p95 ms | 一次完整分析/决策的主线程时间，包括准备、结果消费、候选评分、动作执行和 benchmark 的 job completion 等待 |
| analysis latency p95 ms | 从启动分析到最终消费结果的墙钟时间，包括 worker 执行及跨帧等待 |
| frame p95 ms | Editor 实际墙钟帧间隔；不使用被 captureDeltaTime 固定的 deltaTime |
| fullAnalyses / maxSpatialCandidates | 完整分析次数及最大实际空间调度量，用于检查降频和候选缩小 |
| gateViolations | ε 下限或 Safety 约束被违反次数，必须为 0 |

测试默认关闭全部音效和音乐，使用最低画质、320×180 分辨率，禁用摄像机场景绘制并取消 FPS 上限。模拟步长固定为 1/20 秒（游戏模拟现有的最大步长），计划固定在下一帧完成，以减少机器调度速度对配对种子的影响；通过快速执行固定帧来加速，不使用会被模拟 dt 上限截断的高 timeScale。多线程浮点计算仍可能产生运行间差异；不要把固定种子视为逐位重放保证。这里的低画质且不绘制场景的帧耗时不能冒充正常独立游戏播放器的帧耗时，正式性能验收还应在相同图形配置、硬件和播放器环境中复测。汇总耗时明确为“各局 p95 的平均值”，不是合并全部样本后的 p95。

Objective 使用岚的现有关卡策略和天赋，关闭最终 personality 排序；岚、桃桃保留各自现有配置和天赋，因此比较的是完整 Commander 表现，并非只改变单个 bias 的消融实验。

默认完整验收要求：每模式至少 100 局；Objective 至少有胜局，避免“全部失败也通过”的空验收；无超时和 gate violation；风格 AI 胜率均不低于 Objective 胜率减 5 个百分点；两名 Commander 的建造塔型、升级塔型、扩张距离分布的平均 total variation distance 至少 0.05。动作数组顺序为 None/Hold/Build/Upgrade/Support/Charge，塔型数组按 RougeTowerType 的 standard tower enum 顺序，扩张距离分组为距主塔 ≤4、≤8、>8 格。阈值应在看结果前确定，不要按某一局的结果调整。

即使 Smoke 三局都获胜，也不会把 `sufficientSample` 或 `accepted` 写为 true。

## 2026-09-04 首轮实际结果

固定 seed 1337，各模式一局，静音/最低画质/0.05 s 步长；完整数据见 `Reports/Autoplay/smoke.json` 和 `.csv`。

| 模式 | 胜局 | 结束 HP | 余款 | 风格偏离次数 | Shield 回退 | Gate 违规 | 决策 p95 ms | 帧 p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Objective | 0/1 | 50 | 4263 | 0 | 0 | 0 | 24.75 | 20.03 |
| 岚 | 0/1 | 0 | 7271 | 21 | 33 | 0 | 22.68 | 18.32 |
| 桃桃 | 0/1 | 32 | 2442 | 16 | 18 | 0 | 23.39 | 18.93 |

两名 Commander 的平均分布距离为 0.110。三局都未超时，500 组策略场景及稀疏空间索引检查通过，但三局均失败，不能宣称胜率恢复，也未完成 100 局验收。应先调查现有 objective 评分在 Boss 终局的投资和伤害缺口，再跑完整样本；本轮没有按这个单一种子的结果改权重。
