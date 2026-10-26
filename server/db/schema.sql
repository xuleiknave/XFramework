-- Starline 服务端数据库表结构（MySQL 8.0 / 8.4 LTS）
-- 用于第二阶段（UOS CRUD Storage，托管 MySQL）；第一阶段用 CRUD Save 文档存储，结构见 docs/server-database.md 第 11 节
-- 说明文档：docs/server-database.md
--
-- 约定
--   * 引擎 InnoDB，字符集 utf8mb4；配置表 id（角色、关卡、符文……）用 ascii_bin，省空间、区分大小写
--   * 时间一律存 UTC，DATETIME(3)；由服务端写入，不依赖数据库时区
--   * 主键 BIGINT UNSIGNED 自增；对外暴露的 id（录像）另加不可猜的 public_id
--   * 存档数据都挂在 save_slot 上（一个账号 3 个存档位），设置挂在账号上（所有存档共用）
--   * 外键只用在“删父表时子表必须一起删”的地方（ON DELETE CASCADE），其余靠服务端保证

SET NAMES utf8mb4;

CREATE DATABASE IF NOT EXISTS starline
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_0900_ai_ci;

USE starline;

-- ============================================================================
-- 1. 账号与登录
-- ============================================================================

-- 账号：一个玩家一行。昵称取自 Steam，登录时刷新
CREATE TABLE account (
  account_id      BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  nickname        VARCHAR(64)      NOT NULL DEFAULT '' COMMENT '展示用昵称（Steam 昵称快照）',
  language        VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'zh-Hans',
  status          TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 正常 1 封禁 2 已注销（数据待清理）',
  ban_until       DATETIME(3)      NULL     COMMENT '封禁到期时间；NULL 且 status=1 表示永久',
  ban_reason      VARCHAR(255)     NULL,
  created_at      DATETIME(3)      NOT NULL,
  last_login_at   DATETIME(3)      NULL,
  last_login_ip   VARBINARY(16)    NULL     COMMENT 'INET6_ATON() 的结果，IPv4/IPv6 通用',
  PRIMARY KEY (account_id),
  KEY idx_account_status (status)
) ENGINE=InnoDB COMMENT='玩家账号';

-- 登录方式：现在只有 steam，以后移植手机或小游戏时加 provider，同一账号可以绑多个
CREATE TABLE account_auth (
  auth_id         BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  account_id      BIGINT UNSIGNED  NOT NULL,
  provider        VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'steam / wechat / apple / passport / dev（开发测试用）',
  provider_uid    VARCHAR(64)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'SteamID64 等',
  created_at      DATETIME(3)      NOT NULL,
  last_used_at    DATETIME(3)      NULL,
  PRIMARY KEY (auth_id),
  UNIQUE KEY uk_auth_provider_uid (provider, provider_uid),
  KEY idx_auth_account (account_id),
  CONSTRAINT fk_auth_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='第三方登录绑定';

-- 登录会话：访问令牌用短期 JWT 不落库；这里存刷新令牌（只存哈希）
-- 一个账号同一时间只有一个有效会话：新登录时把旧会话的 revoked_at 填上（旧电脑被踢下线）
CREATE TABLE account_session (
  session_id      BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  account_id      BIGINT UNSIGNED  NOT NULL,
  refresh_hash    BINARY(32)       NOT NULL COMMENT '刷新令牌的 SHA-256',
  client_version  VARCHAR(32)      NOT NULL DEFAULT '',
  device_hint     VARCHAR(64)      NOT NULL DEFAULT '' COMMENT '平台 / 机器名摘要，方便玩家识别和踢下线',
  created_at      DATETIME(3)      NOT NULL,
  expires_at      DATETIME(3)      NOT NULL,
  revoked_at      DATETIME(3)      NULL,
  PRIMARY KEY (session_id),
  UNIQUE KEY uk_session_refresh (refresh_hash),
  KEY idx_session_account (account_id, expires_at),
  CONSTRAINT fk_session_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='登录会话（刷新令牌）';

-- 已购内容：本体和以后的 DLC（新阵营、新行省），登录时按 Steam 拥有情况同步
CREATE TABLE account_entitlement (
  account_id      BIGINT UNSIGNED  NOT NULL,
  sku             VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'base / dlc_xxx',
  granted_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (account_id, sku),
  CONSTRAINT fk_entitlement_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='已购内容';

-- 设置：所有存档共用（与客户端 settings.json 一致）
-- 画质、分辨率等跟着电脑走，只留在本地，不上传；这里只放跨设备有意义的项
CREATE TABLE account_settings (
  account_id      BIGINT UNSIGNED  NOT NULL,
  share_replays   TINYINT(1)       NOT NULL DEFAULT 1 COMMENT '分享我的通关录像（服务端上传前要检查）',
  replay_notice_seen TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '第一次上传前的说明是否已看过',
  prefs           JSON             NULL     COMMENT '其余偏好：音量、战斗倍速、特写开关等，客户端自行解释',
  revision        INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT '每次修改 +1，用于多端合并',
  updated_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (account_id),
  CONSTRAINT fk_settings_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='账号设置';

-- ============================================================================
-- 2. 存档位（指挥官）
-- ============================================================================

-- 每个账号最多 3 个存档位，每个是一名有名字的指挥官（客户端 CommanderProfile）
-- 删除存档是软删除：deleted_at 有值后 active_slot 变 NULL，同一位置可以新建
CREATE TABLE save_slot (
  save_id         BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  account_id      BIGINT UNSIGNED  NOT NULL,
  slot_no         TINYINT UNSIGNED NOT NULL COMMENT '1~3',
  commander_name  VARCHAR(16)      NOT NULL COMMENT '1~8 个字',
  play_seconds    INT UNSIGNED     NOT NULL DEFAULT 0,
  runs_started    INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT '远征次数',
  runs_won        INT UNSIGNED     NOT NULL DEFAULT 0,
  revision        INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT '存档每次修改 +1；客户端发现和缓存的不一致就重新拉快照',
  created_at      DATETIME(3)      NOT NULL,
  last_played_at  DATETIME(3)      NOT NULL,
  deleted_at      DATETIME(3)      NULL,
  active_slot     TINYINT UNSIGNED AS (IF(deleted_at IS NULL, slot_no, NULL)) STORED,
  PRIMARY KEY (save_id),
  UNIQUE KEY uk_save_active_slot (account_id, active_slot),
  KEY idx_save_account (account_id, last_played_at),
  CONSTRAINT fk_save_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE,
  CONSTRAINT ck_save_slot_no CHECK (slot_no BETWEEN 1 AND 3)
) ENGINE=InnoDB COMMENT='存档位 / 指挥官';

-- 货币：金币、局外货币等，一种一行，加新货币不用改表
CREATE TABLE save_currency (
  save_id         BIGINT UNSIGNED  NOT NULL,
  currency_id     VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'gold / meta …',
  amount          BIGINT           NOT NULL DEFAULT 0,
  updated_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (save_id, currency_id),
  CONSTRAINT fk_currency_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE,
  CONSTRAINT ck_currency_amount CHECK (amount >= 0)
) ENGINE=InnoDB COMMENT='存档货币';

-- 可堆叠道具：遗骸、符文图纸等按数量计的东西
CREATE TABLE save_item (
  save_id         BIGINT UNSIGNED  NOT NULL,
  item_id         VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  count           INT UNSIGNED     NOT NULL DEFAULT 0,
  updated_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (save_id, item_id),
  CONSTRAINT fk_item_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='存档道具（可堆叠）';

-- ============================================================================
-- 3. 角色：等级、星级、碎片
-- ============================================================================

-- 一名角色一行。拿到第一个碎片就建行（recruited_at 为空表示还没招募，只是在攒碎片）
-- 等级 1~60，星级 1~5，等级上限 = 20 + (星级-1)×10（数值以配置表为准，服务端校验）
CREATE TABLE save_hero (
  save_id         BIGINT UNSIGNED   NOT NULL,
  hero_id         VARCHAR(32)       CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'units.json 的 id',
  level           SMALLINT UNSIGNED NOT NULL DEFAULT 1,
  exp             INT UNSIGNED      NOT NULL DEFAULT 0 COMMENT '当前等级内的经验',
  star            TINYINT UNSIGNED  NOT NULL DEFAULT 1,
  shards          INT UNSIGNED      NOT NULL DEFAULT 0 COMMENT '持有的本角色碎片（未消耗的）',
  affinity        INT UNSIGNED      NOT NULL DEFAULT 0 COMMENT '好感，图鉴档案随好感和星级解锁',
  skin_id         VARCHAR(32)       CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '外观；NULL 为默认',
  recruited_at    DATETIME(3)       NULL,
  recruit_source  VARCHAR(16)       CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT 'story / shards / other',
  updated_at      DATETIME(3)       NOT NULL,
  PRIMARY KEY (save_id, hero_id),
  CONSTRAINT fk_hero_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE,
  CONSTRAINT ck_hero_level CHECK (level BETWEEN 1 AND 60),
  CONSTRAINT ck_hero_star  CHECK (star BETWEEN 1 AND 5)
) ENGINE=InnoDB COMMENT='存档角色';

-- 升星记录：每升一星一行。用于对账、客服查询和“个人小故事”按升星时间排序
CREATE TABLE save_hero_star_log (
  log_id          BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  hero_id         VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  from_star       TINYINT UNSIGNED NOT NULL,
  to_star         TINYINT UNSIGNED NOT NULL,
  shards_cost     INT UNSIGNED     NOT NULL,
  created_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (log_id),
  KEY idx_star_log_hero (save_id, hero_id),
  CONSTRAINT fk_star_log_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='升星记录';

-- 符文：每一枚是独立物品（以后可能有词条），装在某名角色的某个槽上
CREATE TABLE save_rune (
  rune_uid        BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  rune_id         VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '符文配置 id',
  equipped_hero   VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NULL,
  equipped_slot   TINYINT UNSIGNED NULL COMMENT '1~3；槽 2、3 分别在 ★2、★3 解锁',
  obtained_at     DATETIME(3)      NOT NULL,
  PRIMARY KEY (rune_uid),
  UNIQUE KEY uk_rune_equip (save_id, equipped_hero, equipped_slot),
  KEY idx_rune_save (save_id, rune_id),
  CONSTRAINT fk_rune_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE,
  CONSTRAINT ck_rune_equip CHECK ((equipped_hero IS NULL) = (equipped_slot IS NULL))
) ENGINE=InnoDB COMMENT='存档符文';

-- ============================================================================
-- 4. 主线关卡、剧情、解锁、图鉴
-- ============================================================================

-- 关卡进度：三幕九章约 90 关 + 精英关。stars_mask 三颗星各占一位
CREATE TABLE save_stage (
  save_id         BIGINT UNSIGNED  NOT NULL,
  stage_id        VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '如 ch1-03',
  stars_mask      TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'bit0~2 = 三个星级条件，历史最好',
  clear_count     INT UNSIGNED     NOT NULL DEFAULT 0,
  fail_count      INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT '连续失败次数，通关后清零；失败 2 次提示参考录像',
  sweep_count     INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT '扫荡次数',
  best_power      INT UNSIGNED     NULL     COMMENT '本存档通关这关用过的最低战力',
  first_cleared_at DATETIME(3)     NULL,
  last_played_at  DATETIME(3)      NOT NULL,
  PRIMARY KEY (save_id, stage_id),
  CONSTRAINT fk_stage_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='主线关卡进度';

-- 一次性事件：剧情已读、教学完成、首通 / 章节宝箱 / 高塔里程碑奖励已领取等
-- 用 key 区分，避免为每种“只发生一次”的事各建一张表
CREATE TABLE save_flag (
  save_id         BIGINT UNSIGNED  NOT NULL,
  flag_key        VARCHAR(64)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '如 story:ch1:intro、reward:ch1:chest2、tutorial:deploy',
  value           INT              NOT NULL DEFAULT 1,
  created_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (save_id, flag_key),
  CONSTRAINT fk_flag_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='一次性事件标记';

-- 奖池解锁：新角色、新符文、新指挥官、新指挥官技能“加入奖池”（远征里能抽到）
CREATE TABLE save_unlock (
  save_id         BIGINT UNSIGNED  NOT NULL,
  unlock_type     VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'hero / rune / commander / skill',
  target_id       VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  source          VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '' COMMENT 'story / lab / tower …',
  unlocked_at     DATETIME(3)      NOT NULL,
  PRIMARY KEY (save_id, unlock_type, target_id),
  CONSTRAINT fk_unlock_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='奖池解锁';

-- 图鉴：角色、敌人、符文、指挥官技能。state 只增不减
CREATE TABLE save_codex (
  save_id         BIGINT UNSIGNED  NOT NULL,
  entry_type      VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'hero / enemy / rune / skill',
  entry_id        VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  state           TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT '1 见过（剪影） 2 已获得 / 已解析',
  first_seen_at   DATETIME(3)      NOT NULL,
  obtained_at     DATETIME(3)      NULL,
  PRIMARY KEY (save_id, entry_type, entry_id),
  CONSTRAINT fk_codex_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='图鉴';

-- ============================================================================
-- 5. 远征、星辉高塔、炼金工坊、操作日志、战斗记录
-- ============================================================================

-- 进行中的远征：一个存档最多一局。局内状态整体存一份快照（由内核序列化），不拆表
CREATE TABLE save_expedition (
  save_id         BIGINT UNSIGNED  NOT NULL,
  seed            BIGINT UNSIGNED  NOT NULL,
  threat_level    TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '威胁等级 0~20',
  act             TINYINT UNSIGNED NOT NULL DEFAULT 1,
  state           MEDIUMBLOB       NOT NULL COMMENT '局内快照（gzip JSON）：地图、耐久、阵容、符文、技能……',
  state_format    SMALLINT UNSIGNED NOT NULL DEFAULT 1,
  kernel_version  VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  started_at      DATETIME(3)      NOT NULL,
  updated_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (save_id),
  CONSTRAINT fk_expedition_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='进行中的远征';

-- 远征结算记录：每局结束写一行，用来算最高威胁等级、发成就
CREATE TABLE save_expedition_record (
  record_id       BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  threat_level    TINYINT UNSIGNED NOT NULL,
  reached_act     TINYINT UNSIGNED NOT NULL,
  won             TINYINT(1)       NOT NULL,
  starters        JSON             NOT NULL COMMENT '首发 3 名角色及等级、星级',
  started_at      DATETIME(3)      NOT NULL,
  ended_at        DATETIME(3)      NOT NULL,
  PRIMARY KEY (record_id),
  KEY idx_exp_record_save (save_id, ended_at),
  CONSTRAINT fk_exp_record_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='远征结算记录';

-- 星辉高塔总进度
CREATE TABLE save_tower (
  save_id         BIGINT UNSIGNED   NOT NULL,
  highest_floor   SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  total_stars     INT UNSIGNED      NOT NULL DEFAULT 0,
  checkpoint_floor SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '最近的存档点（每 10 层）',
  blessings       JSON              NULL COMMENT '已装上的塔层祝福 id 列表，最多 6 个',
  updated_at      DATETIME(3)       NOT NULL,
  PRIMARY KEY (save_id),
  KEY idx_tower_rank_floor (highest_floor, total_stars),
  CONSTRAINT fk_tower_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='星辉高塔进度';

-- 星辉高塔每层的星级
CREATE TABLE save_tower_floor (
  save_id         BIGINT UNSIGNED   NOT NULL,
  floor           SMALLINT UNSIGNED NOT NULL,
  stars_mask      TINYINT UNSIGNED  NOT NULL DEFAULT 0 COMMENT 'bit0 通关 bit1 损失≤2 bit2 特殊条件',
  first_cleared_at DATETIME(3)      NULL,
  PRIMARY KEY (save_id, floor),
  CONSTRAINT fk_tower_floor_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='星辉高塔每层星级';

-- 炼金工坊研究：初始 2 个研究槽，最多 4 个
-- 剩余时间 = base_seconds - reduced_seconds - (现在 - started_at)，由服务端按 UTC 计算
CREATE TABLE save_lab_research (
  research_id     BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  lab_slot        TINYINT UNSIGNED NOT NULL COMMENT '1~4',
  target_id       VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '被解析的敌人 / Boss 武器',
  base_seconds    INT UNSIGNED     NOT NULL,
  reduced_seconds INT UNSIGNED     NOT NULL DEFAULT 0 COMMENT '实战缩短 + 遗骸加速的累计秒数',
  claimed_stage   TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '已领取的阶段成果：0 / 25 / 60 / 100',
  started_at      DATETIME(3)      NOT NULL,
  completed_at    DATETIME(3)      NULL,
  active_slot     TINYINT UNSIGNED AS (IF(completed_at IS NULL, lab_slot, NULL)) STORED,
  PRIMARY KEY (research_id),
  UNIQUE KEY uk_lab_active_slot (save_id, active_slot),
  CONSTRAINT fk_lab_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='炼金工坊研究';

-- 操作日志：对存档的每一次修改请求（开始 / 结算战斗、扫荡、升星、买东西……）一行，成功和失败都记
-- (save_id, request_id) 唯一：request_id 是客户端请求头里的 Idempotency-Key，
-- 断线重发时同一个请求不会执行两次，直接返回上次的结果
-- 只保留最近一段时间（如 90 天），定期清理
CREATE TABLE save_action (
  action_id       BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  request_id      BINARY(16)       NOT NULL COMMENT 'Idempotency-Key（UUID）',
  session_id      BIGINT UNSIGNED  NOT NULL COMMENT '发起请求的登录会话',
  action_type     VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'battle.start / battle.finish / hero.star_up / lab.start …',
  payload         JSON             NOT NULL COMMENT '请求参数；战斗输入另存在 save_battle',
  battle_id       BIGINT UNSIGNED  NULL,
  status          TINYINT UNSIGNED NOT NULL COMMENT '1 成功 2 失败',
  error_code      VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT 'not_enough_shards / invalid_formation / locked …',
  result          JSON             NULL COMMENT '返回给客户端的结果（奖励、变化），重发时原样返回',
  revision_after  INT UNSIGNED     NOT NULL COMMENT '执行后存档的 revision',
  created_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (action_id),
  UNIQUE KEY uk_action_request (save_id, request_id),
  KEY idx_action_save_time (save_id, created_at),
  KEY idx_action_error (status, error_code, created_at),
  CONSTRAINT fk_action_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='存档操作日志';

-- 战斗记录：开始时登记（布阵由服务端按存档数据核对后固定），结束时提交指令，服务端重跑后结算
-- 奖励只按服务端重跑的结果发；通关录像从这里挑（不用再上传一遍）
-- 同一存档同时只能有一场进行中的战斗（uk_battle_running）
-- 只保留最近一段时间；被录像引用的那一份由 replay 表自己存一份，不受清理影响
CREATE TABLE save_battle (
  battle_id       BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  mode            VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'stage / tower / expedition',
  target_id       VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '关卡 id / 高塔层数 / 远征节点',
  data_hash       CHAR(16)         CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  kernel_version  VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  seed            BIGINT UNSIGNED  NOT NULL COMMENT '服务端下发',
  formation       JSON             NOT NULL COMMENT '开始时登记的布阵，含当时的等级、星级、符文',
  power           INT UNSIGNED     NOT NULL COMMENT '服务端按登记的布阵算出的上阵战力',
  status          TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT '1 进行中 2 已结算 3 放弃 4 过期',
  commands        MEDIUMBLOB       NULL COMMENT '结束时提交的带帧号的指令（gzip）',
  used_skill      TINYINT(1)       NULL,
  won             TINYINT(1)       NULL COMMENT '服务端重跑的结果',
  stars_mask      TINYINT UNSIGNED NULL,
  ticks           INT UNSIGNED     NULL,
  result_hash     BINARY(8)        NULL,
  verify_status   TINYINT UNSIGNED NULL COMMENT '1 与客户端一致 2 不一致（按服务端结果处理）',
  started_at      DATETIME(3)      NOT NULL,
  expires_at      DATETIME(3)      NOT NULL COMMENT '超过这个时间还没提交就标记过期（默认开始后 30 分钟）',
  finished_at     DATETIME(3)      NULL,
  running_flag    TINYINT UNSIGNED AS (IF(status = 1, 1, NULL)) STORED COMMENT '进行中为 1，其余为 NULL',
  PRIMARY KEY (battle_id),
  UNIQUE KEY uk_battle_running (save_id, running_flag),
  KEY idx_battle_save (save_id, started_at),
  KEY idx_battle_expire (status, expires_at),
  KEY idx_battle_target (mode, target_id, data_hash, won, power),
  CONSTRAINT fk_battle_save FOREIGN KEY (save_id) REFERENCES save_slot (save_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='战斗记录（开始登记、服务端重跑结算）';

-- ============================================================================
-- 6. 通关录像（方案见 docs/replay-server.md）
-- ============================================================================

-- 服务端支持的“内核 + 配置表”版本。录像、榜单都按 data_hash 分开
CREATE TABLE game_data_version (
  data_hash       CHAR(16)         CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '所有 StarlineData JSON 的哈希',
  kernel_version  VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  client_version  VARCHAR(32)      NOT NULL DEFAULT '' COMMENT '对应的游戏版本号，便于人看',
  status          TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT '1 当前 2 兼容（发布新版本后短时间保留，让更新前开始的战斗还能结算） 3 停用',
  released_at     DATETIME(3)      NOT NULL,
  PRIMARY KEY (data_hash)
) ENGINE=InnoDB COMMENT='内核和配置表版本';

-- 录像：只存输入（关卡、种子、布阵、带帧号的指令），1~3 KB，gzip 后直接放表里
-- 同一玩家、同一关、同一数据版本只保留最好的一份（uk_replay_owner）
CREATE TABLE replay (
  replay_id       BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  public_id       CHAR(22)         CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '对外 id（随机 128 位，base64url），接口里用它',
  stage_id        VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  data_hash       CHAR(16)         CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  kernel_version  VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  format          SMALLINT UNSIGNED NOT NULL DEFAULT 1,
  account_id      BIGINT UNSIGNED  NOT NULL,
  nickname        VARCHAR(64)      NOT NULL COMMENT '上传时的昵称快照',
  power           INT UNSIGNED     NOT NULL COMMENT '服务端重跑后算出的上阵战力（排名依据）',
  used_skill      TINYINT(1)       NOT NULL COMMENT '是否用了指挥官技能',
  hero_count      TINYINT UNSIGNED NOT NULL,
  roster_summary  JSON             NOT NULL COMMENT '阵容缩略：[{"unit":"..","star":2,"level":18}]，列表页直接用',
  ticks           INT UNSIGNED     NOT NULL COMMENT '战斗帧数（20 帧/秒）',
  result_hash     BINARY(8)        NOT NULL COMMENT 'BattleSim.ComputeStateHash()',
  body            MEDIUMBLOB       NOT NULL COMMENT '录像 JSON（gzip）',
  body_size       INT UNSIGNED     NOT NULL,
  like_count      INT UNSIGNED     NOT NULL DEFAULT 0,
  report_count    INT UNSIGNED     NOT NULL DEFAULT 0,
  status          TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT '1 正常 2 隐藏（举报 / 管理员） 3 旧版本（新数据重跑打不赢） 4 作者删除',
  source_battle_id BIGINT UNSIGNED NULL COMMENT '来自哪场已结算的战斗（save_battle），NULL 表示迁移生成',
  migrated_from   BIGINT UNSIGNED  NULL COMMENT '版本更新时从哪份老录像迁移来',
  created_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (replay_id),
  UNIQUE KEY uk_replay_public (public_id),
  UNIQUE KEY uk_replay_owner (stage_id, data_hash, account_id),
  KEY idx_replay_board (stage_id, data_hash, status, power, created_at),
  KEY idx_replay_board_noskill (stage_id, data_hash, status, used_skill, power, created_at),
  KEY idx_replay_account (account_id),
  CONSTRAINT fk_replay_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='通关录像';

-- 点赞：一人一份录像只能赞一次；replay.like_count 在同一事务里 +1
CREATE TABLE replay_like (
  replay_id       BIGINT UNSIGNED  NOT NULL,
  account_id      BIGINT UNSIGNED  NOT NULL,
  created_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (replay_id, account_id),
  KEY idx_like_account (account_id),
  CONSTRAINT fk_like_replay  FOREIGN KEY (replay_id)  REFERENCES replay (replay_id)   ON DELETE CASCADE,
  CONSTRAINT fk_like_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='录像点赞';

-- 举报：昵称不当、录像有问题。管理员处理后可隐藏录像或封号
CREATE TABLE replay_report (
  report_id       BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  replay_id       BIGINT UNSIGNED  NOT NULL,
  account_id      BIGINT UNSIGNED  NOT NULL COMMENT '举报人',
  reason          TINYINT UNSIGNED NOT NULL COMMENT '1 昵称不当 2 疑似作弊 3 其他',
  note            VARCHAR(255)     NULL,
  status          TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 待处理 1 已隐藏 2 驳回',
  created_at      DATETIME(3)      NOT NULL,
  handled_at      DATETIME(3)      NULL,
  PRIMARY KEY (report_id),
  UNIQUE KEY uk_report_once (replay_id, account_id),
  KEY idx_report_pending (status, created_at),
  CONSTRAINT fk_report_replay  FOREIGN KEY (replay_id)  REFERENCES replay (replay_id)   ON DELETE CASCADE,
  CONSTRAINT fk_report_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='录像举报';

-- ============================================================================
-- 7. 流水
-- ============================================================================

-- 资源变动流水：货币、道具、碎片、经验的每次增减。只追加，不改不删
-- 量大以后按月分区或定期归档到冷存储
CREATE TABLE economy_log (
  log_id          BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  save_id         BIGINT UNSIGNED  NOT NULL,
  kind            VARCHAR(16)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'currency / item / shard / exp',
  target_id       VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '货币 id / 道具 id / 角色 id',
  delta           BIGINT           NOT NULL,
  balance         BIGINT           NOT NULL COMMENT '变动后的余额',
  reason          VARCHAR(32)      CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'stage_clear / sweep / star_up / shop / lab / tower / expedition …',
  ref_id          VARCHAR(64)      CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '关联对象，如关卡 id、研究 id',
  action_id       BIGINT UNSIGNED  NULL COMMENT '由哪次操作引起（save_action）',
  created_at      DATETIME(3)      NOT NULL,
  PRIMARY KEY (log_id),
  KEY idx_economy_save (save_id, created_at),
  KEY idx_economy_target (save_id, kind, target_id)
) ENGINE=InnoDB COMMENT='资源变动流水';
