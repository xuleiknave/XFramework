# 客户端架构

Starline 正式工程的代码结构。设计以 [game-design.md](game-design.md) 为准，这里只讲代码怎么组织。

## 1. 分层

```
┌──────────────────────────────────────────────────────────────┐
│ Unity 客户端  Starline.Client（Runtime/Client）               │
│                                                              │
│  App/       GameApp：入口、界面切换、持有当前存档和远征        │
│  Profiles/  存档位：指挥官名字、游戏时长、远征次数（本地 JSON）│
│  Screens/   每个界面一个类 + 一个 UXML 布局                    │
│  Gameplay/  DeployController、BattleController：界面背后的逻辑 │
│  Stage/     BattleStage：3D 棋盘、角色模型、特效、镜头         │
│             MenuScene：主菜单和存档界面背后的 3D 夜景          │
│  UI/        UiRoot（UI Toolkit 根）、主题、字体、角色头顶标签   │
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
| `skills.json` | 指挥官技能；星级倍率 |
| `encounters.json` | 敌方阵容：共用的亡灵祭坛底座 + 各自的单位 |
| `settings.json` | 设置界面的选项（语言、显示模式、帧率、渲染精度、抗锯齿、阴影）和四档画质预设；只有客户端读取 |
| `expedition.json` | 远征规则（耐久、指挥点、水晶上限）、每一幕的开局远征军、地图层、敌人分档、奖池和剧情文本 |

- 数值写成十进制小数（最多 4 位），内核按分数精确转换成定点数，同样的数据在任何电脑上算出同样的结果。
- 数据出错时会报告文件名、行号和字段路径，例如 `units.json 第 12 行：应为 '}'`、`$.units[3].hp：缺少字段`。
- Unity 通过 `Resources.Load<TextAsset>` 读取，测试项目直接读同一批文件，不存在两份数据。
- 以后可以加一个从 Excel 导出 JSON 的脚本，格式不用改。

## 3. 界面（UI Toolkit）

- 唯一的 `UIDocument` 由 `UiRoot` 在运行时创建，分三层：角色头顶标签（world-layer）→ 当前界面（screen-layer）→ 弹窗（modal-layer）。
- 每个界面是 `UiScreen` 的子类，布局在 `Resources/StarlineUI/<名字>.uxml`，样式统一在 `Theme.uss`。可以用 Unity 的 UI Builder 直接打开 UXML 调整。
- 设计分辨率 3840 × 2160（4K），按屏幕高度缩放：Theme.uss 和代码里的像素值都是 4K 像素，1080p 屏幕上整体缩小一半。界面图片素材按 4K 尺寸出图。
- 分辨率和 3D 渲染精度在设置界面里选择；界面跟随屏幕自动缩放，不需要为每档分辨率单独调整。
- 铺满屏幕的布局容器设为 `picking-mode="Ignore"`，这样鼠标在面板之外时可以点到棋盘（`UiRoot.IsPointerOverUi`）。
- 中文字体：随包提供 Noto Sans SC Regular（`Runtime/Resources/StarlineUIFont.ttf`，SIL OFL 协议，见包里的 `ThirdPartyNotices.md`）；万一缺失，退回玩家系统里的中文字体。
- UI Toolkit 通过 EventSystem + Input System 接收输入，`GameApp` 会在场景里没有 EventSystem 时自动创建。

### 界面流程

```
启动 ──读取最近玩过的存档──▶ MainMenu ◀──使用存档 / 返回── SaveSlots（主菜单“存档”按钮进入）
                                  ▲
                                  └──返回 / Esc── Settings（主菜单“设置”按钮进入）
              （还没有存档时，点“开始远征”先弹出“新的指挥官”起名）

MainMenu ──开始远征──▶ Map ──选战斗节点──▶ Deploy ──开战──▶ Battle ──结果弹窗──┐
   ▲                  │  ▲                                                    │
   │                  │  └──────────── 返回地图 / 选完奖励 ◀── Reward ◀──胜利──┤
   │                  └─选补给站：弹窗二选一（留在 Map）                         │
   │                                                Boss 战失败且有耐久 ──▶ Deploy
   └──────────────── RunEnd ◀──── 通关 / 耐久归零 ────────────────────────────┘
```

| 界面 | 类 | 布局 |
|---|---|---|
| 存档（三个存档位，含删除存档弹窗） | `SaveSlotsScreen` | `SaveSlots.uxml` |
| 新建指挥官弹窗（主菜单和存档界面共用） | `NewCommanderDialog` | `NewCommander.uxml` |
| 主菜单 | `MainMenuScreen` | `MainMenu.uxml` |
| 设置（语言、画质；显示设置确认弹窗） | `SettingsScreen` | `Settings.uxml` |
| 地图（含开场剧情、补给站、放弃远征弹窗） | `MapScreen` + `StarMapView` | `Map.uxml` |
| 布阵 | `DeployScreen` + `DeployController` | `Deploy.uxml` |
| 战斗（含结果弹窗） | `BattleScreen` + `BattleController` | `Battle.uxml` |
| 战利品 | `RewardScreen` | `Reward.uxml` |
| 远征结束 | `RunEndScreen` | `RunEnd.uxml` |

### 主菜单夜景

- 主菜单和存档界面背后是实时 3D 场景 `MenuScene`：灯塔城的源晶塔亮着，远征军围在旁边，往远处的法力水晶只亮了第一座，其余是暗的，最远处有一座燃着绿火的亡灵祭坛；天上是星星和月亮，远处一圈暗色山影。全部用现有部队模型和代码生成的网格拼出来，不需要额外美术资源。
- 场景放在离棋盘很远的地方（z = 400），界面的 `UsesMenuScene` 为 true 时显示，此时镜头由它控制（缓慢漂移），灯光切到夜间（`SceneLook.SetNight`）；离开时镜头回到布阵位置。
- 界面上用两张小渐变贴图（`StarlineUI/FadeLeft.png`、`FadeBottom.png`）压暗左侧和底部，保证文字清楚；它们的 .meta 设成不重复平铺、无 mipmap，避免边缘出现细线。

### 存档位

- 启动后直接进主菜单，自动读取最近玩过的存档；主菜单显示当前指挥官，“存档”按钮进入存档界面切换、新建或删除。
- 一共 3 个存档位，每个存档是 `Application.persistentDataPath/saves/slot<N>.json`（`ProfileStore`，先写临时文件再替换，崩溃不会留下半个存档）。
- 现在存档只记指挥官名字、游戏时长、最近游玩时间和远征次数（`CommanderProfile`）；远征进度、角色收集在 M3 加进同一个文件。
- 游戏时长在 `GameApp.Update` 里累加，切换界面、开始和结束远征、退出游戏时保存。
- 单机买断制，没有账号；以后接入 Steam 时，云存档同步这个 saves 文件夹即可。

### 设置

- 设置不分存档，所有存档共用，存在 `Application.persistentDataPath/settings.json`（`GameSettings` / `SettingsStore`）。启动时读取并应用（`SettingsApplier.ApplyAll`）；文件缺失、损坏或含有配置表里已没有的选项时，对应项回到默认值。
- 改动立即生效并保存。显示模式和分辨率改完要在 15 秒内点“保留”，否则自动还原（防止黑屏后无法操作）；在编辑器里这两项不生效，由 Game 视图决定大小。
- 语言：首次启动跟随系统语言，没有对应翻译时用简体中文。现在只有简体中文可选，English 显示“翻译制作中”；本地化（界面和剧情文本按语言切换）还没做，`GameSettings.language` 已经存好，接入时读取它即可。
- 画质：四档预设（低 / 中 / 高 / 极致）一次设置渲染精度、抗锯齿、阴影和泛光；单独改某一项后预设显示“自定义”。垂直同步、帧率上限、显示模式和分辨率不属于预设。默认“高”、全屏、原生分辨率、垂直同步开。
- 渲染管线：启动时复制一份工程的 URP 资源再修改（渲染精度、MSAA、阴影距离和分辨率），在编辑器里试玩不会改动资源文件；FXAA / SMAA 设在主摄像机上，阴影关闭时关掉主光源阴影，泛光开关控制 `SceneLook` 的 Bloom。
- 声音和操作两页先占位（置灰“后续开放”），音量随音频框架一起做。

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
| M3 | 存档与继续远征、设置界面（音量、画质、语言）、音频框架 | 进行中：存档位、新建指挥官、设置界面的语言和画质已完成 |
| M4 | 美术：角色模型（第一幕 15 个单位已由脚本生成并接入）、场景背景、特效、界面美术 | 进行中 |
| M5 | 设计文档的 P3：符文、商店、事件、剧情对话系统 | |
