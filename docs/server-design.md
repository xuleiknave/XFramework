# 服务端设计：必须联网登录，服务端为准

开发者选定（决策 49）：**玩家必须连接服务器、登录后才能玩，养成数据全部以服务端为准。** 原来“不联网也能玩全部内容”的设定去掉了；决策 48 里“离线照常玩、联网后补交”的做法也随之作废。

**服务端全部托管在 UOS**（团结引擎的云服务，决策 57，决策 50）：业务代码是 C# 云函数（Func Stateless），数据库起步用 CRUD Save（按用量计费的文档存储），玩家多了再换 CRUD Storage（托管 MySQL）。**只有云函数能读写数据库**，客户端不直接连。

本文是服务端的整体设计：怎么登录、战斗怎么结算、养成操作怎么提交、工程怎么分、有哪些接口。表结构见 [server-database.md](server-database.md)，通关录像的规则见 [replay-server.md](replay-server.md)（PR #6，其中“断网时先存本地队列”的部分不再需要）。

S1（登录、存档位）已经写好，代码和部署步骤见 [server/README.md](../server/README.md)；后面的阶段仍是设计。

---

## 1. 原则

| 原则 | 具体做法 |
|---|---|
| 服务端是唯一的存档 | 客户端不在本地存养成数据，只缓存服务端发来的快照用来显示；进游戏先登录、再拉存档 |
| 客户端只提交“做了什么” | 打了哪一关（布阵和指令）、给谁升星、买了什么。货币、碎片、等级、掉落都由服务端算 |
| 战斗在客户端播放，在服务端判定 | 客户端本地跑内核、播 3D 表现；结束后把输入交上去，服务端用同一份内核重跑，**以服务端结果发奖励** |
| 一个账号同一时间只在一台电脑上玩 | 新的登录会把旧的踢下线，不用处理两台电脑同时改一个存档 |
| 每次请求都可以安全重试 | 每个修改请求带一个客户端生成的 `Idempotency-Key`，网络断了重发不会重复发奖励 |

和“离线补交”方案相比省掉的东西：本地动作队列、设备序号、离线计时的时钟校正、随机数必须由客户端和服务端各算一遍且一致、老版本战斗长期兼容、从没联网的本地存档导入。

---

## 2. 一次游戏的流程

```
启动 → Steam 登录（自动，无需输入）→ 拉取账号信息和存档列表 → 主菜单
     → 选存档 → 拉取完整存档快照 → 进入游戏
     → 每个操作：客户端发请求 → 服务端校验、执行、写库 → 返回变化 → 客户端更新界面
```

- 登录失败或服务器不可用：停在登录界面，显示原因和“重试”按钮，不能进入游戏。
- **玩的过程中断线**：弹出“正在重新连接”，自动重试；已经发出的请求带着同一个 `Idempotency-Key` 重发，服务端保证只执行一次。重连超过 1 分钟仍失败，回到登录界面。
- **战斗中断线**：战斗是客户端本地播放的，可以继续看完；结束时提交不上就按上一条重连，重连后再提交。战斗在开始时由服务端登记过（第 3 节），只要在有效期（30 分钟）内交上来就照常结算。
- 被另一台电脑登录踢下线：提示“账号在其他设备登录”，回到登录界面。

---

## 3. 战斗：开始时登记，结束时重跑

```
POST /v1/saves/{id}/battles          开始：模式、关卡（或高塔层数、远征节点）、布阵
  服务端：检查关卡已解锁、布阵合法（角色是自己的、等级星级符文以服务端为准、指挥点、格子）
          → 写 save_battle（状态“进行中”），返回 battle_id、种子、配置表版本
客户端本地跑内核、播放战斗，玩家放指挥官技能
POST /v1/battles/{battle_id}/finish  结束：带帧号的指令、客户端算出的结果哈希
  服务端：用开始时登记的布阵 + 提交的指令重跑 → 判定胜负和星级 → 发奖励
          → 更新 save_battle（状态“已结算”）、save_stage、角色经验、掉落 → 返回奖励和变化
```

- **布阵在开始时就定了**，结束时只能交指令。客户端改不了角色属性，也没法挑一个“打赢了的布阵”再补登记。
- **种子**：主线关卡每关固定（`hash(关卡 id)`，replay-server.md 第 4 节）；高塔无尽区段也固定；远征的种子在开局时由服务端生成。种子由服务端下发，客户端不能自选。
- **重跑结果和客户端不一致**（`verify_status = 2`）：按服务端结果算，客户端用返回的结果更新界面。偶尔一次多半是客户端 bug，同一账号频繁出现进后台人工看，不自动封号。
- 放弃战斗：`POST /v1/battles/{id}/abandon`；超过 30 分钟没交的战斗由定时任务标记过期。同一存档同时只能有一场进行中的战斗。
- **掉落**由服务端在结算时用随机数生成，写进返回结果和 `save_action.result`，客户端只负责展示。
- 一场最多 1800 帧，重跑单场几毫秒，不是瓶颈。

通关录像：战斗结算后，如果玩家开着“分享我的通关录像”并且这场战力能进本关最低战力榜，服务端直接从 `save_battle` 收进录像榜，结果放在结算返回里（“你的录像已进入本关最低战力榜第 3 名”）。不需要单独的上传接口。

---

## 4. 养成和其他操作

每种操作一个接口（第 7 节），处理方式相同：

```
读出存档（带 revision）
这个 Idempotency-Key 处理过 → 直接返回上次的结果
按配置表校验（碎片够不够、等级上限、槽位是否解锁……）
  通过 → 算出新存档，revision + 1
  不通过 → 返回错误码，不改数据
写回存档：只有 revision 还是刚才读到的值才写入（乐观锁），否则重读重做（最多 3 次）
记操作日志，返回变化后的内容和新 revision
```

- **同一个存档的修改必须一个接一个**。CRUD Save 阶段整个存档是一份文档，用 revision 做条件写（乐观锁）；换到 MySQL 后改为事务 + `SELECT … FOR UPDATE` 行锁。两种写法都藏在数据层后面（第 6 节），规则代码不变。
- 一个账号同时只能在一台电脑登录，客户端也一次只发一个修改请求，所以冲突本来就很少，乐观锁只是兜底。

- **规则代码只在服务端**（`Starline.Server.Game`），读和客户端同一份 JSON 配置表。客户端也读这些配置表，但只用来**显示**（升星要多少碎片、等级上限是多少、按钮能不能点），不做结算。
- 客户端每次拿到返回的 `revision`。如果发现和自己缓存的对不上（比如在别的电脑上玩过），就重新拉完整快照。
- **计时（炼金工坊）**：开始、加速、领取都以服务端 UTC 时间为准，改本机时间没有用。
- **远征**：一局远征的状态在服务端（`save_expedition`）。内核里现有的 `ExpeditionRun` 本来就是纯 C#，服务端直接用它：开局、选路线、选战利品、补给站都是请求，服务端推进 `ExpeditionRun` 后把快照存回去；远征里的战斗同样走第 3 节的流程。客户端现在本地生成远征种子（`GameApp` 用当前时间），要改成由服务端下发。

### 错误码

| 代码 | 什么时候 |
|---|---|
| `invalid_formation` | 布阵不合法：没有的角色、超出指挥点、格子不对 |
| `not_enough_*` | 碎片、金币、遗骸不够 |
| `locked` | 关卡、槽位、功能还没解锁 |
| `battle_in_progress` | 还有一场没结束的战斗 |
| `battle_expired` | 战斗超过有效期才提交 |
| `client_outdated` | 客户端版本太老（HTTP 426），提示更新 |
| `session_replaced` | 账号在其他设备登录（HTTP 401） |

错误统一返回 `{ "code": "not_enough_shards", "message": "碎片不够" }`，`message` 是中文，可以直接显示。

---

## 5. 版本更新

- 每个客户端版本对应一个配置表版本（`data_hash`）和内核版本。登录时客户端报自己的版本，太老就返回 `client_outdated`，要求更新（Steam 会自动更新，一般遇不到）。
- 发布新版本时，服务端同时加载新旧两版配置表一小段时间（比如 1 天），让更新前已经开始的战斗还能按旧版结算。之后只保留当前版本。
- 配置表在服务端按 `data/<data_hash>/` 存放，启动时全部加载。

---

## 6. 部署在 UOS 和工程结构

### 6.1 用到的 UOS 服务

| 职责 | UOS 服务 | 说明 |
|---|---|---|
| 所有接口（登录、存档、战斗结算、养成、远征、录像榜） | 现在部署成服务器程序镜像（Func Stateful，常驻 HTTP 进程 `starline-server`，`POST /call`，决策 64）；也可以部署成 Func Stateless 云函数 `StarlineService.Call`（决策 63）。两种都是同一个 `ServerRequest`，请求里的 `op` 区分接口 | 战斗重跑单场几毫秒，按 GB·秒计费；上限 100 QPS，个人项目起步够用 |
| 数据库 | CRUD Save → CRUD Storage（托管 MySQL） | 两个阶段见 [server-database.md](server-database.md) 第 0 节；只有云函数能读写 |
| 账号 | Passport：微信小游戏直接登录；Steam、iOS 走“外部 ID 登录”，由云函数先校验 Steam 票据再映射 | 计费口径待确认，太贵就由云函数自己处理登录（replay-server.md 第 12 节） |
| 配置表、客户端资源 | CDN | 配置表 JSON 跟云函数一起打包，客户端的随包发 |

**不用 UOS 的 Cloud Save 存游戏存档**：Cloud Save 是客户端直接读写的，玩家能改，做不到“服务端为准”。存档只放在 CRUD 里，只经过云函数。

### 6.2 工程结构

```
server/
  src/
    Starline.Server.Game/      游戏规则：登录和会话、存档（S1 已写）；以后加战斗登记和结算、养成、商店、工坊、远征、录像榜
                               纯 .NET 类库，不依赖 UOS，也不直接碰数据库；和客户端共用的消息格式在内核的 Online/ 目录
    Starline.Server.Data/      数据层接口 IServerStore + 内存实现
    Starline.Server.Data.CrudSave/  CRUD Save（MongoDB）实现（第一阶段）
    （以后）Starline.Server.Data.MySql/  MySQL 实现（第二阶段，MySqlConnector + Dapper，表结构见 db/schema.sql）
    Starline.Server.Uos/       UOS 云函数入口 StarlineService：取 Passport 身份、调 Game 层；很薄
  tests/
    Starline.Server.Tests/     规则、结算、重试幂等、乐观锁冲突的测试（数据层用内存实现）
  db/
    schema.sql                 第二阶段（CRUD Storage / MySQL）的表结构
```

| 选择 | 理由 |
|---|---|
| 规则写成普通类库，云函数只是入口 | UOS 有什么限制都只影响最外面一层。万一 C# 云函数带不上内核，同一个类库可以包成 ASP.NET 程序部署到 Func Stateful（UOS 托管的 Docker）或自建服务器，游戏和内核代码不用改 |
| 数据层接口 + 两套实现 | 先用便宜的 CRUD Save，换 MySQL 时只换实现；测试用内存实现，不依赖 UOS |
| 存档整份读写 | CRUD Save 阶段一个存档一份文档，读一次、写一次，调用次数最少（按次计费也最省） |

### 6.3 部署和备份

- C# 云函数用团结编辑器里的 Func Stateless 插件上传（`usf-cli` 只支持 JS）：`tools/uos/sync_cloud_service.py` 把 `server/src` 的源码复制到工程的 `Assets/Scripts/CloudService/`，步骤见 server/README.md；配置表以后按 `data/<data_hash>/` 一起打进函数包。
- 每天由一个定时云函数把 CRUD 里的数据导出成 JSON 存档备份，防误删，也方便以后迁出 UOS 或换到 MySQL。
- 开发和正式环境分开两个 UOS 应用。UOS 的试用额度不能用于正式商用。两边代码相同，只差环境变量（开发应用设 `STARLINE_DEV_LOGIN=1` 接受开发者登录）；每次上传后调一次自检云函数 `StarlineService.Status`，看版本、数据库和 Passport 校验是否正常（server/README.md“部署到 UOS”）。

---

## 7. 接口

下面按 REST 的样子列出全部接口，方便看清有哪些功能。**实际实现**（决策 63）是一个云函数 `StarlineService.Call`：客户端交一个 `ServerRequest`（JSON），`op` 对应下表的一行（如 `saves.create`），`session` 是会话令牌，修改类请求的 `key` 就是 Idempotency-Key；返回 `ServerResponse`，`code` 为空表示成功，否则是错误码。消息格式见 `Runtime/Battle/Online/ServerProtocol.cs`，S1 已实现的 op 见 server/README.md。

### 登录与账号

令牌由云函数自己签发（里面带会话 id），不直接用 Passport 的令牌：这样“一个账号只能在一台电脑上登录”和踢下线都由我们控制，以后不用 Passport 也不影响客户端。Passport 只负责“这个人是谁”：客户端把 Passport 令牌放进 `FuncContext`（请求头 `X-UOS-Token`），登录时云函数用 `FuncContext.ValidateToken` 校验。S1 只发一个会话令牌，7 天不用才过期，不分访问令牌和刷新令牌（决策 63）；下表的 `refresh` 暂不需要。

| 方法 | 路径 | 用途 |
|---|---|---|
| POST | `/v1/auth/steam` | 云函数校验 Steam 票据 → 用 Passport 外部 ID 登录换出玩家 id → 签发访问令牌（15 分钟）和刷新令牌（30 天）；第一次登录自动建账号；**作废这个账号的其他会话** |
| POST | `/v1/auth/wechat` | 微信小游戏：Passport 微信登录，之后同上 |
| POST | `/v1/auth/refresh` | 用刷新令牌换新的一对令牌 |
| POST | `/v1/auth/logout` | 作废当前会话 |
| GET | `/v1/me` | 账号信息、已购内容、设置、存档列表 |
| PUT | `/v1/me/settings` | 改设置 |
| DELETE | `/v1/me` | 注销账号：标记注销，30 天后真正删除 |

### 存档

| 方法 | 路径 | 用途 |
|---|---|---|
| POST | `/v1/saves` | 新建存档（存档位、指挥官名字） |
| GET | `/v1/saves/{id}` | 完整存档快照 + revision |
| PATCH | `/v1/saves/{id}` | 改指挥官名字 |
| DELETE | `/v1/saves/{id}` | 删除存档（软删除） |
| POST | `/v1/saves/{id}/playtime` | 报告游戏时长（只做上限检查，用于展示） |

### 战斗

| 方法 | 路径 | 用途 |
|---|---|---|
| POST | `/v1/saves/{id}/battles` | 开始一场战斗（主线、高塔、远征） |
| POST | `/v1/battles/{battle_id}/finish` | 提交指令，服务端重跑并结算 |
| POST | `/v1/battles/{battle_id}/abandon` | 放弃 |
| POST | `/v1/saves/{id}/stages/{stage}/sweep` | 扫荡（三星通关后） |

### 养成

| 方法 | 路径 | 用途 |
|---|---|---|
| POST | `/v1/saves/{id}/heroes/{hero}/recruit` | 碎片召唤 |
| POST | `/v1/saves/{id}/heroes/{hero}/star-up` | 升星 |
| PUT | `/v1/saves/{id}/heroes/{hero}/runes/{slot}` | 装符文（body 里是 `rune_uid`，空表示卸下） |
| POST | `/v1/saves/{id}/shop/buy` | 商店购买 |
| POST | `/v1/saves/{id}/flags` | 剧情已读、教学完成、领取一次性奖励 |

### 玩法

| 方法 | 路径 | 用途 |
|---|---|---|
| POST | `/v1/saves/{id}/expedition` | 开始远征（首发 3 名角色、威胁等级） |
| POST | `/v1/saves/{id}/expedition/move` | 选路线上的下一个节点 |
| POST | `/v1/saves/{id}/expedition/choose` | 选战利品、补给站二选一、事件选项 |
| DELETE | `/v1/saves/{id}/expedition` | 放弃远征 |
| POST | `/v1/saves/{id}/tower/blessings` | 调整塔层祝福 |
| POST | `/v1/saves/{id}/lab` | 开始解析研究 |
| POST | `/v1/saves/{id}/lab/{research}/speedup` | 用遗骸加速 |
| POST | `/v1/saves/{id}/lab/{research}/claim` | 领取阶段成果 |

### 通关录像

| 方法 | 路径 | 用途 |
|---|---|---|
| GET | `/v1/stages/{stage}/board?noskill=0` | 本关最低战力榜（当前版本） |
| GET | `/v1/replays/{public_id}` | 下载录像 |
| POST | `/v1/replays/{public_id}/like` | 点赞 |
| POST | `/v1/replays/{public_id}/report` | 举报 |
| DELETE | `/v1/me/replays` | 删除我上传的全部录像 |

限流：每个账号每分钟 120 次请求；录像榜接口可以被 CDN 缓存 1 分钟。

---

## 8. 要改的地方（按顺序）

| 阶段 | 内容 |
|---|---|
| S0 UOS 验证 | 验证云函数已写好（`server/probe/`，部署步骤见它的 README）。在 UOS 后台部署这个最小的 C# 云函数：带上内核和配置表跑一场战斗、读写一份 CRUD Save 文档（试条件写）、访问 Steam Web API。第 9 节列的问题在这一步得到答案 |
| S1 服务端骨架 | ✅ 已写（server/README.md）：`server/` 工程、登录（UOS Passport；开发用的 UOS 应用另接受 `dev` 登录）、存档位接口、CRUD Save 数据层、客户端存档改走服务器。部署到 UOS 要在团结编辑器里做，还没实际部署过 |
| S2 战斗结算 | 战斗登记和重跑、关卡进度、经验和掉落；配置表新增 `progression.json`（经验曲线、升星消耗、等级上限）、`stages.json`（主线关卡、掉落） |
| S3 客户端联网 | 登录界面（✅ 已做：UOS Passport 和开发者登录，见 client-architecture.md“登录”，还没接服务端）、网络层（令牌刷新、重试、断线重连）、存档从本地 JSON 改为服务端快照；主菜单和存档界面改为读服务端 |
| S4 养成和玩法 | 召唤、升星、符文、商店、炼金工坊、星辉高塔；远征改为服务端推进 |
| S5 录像 | 录像榜、下载、点赞举报 |

客户端和文档要跟着改的地方见第 10 节。

---

## 9. 还没定的事

1. **UOS 要实际部署才能确认的事**（S0 阶段做，需要你的 UOS 账号）：
   - C# 云函数能不能带上内核源码和配置表；冷启动多久（玩家每个操作都要等它）
   - CRUD Save 支不支持条件写（按 revision 更新），单个文档最大多大（一个存档估计 30~80 KB），能不能按字段查询和排序
   - 国内云函数能不能稳定访问 Steam Web API
   - Passport 的计费口径
2. **海外玩家的延迟**：UOS 在国内，现在每个操作都要请求服务器，海外 Steam 玩家每次点击会多等几百毫秒。界面要做到先显示“处理中”、不卡住；真不行再考虑海外节点。
3. **ICP 备案和版号**：UOS 在国内，对外提供服务的域名要备案；国内手机端和小游戏收费要版号。
4. **Steam 商店页要标明“需要联网”**。买断制游戏强制联网容易招差评，商店页和首发说明里要写清楚原因（防作弊、录像和排行榜）。
5. **停服预案**：买断制游戏强制联网，以后如果停服，玩家就玩不了了。建议现在先定一句承诺（比如停服前发布离线补丁），写进商店页或 FAQ。

---

## 10. 客户端和文档里要跟着改的地方

“离线也能玩”去掉后，下面这些地方和新设定不一致。本 PR 只改了文档（标 ✅ 的），客户端代码等服务端 S1~S3 做好后在 S3 里一起改。

### 客户端代码（main 分支）

| 位置 | 现在 | 要改成 |
|---|---|---|
| `Client/App/GameApp.cs` 开始远征 | 用本机时间生成远征种子，`ExpeditionRun` 在客户端推进 | 种子由服务端下发；一局远征在服务端推进，客户端只显示服务端返回的状态 |
| `Client/Screens/MapScreen.cs` | 选节点、补给站（耐久 +1 / 法力水晶）直接改本地 `ExpeditionRun` | 发请求（`expedition/move`、`expedition/choose`），以返回为准 |
| `Client/Screens/RewardScreen.cs`、`RunEndScreen.cs` | 战利品三选一、远征结算在本地算 | 战利品由服务端给出，选择后发请求；结算奖励由服务端发 |
| `Client/Gameplay/DeployController.cs`、`BattleController.cs` | 布阵后直接开战，`PlayerWon` 由本地内核判定 | 开战前 `POST battles` 登记布阵、拿种子；结束后提交指令，胜负和奖励以服务端为准（本地结果只用于播放） |
| 启动流程 | 直接进主菜单 | 先进登录界面（Steam 自动登录、失败重试），新增网络层：令牌刷新、`Idempotency-Key`、断线重连、被踢下线 |

### 还没合并的 PR

| PR | 现在 | 要改成 |
|---|---|---|
| #4 主菜单与本地存档位 | `ProfileStore` 把存档存在本机 `saves/slot<N>.json`；启动读取最近玩过的本地存档；`GameApp.Update` 本地累计游戏时长 | 存档位、新建指挥官、删除存档改为调服务端接口（`/v1/saves`）；界面和三个存档位的设计可以保留；游戏时长定期报给服务端 |
| #5 设置界面 | 设置全部存在本机 `settings.json` | 画质、分辨率等仍然留在本机（跟着电脑走）；语言和以后的“分享我的通关录像”等同步到服务端 `account_settings` |
| #6 通关录像方案 | 断网时录像先存本地队列；有单独的上传接口，上传后服务端再重跑 | 不需要本地队列和上传接口：战斗本来就要在服务端结算，结算后直接收进录像榜（第 3 节）；录像校验类库（`Starline.Replay`）并入本文的 `Starline.Server.Game`，或者由它引用，避免两份重跑代码；录像相关的 UOS 部署和本文第 6 节共用一套 |

### 文档

| 位置 | 改动 |
|---|---|
| `docs/game-design.md` 开头“类型”、1.2 设计目标、4.2“玩家阵容”节点、第 12 节、14.2 | ✅ 去掉“单机”“不联网也能完整游玩”，改为需要联网登录 |
| `docs/decisions.md` | ✅ 新增决策 49；决策 48 标为已作废。决策 3（单机为主）和 42（不做账号登录）里的“单机 / 没有账号”由 49 取代 |
| `README.md`、`CLAUDE.md` 开头的“单机奇幻战略游戏” | ✅ 改为“需要联网登录” |
| `docs/client-architecture.md`（PR #4、#5 里的“存档位”“设置”两节） | 随 PR #4、#5 的改动一起更新 |
| `docs/claude-project-instructions.md` | 还是早期“单机科幻战略游戏”的版本，整份已过时，建议以后按 CLAUDE.md 重写 |
