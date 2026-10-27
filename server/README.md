# 星线服务端（UOS）

服务端全部托管在 UOS（决策 50、57、63）：业务代码是一个 C# 云函数类 `StarlineService`（Func Stateless），数据放在 CRUD Save（MongoDB），账号用 Passport。设计见 [server-design.md](../docs/server-design.md)，数据结构见 [server-database.md](../docs/server-database.md)。

现在做到 **S1**：登录（UOS Passport、开发者登录）、一个账号同时只能在一台设备上登录、存档位的列出 / 新建 / 删除、上报游戏时长和远征次数。战斗结算（S2）、养成（S4）、录像榜（S5）还没写。

## 代码结构

```
server/
  src/
    Starline.Server.Game/         规则：登录和会话、存档（GameServer）。不依赖 UOS，也不直接碰数据库
    Starline.Server.Data/         数据层接口 IServerStore + 内存实现（测试用）
    Starline.Server.Data.CrudSave/ CRUD Save（MongoDB）实现
    Starline.Server.Uos/          云函数入口 StarlineService：只有一个 Call，把请求交给 GameServer
  tests/Starline.Server.Tests/    规则测试
  probe/                          S0 验证云函数（部署前先跑它，见 probe/README.md）
  db/schema.sql                   第二阶段（CRUD Storage / MySQL）的表
```

客户端和服务端之间的消息格式在 `Starline/Packages/com.starline.game/Runtime/Battle/Online/ServerProtocol.cs`（内核程序集里，客户端和服务端编译同一份）。每次调用是一个 `ServerRequest`（JSON），`op` 说明做什么，返回一个 `ServerResponse`，`code` 为空表示成功，否则是 `ServerErrors` 里的错误码，客户端按 `UI.csv` 的 `server.error.*` 显示。

| op | 用途 |
|---|---|
| `auth.login` | 登录：`method` 是 `uos`（云函数校验 Passport 令牌）或 `dev`（开发者登录）；返回账号 id 和会话令牌，**同一账号的旧会话随之失效** |
| `auth.logout` | 退出登录 |
| `saves.list` | 账号的全部存档位 |
| `saves.create` | 在空存档位新建指挥官（名字 1~8 个字） |
| `saves.delete` | 删除存档（移到 `saves_deleted`，留着给客服恢复） |
| `saves.report` | 上报游戏时长和远征次数的增量：时长按实际经过的时间封顶，远征一次最多记 5 场。远征改到服务端推进（S4）以前，这几个数只用于显示，不发奖励 |

修改类的请求（create、delete、report）都带客户端生成的 `key`，网络断了用同一个 key 重发，服务端只执行一次。

## 在本机测试

```
dotnet run --project server/tests/Starline.Server.Tests                                      # 内存数据库
dotnet run --project server/tests/Starline.Server.Tests -- --mongo mongodb://127.0.0.1:27017  # 再用 MongoDB 跑一遍
```

最后一行应是“全部通过”。MongoDB 可以用 Docker 起一个：`docker run -d --rm -p 27017:27017 mongo:7.0`。每次测试建一个随机名字的库（`starline_test_…`），不会动别的数据。

云端开发环境里两种都跑过（MongoDB 7.0）。

## 部署到 UOS

UOS 的 C# 云函数要从团结编辑器里的 Func Stateless 插件上传（UOS 文档“开发指南(C#)”），代码放在工程的 `Assets/Scripts/CloudService/`。服务端代码平时放在 `server/src`，部署前用脚本复制过去。

> 云端开发环境里没有团结编辑器，也装不了 UOS 的编辑器插件，所以下面的步骤是按 UOS 文档写的，**没有实际跑过**。哪一步和后台或插件对不上，把截图或报错发给我。

### 准备（只做一次）

1. **建两个 UOS 应用**：开发用一个、正式上线用一个（试用额度不能商用）。下面都先在开发用的应用上做。
2. 先按 [probe/README.md](probe/README.md) 部署验证云函数，确认内核重跑和条件写都没问题（S0）。
3. 在 UOS 后台给应用开通：
   - **Func Stateless**，Runtime 选 **Dotnet**
   - **CRUD**，数据库选 **MongoDB**（S0 的验证云函数用的也是它；如果后台的 CRUD Save 不是 MongoDB，先告诉我）
   - **Passport Login**（登录界面已经在用，见仓库根目录 README 的“UOS 登录”）
4. 在团结编辑器的 **UOS Launcher** 里关联这个应用，安装 **Func Stateless SDK**。
5. 用 NuGetForUnity 装云函数要用的库：**MongoDB.Driver**、**UOS.FuncStateless.MongoDB**（UOS 文档里列为测试过可用）。Newtonsoft.Json 运行环境自带，团结工程里也已经有。
6. 在 UOS 后台这个云函数的**环境变量**里加 `STARLINE_DEV_LOGIN = 1`，开发用的应用才接受开发者登录。**正式应用不要加。** 数据库只有一个、并且授权 UOS 保存数据库密码时不用填别的；否则再加 `STARLINE_DB_ID`、`STARLINE_DB_PASSWORD`（后台显示的数据库 id 和密码）。

### 每次部署

1. 复制服务端代码到工程里：

   ```
   python3 tools/uos/sync_cloud_service.py
   ```

   它把 `server/src` 下的源码复制到 `Starline/Assets/Scripts/CloudService/Starline/`，每个文件都包在 `#if STARLINE_UOS_FUNC` 里。这个目录在 `.gitignore` 里，改代码要改 `server/src` 里的原文件，再跑一次脚本。
2. `Project Settings > Player > Other Settings > Scripting Define Symbols` 加上 `STARLINE_UOS_FUNC`（UOS 登录还要有 `STARLINE_UOS_PASSPORT`），点 Apply。这会编译复制过来的服务端代码和 `Assets/Scripts/Online/UosGameServer.cs`，客户端从此走服务器：登录后从服务器读存档，新建、删除存档和游戏时长都发给服务器。
3. **本地模式试一遍**：菜单 `UOS > Func Stateless > Open Panel`，保持本地模式，点 Play，用开发者登录进游戏，新建一个指挥官、玩一会儿、删掉。本地模式下云函数就在编辑器里跑，但数据库连的是 UOS 上的 MongoDB：要先在 UOS 后台的数据库页面打开“临时公网访问”，不然会报“请在 UOS 页面中选择 开启临时公网访问”（这句报错来自 UOS 的 CRUD SDK）。部署到 UOS 上以后走内网，不需要公网访问。
4. **上传**：切到远程模式，点“上传云函数”。上传完把工具切到“远程调用”，再点 Play 试一遍，在 UOS 后台看调用日志。

如果上传时报找不到 `Starline.Online` 里的类型（`ServerRequest` 等，它们在内核程序集里），改用 `python3 tools/uos/sync_cloud_service.py --with-protocol` 把它们也复制进去；编辑器会提示 CS0436（同名类型），不影响运行。

没加 `STARLINE_UOS_FUNC` 时游戏和以前一样：存档放在本机 `saves/slot<N>.json`，登录只确认是谁在玩。

### 还没验证的地方

| 位置 | 是什么 | 怎么确认 |
|---|---|---|
| `StarlineService.PassportUserId` | 用 `FuncContext.ValidateToken()` 校验 Passport 令牌、`FuncContext.GetUserId()` 取玩家 id。NuGet 上的 SDK（0.1.0）还没有这两个方法，文档也没写返回类型，所以代码在运行时查找它们 | UOS 登录后能进主菜单就是通过了；失败时登录界面显示“账号验证没有通过”，编辑器日志里有原因 |
| `UosGameServer.Call` | 调用前用 `AuthTokenManager.GetAccessToken()`（命名空间按 `Unity.UOS.Auth` 写的）取 Passport 令牌，放进 `FuncContext.SetToken`，SDK 把它作为请求头 `X-UOS-Token` 发给云函数 | 编译不过时把报错发给我；可能 SDK 本来就会自动带上令牌 |
| 远程模式的客户端代码 | 插件在远程模式下用生成的代码替换云函数类，客户端照样 `new StarlineService().Call(...)` | 远程调用能进游戏就是通过了 |
| 冷启动 | 默认超时 3 秒；第一次调用要连数据库 | 看 S0 的 `Ping` 结果，太慢就在后台调大超时 |

## 以后的阶段

- **S2 战斗结算**：`Starline.Server.Game` 编进完整内核（现在只编了 `Online/` 里的消息格式），加战斗登记和重跑；`saves` 文档加上养成数据，幂等记录改放进存档文档的 `recentRequests`（server-database.md 第 11 节），和存档在同一次写入里更新。
- **资源下载走 UOS CDN**：Steam 版的更新由 Steam 分发，语言表随包发布（决策 59），现在没有要放 CDN 的东西。做微信小游戏或手机版需要远程加载资源时再接。
