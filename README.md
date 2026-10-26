# Starline（星线）

奇幻战略游戏（Steam 买断制，需联网登录）：亡灵吸干了连接人类城邦的法力水晶网络“星线”，你率领晨星远征军一段段重新点亮它。战前排兵布阵，用魔网连接你的角色，开战前先推演一百场战争，然后看远征军自动交火决出胜负。

- 游戏设计文档：[docs/game-design.md](docs/game-design.md)
- 界面原型图：[docs/prototype/](docs/prototype/)
- 客户端架构（代码结构、配置表、界面流程）：[docs/client-architecture.md](docs/client-architecture.md)
- 确定性仿真内核架构参考：[docs/architecture.md](docs/architecture.md)
- 决策记录（定了什么、为什么）：[docs/decisions.md](docs/decisions.md)
- 完整开发对话：[docs/conversation-log.md](docs/conversation-log.md)

## 目录结构

```
Starline/                          Unity 工程（用 Unity Hub 直接打开这个文件夹）
├── Assets/
├── Packages/manifest.json
├── Packages/com.starline.game/    游戏代码（嵌入式包）
│   ├── Runtime/Battle/            确定性战斗内核（纯 C#，不依赖 Unity）：配置读取、棋盘、魔网、战斗、推演、远征
│   ├── Runtime/Client/            Unity 客户端：App 入口、Screens 界面、Gameplay 布阵/战斗逻辑、Stage 3D 舞台、UI
│   ├── Runtime/Resources/
│   │   ├── StarlineData/          配置表（JSON）：角色、克制、技能、敌方阵容、远征
│   │   └── StarlineUI/            界面布局（UXML）和主题样式（USS）
│   ├── Localization/              语言表（CSV）：UI.csv 界面文字、Game.csv 角色名/剧情/消息
│   └── Editor/                    编辑器菜单：工程初始化、URP 配置、导入语言表
└── ProjectSettings/
src/Starline.Battle/               用 .NET 编译同一份内核源码（测试与批量模拟用）
tests/Starline.Battle.Tests/       内核测试
docs/                              设计文档与原型图
```

## 在 Unity 中试玩

内容：**第一幕“边境行省”的完整一局远征**（约 15~25 分钟）：

**地图选节点 → 横向布阵、摆放法力水晶 → 战前推演 → 自动战斗（可放指挥官技能）→ 战利品三选一（3 名同名同星角色自动合成高一星）→ 下一个节点 …… → Boss“骸骨要塞”**

1. 用 Unity Hub 安装 **Unity 6000.6.6**（工程按这个版本配置，其他 Unity 6 版本一般也能打开）。
2. Unity Hub 里点 **Add → Add project from disk**，选择仓库里的 `Starline` 文件夹。
   - 如果 Hub 提示编辑器版本不一致，选择你安装的 Unity 6 版本打开即可。
3. 第一次打开时如果提示启用新输入系统（Input System），选 **Yes**，编辑器会重启。
4. **等待 URP 自动配置**：第一次打开时会自动安装 URP（通用渲染管线）包，并在 `Assets/Settings/` 下生成渲染配置，Console 里会依次出现“正在安装 URP”和“已启用 URP 渲染管线”。整个过程需要一两分钟。
5. 菜单栏点 `Starline > 初始化工程（场景与界面配置）`，会生成语言表（`Assets/Localization/`，同时创建 Addressables 配置）并生成、打开 `Assets/Scenes/Main.unity`，然后点 **Play**。
   - 第一次打开时 Unity 会安装 Localization 和 Addressables 包。
   - 测试其他语言：Play 前在 Game 视图上方的 Localization 下拉框里切换 Locale，或者打包后加命令行参数 `-language=en`。
   - 以前生成过的 `Assets/Scenes/PlayablePrototype.unity` 已经不用了（里面的脚本已删除），可以直接删掉。

### 中文字体

界面字体是随工程提供的 **Noto Sans SC Regular**（`Runtime/Resources/StarlineUIFont.ttf`，SIL OFL 开源协议，可免费用于商业游戏）。想换字体，用新字体文件替换它即可（文件名不变）。发布时要把 `ThirdPartyNotices.md` 和 `Licenses/` 里的许可证随游戏一起提供。

### 修改数值

角色、敌人、技能和地图的数值在 `Starline/Packages/com.starline.game/Runtime/Resources/StarlineData/` 的 JSON 文件里，改完保存，下次点 Play 就生效。名字、说明和剧情等文字在 `Packages/com.starline.game/Localization/*.csv`，保存后 Unity 自动重新导入。改完建议跑一遍内核测试，看平衡模拟的变化。

### 渲染

- 使用 **URP**，开启 HDR、4 倍抗锯齿、软阴影和后处理：泛光（法术、魔网连线、爆炸会发光）、ACES 色调映射、暗角、对比度微调。
- 如果自动配置没有生效（画面是粉红色，或者没有发光效果）：
  1. 菜单点 `Starline > 重新配置 URP 渲染`；
  2. 仍然不行的话手动设置：在 Project 窗口右键 `Create > Rendering > URP Asset (with Universal Renderer)`，然后在 `Project Settings > Graphics` 和 `Project Settings > Quality` 里把它设为渲染管线。

### 远征

| 规则 | 说明 |
|---|---|
| 开局 | 源晶塔、2 个法力水晶、重甲骑士 / 游侠 / 奥术法师各 1 支 |
| 地图 | 6 层节点，每次只能去和当前节点相连的下一层节点；悬停节点可以看情报 |
| 节点 | 战斗、精英（更强，奖励必有 ★★ 角色）、补给站（耐久 +1 或法力水晶 +1）、Boss |
| 远征军耐久 | 5 点。普通战失败 −1，跳过该节点、没有奖励；Boss 战失败 −2，可以重打；归零远征结束 |
| 指挥点上限 | 从 12 开始，每过一个战斗节点 +2（输赢都算），最高 24 |
| 合成 | 3 名同名同星的角色自动合成 1 支高一星：★2 耐久 ×1.6、伤害 ×1.5；★3 耐久 ×2.5、伤害 ×2.2 |
| 角色损失 | 战斗中倒下的角色战后全部恢复，不会永久失去 |

### 布阵

| 操作 | 方式 |
|---|---|
| 选择要上阵的角色 | 点左侧编队列表中“后备”的角色；点“已上阵”的角色会把它从棋盘上拿起来 |
| 放置 | 左键点我方（下半边）格子；大型单位以点击的格子为一角占 2×2 |
| 放置法力水晶 | 选中“法力水晶”后，左键点格子交叉点（会显示绿色小点） |
| 移动 | 不选任何角色时，左键点已放置的单位把它拿起来，再点新位置 |
| 撤下 | 右键点已放置的单位（源晶塔只能移动，不能撤下） |
| 取消选择 | 右键或 Esc（拿起来的单位会回到原位） |
| 自动补满 | 把后备角色和剩余水晶自动放到合适的位置 |
| 战前推演 | 右侧面板，消耗 1 点情报点，模拟 100 场 |

绿色格子是魔网覆盖范围，绿线是魔网连线。头顶显示“✕ 未接入”的角色开局就没有魔网：法力用完就停手，护盾也不会恢复。

### 战斗

- 角色全部自动战斗，可切换 1x / 2x / 4x 或跳过。
- **视角**：在战场上按住左键（或中键、单指）拖动旋转镜头，滚轮或双指缩放；点顶栏“视角”或按 V 切换默认、俯视、侧面、敌方视角，点“复位”回到默认。
- 战场竖向摆放（我方在下、敌方在上）。窗口比高宽还高时界面自动换成竖屏布局，可以把 Game 视图设成 1080×1920 试竖屏。
- **陨星术**（法术点数 4）：点按钮后在战场上左键选落点，右键取消。
- **守护结界**（法术点数 3）：全体恢复一半护盾。
- 法术点数每秒 +1。魔网节点被摧毁后，魔网连线会变红，断开的一侧角色失去魔网。

### 兵种克制

狮鹫骑士 克 重装单位、远程单位、魔网节点；弩手 克 狮鹫；重装单位 克 轻装单位；轻装单位 克 远程单位；远程单位 克 重装单位。

## 运行内核测试

需要 .NET 8 SDK：

```
dotnet run --project tests/Starline.Battle.Tests
```

重新生成数值平衡表 [docs/balance-sheet.md](docs/balance-sheet.md)（从配置表汇总数值，并跑约 6000 场纯逻辑对战）：

```
dotnet run --project tests/Starline.Battle.Tests -- --balance
```
