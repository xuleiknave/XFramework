# UOS 验证云函数（S0）

写服务端之前，先在 UOS 上部署这个最小的云函数，确认 [server-design.md](../../docs/server-design.md) 第 9 节列出的几件事。它不改任何游戏数据，跑完会清掉自己写的东西。

| 函数 | 检查什么 | 怎么算通过 |
|---|---|---|
| `Ping` | 冷启动和热调用的耗时 | 看客户端测到的总耗时：冷启动（`coldStart: true`）和热调用各多久 |
| `Battle` | 云函数能不能带上内核和配置表跑战斗，结果和本地 .NET 是否**逐位一致** | `内核重跑 20 场` 的合并哈希是 `c701cccfce654a73` |
| `Steam` | 国内云函数能不能访问 Steam Web API（Steam 登录要用） | 两个地址都返回 HTTP 200，耗时可接受 |
| `Database` | 数据库能不能整份读写一个 80 KB 的存档、支不支持按 revision 的条件写（乐观锁）、单个文档最大多大、能不能按字段筛选排序 | 除了超过 256 KB 的大小档位，全部 `ok: true` |

每个函数返回一段 JSON 报告，`items` 里每一项有 `ok`（是否通过）、`ms`（耗时）、`detail`（看到了什么）。

## 代码结构

```
server/probe/
  Starline.Probe/          检查逻辑（不依赖 UOS）；内核源码和配置表直接编进这个 dll
  Starline.Probe.Mongo/    用 MongoDB 实现数据库检查，云函数里通过 UOS 的 CRUD SDK（UOS.FuncStateless.MongoDB）连接
  Starline.Probe.Uos/      云函数入口 ProbeService：[CloudService] 类，每个检查一个 [CloudFunc]（UOS.FuncStateless.SDK）
  Starline.Probe.Local/    在本机跑同一套检查
```

## 在本机先跑一遍

```
dotnet run --project server/probe/Starline.Probe.Local                          # 内存数据库
dotnet run --project server/probe/Starline.Probe.Local -- --mongo mongodb://127.0.0.1:27017 --no-steam
dotnet run --project server/probe/Starline.Probe.Local -- --print-golden         # 改了内核或配置表后，重新算合并哈希
```

在云端开发环境（.NET 8 / Linux x64）上已经跑过：内核 20 场合并哈希 `c701cccfce654a73`，平均每场约 15 ms；用 Docker 的 MongoDB 7.0 跑数据库检查全部通过（条件写正确拒绝旧版本，8 个请求同时写只有 1 个成功，15 MB 文档也能存）。云端环境访问不了 Steam，所以 Steam 那一项只能在 UOS 上看。

改了内核或配置表以后，`Battle` 的期望哈希会变：用 `--print-golden` 算出新值，更新 `BattleProbe.GoldenHash`。

## 在 UOS 上部署

UOS 的文档页在云端开发环境里打不开，下面的步骤是按 UOS 公开的 SDK（NuGet 上的 `UOS.FuncStateless.SDK` 和 `UOS.FuncStateless.MongoDB`）写的。如果后台的实际界面不一样，以后台为准，**遇到对不上的地方把截图或报错发给我**。

1. **建一个开发用的 UOS 应用**（不要用以后正式上线的那个），开通：
   - **Func Stateless**，语言选 C#
   - **CRUD**，选 MongoDB。如果后台里 CRUD Save 不是 MongoDB（比如只有键值或别的接口），记下它提供的是什么，`Database` 这一项先跳过
2. **拿到 C# 云函数模板**：按 UOS 后台的指引（或在团结编辑器里装 UOS 的 Func Stateless 插件）创建 C# 云函数工程。
3. **把验证代码放进去**，二选一：
   - 模板是普通 .NET 工程：把 `Starline.Probe`、`Starline.Probe.Mongo` 两个工程加进去并引用，再把 `Starline.Probe.Uos/ProbeService.cs` 复制到模板里放云函数的位置
   - 模板只收源码文件：把 `Starline.Probe/*.cs`、`Starline.Probe.Mongo/*.cs`、`ProbeService.cs` 和内核源码 `Starline/Packages/com.starline.game/Runtime/Battle/**/*.cs` 一起放进去；配置表 `StarlineData/*.json` 要设成嵌入资源，资源名是 `StarlineData.<文件名>.json`（参考 `Starline.Probe.csproj` 的写法）
4. **部署**，然后从后台的测试界面（或客户端）依次调用：
   1. `Ping`：先等 15 分钟以上没人调用，调一次（冷启动），马上再调两次（热调用），记下每次客户端看到的总耗时
   2. `Battle`
   3. `Steam`
   4. `Database`：参数 `databaseId` 和 `password` 先都留空（只有一个数据库、且授权 UOS 保存数据库密码时可以自动连接）；连不上再填后台显示的数据库 id 和密码
5. **把每次调用返回的 JSON 和 `Ping` 的耗时贴给我**。

## 结果怎么用

| 结果 | 影响 |
|---|---|
| `Battle` 哈希一致 | 服务端可以直接用内核重跑战斗，方案不变 |
| `Battle` 跑不起来或哈希不一致 | 改用 Func Stateful（UOS 托管的 Docker）部署同一份代码，或者先查清差异再说 |
| 条件写通过 | 存档按 server-database.md 第 11 节的方式整份读写 + 乐观锁 |
| 条件写不支持 | 改为“一个账号只在一台电脑登录 + 客户端一次只发一个修改请求”来避免冲突，或者直接上 CRUD Storage（MySQL）用事务 |
| 排序查询通过 | 录像榜可以直接查询，`boards` 文档只做缓存 |
| `Steam` 访问不稳定 | Steam 登录的票据校验要走海外中转，或者放到 Func Stateful / 海外节点 |
| 冷启动很慢（几秒） | 每个操作都要请求服务器，要考虑保持预热或用 Func Stateful |
