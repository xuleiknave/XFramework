# Starline（星线）

单机科幻战略游戏：指挥人类舰队从保卫地球开始，一路打进太阳系，再穿越星门征战宇宙。战前排兵布阵，用补给网络连接你的舰队，开战前先推演一百场战争，然后看自动战斗决出胜负。

- 游戏设计文档：[docs/game-design.md](docs/game-design.md)
- 界面原型图：[docs/prototype/](docs/prototype/)
- 确定性仿真内核架构参考：[docs/architecture.md](docs/architecture.md)

## 目录结构

```
Starline/                          Unity 工程（用 Unity Hub 直接打开这个文件夹）
├── Assets/
├── Packages/manifest.json
├── Packages/com.starline.game/    游戏代码（嵌入式包）
│   ├── Runtime/Battle/            确定性战斗内核（纯 C#，不依赖 Unity）：棋盘、补给网络、战斗、推演
│   ├── Runtime/Client/            Unity 显示与操作（可玩原型）
│   └── Editor/                    编辑器菜单
└── ProjectSettings/
src/Starline.Battle/               用 .NET 编译同一份内核源码（测试与批量模拟用）
tests/Starline.Battle.Tests/       内核测试
docs/                              设计文档与原型图
```

## 可玩原型：在 Unity 中试玩

内容：一场“精英战：沙暴伏击”，完整流程是 **横向布阵 → 摆放中继站 → 战前推演 → 自动战斗（可放指挥官技能）→ 结算 → 调整布阵再打**。

1. 用 Unity Hub 安装 **Unity 6.3 LTS**。
2. Unity Hub 里点 **Add → Add project from disk**，选择仓库里的 `Starline` 文件夹。
   - 如果 Hub 提示编辑器版本不一致，选择你安装的 6.3 LTS 版本打开即可。
3. 第一次打开时如果提示启用新输入系统（Input System），选 **Yes**，编辑器会重启。
4. 菜单栏点 `Starline > 创建可玩原型场景`，然后点 **Play**。

工程使用 Unity 内置渲染管线，不需要另外安装 URP。

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
dotnet run --project tests/Starline.Battle.Tests
```
