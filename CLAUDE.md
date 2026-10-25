# Starline（星线）

单机奇幻战略游戏（Steam 买断制）。个人开发者与 Claude 合作开发。开发者用中文交流，回复请用中文。

## 先读这些

- `docs/game-design.md`：游戏设计文档（以它为准）
- `docs/decisions.md`：关键决策和理由，改设计前先看
- `docs/conversation-log.md`：此前的完整开发对话（开发者要求“把对话迁移到项目里”时，用 `python3 tools/export_conversation.py ~/.claude/projects/<项目目录>/*.jsonl` 重新导出）
- `docs/story.md`：故事背景、五个阵营、三幕九章、角色关系
- `docs/art-direction.md`：美术方向、色板、部队模型规格和生成流程
- `docs/client-architecture.md`：客户端代码结构、配置表、界面流程、里程碑
- `README.md`：目录结构、在 Unity 中试玩的步骤、操作说明

## 结构

- `Starline/`：Unity 6 工程（6000.6.6，URP）。游戏代码在嵌入式包 `Starline/Packages/com.starline.game`：
  - `Runtime/Battle/`：纯 C# 内核，不依赖 Unity（定点数、确定性随机数）。命名空间 `Starline.Battle`；远征逻辑在 `Expedition/`，命名空间 `Starline.Expedition`
  - `Runtime/Resources/StarlineData/`：JSON 配置表（所有数值和剧情文本），内核通过 `GameData` 读取
  - `Runtime/Resources/StarlineUI/`：UI Toolkit 布局（UXML）和主题（Theme.uss）
  - `Runtime/Client/`：Unity 客户端。`App/GameApp` 入口与界面切换；`Screens/` 每个界面一个类；`Gameplay/` 布阵和战斗逻辑；`Stage/` 3D 棋盘、模型、特效；`UI/` UiRoot、字体、头顶标签
  - `Editor/`：菜单 `Starline > 初始化工程（场景与界面配置）`、URP 自动配置
- `tools/models/build_models.py`：用 Blender 脚本生成部队模型，输出到 `Runtime/Resources/StarlineModels`（见 `docs/art-direction.md`）
- `tools/music/compose.py`：程序化生成背景音乐，输出到 `Runtime/Resources/StarlineMusic`，选曲表 `music.json`（见 `docs/music.md`）
- `src/Starline.Battle/`：用 .NET 编译同一份内核源码
- `tests/Starline.Battle.Tests/`：无依赖的测试程序，包含自动玩家平衡模拟

## 命令

```
dotnet run --project tests/Starline.Battle.Tests   # 内核测试，最后一行应是“全部通过”
```

云端环境没有 Unity 编辑器，客户端代码无法实际运行，只能靠测试和类型检查（UXML 至少要检查是合法的 XML），改动后要提醒开发者在本地 Unity 中试玩。

## 约定

- 内核必须保持确定性：不用 float、不用 System.Random、不依赖字典遍历顺序；战斗逻辑放内核，Client 只做表现
- 数值和文本放 JSON 配置表，不要写死在代码里
- 界面用 UI Toolkit：布局写 UXML、样式写 Theme.uss，C# 只负责绑定和动态内容；铺满屏幕的容器设 `picking-mode="Ignore"`
- 内核代码目标 netstandard2.1 / C# 9（Unity 6 支持的版本）
- 调整数值后跑测试，看平衡模拟的输出
- 设计变更同步更新 `docs/game-design.md`（重大决定补进 `docs/decisions.md`）
- 用户可见文本用中文，术语与设计文档一致（部队、法力水晶、源晶塔、远征军耐久等）
