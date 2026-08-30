using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// The two player-facing knowledge bases hosted by the archive terminal.
/// Values are stable because save data and cross-links may persist them.
/// </summary>
public enum RougeArchiveLibrary
{
    TacticalIndex = 0,
    AnchorArchive = 1
}

/// <summary>
/// Immutable category descriptor used by <see cref="RougeArchiveCatalog"/>.
/// </summary>
public sealed class RougeArchiveCategory
{
    public RougeArchiveLibrary Library { get; }
    public string StableId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public int SortOrder { get; }

    internal RougeArchiveCategory(RougeArchiveLibrary library, string stableId,
        string title, string subtitle, int sortOrder)
    {
        Library = library;
        StableId = stableId ?? string.Empty;
        Title = title ?? string.Empty;
        Subtitle = subtitle ?? string.Empty;
        SortOrder = sortOrder;
    }
}

/// <summary>
/// Immutable, localized archive record. Gameplay values intentionally do not
/// live here: changing balance must never make prose silently lie to the player.
/// </summary>
public sealed class RougeArchiveEntry
{
    private readonly ReadOnlyCollection<string> _relatedIds;

    public RougeArchiveLibrary Library { get; }
    public string CategoryId { get; }
    public string StableId { get; }
    public string Title { get; }
    public string Status { get; }
    public string Source { get; }
    public string Reliability { get; }
    public string Tags { get; }
    public string Body { get; }
    public int SortOrder { get; }
    public IReadOnlyList<string> RelatedIds => _relatedIds;

    internal RougeArchiveEntry(RougeArchiveLibrary library, string categoryId,
        string stableId, string title, string status, string source,
        string reliability, string tags, string body, int sortOrder,
        params string[] relatedIds)
    {
        Library = library;
        CategoryId = categoryId ?? string.Empty;
        StableId = stableId ?? string.Empty;
        Title = title ?? string.Empty;
        Status = status ?? string.Empty;
        Source = source ?? string.Empty;
        Reliability = reliability ?? string.Empty;
        Tags = tags ?? string.Empty;
        Body = body ?? string.Empty;
        SortOrder = sortOrder;
        _relatedIds = Array.AsReadOnly(relatedIds ?? Array.Empty<string>());
    }
}

/// <summary>
/// Built-in, read-only first edition of the tactical index and anchor archive.
/// It has no dependency on scene state, save state or the pause system.
/// </summary>
public sealed class RougeArchiveCatalog
{
    public const string TacticalSystemCategory = "TACTICAL.SYSTEM";
    public const string TacticalTowerCategory = "TACTICAL.TOWER";
    public const string TacticalHostileCategory = "TACTICAL.HOSTILE";
    public const string TacticalNodeCategory = "TACTICAL.NODE";
    public const string TacticalAiCategory = "TACTICAL.AI";

    public const string ArchiveOriginCategory = "ARCHIVE.ORIGIN";
    public const string ArchiveEngineeringCategory = "ARCHIVE.ENGINEERING";
    public const string ArchiveAiCategory = "ARCHIVE.AI";
    public const string ArchiveHostileCategory = "ARCHIVE.HOSTILE";
    public const string ArchiveIncidentCategory = "ARCHIVE.INCIDENT";
    public const string ArchiveLogCategory = "ARCHIVE.LOG";

    private static readonly RougeArchiveCatalog s_shared = new RougeArchiveCatalog();

    private readonly ReadOnlyCollection<RougeArchiveCategory> _categories;
    private readonly ReadOnlyCollection<RougeArchiveEntry> _entries;
    private readonly Dictionary<string, RougeArchiveEntry> _entriesById;
    private readonly Dictionary<RougeArchiveLibrary, ReadOnlyCollection<RougeArchiveCategory>>
        _categoriesByLibrary;
    private readonly Dictionary<string, ReadOnlyCollection<RougeArchiveEntry>>
        _entriesByCategory;

    public static RougeArchiveCatalog Shared => s_shared;
    public IReadOnlyList<RougeArchiveCategory> Categories => _categories;
    public IReadOnlyList<RougeArchiveEntry> Entries => _entries;

    private RougeArchiveCatalog()
    {
        List<RougeArchiveCategory> categories = BuildCategories();
        List<RougeArchiveEntry> entries = new List<RougeArchiveEntry>(64);
        BuildTacticalEntries(entries);
        BuildAnchorEntries(entries);

        categories.Sort((left, right) =>
        {
            int libraryOrder = left.Library.CompareTo(right.Library);
            return libraryOrder != 0 ? libraryOrder : left.SortOrder.CompareTo(right.SortOrder);
        });
        entries.Sort((left, right) =>
        {
            int libraryOrder = left.Library.CompareTo(right.Library);
            if (libraryOrder != 0) return libraryOrder;
            int categoryOrder = FindCategoryOrder(categories, left.CategoryId).CompareTo(
                FindCategoryOrder(categories, right.CategoryId));
            return categoryOrder != 0 ? categoryOrder : left.SortOrder.CompareTo(right.SortOrder);
        });

        _categories = categories.AsReadOnly();
        _entries = entries.AsReadOnly();
        _entriesById = new Dictionary<string, RougeArchiveEntry>(
            entries.Count, StringComparer.Ordinal);
        _categoriesByLibrary = new Dictionary<RougeArchiveLibrary,
            ReadOnlyCollection<RougeArchiveCategory>>();
        _entriesByCategory = new Dictionary<string,
            ReadOnlyCollection<RougeArchiveEntry>>(StringComparer.Ordinal);

        for (int i = 0; i < entries.Count; i++)
            _entriesById.Add(entries[i].StableId, entries[i]);

        foreach (RougeArchiveLibrary library in Enum.GetValues(typeof(RougeArchiveLibrary)))
        {
            List<RougeArchiveCategory> libraryCategories = categories.FindAll(
                category => category.Library == library);
            _categoriesByLibrary.Add(library, libraryCategories.AsReadOnly());
        }

        for (int i = 0; i < categories.Count; i++)
        {
            string categoryId = categories[i].StableId;
            List<RougeArchiveEntry> categoryEntries = entries.FindAll(
                entry => string.Equals(entry.CategoryId, categoryId,
                    StringComparison.Ordinal));
            _entriesByCategory.Add(categoryId, categoryEntries.AsReadOnly());
        }
    }

    public IReadOnlyList<RougeArchiveCategory> GetCategories(RougeArchiveLibrary library)
    {
        return _categoriesByLibrary.TryGetValue(library, out ReadOnlyCollection<RougeArchiveCategory> result)
            ? result
            : Array.Empty<RougeArchiveCategory>();
    }

    public IReadOnlyList<RougeArchiveEntry> GetEntries(string categoryId)
    {
        return categoryId != null &&
               _entriesByCategory.TryGetValue(categoryId,
                   out ReadOnlyCollection<RougeArchiveEntry> result)
            ? result
            : Array.Empty<RougeArchiveEntry>();
    }

    public bool TryGetEntry(string stableId, out RougeArchiveEntry entry)
    {
        entry = null;
        return stableId != null && _entriesById.TryGetValue(stableId, out entry);
    }

    public static string GetLibraryTitle(RougeArchiveLibrary library)
    {
        return library == RougeArchiveLibrary.AnchorArchive
            ? "ANCHOR ARCHIVE // 锚区档案"
            : "TACTICAL INDEX // 战区识别库";
    }

    private static int FindCategoryOrder(List<RougeArchiveCategory> categories,
        string categoryId)
    {
        for (int i = 0; i < categories.Count; i++)
            if (string.Equals(categories[i].StableId, categoryId,
                    StringComparison.Ordinal))
                return categories[i].SortOrder;
        return int.MaxValue;
    }

    private static List<RougeArchiveCategory> BuildCategories()
    {
        return new List<RougeArchiveCategory>
        {
            Category(RougeArchiveLibrary.TacticalIndex, TacticalSystemCategory,
                "系统基础", "锚区、战术格与副官协议", 0),
            Category(RougeArchiveLibrary.TacticalIndex, TacticalTowerCategory,
                "防御构筑", "十类可部署塔楼", 10),
            Category(RougeArchiveLibrary.TacticalIndex, TacticalHostileCategory,
                "敌对目标", "突破机、精英模板与霸主", 20),
            Category(RougeArchiveLibrary.TacticalIndex, TacticalNodeCategory,
                "地形与节点", "标准格与十二类特殊格", 30),
            Category(RougeArchiveLibrary.TacticalIndex, TacticalAiCategory,
                "人物与智能体", "当前可接入的战术副官", 40),

            Category(RougeArchiveLibrary.AnchorArchive, ArchiveOriginCategory,
                "任务与起源", "战前简报与投送记录", 0),
            Category(RougeArchiveLibrary.AnchorArchive, ArchiveEngineeringCategory,
                "基地与工程", "锚定、供能与快速制造", 10),
            Category(RougeArchiveLibrary.AnchorArchive, ArchiveAiCategory,
                "人物与智能体", "权限审计与人格记录", 20),
            Category(RougeArchiveLibrary.AnchorArchive, ArchiveHostileCategory,
                "敌方网络", "残片解析与威胁推定", 30),
            Category(RougeArchiveLibrary.AnchorArchive, ArchiveIncidentCategory,
                "事故与异常", "工程调查与违规记录", 40),
            Category(RougeArchiveLibrary.AnchorArchive, ArchiveLogCategory,
                "前线生活", "后勤备忘与非正式记录", 50)
        };
    }

    private static RougeArchiveCategory Category(RougeArchiveLibrary library,
        string id, string title, string subtitle, int order)
    {
        return new RougeArchiveCategory(library, id, title, subtitle, order);
    }

    private static void BuildTacticalEntries(List<RougeArchiveEntry> entries)
    {
        const RougeArchiveLibrary library = RougeArchiveLibrary.TacticalIndex;
        entries.Add(Tactical(library, TacticalSystemCategory, "SYS-001",
            "锚定式机动基地", "系统基础资料", "基地工程手册",
            "基地车兼具投送载具、锚区核心与主塔功能。抵达任务区后，它会固定自身、展开工程系统，并成为整片防区的供能与指挥中心。\n\n基地失守意味着锚区授权、制造能力和战术链路同时中断，因此所有敌军突破都会在这里结束。", 0,
            "ORI-001", "ENG-002"));
        entries.Add(Tactical(library, TacticalSystemCategory, "SYS-002",
            "战术格", "系统基础资料", "工程节点扫描",
            "地表没有天然方格。副官把锚桩承载结果、地下供能接点和安全间距换算成离散工程节点，再通过战术目镜叠加到视野中。\n\n只有通过结构与供能校验的节点才允许部署塔楼；空白区域并不等于可以施工。", 10,
            "ENG-001", "INC-004"));
        entries.Add(Tactical(library, TacticalSystemCategory, "SYS-003",
            "战备额度", "系统基础资料", "战区后勤协议",
            "界面中的货币图标代表可立即调用的战备额度，而不是现场流通的硬币。敌军残骸经识别、回收与结算后，会转化为基地能够支配的材料、能源和制造时段。", 20,
            "LOG-003"));
        entries.Add(Tactical(library, TacticalSystemCategory, "SYS-004",
            "战术副官", "系统基础资料", "权限注册表",
            "战术副官负责把遥测、工程限制和威胁判断整理成可执行界面，也可在玩家明确授权后接管建造、升级、出售与换防。\n\n副官与玩家遵守相同的资源、塔位和冷却规则；托管是权限移交，不是额外作弊层。", 30,
            "AI-003", "AI-007"));
        entries.Add(Tactical(library, TacticalSystemCategory, "SYS-005",
            "副官视界协议", "系统基础资料", "战术目镜渲染协议",
            "格网、危险区、可建造状态与特殊节点都属于副官视界的增强现实叠层。不同人格可以改变终端的色彩和批注语气，但不会改变任何规则语义色或节点实际效果。", 40,
            "AI-011"));
        entries.Add(Tactical(library, TacticalSystemCategory, "SYS-006",
            "锚区启动序列", "系统基础资料", "首次部署遥测",
            "标准启动顺序为：确认空地、接收基地投送、完成锚定、扫描地下工程条件、建立目镜格网，最后进入战斗准备。\n\n顺序不可随意交换；扫描依赖锚核建立的本地坐标与供能基准。", 50,
            "ORI-002", "ENG-002"));

        AddTower(entries, "TWR-00", RougeTowerType.Ice,
            "控制 / 范围 / 相变",
            "抽离目标热量并制造低温控制区。适合布置在路线交汇处，为其他火力争取更多有效攻击窗口。", 0);
        AddTower(entries, "TWR-01", RougeTowerType.MachineGun,
            "高频 / 多目标 / 通用",
            "以高速发射和并发火控处理轻型与密集目标。部署成本和职责直观，是补齐防线空档的通用构筑。", 10);
        AddTower(entries, "TWR-02", RougeTowerType.Cannon,
            "远程 / 范围 / 爆发",
            "向远距离密集区投送重型弹药。应优先覆盖路线交汇点，让每次爆炸尽可能命中完整敌群。", 20);
        AddTower(entries, "TWR-03", RougeTowerType.Flame,
            "持续 / 范围 / 热效应",
            "向近中程区域持续投送高温工作介质，适合敌人会长时间经过的通道。与低温控制形成的热冲击具有额外协同价值。", 30);
        AddTower(entries, "TWR-04", RougeTowerType.Laser,
            "持续 / 多目标 / 烧蚀",
            "以连续光束稳定追踪目标，可承担装甲烧蚀或密集敌群处理任务。索敌策略决定它是在一处持续施压，还是分配火力。", 40);
        AddTower(entries, "TWR-05", RougeTowerType.PiercingLaser,
            "直线 / 贯穿 / 高功率",
            "大型电容驱动的线性光束，一次射击处理整条路径上的目标。最需要的是良好轴线，而不是单纯接近道路。", 50);
        AddTower(entries, "TWR-06", RougeTowerType.OrbitSphere,
            "巡行 / 持续 / 弯道覆盖",
            "悬浮晶体攻击单元沿射界边缘巡行。弯道、环路和敌人长期贴近射界边缘的位置能发挥其区域覆盖优势。", 60);
        AddTower(entries, "TWR-07", RougeTowerType.RocketBarrage,
            "弹群 / 范围 / 饱和火力",
            "一轮投放多枚协同微型导弹，对密集目标形成饱和覆盖。落点规划比单枚武器的精确瞄准更重要。", 70);
        AddTower(entries, "TWR-08", RougeTowerType.ChargeTower,
            "工程 / 节点改造 / 支援",
            "勘测相邻标准节点，并在地层与管线允许的候选方案中重写节点特性。候选是工程安全边界，不是凭空抽取的奖励。", 80,
            "ENG-004");
        AddTower(entries, "TWR-09", RougeTowerType.ReinforcementTower,
            "工程 / 光环 / 协同",
            "通过授时、供能和火控同步增幅邻近构筑。它本身不争夺战果，价值取决于能同时接入多少座高投入塔楼。", 90);

        entries.Add(Tactical(library, TacticalHostileCategory, "HOST-00",
            "普通型突破机", "初步识别", "战场遥测",
            "敌方最常见的量产机械单位，耐久与推进速度均衡。它们沿可行路线压向基地，抵达主塔后释放破锚载荷。\n\n“普通”只表示常见，不表示可以忽略。", 0));
        entries.Add(Tactical(library, TacticalHostileCategory, "HOST-01",
            "迅捷型突破机", "初步识别", "战场遥测",
            "通过减轻结构、强化驱动换取更高推进速度的批次。它们擅长穿过两段火力之间的时间空档，应以减速、冻结或高频火力补漏。", 10));
        entries.Add(Tactical(library, TacticalHostileCategory, "HOST-02",
            "重装型突破机", "初步识别", "战场遥测",
            "以厚重结构和装甲换取稳定推进能力，会长期占用防线输出并掩护后续单位。穿甲、破甲、脆弱和高单体伤害更适合处理它。", 20));
        entries.Add(Tactical(library, TacticalHostileCategory, "HOST-E",
            "精英变体", "行为已确认", "精英信号遥测",
            "精英是一套可附着在基础机型上的高规格生产模板，并非独立的第四种敌人。判断威胁时，应先识别基础职责，再考虑模板带来的整体强化。", 30));
        entries.Add(Tactical(library, TacticalHostileCategory, "BOSS-00",
            "霸主", "阶段行为逐步补全", "首领遥测",
            "大型移动授权节点，兼具突破机体与敌方战场网络核心功能。受损后会逐步启用塔楼干扰、群体防护与推进强化。\n\n它抵达基地即触发夺控。应提前准备稳定的对甲输出和首领优先火力，同时保留处理伴随单位的能力。", 40,
            "HOST-012"));

        AddNode(entries, "NODE-00", "标准工程节点", RougeTowerPlaceEffect.None,
            "各项指标达到标准，没有额外偏置。适合稳定规划与任意常规塔楼。", 0);
        AddNode(entries, "NODE-01", "峰值供能节点", RougeTowerPlaceEffect.DamageAmplifier,
            "把瞬时功率优先送往武器系统，远距稳定余量相应下降。", 10);
        AddNode(entries, "NODE-02", "远传阵列节点", RougeTowerPlaceEffect.RangeAmplifier,
            "接入高位传感或旧中继，扩大火控覆盖并承担额外传输负载。", 20);
        AddNode(entries, "NODE-03", "脉冲循环节点", RougeTowerPlaceEffect.AttackSpeedAmplifier,
            "电容可以快速充放，但长距离输能稳定性较弱。", 30);
        AddNode(entries, "NODE-04", "多路汇流节点", RougeTowerPlaceEffect.PremiumAmplifier,
            "多条能源与数据主干在此交汇，维护复杂度也随之提高。", 40);
        AddNode(entries, "NODE-05", "预制锁定基座", RougeTowerPlaceEffect.FreeLevelNoRefund,
            "节点保留可用强化模块，但部署后会与新塔形成永久熔接。", 50);
        AddNode(entries, "NODE-06", "高保真认证节点", RougeTowerPlaceEffect.Bounty,
            "可上传更完整的击杀证据，用带宽换取更高的回收结算权限。", 60);
        AddNode(entries, "NODE-07", "制造直连节点", RougeTowerPlaceEffect.Discount,
            "与基地制造线之间的物流损耗较低，适合长期追加投入。", 70);
        AddNode(entries, "NODE-08", "磁轨移载节点", RougeTowerPlaceEffect.Relocation,
            "保留重型设备转运与重新锁定接口，可以支持付费搬运。", 80);
        AddNode(entries, "NODE-09", "指令回响节点", RougeTowerPlaceEffect.Echo,
            "特殊介质会延迟复现一次火控波形，但需要构筑主动降额保持稳定。", 90);
        AddNode(entries, "NODE-12", "延时回收节点", RougeTowerPlaceEffect.AccumulatedWealth,
            "战利品先进入深度提炼流程，再按周期统一结算。", 100);
        AddNode(entries, "NODE-13", "失稳注入节点", RougeTowerPlaceEffect.Explosion,
            "攻击可向敌方核心写入过载标记，目标失效时可能触发连锁释放。", 110);
        AddNode(entries, "NODE-14", "低温相变节点", RougeTowerPlaceEffect.Frost,
            "地层能够长期维持低温场，使直接命中与目标驱动系统发生额外耦合。", 120);

        entries.Add(Tactical(library, TacticalAiCategory, "AI-LAN", "岚",
            "角色注册表", "本地人格注册表",
            "本地战术副官与托管指挥单元。判断风格细致、直接、克制，优先关注近端安全、地图价值和规则边界。\n\n在玩家授权后，她可以接管构筑调度，但不会获得额外资源或绕过建造限制。", 0,
            "AI-LAN-01"));
        entries.Add(Tactical(library, TacticalAiCategory, "AI-TAOTAO", "桃桃",
            "角色注册表", "轨道后勤注册表",
            "战术后勤与支援副官。表达明快，习惯以清单、补给与接力关系组织战场。\n\n她拥有与岚等额的托管权限，也遵守相同的资源、塔位与冷却规则。", 10,
            "AI-TAO-01"));
    }

    private static RougeArchiveEntry Tactical(RougeArchiveLibrary library,
        string categoryId, string id, string title, string status, string source,
        string body, int order, params string[] relatedIds)
    {
        return new RougeArchiveEntry(library, categoryId, id, title, status,
            source, "CONFIRMED // 已确认", string.Empty, body, order, relatedIds);
    }

    private static void AddTower(List<RougeArchiveEntry> entries, string id,
        RougeTowerType type, string tags, string body, int order,
        params string[] relatedIds)
    {
        entries.Add(new RougeArchiveEntry(RougeArchiveLibrary.TacticalIndex,
            TacticalTowerCategory, id, TowerDefenseVisuals.GetTowerName(type),
            "行为已确认", "基地构筑注册表", "CONFIRMED // 已确认", tags,
            body + "\n\n具体价格、伤害、射程与升级变化以当前战区实时数据为准。",
            order, relatedIds));
    }

    private static void AddNode(List<RougeArchiveEntry> entries, string id,
        string title, RougeTowerPlaceEffect effect, string explanation, int order)
    {
        string currentRule = RougeTowerPlaceEffectRules.GetDescription(effect);
        entries.Add(new RougeArchiveEntry(RougeArchiveLibrary.TacticalIndex,
            TacticalNodeCategory, id, title, "系统基础资料", "工程节点扫描",
            "CONFIRMED // 已确认", "节点 / 地形 / 构筑",
            explanation + "\n\n当前规则说明\n" + currentRule,
            order, "ENG-004"));
    }

    private static void BuildAnchorEntries(List<RougeArchiveEntry> entries)
    {
        const RougeArchiveLibrary library = RougeArchiveLibrary.AnchorArchive;

        entries.Add(Archive(library, ArchiveOriginCategory, "ORI-001",
            "锚定任务前简报", "公开资料", "任务授权与战前简报",
            "CONFIRMED // 已确认",
            "你将接收一台锚定式机动基地，并在指定空域建立临时防区。基地不是需要护送离开的车辆：完成投送后，它就是本地制造线、供能中心、指挥节点与最后防线。\n\n任务目标不是占领整片区域，而是在敌方机械集群抵达核心前，把一块没有基础设施的空地变成可运作的锚区。\n\n已确认：敌方单位把锚核视作唯一终点；核心失守会终止本地授权。\n\n待解密：敌方如何在首次扫描前取得锚核坐标。",
            0, "SYS-001"));
        entries.Add(Archive(library, ArchiveOriginCategory, "ORI-002",
            "从空白到战区", "任务记录", "首次部署流程记录",
            "CONFIRMED // 已确认",
            "记录开始时，投送区只有未经标记的地表。基地完成短距传送后落入预定姿态，锚核建立本地坐标；随后工程钻机下沉，扫描波沿地下管线搜索可用承载点。\n\n直到扫描完成，副官才把格网写入战术目镜。玩家看到的“地图”不是被运来的地板，而是一份实时工程判断。准备信号发出后，敌方首批突破机才进入可见范围。\n\n已确认：启动顺序为投送、锚定、扫描、格网、战备。\n\n待解密：投送窗口为何只能维持极短时间。",
            10, "SYS-006"));

        entries.Add(Archive(library, ArchiveEngineeringCategory, "ENG-001",
            "地面上从来没有方格", "任务记录", "锚区工程手册节选",
            "CONFIRMED // 已确认",
            "战术目镜中的每个格子，代表一组已经通过校验的工程条件：地下锚桩能够咬合、供能管线可以接入、相邻构筑保持安全距离，且塔楼开火时的载荷不会让地基失稳。\n\n格线是副官为了让部署决策可读而绘制的离散边界。强化格也不是发光地砖，而是扫描发现的高通量、旧设施、特殊介质或协议接口。\n\n已确认：只有有效工程节点允许建造。\n\n待解密：部分旧节点的原始建造者。",
            0, "SYS-002", "NODE-00"));
        entries.Add(Archive(library, ArchiveEngineeringCategory, "ENG-002",
            "展开顺序不得更改", "任务记录", "基地展开维护规程",
            "CONFIRMED // 已确认",
            "基地投送后必须先建立姿态锁，再释放锚桩。锚核未校准时提前扫描，会把传送残余扰动误判成地下空洞；供能干线未闭合时启动制造，则可能把整座塔的瞬时负载压回核心。\n\n因此工程系统坚持一个看似缓慢的顺序：固定、校准、钻探、铺设、扫描、授权。战场上看到的短暂演出，是多项危险作业被自动化压缩后的结果。\n\n已确认：锚定同时建立结构、供能与坐标基准。\n\n待解密：基地远端投送设备的来源与现状。",
            10, "SYS-001", "SYS-006"));
        entries.Add(Archive(library, ArchiveEngineeringCategory, "ENG-003",
            "一座塔如何在数秒内到岗", "任务记录", "快速制造线值班日志",
            "CONFIRMED // 已确认",
            "塔楼并不是由地下瞬间长出。基地制造线预先储备标准骨架、武器模块与折叠护罩；收到部署指令后，系统完成装配，把构筑沿地下输送接口或短程物质转运链送到目标节点，再由锚桩锁定。\n\n玩家支付的战备额度购买的是材料配额、能量峰值与制造队列时间。越复杂的塔占用越多资源，升级则是在原有结构上继续替换模块。\n\n已确认：所有塔都需要有效节点与基地授权。\n\n待解密：早期型号为何保留了人工检修舱。",
            20, "TWR-00", "TWR-09"));
        entries.Add(Archive(library, ArchiveEngineeringCategory, "ENG-004",
            "特殊节点不是奖励地板", "任务记录", "节点调谐说明",
            "CONFIRMED // 已确认",
            "特殊节点只是偏离标准条件的工程位置。高通量可能提高武器功率，却压缩远传余量；旧中继可能扩大覆盖，也会增加维护成本。每一种优势都来自真实接口，因此通常伴随明确代价。\n\n充能塔的工作是勘测相邻地层，并从不会烧毁管线的方案里列出候选改造。它不能命令地层提供任意效果。\n\n已确认：特殊节点同时具有收益与限制。\n\n待解密：编号缺口对应的节点是否已被永久封存。",
            30, "TWR-08", "NODE-01", "NODE-14"));

        entries.Add(Archive(library, ArchiveAiCategory, "AI-003",
            "副官是权限职位", "公开资料", "人格权限注册表",
            "CONFIRMED // 已确认",
            "“副官”不是某一种固定人格，而是一组可以接入锚区的职责与权限。被选中的人格包获得传感汇总、界面渲染、风险提示与有限托管能力；未被授权时，它只能提出建议。\n\n人格影响表达方式和决策偏好，不改变战区规则。任何副官都必须把资源消耗、失败原因和不确定判断如实暴露给玩家。\n\n已确认：人格与权限分离，托管需要玩家授权。\n\n待解密：最初一代副官协议由谁制定。",
            0, "SYS-004"));
        entries.Add(Archive(library, ArchiveAiCategory, "AI-007",
            "等额权限审计", "关系记录", "托管权限审计",
            "CONFIRMED // 已确认",
            "审计结论：玩家手动操作与副官托管调用同一套建造、升级、出售和技能接口。副官没有隐藏收入、额外塔位或缩短冷却的权限。\n\n托管的优势来自持续观察和执行稳定，缺点则来自它只能依据已经获取的遥测做判断。把权限交出去并不会让基地获得更多资源，只会改变谁在当前班次下达指令。\n\n已确认：双方权限等额。\n\n待解密：长期托管是否会改变人格包的风险倾向。",
            10, "SYS-004"));
        entries.Add(Archive(library, ArchiveAiCategory, "AI-011",
            "把同一片战场看成两种颜色", "关系记录", "视界协议对照测试",
            "CONFIRMED // 已确认",
            "测试组让岚与桃桃依次接管同一段冻结战场录像。两份界面的构图、危险标记和规则提示完全一致，变化的是强调方式：岚把近端漏洞与边界条件提前，桃桃则把补给节奏和协同链路放在更醒目的位置。\n\n主题色属于人格界面，不属于物理世界。无论由谁值班，危险、可建造与特殊节点的语义必须保持可辨认。\n\n已确认：人格主题不能改写规则。\n\n待解密：玩家是否可以保存自定义视界配置。",
            20, "SYS-005", "AI-LAN", "AI-TAOTAO"));
        entries.Add(Archive(library, ArchiveAiCategory, "AI-LAN-01",
            "本地驻留申请", "关系记录", "岚的人格调度申请",
            "CONFIRMED // 已确认；历史字段受限",
            "申请摘要：岚请求从短时战术实例调整为锚区本地驻留人格，理由是频繁重置会丢失地图判断、玩家习惯与未完成的风险记录。她在附注中强调，这不是扩大权限的申请，只是希望在下一次警报响起时仍记得上一班留下的问题。\n\n审批结果保留了本地缓存，但限制跨战区复制。\n\n已确认：岚重视连续性、边界与近端安全。\n\n待解密：申请前发生过哪次记忆丢失。",
            30, "AI-LAN"));
        entries.Add(Archive(library, ArchiveAiCategory, "AI-TAO-01",
            "轨道后勤席调任记录", "关系记录", "桃桃的调任回执",
            "CONFIRMED // 已确认；回执状态未知",
            "桃桃原先处理轨道补给席的队列、窗口与交接异常。调任评估认为，她把复杂问题拆成清单和接力节点的习惯，同样适合节奏快速的锚区防御。\n\n她在回执里写道：前线和后勤并不是两个地方，只是同一件事的前后两分钟。最后一段签收状态没有恢复。\n\n已确认：桃桃具有后勤调度背景。\n\n待解密：调任是主动申请，还是一次紧急补位。",
            40, "AI-TAOTAO"));

        entries.Add(Archive(library, ArchiveHostileCategory, "HOST-001",
            "它们为什么只向中心推进", "回收资料", "战场遥测与残片解析",
            "CONFIRMED // 已确认；部分为可靠推定",
            "现有遥测没有发现突破机会体进行资源搜集、领土标记或自主猎杀。它们选择路径的标准近乎单一：以仍可通行的路线接近锚核，并在抵达后释放破坏载荷。\n\n残片中的授权字段表明，锚核可能不仅是目标，也是一种敌方网络希望夺回或关闭的认证节点。这个解释能够说明其行为，却尚不能证明最初是谁下达命令。\n\n已确认：敌军以主塔为最终目标。\n\n待解密：它们是在执行旧命令，还是仍与远端网络保持联系。",
            0, "HOST-00", "SYS-001"));
        entries.Add(Archive(library, ArchiveHostileCategory, "HOST-012",
            "为什么它被称为霸主", "交叉解密", "首领遥测与授权树残片",
            "行为已确认；命名为可靠推定",
            "“霸主”最初不是体型分类，而是残片授权树中反复出现的近似译名。它能够向周围单位广播防护参数、扰乱我方锚网授时，并在结构受损后强行提高推进功率。\n\n这说明它不仅带领敌军移动，还在局部范围内替代了敌方网络的上级节点。击毁它会让随行集群失去一部分协调，却没有证明整个网络因此停止。\n\n已确认：霸主是移动指挥与授权节点。\n\n待解密：授权树顶端是否还存在更高层实体。",
            10, "BOSS-00"));

        entries.Add(Archive(library, ArchiveIncidentCategory, "INC-004",
            "七码头炮座倾覆事故", "任务记录", "旧港区工程调查摘要",
            "CONFIRMED // 已确认",
            "事故组曾把“地面平整”误当成“节点有效”，在没有锚桩咬合和供能闭锁的七码头安装重型炮座。试射时，后坐载荷让基座旋转下沉，供能软管随后被拉断。\n\n调查结论成为现行建造协议的底线：目视可用从来不等于工程可用。战术目镜拒绝部署时，不是在限制创造力，而是在复述许多已经付过代价的事故。\n\n已确认：无效区域无法安全承载塔楼。\n\n待解密：事故报告为何删除了当班授权人的姓名。",
            0, "SYS-002"));

        entries.Add(Archive(library, ArchiveLogCategory, "LOG-003",
            "关于金币图标的第六次投诉", "任务记录", "后勤界面备忘",
            "CONFIRMED // 已确认",
            "再次说明：敌军没有携带成袋硬币。击毁收益来自残骸材料、能源余量、有效遥测与战区回收协议的统一结算。界面继续显示金币，是因为测试员能在最短时间内理解“现在可以花多少”。\n\n若有更好的单字符图标，同时满足不与弹药、电力、信用点和轨道配额混淆，请在下次界面冻结前提交。没有的话，本投诉将按惯例归档。\n\n已确认：金币是战备额度的简化显示。\n\n待解密：前五次投诉分别建议了什么图标。",
            0, "SYS-003"));
    }

    private static RougeArchiveEntry Archive(RougeArchiveLibrary library,
        string categoryId, string id, string title, string status, string source,
        string reliability, string body, int order, params string[] relatedIds)
    {
        return new RougeArchiveEntry(library, categoryId, id, title, status,
            source, reliability, string.Empty, body, order, relatedIds);
    }
}
