# 客户端架构

Starline 正式工程的代码结构。设计以 [game-design.md](game-design.md) 为准，这里只讲代码怎么组织。

## 1. 分层

```
┌──────────────────────────────────────────────────────────────┐
│ Unity 客户端  Starline.Client（Runtime/Client）               │
│                                                              │
│  App/       GameApp：入口、界面切换、持有当前远征              │
│  Screens/   每个界面一个类 + 一个 UXML 布局                    │
│  Gameplay/  DeployController、BattleController：界面背后的逻辑 │
│  Stage/     BattleStage：3D 棋盘、角色模型、特效、镜头         │
│  UI/        UiRoot（UI Toolkit 根）、主题、字体、角色头顶标签   │
│  Audio/     MusicPlayer：按 music.json 选曲、淡入淡出（见 music.md）│
└───────────────┬──────────────────────────────────────────────┘
                │ 只调用，不反向依赖
┌───────────────▼──────────────────────────────────────────────┐
│ 内核  Starline.Battle（Runtime/Battle，纯 C#，不依赖 Unity）   │
│  Data/       JSON 解析、GameData、单位表、克制表               │
│  Board/      棋盘坐标、布阵规则、魔网                      │
│  Sim/        确定性战斗模拟、指挥官技能                        │
│  Analysis/   战前推演                                         │
│  Expedition/ 远征：地图、远征军与合成、奖励、自动布阵、远征配置   │
└───────────────┬──────────────────────────────────────────────┘
                │ 读取
┌───────────────▼──────────────────────────────────────────────┐
│ 配置表  Runtime/Resources/StarlineData/*.json                 │
│  units · counters · skills · encounters · expedition         │
└──────────────────────────────────────────────────────────────┘
```

规则：

- 游戏规则和数值只放在内核和配置表里。客户端只负责显示和输入，不做任何会影响战斗结果的计算。
- 内核同时被 Unity 和 .NET 测试项目编译（`src/Starline.Battle`），所以改内核后跑 `dotnet run --project tests/Starline.Battle.Tests` 就能验证。
- 内核必须保持确定性：只用定点数 `Fix` 和 `DeterministicRandom`，不用 float、`System.Random`，也不依赖字典的遍历顺序。

## 2. 配置表

| 文件 | 内容 |
|---|---|
| `units.json` | 角色和敌方单位的固有属性（生命、护盾、伤害、射程、法力、索敌偏好等） |
| `counters.json` | 克制表：攻击方定位对目标定位的伤害倍率 |
| `skills.json` | 指挥官技能；星级倍率（★1~★5） |
| `encounters.json` | 敌方阵容：共用的亡灵祭坛底座 + 各自的单位；可选 `terrain`（战斗场景）和 `layout` / `layoutSeed`（地块布局） |
| `terrains.json` | 战斗场景（设计文档 5.5）：每章的战场环境和棋盘地块，内核按定点数结算；名字和说明在 `Localization/Game.csv` |
| `expedition.json` | 远征规则（耐久、指挥点、水晶上限）、每一幕的开局远征军、地图层、敌人分档、奖池 |

- 数值写成十进制小数（最多 4 位），内核按分数精确转换成定点数，同样的数据在任何电脑上算出同样的结果。
- 数据出错时会报告文件名、行号和字段路径，例如 `units.json 第 12 行：应为 '}'`、`$.units[3].hp：缺少字段`。
- Unity 通过 `Resources.Load<TextAsset>` 读取，测试项目直接读同一批文件，不存在两份数据。
- 所有数值的汇总和对战模拟结果见 [balance-sheet.md](balance-sheet.md)，由 `dotnet run --project tests/Starline.Battle.Tests -- --balance` 生成。
- 以后可以加一个从 Excel 导出 JSON 的脚本，格式不用改。
- JSON 里只有数值和 id，玩家看到的文字（名字、说明、简报、剧情）都在语言表里，见下一节。

## 2.1 本地化（Unity Localization）

用 Unity 官方的 Localization 包（`com.unity.localization`，依赖 Addressables）。

| 位置 | 内容 |
|---|---|
| `Packages/com.starline.game/Localization/UI.csv` | 界面文字：UXML 里的 `text="#key"`、C# 里的 `Loc.Ui("key", 参数…)` |
| `Packages/com.starline.game/Localization/Game.csv` | 内核产生的文字：`unit.<id>.name/description`、`skill.<id>.*`、`encounter.<id>.name/briefing`、`act.<id>.*`（幕名和剧情），以及远征消息、布阵错误、推演败因和建议 |
| `Packages/com.starline.game/Localization/Characters.csv` | 角色：`faction.<id>.*`（阵营名和主题）、`role.<id>.name`（职业）、`hero.<id>.name/title`、`hero.<id>.skill<n>.name/desc`、`bio.<id>.1~5`（小传，★1~★5 解锁）。C# 里用 `Loc.Hero("key")` |
| `Packages/com.starline.game/Localization/Story.csv` | 主线剧情：`story.act.*`、`story.chapter.<n>.*`（章名、简介、章末钩子、Boss）、`story.thread.*`（五根线）、`story.lore.*`（档案馆）、`story.ending.*`、`story.recap.*`（星线编年）、`story.letter.*`（诺娃的信）。C# 里用 `Loc.Story("key")` |
| `Assets/Localization/` | 由 CSV 生成的 Unity 资源：Localization Settings、每种语言一个 Locale、`UI`、`Game`、`Characters`、`Story` 四个字符串表 |

- **CSV 是文本的源头**：列为 `key, zh-Hans, en, note`，一行一条。改 CSV（或 git pull 拉到新 CSV）后 Unity 会自动重新导入；也可以点菜单 `Starline > 本地化 > 从 CSV 导入语言表`。在 Unity 的表格编辑器里改了文本，要点 `导出语言表到 CSV` 写回去，否则下次导入会被 CSV 覆盖。
- 加一种语言就是在 CSV 里加一列（列名是语言代码，如 `ja`、`zh-Hant`），导入时自动建 Locale。空单元格表示没翻译，显示简体中文。
- 占位符用 `{0}`、`{1}`，译文的占位符必须和中文一致（测试会检查）。名字类参数由代码先翻译再填进去。
- 内核不含任何文字：返回 `LocText`（key + 参数，参数可以是另一个 `LocText`，例如角色名），客户端用 `Loc.T` 翻译。这样服务端重跑战斗、测试都不受语言影响。测试用 `Game.csv` 的中文列输出日志。
- 启动语言：命令行 `-language=en` → 系统语言 → 简体中文。设置里保存的语言在初始化后用 `Loc.SetLanguage(code)` 应用；`GameApp` 等 `Loc.Initialize()` 完成后才显示主菜单。切换语言后已打开的界面要重建才会换文字（`Loc.LanguageChanged`）。
- 语言表打进安装包（Addressables 本地分组，随 Player 一起构建），不走 CDN；更新随 Steam 版本发布。
- 内核测试会检查：每条都有中文、译文占位符一致、每个角色 / 技能 / 阵容 / 幕都有文本、47 名角色都有 5 段小传、UXML 和客户端代码用到的 key 都在 `UI.csv` 里且没有多余的行。
- 翻译流程、术语对照和英文译名见 [localization.md](localization.md)。

## 3. 界面（UI Toolkit）

- 唯一的 `UIDocument` 由 `UiRoot` 在运行时创建，分三层：角色头顶标签（world-layer）→ 当前界面（screen-layer）→ 弹窗（modal-layer）。
- 每个界面是 `UiScreen` 的子类，布局在 `Resources/StarlineUI/<名字>.uxml`，样式统一在 `Theme.uss`。可以用 Unity 的 UI Builder 直接打开 UXML 调整。
- 设计分辨率 3840 × 2160（4K），按屏幕高度缩放：Theme.uss 和代码里的像素值都是 4K 像素，1080p 屏幕上整体缩小一半。界面图片素材按 4K 尺寸出图。
- 清晰度（渲染分辨率，如 1080p / 1440p / 4K）将在设置界面（M3）里选择；界面跟随屏幕自动缩放，不需要为每档分辨率单独调整。
- 铺满屏幕的布局容器设为 `picking-mode="Ignore"`，这样鼠标在面板之外时可以点到棋盘（`UiRoot.IsPointerOverUi`）。
- 中文字体：随包提供 Noto Sans SC Regular（`Runtime/Resources/StarlineUIFont.ttf`，SIL OFL 协议，见包里的 `ThirdPartyNotices.md`）；万一缺失，退回玩家系统里的中文字体。
- UI Toolkit 通过 EventSystem + Input System 接收输入，`GameApp` 会在场景里没有 EventSystem 时自动创建。

### 界面流程

```
MainMenu ──开始远征──▶ Map ──选战斗节点──▶ Deploy ──开战──▶ Battle ──结果弹窗──┐
   ▲                  │  ▲                                                    │
   │                  │  └──────────── 返回地图 / 选完奖励 ◀── Reward ◀──胜利──┤
   │                  └─选补给站：弹窗二选一（留在 Map）                         │
   │                                                Boss 战失败且有耐久 ──▶ Deploy
   └──────────────── RunEnd ◀──── 通关 / 耐久归零 ────────────────────────────┘
```

| 界面 | 类 | 布局 |
|---|---|---|
| 主菜单 | `MainMenuScreen` | `MainMenu.uxml` |
| 地图（含开场剧情、补给站、放弃远征弹窗） | `MapScreen` + `StarMapView` | `Map.uxml` |
| 布阵 | `DeployScreen` + `DeployController` | `Deploy.uxml` |
| 战斗（含结果弹窗） | `BattleScreen` + `BattleController` | `Battle.uxml` |
| 战利品 | `RewardScreen` | `Reward.uxml` |
| 远征结束 | `RunEndScreen` | `RunEnd.uxml` |

### 新增一个界面

1. 在 `Resources/StarlineUI/` 新建 `Xxx.uxml`，根元素加 `class="screen"` 和 `picking-mode="Ignore"`。
2. 新建 `XxxScreen : UiScreen`，构造函数里 `base(app, "Xxx")`，用 `Find<T>("name")` 和 `Button("name", 回调)` 绑定元素。名字写错会在第一次打开界面时直接报错。
3. 在 `GameApp` 里加一个 `ShowXxx()`，调用 `Show(new XxxScreen(this))`。

## 4. 场景与工程初始化

- 菜单 `Starline > 初始化工程（场景与界面配置）`：
  - 生成 `Assets/Resources/StarlinePanelSettings.asset`（界面缩放设置 + Unity 默认运行时主题 `Assets/UI/StarlineRuntimeTheme.tss`）。
  - 生成 `Assets/Scenes/Main.unity`（只有相机和 `GameApp`），并设为构建列表里唯一的场景。
- 界面配置在第一次打开工程时也会自动生成；缺少它时游戏会用临时设置运行，并在 Console 提示。
- URP 的安装和配置沿用原来的 `UrpInstaller` / `UrpSetup`。

- 角色模型在 `Resources/StarlineModels/<单位 id>.fbx`，由 `tools/models/build_models.py` 生成（见 [art-direction.md](art-direction.md)）。`ModelLibrary` 按单位 id 加载并换上运行时材质，`UnitView` 按魔网接入状态设置发光颜色；没有模型的单位退回几何体占位。

## 5. 里程碑

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M1 | 数值迁移到 JSON 配置表，测试结果与迁移前逐字一致 | ✅ |
| M2 | 客户端重建：GameApp、界面系统、UI Toolkit 全部界面、战斗舞台分层、一键初始化 | ✅ 代码完成，待在 Unity 中验证 |
| M3 | 存档与继续远征、设置界面（音量、画质、语言）、音频框架 | 下一步 |
| M4 | 美术：角色模型（第一幕 15 个单位已由脚本生成并接入）、场景背景、特效、界面美术 | 进行中 |
| M5 | 设计文档的 P3：符文、商店、事件、剧情对话系统 | |
