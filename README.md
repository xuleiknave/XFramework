# XFramework

单机科幻战略游戏（项目代号，暂名《星火远征》）：指挥人类舰队从保卫地球开始，逐级突破“地球级 → 太阳系级 → 宇宙级”。关卡战役加剧情，随机远征加收集养成，可暂停的即时战斗中运用补给与威慑取胜。

- 游戏设计文档：[docs/game-design.md](docs/game-design.md)
- 确定性仿真内核架构参考：[docs/architecture.md](docs/architecture.md)

## 目录结构

```
unity/com.xframework.starfire/   Unity 包（可添加到任意 Unity 6.3 工程）
├── Runtime/Battle/              确定性战斗内核（纯 C#，不依赖 Unity）
├── Runtime/Client/              Unity 显示与操作（P0 原型）
└── Editor/                      编辑器菜单
src/XFramework.Battle/           用 .NET 编译同一份内核源码（测试与批量模拟用）
tests/XFramework.Battle.Tests/   内核测试
```

## P0 战斗原型：在 Unity 中试玩

1. 用 Unity Hub 安装 **Unity 6.3 LTS**，新建一个 **Universal 3D** 模板工程（工程放在本仓库之外的任意位置即可）。
2. 打开 `Window > Package Manager`，点左上角 `+` → `Install package from disk...`，选择本仓库的 `unity/com.xframework.starfire/package.json`。
3. 如果提示是否启用新输入系统（Input System），选 **Yes** 并重启编辑器。
4. 菜单栏点 `XFramework > 创建 P0 战斗原型场景`，然后点 **Play**。

操作方式：

| 操作 | 按键 |
|---|---|
| 选择 / 框选我方单位 | 左键点击 / 拖动（按住 Shift 追加） |
| 移动（沿途开火） | 右键点击地面 |
| 集火 | 右键点击敌方单位 |
| 攻击移动（遇敌停下战斗） | 按住 A + 右键点击地面 |
| 停止 | S |
| 编组 / 选组 | Ctrl + 数字 / 数字 |
| 暂停（暂停中可下指令） | 空格 |
| 切换速度 0.5x / 1x / 2x | Tab |
| 镜头 | 方向键、按住鼠标中键拖动、滚轮缩放 |

P0 兵种克制：**战机群 克 战列舰，战列舰 克 护卫舰，护卫舰 克 战机群**。

## 运行内核测试

需要 .NET 8 SDK：

```
dotnet run --project tests/XFramework.Battle.Tests
```
