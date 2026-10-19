# Starline（星线）

单机科幻战略游戏（Steam 买断制）。个人开发者与 Claude 合作开发。开发者用中文交流，回复请用中文。

## 先读这些

- `docs/game-design.md`：游戏设计文档（以它为准）
- `docs/decisions.md`：关键决策和理由，改设计前先看
- `docs/conversation-log.md`：此前的完整开发对话
- `README.md`：目录结构、在 Unity 中试玩的步骤、操作说明

## 结构

- `Starline/`：Unity 6 工程（6000.6.6，URP）。游戏代码在嵌入式包 `Starline/Packages/com.starline.game`：
  - `Runtime/Battle/`：纯 C# 内核，不依赖 Unity（定点数、确定性随机数）。命名空间 `Starline.Battle`；远征逻辑在 `Expedition/`，命名空间 `Starline.Expedition`
  - `Runtime/Client/`：Unity 表现层（IMGUI 界面、基本几何体模型），`GameController` 驱动整局流程
  - `Editor/`：菜单 `Starline > 创建可玩原型场景`、URP 自动配置
- `src/Starline.Battle/`：用 .NET 编译同一份内核源码
- `tests/Starline.Battle.Tests/`：无依赖的测试程序，包含自动玩家平衡模拟

## 命令

```
dotnet run --project tests/Starline.Battle.Tests   # 内核测试，最后一行应是“全部通过”
```

云端环境没有 Unity 编辑器，客户端代码无法实际运行，只能靠测试和类型检查，改动后要提醒开发者在本地 Unity 中试玩。

## 约定

- 内核必须保持确定性：不用 float、不用 System.Random、不依赖字典遍历顺序；战斗逻辑放内核，Client 只做表现
- 内核代码目标 netstandard2.1 / C# 9（Unity 6 支持的版本）
- 调整数值后跑测试，看平衡模拟的输出
- 设计变更同步更新 `docs/game-design.md`（重大决定补进 `docs/decisions.md`）
- 用户可见文本用中文，术语与设计文档一致（舰船、中继信标、补给母舰、舰队耐久等）
