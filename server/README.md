# 星线服务端（UOS）

服务端全部托管在 UOS（决策 50、57、63、64、65）：同一套规则部署成 C# **云函数**（`StarlineService`，**现在用这个**），也可以部署成 UOS 的**服务器程序镜像**（常驻的 HTTP 进程 `starline-server`，留给本机调试和以后的对战服务器）。数据放在 MongoDB，账号用 Passport。设计见 [server-design.md](../docs/server-design.md)，数据结构见 [server-database.md](../docs/server-database.md)。

现在做到 **S1**：登录（UOS Passport、开发者登录）、一个账号同时只能在一台设备上登录、存档位的列出 / 新建 / 删除、上报游戏时长和远征次数。战斗结算（S2）、养成（S4）、录像榜（S5）还没写。

## 代码结构

```
server/
  src/
    Starline.Server.Game/         规则：登录和会话、存档（GameServer）。不依赖 UOS，也不直接碰数据库
    Starline.Server.Data/         数据层接口 IServerStore + 内存实现（测试用）
    Starline.Server.Data.CrudSave/ CRUD Save（MongoDB）实现
    Starline.Server.Uos/          云函数入口 StarlineService：Call 把请求交给 GameServer，Status 是部署后的自检
    Starline.Server.Host/         常驻进程入口 starline-server：HTTP 服务（POST /call、GET /status），部署成 UOS 的服务器程序镜像
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

## 两种部署方式

规则和数据层只有一份，外面有两个入口，按 UOS 后台选的产品用其中一个：

| | 服务器程序镜像（Func Stateful / Multiverse） | 云函数（Func Stateless，**现在用这个**） |
|---|---|---|
| 入口 | `Starline.Server.Host`，打包成 zip 上传 | `Starline.Server.Uos`，在团结编辑器里用插件上传 |
| 客户端怎么连 | HTTP：`StarlineData/server.json` 里填服务器地址 | 插件生成的调用代码，要定义 `STARLINE_UOS_FUNC` |
| UOS 账号登录 | **还不能用**：UOS 文档没写常驻服务器怎么校验 Passport 令牌（云函数里有 `FuncContext.ValidateToken`）。现在只能用开发者登录 | 能用（API 未实测） |
| 数据库 | MongoDB 连接字符串，或 UOS CRUD（要开公网访问） | UOS CRUD，走内网 |

## 部署成服务器程序镜像

### 打包

```
python3 tools/uos/package_server.py
```

生成 `dist/starline-server-<版本>.zip`（约 7 MB，`dist/` 不提交）。做法和 UOS 文档里的 C# 示例一样：`dotnet publish -c Release -r linux-x64 --no-self-contained`，把发布目录里的全部文件放在 zip 根目录。程序目标是 .NET 8，允许前滚到新的大版本，在 `mcr.microsoft.com/dotnet/runtime:9.0` 里实测能跑（运行时 9.0.22，内存库和 MongoDB 7.0 都试过）。

### 在 UOS 后台制作镜像

| 字段 | 填什么 |
|---|---|
| 镜像标签 | 小写字母、数字、`.`、`-`，比如 `s1-2026-10-28` |
| 添加服务器程序 | 选 **上传**，传上面的 zip。URL 方式要先把 zip 放到一个公开地址，Docker 方式要自己维护镜像仓库，都比上传麻烦 |
| 需要增加执行权限的文件 | `starline-server` |
| 入口程序启动命令 | `./starline-server` 加下面的参数 |
| 端口 | `8899`（程序默认监听的端口，和 UOS 示例一致；要改就加 `--port`） |

### 启动参数

参数也可以写成环境变量（括号里），两个都有时参数优先。

| 参数 | 作用 |
|---|---|
| `--memory`（`STARLINE_STORE=memory`） | 数据只放内存，重启就清空。**第一次部署先用它**，确认程序能跑、客户端能连上，不涉及数据库 |
| `--mongo <连接字符串>`（`STARLINE_MONGO`） | 用这个 MongoDB，比如 `mongodb://用户:密码@地址:端口` |
| `--db-name <库名>`（`STARLINE_DB_NAME`） | 默认 `starline` |
| 都不给 | 用 UOS CRUD 的 MongoDB：需要环境变量 `UOS_APP_ID`、`UOS_APP_SERVICE_SECRET`（UOS 后台的应用 ID 和服务密钥）；多个库时加 `--db-id`，没授权保存密码时加 `--db-password`。常驻服务器不在云函数的内网里，CRUD SDK 走的是公网地址，要在数据库页面打开“临时公网访问” |
| `--dev-login`（`STARLINE_DEV_LOGIN=1`） | 接受开发者登录。**只在开发用的应用上加** |

开发用的应用第一次可以这样启动：`./starline-server --memory --dev-login`。

数据库连接字符串、密码写在启动命令里会显示在后台，能设环境变量的话优先用环境变量。

### Multiverse 就绪上报

Multiverse 要求服务器进程启动后报告“就绪”（`sdk.ready()`），否则启动配置测试会失败：“Game server is not ready, please check if your game package has called sdk.ready()”。程序发现 `AGONES_SDK_*` 环境变量（Multiverse 在容器里放了 Agones SDK 边车）时会自动做这件事，不需要另加参数：端口打开后先 `POST /ready`，之后每 2 秒 `POST /health`。用的是边车的 REST 接口（`AGONES_SDK_HTTP_PORT`，默认 9358），连不上再改走 gRPC（`AGONES_SDK_GRPC_PORT`，默认 9357）。没有用 UOS 的 `UOS.Multiverse.SDK` 包，因为它带着 Grpc.Core 的本机库。成功时日志里有一行“Multiverse：已报告就绪”，失败时会写出两个端口各自的错误。REST 这条路在本机用模拟的边车测过，gRPC 那条和真正的 Multiverse 还没验证。

用内存库时只能开 **1 台**服务器（每台各有一份数据）；用 MongoDB 时可以开多台，会话和存档都在库里。

### 确认部署成功

1. 后台显示服务器运行后，记下它的 IP 和端口，浏览器打开 `http://IP:端口/status`，应该看到 `allOk: true`、版本号、`runtime` 是 9.0.x。
2. 把 `Starline/Packages/com.starline.game/Runtime/Resources/StarlineData/server.json` 的 `url` 改成 `http://IP:端口`（或者启动参数 `-server http://IP:端口` 临时覆盖），在编辑器里点 Play，用开发者登录进游戏，新建指挥官、删掉，再重新登录确认存档还在。客户端会走 HTTP，不需要装 Func Stateless SDK。
3. 现在是明文 HTTP：工程设置里已经把“Allow downloads over HTTP”设成只在开发版允许（`insecureHttpOption: 1`）。正式发布前要换成 HTTPS（域名、证书和备案），不然正式包连不上。

### 为什么不用它做账号和存档服务

在 Multiverse 上实测过（2026-10-28）：镜像能通过启动测试，但 Multiverse 是“一局一台”的对战服务器托管，免费试用单台服务器最长存活 **1 小时**（“The allocation TTL ... has exceeded max allocation TTL 1h0m0s”），每次分配 IP 都会变，客户端没有固定地址可连，所以账号和存档服务改用云函数（决策 65）。

### 还要解决的

- **UOS 账号登录**：服务器要能校验 Passport 令牌才能确认玩家身份。需要知道 UOS 给常驻服务器的校验方式（JWT 公钥或校验接口），可以在 UOS 后台或问 UOS 客服：“自建 / Func Stateful 服务器怎么校验 Passport 的 access token 并拿到 userId”。知道以后我在 `Starline.Server.Host` 里接上，客户端把令牌放进请求头 `X-UOS-Token`。
- **固定地址**：客户端填的是服务器的 IP。服务器换了 IP 要改 `server.json` 重新发包，正式上线前要有固定域名。

## 部署成云函数

这一节是 Func Stateless（云函数）的做法，账号和存档服务现在用它（决策 65）。UOS 的 C# 云函数要从团结编辑器里的 Func Stateless 插件上传（UOS 文档“开发指南(C#)”），代码放在工程的 `Assets/Scripts/CloudService/`，所有函数代码都要在这一个目录里。服务端代码平时放在 `server/src`，部署前用脚本复制过去。

> 云端开发环境里没有团结编辑器，也装不了 UOS 的编辑器插件，所以下面的步骤是按 UOS 文档写的，**没有实际跑过**。哪一步和后台或插件对不上，把截图或报错发给我。

#### 两个环境

开发和正式各建一个 UOS 应用（试用额度不能商用）。两边的代码一样，只有下面这些设置不同：

| 设置 | 开发用的应用 | 正式应用 |
|---|---|---|
| 云函数环境变量 `STARLINE_DEV_LOGIN` | `1`（接受开发者登录） | **不设** |
| 云函数环境变量 `STARLINE_DB_ID`、`STARLINE_DB_PASSWORD` | 应用里只有一个 MongoDB、并且授权 UOS 保存数据库密码时不用设；否则填后台显示的数据库 id 和密码 | 同左 |
| 云函数超时 | 建议 10 秒（默认 3 秒，实例第一次调用要连数据库） | 同左 |
| 数据库“临时公网访问” | 在编辑器本地模式下调试时打开，调完关掉 | **一直关着**（部署在 UOS 上的云函数走内网） |
| Passport 登录渠道 | 手机号就够 | 按上线计划配置 |
| 客户端 | 编辑器、开发版（有开发者登录按钮） | 正式包（没有开发者登录按钮） |

`UOS_APP_ID`、`UOS_APP_SERVICE_SECRET` 由 UOS 提供：云函数里 CRUD SDK 先读这两个环境变量，读不到再用 UOS Launcher 里关联的应用（本地模式就是这种情况）。应用 ID 和密钥都不要写进代码或提交到仓库。

#### 准备（每个应用做一次）

1. 在 UOS 后台给应用开通：
   - **Func Stateless**，Runtime 选 **Dotnet**
   - **CRUD**，新建一个 **MongoDB** 数据库，等状态变成“运行中”；勾选让 UOS 保存数据库密码。库和集合不用手动建：云函数第一次调用时自动创建（库名 `starline`，集合见 server-database.md 第 11 节），索引也是这时建好的
   - **Passport Login**，打开要用的登录渠道（见仓库根目录 README 的“UOS 登录”）
2. 在团结编辑器的 **UOS Launcher** 里关联这个应用，安装 **Func Stateless SDK** 和 **Passport Login SDK**。
3. 菜单 `UOS > Func Stateless > Import NuGetForUnity` 导入 NuGetForUnity，然后在 `NuGet > Preferences` 里把 **Packages Config Path** 设成 `Assets/Scripts/CloudService`，**Packages Install Path** 设成 `Assets/Scripts/CloudService/Packages`（UOS 文档要求第三方库装在云函数目录下）。
4. `Project Settings > Player > Other Settings > Scripting Define Symbols` 加上 `STARLINE_UOS_FUNC` 和 `STARLINE_UOS_PASSPORT`，点 Apply。

#### 每次部署

1. 复制服务端代码：

   ```
   python3 tools/uos/sync_cloud_service.py
   ```

   它把 `server/src` 下的源码平铺复制到 `Starline/Assets/Scripts/CloudService/`，每个文件第一行是“Copied by tools/uos/sync_cloud_service.py”，整个文件包在 `#if STARLINE_UOS_FUNC` 里；再跑一次会先删掉上次复制的文件，插件自己生成的示例和 `Packages` 目录不动。同时生成 `packages.config`（MongoDB.Driver 2.28.0、UOS.FuncStateless.MongoDB 1.1.8），第一次要在团结里点 `NuGet > Restore Packages` 装上。这个目录在 `.gitignore` 里，改代码要改 `server/src` 里的原文件，再跑一次脚本。
2. 回到团结等编译完。客户端从此走服务器：登录后从服务器读存档，新建、删除存档和游戏时长都发给服务器（`Assets/Scripts/Online/UosGameServer.cs`）。
3. **本地模式试一遍**：菜单 `UOS > Func Stateless > Open Panel`，保持本地模式，打开数据库的“临时公网访问”，点 Play，用开发者登录进游戏，新建一个指挥官、玩一会儿、删掉。本地模式下云函数在编辑器里跑，开发者登录总是接受（部署到 UOS 上以后不会，代码按 UOS 运行环境的 `TENCENTCLOUD_RUNENV` 区分），但数据库是 UOS 上的真库，所以本地模式只连开发用的应用。没开公网访问会报“请在 UOS 页面中选择 开启临时公网访问”。
4. **上传**：切到远程模式，点“上传云函数”。第一次上传后在 UOS 后台的云函数页面设好上面表里的环境变量和超时（改环境变量不用重新上传）。
5. **自检**：在 UOS 后台的“调试函数”里，PARAMS 填 `Status`，BODY 留空，点调用。返回的 JSON 里 `allOk` 应该是 `true`，每一项的意思：

   | 项 | 正常的样子 | 不正常时 |
   |---|---|---|
   | 服务端版本 | 和 `GameServer.Version` 一样 | 不一样说明上传的不是最新代码，重新跑第 1 步再上传 |
   | 开发者登录 | 开发应用“接受”，正式应用“不接受” | 检查 `STARLINE_DEV_LOGIN` |
   | 环境变量 | 只显示设没设，不显示值 | |
   | 数据库 | `ok: true`，第一次调用的连接时间在 1~2 秒内 | “No databases available”：还没建 MongoDB；“Multiple databases”：设 `STARLINE_DB_ID`；“Password cannot be empty”：授权保存密码或设 `STARLINE_DB_PASSWORD` |
   | Passport 令牌校验 | `ok: true` | SDK 太旧，UOS 账号登录会失败：升级 Func Stateless SDK |

6. **远程调用试一遍**：工具切到“远程调用”，点 Play，用 UOS 账号登录、新建指挥官，在 UOS 后台看调用日志。用两台电脑（或编辑器加一个开发版）登录同一个账号，先登录的那台再操作时应该回到登录界面，提示“账号已在其他设备登录”。
7. **打客户端包前**切到远程模式（UOS 文档要求）。

如果上传时报找不到 `Starline.Online` 里的类型（`ServerRequest` 等，它们在内核程序集里），改用 `python3 tools/uos/sync_cloud_service.py --with-protocol` 把它们也复制进去；编辑器会提示 CS0436（同名类型），不影响运行。

没加 `STARLINE_UOS_FUNC` 时游戏和以前一样：存档放在本机 `saves/slot<N>.json`，登录只确认是谁在玩。

S0 的验证云函数（[probe/README.md](probe/README.md)）检查内核能不能在云函数里逐位一致地重跑战斗，S2（战斗结算）之前必须跑；S1 只用到数据库和 Passport，上面的 `Status` 已经覆盖。

#### 上线前检查（正式应用）

- [ ] 正式应用的云函数**没有** `STARLINE_DEV_LOGIN`，`Status` 显示“开发者登录：不接受”
- [ ] 数据库“临时公网访问”是关着的
- [ ] `Status` 的 `allOk` 是 `true`，版本是这次要发的版本
- [ ] 正式包里没有开发者登录按钮（只在编辑器和 Development Build 出现）
- [ ] 打包前切到了远程模式
- [ ] 每天备份（server-design.md 第 6.3 节，定时云函数还没写）在玩家进来前补上

#### 还没验证的地方

| 位置 | 是什么 | 怎么确认 |
|---|---|---|
| `StarlineService.PassportUserId` | 用 `FuncContext.ValidateToken()` 校验 Passport 令牌、`FuncContext.GetUserId()` 取玩家 id。NuGet 上的 SDK（0.1.0）还没有这两个方法，文档也没写返回类型，所以代码在运行时查找它们 | `Status` 的“Passport 令牌校验”；UOS 登录后能进主菜单就是通过了 |
| `UosGameServer.Call` | 调用前用 `AuthTokenManager.GetAccessToken()`（命名空间按 `Unity.UOS.Auth` 写的）取 Passport 令牌，放进 `FuncContext.SetToken`，SDK 把它作为请求头 `X-UOS-Token` 发给云函数 | 编译不过时把报错发给我；可能 SDK 本来就会自动带上令牌 |
| 远程模式 | 插件在远程模式下只保留云函数类里带 `[CloudFunc]` 的方法，客户端照样 `new StarlineService().Call(...)`；复制过去的其他服务端文件远程模式下怎么处理，文档没写 | 远程调用能进游戏、打包能通过就是没问题 |
| 客户端包里的服务端代码 | 本地模式需要服务端代码和 MongoDB 驱动编进编辑器；文档没说远程模式打包时会不会去掉 | 打一个包看大小和 `Managed` 目录里有没有 MongoDB.Driver.dll；有的话告诉我，我把它们限制成只在编辑器里编译 |

## 以后的阶段

- **S2 战斗结算**：`Starline.Server.Game` 编进完整内核（现在只编了 `Online/` 里的消息格式），加战斗登记和重跑；`saves` 文档加上养成数据，幂等记录改放进存档文档的 `recentRequests`（server-database.md 第 11 节），和存档在同一次写入里更新。
- **资源下载走 UOS CDN**：Steam 版的更新由 Steam 分发，语言表随包发布（决策 59），现在没有要放 CDN 的东西。做微信小游戏或手机版需要远程加载资源时再接。
