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
│             MenuScene：主菜单背后的 3D 夜景（预制体，可编辑）  │
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
│  Replay/     录像录制与回放、录像校验、上阵战力、关卡种子     │
└───────────────┬──────────────────────────────────────────────┘
                │ 读取
┌───────────────▼──────────────────────────────────────────────┐
│ 配置表  Runtime/Resources/StarlineData/*.json                 │
│  units · counters · skills · encounters · expedition · power │
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
| `terrains.json` | 战斗场景（设计文档 5.5）：每章的战场环境和棋盘地块，内核按定点数结算；名字、说明和效果文字在 `Localization/Game.csv`（`tile.<id>.name/description/effect<n>`、`terrain.<id>.*`，effect 按 effects 里的顺序从 1 数） |
| `settings.json` | 设置界面的选项（语言、显示模式、帧率、渲染精度、抗锯齿、阴影）和四档画质预设；只有客户端读取。选项名写 `"#key"`（UI 表）或原样显示的文字 |
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
- 启动语言：命令行 `-language=en` → 设置里保存的语言 → 系统语言 → 简体中文。`GameApp` 把设置里的语言传给 `Loc.Initialize(language)`，它等语言表加载完、用 `Loc.SetLanguage(code)` 切过去并等新语言的表加载完，才显示主菜单。
- 切换语言：设置界面点语言卡片后保存设置并调用 `Loc.SetLanguage`；新语言的表加载完后触发 `Loc.LanguageChanged`，`GameApp` 在下一帧用当前界面的 `UiScreen.Rebuild()` 重建它（设置界面重建后停在语言页），所以文字立即换掉。别的界面不重建（返回 null），离开设置后新建的界面自然是新语言。
- 配置表里给玩家看的选项名（`settings.json` 的预设、显示模式、开 / 关等）写成 `"#key"`，界面用 `Loc.Text(name)` 取 UI 表；不以 `#` 开头的（语言名、FXAA、帧率数字）原样显示。
- 语言表打进安装包（Addressables 本地分组，随 Player 一起构建），不走 CDN；更新随 Steam 版本发布。
- 内核测试会检查：每条都有中文、译文占位符一致、每个角色 / 技能 / 阵容 / 幕都有文本、47 名角色都有 5 段小传、UXML 和客户端代码用到的 key 都在 `UI.csv` 里且没有多余的行。
- 翻译流程、术语对照和英文译名见 [localization.md](localization.md)。

## 3. 界面（UI Toolkit）

- 唯一的 `UIDocument` 由 `UiRoot` 在运行时创建，分三层：角色头顶标签（world-layer）→ 当前界面（screen-layer）→ 弹窗（modal-layer）。
- 每个界面是 `UiScreen` 的子类，布局在 `Resources/StarlineUI/<名字>.uxml`，样式统一在 `Theme.uss`。可以用 Unity 的 UI Builder 直接打开 UXML 调整。
- 设计分辨率 3840 × 2160（4K），16:9 及更宽的屏幕按高度缩放，比 16:9 窄的屏幕（16:10、4:3、拉窄的窗口）按宽度缩放（PanelSettings 的 Expand 模式），保证整张 4K 画板都在屏幕内：Theme.uss 和代码里的像素值都是 4K 像素，1080p 屏幕上整体缩小一半。界面图片素材按 4K 尺寸出图。
- 竖屏（屏幕高大于宽）时设计分辨率换成 2160 × 3840，按屏幕宽度缩放，根元素加上 `portrait` 类；Theme.uss 里用 `.portrait ...` 选择器改竖屏布局（布阵和战斗界面把面板挪到下方）。UiRoot 每帧检查朝向，窗口转向时自动切换（决策 52）。
- 3D 镜头（`Stage/CameraRig`）按“棋盘可见区域”取景：布阵和战斗界面各有一个名为 `board-area` 的空白元素，界面把它在屏幕上的位置告诉镜头，镜头用偏心投影把棋盘放在这块区域正中，并自动算距离让整个棋盘装得下。战斗中拖动旋转、滚轮或双指缩放、`V` 键或“视角”按钮切换预设（决策 53）。
- 分辨率和 3D 渲染精度在设置界面里选择；界面跟随屏幕自动缩放，不需要为每档分辨率单独调整。
- 铺满屏幕的布局容器设为 `picking-mode="Ignore"`，这样鼠标在面板之外时可以点到棋盘（`UiRoot.IsPointerOverUi`）。
- 中文字体：随包提供 Noto Sans SC Regular（`Runtime/Resources/StarlineUIFont.ttf`，SIL OFL 协议，见包里的 `ThirdPartyNotices.md`）；万一缺失，退回玩家系统里的中文字体。
- UI Toolkit 通过 EventSystem + Input System 接收输入，`GameApp` 会在场景里没有 EventSystem 时自动创建。

### 界面流程

```
启动 ──▶ Login ──登录成功、读取最近玩过的存档──▶ MainMenu ◀──使用存档 / 返回── SaveSlots（主菜单“存档”按钮进入）
          ▲                                   │
          └──────────── 退出登录 ─────────────┘
                                  ▲
                                  └──返回 / Esc── Settings（主菜单“设置”按钮进入）
              （还没有存档时，点“开始远征”先弹出“新的指挥官”起名）

MainMenu ──开始远征──▶ Map ──选战斗节点──▶ Deploy ──开战──▶ Battle ──结果弹窗──┐
   ▲                  │  ▲                                                    │
   │                  │  └──────────── 返回地图 / 选完奖励 ◀── Reward ◀──胜利──┤
   │                  └─选补给站：弹窗二选一（留在 Map）                         │
   │                                                Boss 战失败且有耐久 ──▶ Deploy
   └──────────────── RunEnd ◀──── 通关 / 耐久归零 ────────────────────────────┘

Battle 结果弹窗 ──回放本场──▶ Battle（回放，录像只在内存里）──结束──▶ 结果弹窗原来的下一步
```

| 界面 | 类 | 布局 |
|---|---|---|
| 登录（UOS 账号、开发者登录） | `LoginScreen` | `Login.uxml` |
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

- 主菜单和存档界面背后是实时 3D 场景：灯塔城的源晶塔亮着，远征军（卡通角色，播放待机动画，脚下有发光环）围在旁边，往远处的法力水晶只亮了第一座，最远处有一座燃着绿火的亡灵祭坛；天上是星星和月亮，远处一圈圆润的山丘。
- 场景是预制体 `Assets/Resources/StarlineMenuScene.prefab`，直接提交在工程里，在 Unity 里双击就能编辑，改完提交即可。没有编辑器脚本生成或覆盖它；预制体缺失时运行时只报错并显示空夜空。
- 预制体里每样东西都是普通物体，用移动、旋转、缩放工具摆位置，在 Inspector 里改参数：

| 组件 | 用在 | 可以改 |
|---|---|---|
| `MenuSceneRoot` | 根物体 | 镜头视角、漂移幅度和速度、夜间环境光、天空颜色 |
| `Camera Anchor`（子物体） | 镜头位置 | 选中后用 `GameObject > Align With View` 对准场景视图；它上面的 Camera 只用来在编辑器里预览画面 |
| `MenuProp` | 每个部队 / 建筑 | 单位 id 和星级（换模型）、脚下光环半径、发光颜色和强度、呼吸闪烁、上下浮动；有动画的角色运行时播放待机 |
| `MenuGlowLine` | 魔网光带 | 起点、终点物体、两端高度、颜色、宽度 |
| `MenuSky` / `MenuMountains` | 星空 / 山丘 | 数量、随机种子、距离、高度、宽度（山丘还有厚度）、颜色 |
| `MenuTint` | 地面、月亮 | 颜色、是否自发光 |
| `Moonlight`、`源晶塔灯光` | 普通 Light | 颜色、强度、方向；`MenuLightPulse` 让塔光缓慢呼吸 |

- 模型、网格和材质都是运行时（编辑器里也会）按参数生成的，不会存进预制体，所以预制体文件很小，改了参数立刻看到效果。
- 场景放在离棋盘很远的地方（z = 400），界面的 `UsesMenuScene` 为 true 时显示，期间镜头跟着 `Camera Anchor`，棋盘的太阳光和轮廓光关掉（`SceneLook.SetNight`）；离开时镜头回到布阵位置。
- 界面上用两张小渐变贴图（`StarlineUI/FadeLeft.png`、`FadeBottom.png`）压暗左侧和底部，保证文字清楚；它们的 .meta 设成不重复平铺、无 mipmap，避免边缘出现细线。

### 登录

- 启动后先进登录界面（决策 62），没登录时 `GameApp.ShowMainMenu` 也会退回这里。界面上每种登录方式一个按钮，登录中按钮锁住，“取消”可以放弃等待（第三方面板被关掉却没有回调时用）；失败时显示原因，再点一次就是重试。
- 登录方式是 `ILoginProvider`（`Runtime/Client/Online/`），由 `AccountService` 统一管理：`Register` 注册、`SignIn` 登录并记住这次的方式（PlayerPrefs `StarlineLastLogin`，下次启动自动用它登录）、`SignOut` 退出并忘掉。登录结果是 `AuthSession`（登录方式、账号 id、显示名、游戏服务器令牌）。
- `DevLogin`：只在编辑器和开发版注册，填任意账号名，不需要密码，名字记在 PlayerPrefs `StarlineDevAccount`。有游戏服务器时对应服务端的 `dev` 登录，只有设了 `STARLINE_DEV_LOGIN=1` 的开发用 UOS 应用接受。
- `UosPassportLogin`（`Assets/Scripts/Online/`）：UOS Passport 登录，用 SDK 自带的登录面板 `PassportUI`（`InvokeLoginManually`，点按钮才弹出）。放在 Assets 里是为了不用在 asmdef 里写 SDK 的程序集名；它启动时自己注册到 `AccountService`。整个文件包在 `#if STARLINE_UOS_PASSPORT` 里，没装 SDK 时工程照常编译，登录界面只显示开发者登录。
- 有游戏服务器时，`AccountService.SignIn` 在登录方式成功后再登录服务器（`auth.login`）：云函数校验 Passport 令牌（开发者登录则交账号名），返回账号 id 和会话令牌，`AuthSession.AccessToken` 就是这个令牌。服务器拒绝或连不上时登录界面显示“无法登录游戏服务器：原因”，登录方式本身保持登录，再点一次只重试服务器。没有游戏服务器时 `AccessToken` 为空。
- 新增一种登录方式（Steam、微信小游戏）：写一个 `ILoginProvider`，在 `LoginMethods` 加 id，在 `Login.uxml` 加按钮、`LoginScreen` 里绑定，`LoginScreen.MethodName` 加名字。

### 存档位

- 登录后进主菜单，自动读取最近玩过的存档；主菜单显示当前指挥官，“存档”按钮进入存档界面切换、新建或删除。
- 一共 3 个存档位，登录后由 `SaveRepository` 一次读进内存（`GameApp.LoadSaves`），界面直接读它：
  - **有游戏服务器**（`server.json` 填了地址，或装了 Func Stateless SDK、定义了 `STARLINE_UOS_FUNC`）：`ServerSaveRepository`，存档在 UOS 上（server/README.md）。新建、删除要等服务器回复，失败时弹窗里显示原因；游戏时长和远征次数按增量上报，最多一分钟报一次（远征次数变了、切到后台、退出时马上报）。服务器说账号在别处登录或会话过期时（`GameServerClient.SessionLost`），回到登录界面并显示原因。
  - **没有游戏服务器**：`LocalSaveRepository`，每个存档是 `Application.persistentDataPath/saves/slot<N>.json`（`ProfileStore`，先写临时文件再替换，崩溃不会留下半个存档）。
- 网络层是 `GameServerClient`（`Runtime/Client/Online/`）：把 `ServerRequest` 转成 JSON 交给 `IServerTransport`，修改类请求自动带幂等键、网络错误时用同一个键重发两次。两种实现：`HttpGameServer`（`StarlineData/server.json` 填了地址或带启动参数 `-server <地址>` 时启用，连 UOS 服务器程序镜像或本机的 `starline-server`，优先，正式部署时 `url` 留空）和云函数版 `UosGameServer`（`Assets/Scripts/Online/`，和 `UosPassportLogin` 一样放在 Assets 里、用宏 `STARLINE_UOS_FUNC` 开关，正式用这个，决策 65）。
- 现在存档只记指挥官名字、游戏时长、最近游玩时间和远征次数（`CommanderProfile`）；远征进度、角色收集在 M3 加进同一个文件。
- 游戏时长在 `GameApp.Update` 里累加，切换界面、开始和结束远征、退出游戏时保存（服务器版按上面的节奏上报）。

### 设置

- 设置不分存档，所有存档共用，存在 `Application.persistentDataPath/settings.json`（`GameSettings` / `SettingsStore`）。启动时读取并应用（`SettingsApplier.ApplyAll`）；文件缺失、损坏或含有配置表里已没有的选项时，对应项回到默认值。
- 改动立即生效并保存。显示模式和分辨率改完要在 15 秒内点“保留”，否则自动还原（防止黑屏后无法操作）；在编辑器里这两项不生效，由 Game 视图决定大小。
- 语言：首次启动跟随系统语言，没有对应翻译时用简体中文。可选简体中文和 English（英文是初稿，卡片上标“初稿 · Draft”，没翻译的条目显示中文）；点一下立即切换并保存，下次启动沿用（见 2.1 节）。
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
