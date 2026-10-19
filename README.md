# Starline（星线）

单机科幻战略游戏：蜂群切断了连接人类殖民地的超空间航线“星线”，你指挥晨星舰队一段段重新点亮它。战前排兵布阵，用补给网络连接你的舰队，开战前先推演一百场战争，然后看舰队自动交火决出胜负。

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
│   ├── Runtime/Battle/            确定性战斗内核（纯 C#，不依赖 Unity）：配置读取、棋盘、补给网络、战斗、推演、远征
│   ├── Runtime/Client/            Unity 客户端：App 入口、Screens 界面、Gameplay 布阵/战斗逻辑、Stage 3D 舞台、UI
│   ├── Runtime/Resources/
│   │   ├── StarlineData/          配置表（JSON）：舰船、克制、技能、敌方阵容、远征
│   │   └── StarlineUI/            界面布局（UXML）和主题样式（USS）
│   └── Editor/                    编辑器菜单：工程初始化、URP 配置
└── ProjectSettings/
src/Starline.Battle/               用 .NET 编译同一份内核源码（测试与批量模拟用）
tests/Starline.Battle.Tests/       内核测试
docs/                              设计文档与原型图
```

## 在 Unity 中试玩

内容：**第一幕“边境星区”的完整一局远征**（约 15~25 分钟）：

**星图选节点 → 横向布阵、摆放中继信标 → 战前推演 → 自动战斗（可放指挥官技能）→ 战利品三选一（3 艘同名同星舰船自动合成高一星）→ 下一个节点 …… → Boss“残骸要塞”**

1. 用 Unity Hub 安装 **Unity 6000.6.6**（工程按这个版本配置，其他 Unity 6 版本一般也能打开）。
2. Unity Hub 里点 **Add → Add project from disk**，选择仓库里的 `Starline` 文件夹。
   - 如果 Hub 提示编辑器版本不一致，选择你安装的 Unity 6 版本打开即可。
3. 第一次打开时如果提示启用新输入系统（Input System），选 **Yes**，编辑器会重启。
4. **等待 URP 自动配置**：第一次打开时会自动安装 URP（通用渲染管线）包，并在 `Assets/Settings/` 下生成渲染配置，Console 里会依次出现“正在安装 URP”和“已启用 URP 渲染管线”。整个过程需要一两分钟。
5. 菜单栏点 `Starline > 初始化工程（场景与界面配置）`，会生成并打开 `Assets/Scenes/Main.unity`，然后点 **Play**。
   - 以前生成过的 `Assets/Scenes/PlayablePrototype.unity` 已经不用了（里面的脚本已删除），可以直接删掉。

### 中文字体

界面默认使用系统里的中文字体（微软雅黑、苹方等）。如果中文显示成方块，或者准备发布时，到 Google Fonts 下载 **Noto Sans SC**（SIL OFL 开源协议，可商用），把字体文件改名为 `StarlineUIFont.ttf`（或 `.otf`），放进 `Assets/Resources/` 文件夹即可。

### 修改数值

舰船、敌人、技能、星图和剧情文本都在 `Starline/Packages/com.starline.game/Runtime/Resources/StarlineData/` 的 JSON 文件里，改完保存，下次点 Play 就生效。改完建议跑一遍内核测试，看平衡模拟的变化。

### 渲染

- 使用 **URP**，开启 HDR、4 倍抗锯齿、软阴影和后处理：泛光（激光、补给线、爆炸会发光）、ACES 色调映射、暗角、对比度微调。
- 如果自动配置没有生效（画面是粉红色，或者没有发光效果）：
  1. 菜单点 `Starline > 重新配置 URP 渲染`；
  2. 仍然不行的话手动设置：在 Project 窗口右键 `Create > Rendering > URP Asset (with Universal Renderer)`，然后在 `Project Settings > Graphics` 和 `Project Settings > Quality` 里把它设为渲染管线。

### 远征

| 规则 | 说明 |
|---|---|
| 开局 | 补给母舰、2 个中继信标、战列舰 / 护卫舰 / 导弹舰各 1 艘 |
| 星图 | 6 层节点，每次只能去和当前节点相连的下一层节点；悬停节点可以看情报 |
| 节点 | 战斗、精英（更强，奖励必有 ★★ 舰船）、补给站（耐久 +1 或中继信标 +1）、Boss |
| 舰队耐久 | 5 点。普通战失败 −1，跳过该节点、没有奖励；Boss 战失败 −2，可以重打；归零远征结束 |
| 指挥点上限 | 从 12 开始，每过一个战斗节点 +2（输赢都算），最高 24 |
| 合成 | 3 艘同名同星的舰船自动合成 1 艘高一星：★2 耐久 ×1.6、伤害 ×1.5；★3 耐久 ×2.5、伤害 ×2.2 |
| 舰船损失 | 战斗中被击毁的舰船战后全部修复，不会永久失去 |

### 布阵

| 操作 | 方式 |
|---|---|
| 选择要上阵的舰船 | 点左侧编队列表中“后备”的舰船；点“已上阵”的舰船会把它从棋盘上拿起来 |
| 放置 | 左键点我方（左半边）格子；大型舰船以点击的格子为一角占 2×2 |
| 放置中继信标 | 选中“中继信标”后，左键点格子交叉点（会显示绿色小点） |
| 移动 | 不选任何舰船时，左键点已放置的单位把它拿起来，再点新位置 |
| 撤下 | 右键点已放置的单位（补给母舰只能移动，不能撤下） |
| 取消选择 | 右键或 Esc（拿起来的单位会回到原位） |
| 自动补满 | 把后备舰船和剩余信标自动放到合适的位置 |
| 战前推演 | 右侧面板，消耗 1 点情报点，模拟 100 场 |

绿色格子是补给覆盖范围，绿线是补给线。头顶显示“✕ 未接入”的舰船开局就没有补给：弹药打完就停火，护盾也不会恢复。

### 战斗

- 舰船全部自动战斗，可切换 1x / 2x / 4x 或跳过。
- **轨道打击**（补给 4）：点按钮后在战场上左键选落点，右键取消。
- **紧急护盾**（补给 3）：全体恢复一半护盾。
- 补给点数每秒 +1。补给节点被摧毁后，补给线会变红，断开的一侧舰船失去补给。

### 兵种克制

舰载机群 克 重型舰、远程舰、补给节点；点防御舰 克 舰载机；重型舰 克 轻型舰；轻型舰 克 远程舰；远程舰 克 重型舰。

## 运行内核测试

需要 .NET 8 SDK：

```
dotnet run --project tests/Starline.Battle.Tests
```
