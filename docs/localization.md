# 本地化与翻译表

本地化方案（Unity Localization + 仓库里的 CSV 语言表）见 [client-architecture.md](client-architecture.md) 2.1 节和决策 50。这里讲翻译表本身：有哪些表、key 怎么起、怎么交给译者，以及中英文术语对照。

## 1. 四张表

都在 `Starline/Packages/com.starline.game/Localization/`，列为 `key, zh-Hans, en, note`。简体中文是源语言，`note` 列写给译者看（占位符含义、在哪里显示、是不是草案）。

| 表 | 条数 | 内容 |
|---|---|---|
| `UI.csv` | 129 | 界面文字（UXML 和客户端 C#） |
| `Game.csv` | 78 | 内核产生的文字：单位、指挥官技能、敌方阵容、远征第一幕剧情、远征消息、推演败因和建议 |
| `Characters.csv` | 448 | 5 个阵营、6 个职业、47 名角色（45 名 + 2 名传说角色）的名字、称号、专属技能、5 段小传 |
| `Story.csv` | 109 | 主线三幕九章：章名、简介、章末钩子、Boss、五根线、档案馆条目、结局、星线编年、诺娃的信 |

英文列现在全部填了**初稿**，发售前要请母语译者校对。

## 2. key 的规则

- 小写字母、数字、下划线，用点分层：`<类别>.<id>.<字段>`，例如 `hero.gavin.title`、`bio.gavin.3`、`story.chapter.6.hook`。
- 角色 id 用英文名（`gavin`、`big_tom`、`old_oak`），和 `units.json` 的单位 id 不是一回事：单位 id 是职业（`battleship` = 重甲骑士），角色 id 是人。
- 小传 `bio.<id>.1` 招募时就能看，`.2`~`.5` 在 ★2~★5 逐段解锁（设计文档 4.8 节）。尾声里 ★5 角色的个人结局从第 5 段延伸。
- 占位符 `{0}`、`{1}` 的含义写在 `note` 里；译文可以调换顺序，但不能增减（测试会查）。

## 3. 文本的源头

- 角色小传和主线剧情的设定写在 [characters.md](characters.md)、[story.md](story.md)，那里是设计稿；**游戏里显示的文字以 CSV 为准**。改设定时两边一起改。
- `Story.csv` 里 `note` 标了“草案”的条目是整理时新写的（打结时五人的台词、各阵营尾声、雷恩的告别、诺娃的信），story.md 只写了要点，需要你过目。
- 技能名和描述也是草案，跟设计文档 8.3 节一致；数值定下来后再补具体数字。

## 4. 交给译者

1. 把四个 CSV 发给译者（或导成一个四页签的 Excel），只改语言列，不动 `key` 和 `note`。
2. Excel 直接双击打开 UTF-8 的 CSV 可能乱码，用“数据 > 从文本/CSV 导入”选 UTF-8，或者用 Google 表格、LibreOffice 打开。
3. 新语言加一列，列名用语言代码（`ja`、`ko`、`zh-Hant`、`de`、`fr`……），留空的格子游戏里显示简体中文。
4. 收回后放回原位，跑 `dotnet run --project tests/Starline.Battle.Tests` 检查占位符，再在 Unity 里点 `Starline > 本地化 > 从 CSV 导入语言表`。

英文比中文长 1.5~2 倍，界面按钮、标签和布阵面板要在 Unity 里切到英文看一遍有没有溢出。日文、韩文还要换字体（Noto Sans SC 不含假名和谚文）。

## 5. 术语对照

同一个中文术语在所有表里用同一个英文。

**世界与剧情**

| 中文 | English |
|---|---|
| 星线 | the Starline |
| 魔网 | the Mana Web |
| 法力水晶 | Mana Crystal |
| 源晶塔 | Source Spire |
| 初源 / 初源水晶 | the Prime / the Prime Crystal |
| 永夜 / 永夜之地 / 永夜之心 | the Endless Night / the Land of Endless Night / Heart of the Endless Night |
| 织网者 / 织网者盟约 | the Weavers / the Weavers' Pact |
| 断线日 | Severance Day |
| 死气 | death-mist |
| 灯塔城 | Lighthaven |
| 边境行省 / 王国腹地 | the Border Provinces / the Heartland |
| 骸骨要塞 | the Ossuary Fortress |
| 饥饿的城邦 | the Hungering City |
| 水晶枢纽 | the crystal nexus |
| 迷雾森林 / 初源之路 | the Misty Forest / the Road to the Prime |
| 五根线：守护、自由、知识、救赎、信任 | the Five Threads: Guardianship, Freedom, Knowledge, Redemption, Trust |
| 指挥官 / 持线人 | Commander / the Threadholder |
| 诺娃 | Nova |
| 林大师 | Master Lin |
| 巫妖王 | the Lich King |
| 档案馆 / 星线编年 | the Archive / Chronicle of the Starline |
| 星辉高塔 | the Starlight Tower |

**阵营与职业**

| 中文 | English |
|---|---|
| 晨星骑士团 | Order of the Morning Star |
| 边境游侠会 | Frontier Rangers |
| 银月法师塔 | Silver Moon Tower |
| 亡灵 | the Undead |
| 影歌精灵 | Shadowsong Elves |
| 轻装 / 重装 / 飞骑 / 对空 / 远程 / 支援 | Light / Heavy / Flyer / Anti-Air / Ranged / Support |
| 英灵 | Heroic Spirit |
| 传说角色 | Legendary Hero |

**玩法**

| 中文 | English |
|---|---|
| 角色 | hero |
| 远征 / 远征军 / 远征军耐久 | Expedition / the expedition / Expedition Durability |
| 指挥点 | Command Points |
| 情报点 / 战前推演 | Intel / Battle Forecast |
| 指挥官技能 / 法术点数 | Commander Skill / Spell Points |
| 接入 / 未接入 / 离网 | linked / Unlinked / off-web |
| 魔网节点 / 魔网连线 / 源口 | Mana Web node / link / wellhead |
| 骨碑 / 亡灵祭坛 | Bone Obelisk / Undead Altar |
| 补给站 / 精英 | Supply Post / Elite |
| 战利品 | Spoils |
| 合成 / 升星 / 碎片 | merge / star up / shards |
| 羁绊 | bond |

## 6. 角色英文名

名字是中文音译名的原文，绰号按意思翻：

- 晨星骑士团：加文 Gavin、布兰 Bran、卡西 Cassie、艾琳 Eileen、塞德里克 Cedric、奥莉薇亚 Olivia、哈罗德 Harold、菲恩 Finn、雷恩爵士 Sir Rayne
- 边境游侠会：罗兰 Roland、夜莺 Nightingale、托恩 Thorne、玛塔 Marta、贝丝 Beth、格雷 Grey、小麦 Wheat、杜克 Duke、约翰 John
- 银月法师塔：赛琳 Celine、泽恩 Zane、莫娜 Mona、铁砧 Anvil、欧文 Owen、苏菲 Sophie、阿尔德 Alder、微光 Glimmer、卡兰 Kalan
- 亡灵：灰爪 Greyclaw、胖汤姆 Big Tom、低语 Whisper、维克多 Victor、毒眼 Venomeye、莫格 Mog、赛薇 Sevi、卡斯 Kass、瓦伦 Valen
- 影歌精灵：希尔瓦 Silva、塞伦 Seren、老橡 Old Oak、莉安 Lian、卡蕾 Karei、埃兰 Elan、维拉 Vera、伊娜 Ina、法埃 Fae
- 传说角色：噬魔巨龙 Manaeater Wyrm、莫德雷德 Mordred
- 小传里的名字：狮鹫疾风 Gale、栗子 Chestnut、守城弩“老太婆” the Old Girl、老灰 Old Grey、小冰块 Little Ice Cube、陷阱“晚饭” Supper
