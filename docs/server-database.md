# 服务端数据结构（UOS CRUD Save / MySQL）

开发者要求：服务端用 C# 开发，数据库用 MySQL，先设计表结构。之后定为服务端全部托管在 UOS（决策 50）：数据库起步用 CRUD Save（文档存储），量大后换 CRUD Storage（托管 MySQL）。本文第 0 节说明两个阶段，第 11 节是 CRUD Save 阶段的文档结构；第 1~10 节是 MySQL 阶段的表，建表语句在 [`server/db/schema.sql`](../server/db/schema.sql)。

这一步只有数据结构设计，还没有服务端代码。玩家必须联网登录才能玩（决策 49），服务端怎么登录、怎么结算、有哪些接口，见 [server-design.md](server-design.md)。通关录像的业务规则（重跑验证、榜单）见 [replay-server.md](replay-server.md)（PR #6 的方案草案）；录像相关的表收在本文第 6 节，CRUD Save 阶段的放法见第 11 节。

---

## 0. 两个阶段

| 阶段 | 数据库 | 什么时候 | 数据怎么放 |
|---|---|---|---|
| 一：上线初期 | UOS **CRUD Save**（按用量计费的文档存储，起步每月几块钱） | 现在起 | 一个存档一份文档，整份读写（第 11 节） |
| 二：玩家多了 | UOS **CRUD Storage**（托管 MySQL，1 核约 3.20 元/天） | 出现下面任一情况：要做跨玩家的排行和天梯查询；运营要按条件查数据；日志量让按次计费变贵；碰到 CRUD Save 的限制（文档大小、查询能力） | 本文第 1~10 节的 27 张表 |

- **两个阶段的数据内容完全一样**，只是放法不同：第 11 节给出了每份文档对应哪几张表。
- **只有云函数能读写数据库**，客户端不直接连。
- 服务端的规则代码只通过数据层接口读写（[server-design.md](server-design.md) 第 6 节），换阶段时只换数据层的实现。
- 迁移：停服维护，导出 CRUD Save 的全部文档，用一个导入脚本拆成表写进 MySQL，切换数据层配置后开服。每天的 JSON 备份也是同一种格式，可以先拿备份演练。

---

## 1. 总体约定

| 项目 | 约定 | 理由 |
|---|---|---|
| 版本 | MySQL 8.0 以上（已在 8.4 LTS 上建表验证；CRUD Storage 的 MySQL 版本到时确认） | 要用 `JSON`、`CHECK` 约束和生成列 |
| 引擎 / 字符集 | InnoDB，`utf8mb4` | 昵称、指挥官名字是中文，也可能有 emoji |
| 配置表 id | `VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin` | 角色、关卡、符文的 id 来自 `StarlineData` 的 JSON，只有英文；ascii 索引更小，区分大小写和内核一致 |
| 时间 | `DATETIME(3)`，一律 UTC，由服务端写入 | 炼金工坊的研究计时以服务端时间为准，不信任客户端时钟 |
| 主键 | `BIGINT UNSIGNED` 自增 | 单库足够；对外暴露的录像另有随机的 `public_id`，避免被按顺序遍历 |
| 外键 | 只在“父行删了子行必须一起删”的地方用 `ON DELETE CASCADE` | 注销账号、删存档时一条语句清干净；其余关系靠服务端保证，减少锁 |
| 数值规则 | 等级上限、升星消耗等**不写进数据库**，由服务端读同一份配置表校验 | 数值和文本放 JSON 配置表（项目约定），改数值不用改表 |

### 数据挂在哪里

```
account（账号）
 ├─ account_auth          登录方式（Steam）
 ├─ account_session       刷新令牌
 ├─ account_entitlement   本体和 DLC
 ├─ account_settings      设置（所有存档共用）
 ├─ replay …              通关录像（按账号，不按存档）
 └─ save_slot（存档位 1~3，每个是一名指挥官）
     ├─ save_currency / save_item          货币、道具
     ├─ save_hero / save_hero_star_log     角色、升星记录
     ├─ save_rune                          符文
     ├─ save_stage / save_flag             关卡进度、剧情和奖励标记
     ├─ save_unlock / save_codex           奖池解锁、图鉴
     ├─ save_expedition / …_record         远征
     ├─ save_tower / save_tower_floor      星辉高塔
     ├─ save_lab_research                  炼金工坊
     ├─ save_action / save_battle          操作日志、战斗记录（服务端重跑）
     └─ economy_log                        资源流水
```

客户端现在有 3 个存档位，每个存档是一名有名字的指挥官（决策 42），设置所有存档共用（决策 43）。服务端照搬这个结构，存档从本地文件搬到服务端：**养成数据挂在存档上，设置和录像挂在账号上**。

---

## 2. 账号与登录

| 表 | 用途 |
|---|---|
| `account` | 一个玩家一行：昵称、状态（正常 / 封禁 / 注销）、最后登录时间和 IP |
| `account_auth` | 登录方式：`steam`（`provider_uid` 存 SteamID64）、`wechat`（微信小游戏）、`apple` 等，`(provider, provider_uid)` 唯一，同一账号可以绑多个。经过 Passport 登录时，`passport` 这一行存 Passport 的玩家 id |
| `account_session` | 刷新令牌，只存 SHA-256；访问令牌用短期 JWT，不落库。**一个账号同一时间只有一个有效会话**：新登录时作废旧会话，旧电脑被踢下线，这样不会有两台电脑同时改一个存档 |
| `account_entitlement` | 已购内容（`base`、以后的 DLC），登录时按 Steam 的拥有情况同步 |
| `account_settings` | 跨设备有意义的设置 |

**登录流程**（沿用 replay-server.md 第 6、12 节）：客户端用 Steamworks 取 Web API 票据 → 云函数调 Steam `AuthenticateUserTicket` 换出 SteamID → Passport 外部 ID 登录 → 查 `account_auth`，没有就新建 `account` → 云函数签发自己的访问令牌和刷新令牌。微信小游戏直接走 Passport 的微信登录。不做用户名密码注册；`dev` 这个 provider 只给开发测试用。

**设置只同步一部分**：客户端的 `settings.json` 里大部分是画质、分辨率、显示模式，跟着电脑走，换一台电脑不该照搬，所以不上传。服务端只存：

- `share_replays`：“分享我的通关录像”开关，服务端收录像前要检查，所以单独成列
- `replay_notice_seen`：第一次上传前的说明是否看过
- `prefs`（JSON）：音量、战斗倍速、特写开关等，服务端不解释，客户端自己读写

---

## 3. 存档位与角色

### save_slot（存档位 / 指挥官）

对应客户端的 `CommanderProfile`：指挥官名字、游戏时长、远征次数和胜场、创建和最近游玩时间。

- 每个账号最多 3 个**有效**存档。删除存档是软删除（`deleted_at`），生成列 `active_slot` 在删除后变成 NULL，唯一索引 `(account_id, active_slot)` 只约束没删的存档，所以删了以后同一个位置可以马上新建。软删除的存档留一段时间给客服恢复，再由定时任务真正删除（级联清掉所有子表）。
- `revision`：每次修改存档 +1，每个返回都带上。客户端发现和自己缓存的不一致（比如在别的电脑上玩过），就重新拉完整快照。

### 货币与道具

- `save_currency`：金币、局外货币等，一种一行，`CHECK (amount >= 0)` 防止扣成负数。加新货币不用改表。
- `save_item`：按数量计的东西，比如炼金工坊的遗骸。

### save_hero（角色）

一名角色一行，主键 `(save_id, hero_id)`，`hero_id` 是 `units.json` 里的 id。

| 列 | 说明 |
|---|---|
| `level` / `exp` | 等级 1~60，`exp` 是当前等级内的经验 |
| `star` | ★1~★5 |
| `shards` | 持有的本角色碎片（还没花掉的） |
| `recruited_at` | 招募时间。**拿到第一个碎片就建行**，`recruited_at` 为空表示还没招募、只是在攒碎片（敌对阵营的角色主要靠碎片召唤，设计文档 4.8） |
| `recruit_source` | 剧情加入 / 碎片召唤 |
| `affinity` | 好感，图鉴档案随好感和星级解锁（story.md 第 5 节） |
| `skin_id` | 外观，NULL 是默认 |

等级上限（★1 为 20，每升一星 +10）和升星消耗（20 / 40 / 80 / 120 碎片）只做 `level`、`star` 的范围检查，具体规则由服务端读配置表校验。

### save_hero_star_log（升星记录）

每升一星写一行：从几星到几星、花了多少碎片、什么时候。用来对账和客服查询；个人小故事随升星逐段解锁，解锁到第几段直接由当前星级决定，不需要单独存。

### save_rune（符文）

每一枚符文一行（`rune_uid`），而不是按种类计数：以后符文可能有随机词条，而且需要知道“这一枚装在谁身上”。

- `equipped_hero` + `equipped_slot` 都为空表示在背包里；`CHECK` 保证两者同时为空或同时有值。
- 唯一索引 `(save_id, equipped_hero, equipped_slot)` 保证一个槽只能装一枚。
- 槽 2、槽 3 在 ★2、★3 解锁、双向装配限制（设计文档 8.5）由服务端校验。

---

## 4. 主线关卡、剧情、解锁、图鉴

| 表 | 用途 |
|---|---|
| `save_stage` | 每关一行：历史最好的三颗星（`stars_mask`，三个条件各占一位）、通关次数、扫荡次数、本存档通关用过的最低战力、首通时间。`fail_count` 是连续失败次数，失败 2 次后提示“参考录像”（replay-server.md 第 2 节），通关后清零 |
| `save_flag` | 所有“只发生一次”的事：剧情已读（`story:ch1:intro`）、章节宝箱 / 首通 / 高塔里程碑奖励已领（`reward:…`）、教学完成（`tutorial:…`）。用 key 区分，不必为每种事单独建表；主键保证同一个奖励不会领两次 |
| `save_unlock` | 新角色、新符文、新指挥官、新指挥官技能**加入奖池**（设计文档 4.6），记录来源（剧情、炼金工坊、高塔） |
| `save_codex` | 图鉴：角色、敌人、符文、指挥官技能。`state` 1 = 见过（显示剪影），2 = 已获得 / 已解析；只增不减 |

图鉴和奖池解锁分两张表：图鉴是“见没见过、拿没拿到”，奖池是“远征里能不能抽到”。比如炼金工坊研究到 60% 时符文进奖池，100% 时敌人才进图鉴（设计文档第 10 节），两者时间点不同。

---

## 5. 远征、星辉高塔、炼金工坊、操作日志

| 表 | 用途 |
|---|---|
| `save_expedition` | **进行中**的一局远征，由服务端用内核的 `ExpeditionRun` 推进，一个存档最多一局。局内状态（地图、耐久、临时角色、符文、技能、走到哪个节点）整体存成一份内核序列化的快照（`state`，gzip JSON），不拆表：局内数据只在这一局有效，结构随玩法变化也快，拆表反而难维护。带 `kernel_version`，内核升级后服务端知道怎么读老快照 |
| `save_expedition_record` | 每局结束写一行：威胁等级、走到第几幕、输赢、首发 3 名角色。算“最高威胁等级”和成就用 |
| `save_tower` | 星辉高塔总进度：最高层、总星数、最近存档点、已装的塔层祝福（JSON，最多 6 个）。`(highest_floor, total_stars)` 有索引，给以后的排行榜用 |
| `save_tower_floor` | 每层的星级（三个条件各占一位）和首通时间 |
| `save_lab_research` | 炼金工坊研究：研究对象、基础时长、累计缩短的秒数（实战缩短 + 遗骸加速）、已领取到哪一档成果（25 / 60 / 100%）。剩余时间 = 基础时长 − 已缩短 − 已经过的时间，**服务端按 UTC 计算**，改本机时间没用。研究槽同一时间只能有一个进行中的研究，用和存档位一样的生成列唯一索引保证 |

### 操作日志与战斗记录

养成数据全部以服务端为准（决策 49），做法见 [server-design.md](server-design.md)。

| 表 | 用途 |
|---|---|
| `save_action` | 每个修改请求一行（成功和失败都记）：类型、参数、结果或错误码、执行后的 revision、哪个会话发的。`request_id` 是请求头里的 `Idempotency-Key`，唯一索引 `(save_id, request_id)` 让断线重发不会把同一个请求执行两次（第二次直接返回 `result`）。保留最近 90 天 |
| `save_battle` | 每场主线 / 高塔 / 远征战斗。**开始时登记**：服务端核对布阵后把它（连同当时的等级、星级、符文）、种子、配置表版本、战力存下来，状态“进行中”；**结束时**客户端只交带帧号的指令，服务端重跑，写入胜负、星级、帧数、状态哈希，状态“已结算”。超过 `expires_at`（默认 30 分钟）没交的由定时任务标为过期。生成列 `running_flag` 加唯一索引，保证一个存档同时只有一场进行中的战斗。奖励只按重跑结果发；通关录像从这里挑，不用再上传 |

`economy_log.action_id` 指回引起变动的操作，查“这 50 个碎片是哪来的”可以一路追到哪场战斗。

---

## 6. 通关录像

规则见 [replay-server.md](replay-server.md)，这里只说表。

| 表 | 用途 |
|---|---|
| `game_data_version` | 服务端支持的“内核版本 + 配置表哈希”。状态：当前 / 兼容（发布新版本后短时间保留，让更新前开始的战斗还能结算）/ 停用 |
| `replay` | 录像本体和列表页需要的摘要 |
| `replay_like` | 点赞，一人一份只能赞一次 |
| `replay_report` | 举报（昵称不当、疑似作弊），一人一份只能举报一次 |

`replay` 的要点：

- 不需要单独上传：战斗结算后，玩家开着分享开关且战力能进榜，服务端自动从 `save_battle` 收进来（`source_battle_id` 指回那场战斗）。
- 录像只有 1~3 KB（只存关卡、种子、布阵和带帧号的指令），gzip 后直接放 `MEDIUMBLOB`，不需要另外的文件存储。
- `power` 是**服务端重跑后算出的**战力，不是客户端报的数。
- `roster_summary`（JSON）是列表页要显示的阵容缩略，列表接口不用解压录像本体。
- 唯一索引 `(stage_id, data_hash, account_id)`：同一玩家在同一关、同一数据版本只保留最好的一份，新录像更好就覆盖。
- 榜单查询 `WHERE stage_id=? AND data_hash=? AND status=1 ORDER BY power, created_at LIMIT 20` 走 `idx_replay_board`，已用 `EXPLAIN` 确认；“只看未用技能”走 `idx_replay_board_noskill`。
- 版本更新后老录像用新数据重跑：还能赢的复制一份到新 `data_hash`（`migrated_from` 指回原录像），打不赢的标 `status = 3`（旧版本）。
- 录像挂在账号上而不是存档上：榜单展示的是玩家（Steam 昵称），删存档不该删掉已经上榜的录像。玩家在设置里“删除我上传的全部录像”时标 `status = 4`。

---

## 7. 资源流水 economy_log

货币、道具、碎片、经验的每次增减写一行：变化量、变化后的余额、原因（通关、扫荡、升星、商店、炼金工坊……）和关联对象。只追加、不修改。

- 玩家说“我的碎片少了”时能查清楚；发现异常增长时能定位是哪个接口。
- 这是增长最快的表。上线初期量不大；以后按月分区（分区表不能有外键，所以这张表本来就没加外键），或者定期把半年前的数据导到冷存储。

---

## 8. 已验证

在 Docker 的 MySQL 8.4 里执行 `schema.sql`，27 张表全部建成，并做了冒烟测试：

- 删除存档后同一存档位可以新建；两个有效存档占同一个位置会被唯一索引拒绝
- 符文只填角色不填槽位会被 `CHECK` 拒绝
- 删除账号后存档、角色、符文、录像全部级联删除
- 榜单查询走 `idx_replay_board`
- 同一个 `Idempotency-Key` 的请求不能写两次
- 同一存档已有一场进行中的战斗时，再登记一场会被唯一索引拒绝；前一场结算后可以登记新的

本地验证命令：

```
docker run -d --name starline-db -e MYSQL_ROOT_PASSWORD=pw -v "$PWD/server/db:/sql:ro" mysql:8.4
docker exec starline-db sh -c 'mysql -uroot -ppw < /sql/schema.sql'
```

---

## 9. 已定和还没定的事

1. ~~养成数据以谁为准~~：**已定（决策 49）**，玩家必须联网登录才能玩，服务端是唯一的存档。做法见 [server-design.md](server-design.md)。
2. 主线关卡和精英关卡的 id 格式（暂定 `ch1-03`、`ch1-e1`），等关卡配置表出来再定。
3. 排行榜（高塔最高层、总星数）按存档还是按账号算：现在 `save_tower` 是按存档存的，默认取一个账号里最好的存档上榜。

---

## 10. 下一步

见 [server-design.md](server-design.md) 第 8 节：先搭服务端骨架（登录、存档），再做战斗结算。

---

## 11. 第一阶段：CRUD Save 的文档结构

CRUD Save 是文档存储，没有多表事务和行锁。按“一次操作尽量只读写一份文档”来放数据：**一个存档的全部养成数据放在一份文档里**，整份读、整份写。

### 集合

| 集合 | 文档键 | 内容 | 对应第二阶段的表 |
|---|---|---|---|
| `accounts` | 账号 id | 昵称、状态和封禁、登录方式列表、已购内容、设置、**当前会话**（会话 id、刷新令牌哈希、过期时间、客户端版本）、存档位摘要（存档 id、位置、指挥官名字、时长、最近游玩） | `account`、`account_auth`、`account_session`、`account_entitlement`、`account_settings`、`save_slot` 的摘要列 |
| `identities` | `{provider}:{uid}`，如 `steam:7656119…` | 账号 id | `account_auth` 的唯一索引：登录时按键直接查，不用条件查询 |
| `saves` | 存档 id | 见下面的“存档文档” | `save_slot` 及其下全部 `save_*` 表 |
| `battles` | 战斗 id | 和 `save_battle` 的列一样：登记的布阵、种子、版本、战力、状态、指令、重跑结果、有效期 | `save_battle` |
| `logs` | 自动 id | 一次操作一条：存档 id、类型、参数、结果或错误码、revision、时间，以及这次的资源变化列表（货币、碎片、经验各变了多少、变化后余额） | `save_action`、`economy_log`、`save_hero_star_log`、`save_expedition_record` |
| `replays` | 录像 public_id | 和 `replay` 的列一样 | `replay` |
| `boards` | `{关卡 id}:{data_hash}` | 本关最低战力榜：排好序的前 20 份录像摘要、上榜门槛；“只看未用技能”另存一份前 20 | 替代 `idx_replay_board` 的排序查询 |
| `replayVotes` | `{public_id}:{账号 id}` | 点了赞没有、举报原因和处理状态 | `replay_like`、`replay_report` |
| `gameVersions` | data_hash | 内核版本、状态、发布时间 | `game_data_version` |

S1 已实现的部分（`Starline.Server.Data.CrudSave`）：`accounts`（文档键是账号 id，含当前会话）、`identities`（键 `uos:<Passport 玩家 id>` 或 `dev:<账号名>`）、`saves`（键 `<账号 id>:<存档位>`，现在只有下面 `commander` 里的字段）、`saves_deleted`（删除的存档，键后面加删除时间）、`idempotency`（键 `<账号 id>/<key>`，存修改请求的返回）。S1 的修改请求只碰一份文档，幂等记录先单独放一个集合；S2 开始做跨文档的战斗结算时，改成下面写的存档内 `recentRequests`。

### 存档文档

```json
{
  "saveId": "…", "accountId": "…", "slot": 1, "revision": 128,
  "commander": { "name": "加文", "playSeconds": 36000, "runsStarted": 12, "runsWon": 5,
                 "createdAt": "…", "lastPlayedAt": "…", "deletedAt": null },
  "currencies": { "gold": 3200, "meta": 40 },
  "items": { "remains_ghoul": 6 },
  "heroes": { "knight_aldric": { "level": 25, "exp": 120, "star": 2, "shards": 14,
                                 "affinity": 30, "skin": null, "recruitedAt": "…", "source": "story" } },
  "runes": [ { "uid": 17, "id": "r_guard_1", "hero": "knight_aldric", "slot": 1 } ],
  "stages": { "ch1-03": { "stars": 7, "clears": 9, "fails": 0, "sweeps": 4, "bestPower": 1840, "firstClearedAt": "…" } },
  "flags": { "story:ch1:intro": 1 },
  "unlocks": { "hero": ["frigate"], "rune": [], "commander": [], "skill": [] },
  "codex": { "enemy:ghoul": 2 },
  "tower": { "highest": 23, "stars": 61, "checkpoint": 20, "blessings": [], "floors": { "1": 7, "2": 7 } },
  "lab": [ { "id": 3, "slot": 1, "target": "ghoul", "baseSeconds": 43200, "reduced": 3600,
             "claimed": 25, "startedAt": "…", "completedAt": null } ],
  "expedition": null,
  "runningBattleId": null,
  "recentRequests": [ { "key": "…", "type": "hero.star_up", "revision": 127, "result": { } } ]
}
```

- **大小**：45 名角色、约 100 关、150 条图鉴、几十枚符文、一局远征快照、最近 50 个请求，估计 30~80 KB。要确认 CRUD Save 的单文档上限。
- **并发**：写回时带上读到的 `revision` 做条件写，revision 变了就重读重做（乐观锁）。如果 CRUD Save 不支持条件写，就靠“一个账号只在一台电脑登录 + 客户端一次只发一个修改请求”保证不冲突，这一点 S0 阶段要先验证。
- **幂等**：`recentRequests` 保留最近 50 个请求的 `Idempotency-Key` 和结果，和存档在同一次写入里更新，所以“执行了但没记下”或“记下了但没执行”都不会发生。完整的历史在 `logs` 里。
- **跨文档的操作**（结算战斗要写 `saves` 和 `battles` 两份）：先写存档（`recentRequests` 里记下战斗 id），再把战斗标为已结算。中间失败时，客户端用同一个 key 重发，云函数在 `recentRequests` 里看到已经结算过，直接返回结果并补上战斗的状态。
- **录像榜**：如果 CRUD Save 不能“按关卡筛选、按战力排序取前 20”，就由云函数维护 `boards` 里排好序的榜单文档（新录像进榜时条件写更新）。即使能查，`boards` 也让读榜只要一次读取，CDN 缓存也方便。
- **不做的事**：跨玩家的排行（高塔最高层、天梯）在第一阶段只做“每个榜一份排好序的文档”的简单版本；需要复杂查询时就是升级到第二阶段的时机。
