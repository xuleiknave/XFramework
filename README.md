# XFramework

单机科幻战略游戏（项目代号，暂名《星火远征》）：指挥人类舰队从保卫地球开始，逐级突破“地球级 → 太阳系级 → 宇宙级”。关卡战役加剧情，随机远征加收集养成，可暂停的即时战斗中运用补给与威慑取胜。

- 游戏设计文档：[docs/game-design.md](docs/game-design.md)
- 确定性仿真内核架构参考：[docs/architecture.md](docs/architecture.md)

## 目录结构

```
unity/com.xframework.starfire/   Unity 包（可添加到任意 Unity 6.3 工程）
├── Runtime/Battle/              确定性战斗内核（纯 C#，不依赖 Unity）：棋盘、补给网络、战斗、推演
├── Runtime/Client/              Unity 显示与操作（可玩原型）
└── Editor/                      编辑器菜单
src/XFramework.Battle/           用 .NET 编译同一份内核源码（测试与批量模拟用）
tests/XFramework.Battle.Tests/   内核测试
```

## 可玩原型：在 Unity 中试玩

内容：一场“精英战：沙暴伏击”，完整流程是 **横向布阵 → 摆放中继站 → 战前推演 → 自动战斗（可放指挥官技能）→ 结算 → 调整布阵再打**。

1. 用 Unity Hub 安装 **Unity 6.3 LTS**，新建一个 **Universal 3D** 模板工程（工程放在本仓库之外的任意位置即可）。
2. 打开 `Window > Package Manager`，点左上角 `+` → `Install package from disk...`，选择本仓库的 `unity/com.xframework.starfire/package.json`。
3. 如果提示是否启用新输入系统（Input System），选 **Yes** 并重启编辑器。
4. 菜单栏点 `XFramework > 创建可玩原型场景`，然后点 **Play**。

### 布阵

| 操作 | 方式 |
|---|---|
| 选择要上阵的兵器 | 点左侧编队列表 |
| 放置 | 左键点我方（左半边）格子；大型兵器以点击的格子为一角占 2×2 |
| 放置中继站 | 选中“中继站”后，左键点格子交叉点（会显示绿色小点） |
| 移动 | 不选任何兵器时，左键点已放置的单位把它拿起来，再点新位置 |
| 撤下 | 右键点已放置的单位（补给母舰只能移动，不能撤下） |
| 取消选择 | 右键或 Esc |
| 战前推演 | 右侧面板，消耗 1 点情报点，模拟 100 场 |

绿色格子是补给覆盖范围，绿线是补给线。头顶显示“✕ 未接入”的兵器开局就没有补给：弹药打完就停火，护盾也不会恢复。

### 战斗

- 兵器全部自动战斗，可切换 1x / 2x / 4x 或跳过。
- **轨道打击**（补给 4）：点按钮后在战场上左键选落点，右键取消。
- **紧急护盾**（补给 3）：全体恢复一半护盾。
- 补给点数每秒 +1。补给节点被摧毁后，补给线会变红，断开的一侧兵器失去补给。

### 兵种克制

空中 克 重型、远程；防空 克 空中；重型 克 轻型；轻型 克 远程；远程 克 重型。

## 运行内核测试

需要 .NET 8 SDK：

```
dotnet run --project tests/XFramework.Battle.Tests
```
