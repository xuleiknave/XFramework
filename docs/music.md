# 背景音乐

星线的配乐方向、曲目清单、客户端选曲规则，以及给 AI 音乐工具的提示词。第一版的 17 首曲子由 `tools/music/compose.py` 程序化生成，已经可以在游戏里播放；正式发售前建议按第 5 节的提示词用 AI 音乐工具或请作曲重做主要曲目，替换同名 `.ogg` 即可，不用改代码。

## 1. 风格

**一句话**：“小编制的奇幻管弦乐，加上法力水晶的铃音”。参考《王国保卫战》《陷阵之志》《杀戮尖塔》那种循环耐听、不抢戏的战斗音乐，以及《塞尔达传说》里一段旋律在不同场景反复变奏的做法。

- **核心乐器**：弦乐、竖琴、长笛、圆号、定音鼓。晨星骑士团偏铜管和军鼓，游侠会偏鲁特琴、长笛和手鼓，银月法师塔偏钢片琴和竖琴，亡灵偏低沉男声合唱、管风琴和太鼓，影歌精灵偏女声合唱和风铃。
- **法力水晶的声音**：一种带泛音拍频的玻璃钟声（合成器里的 `crystal`），出现在主菜单、布阵、湖心枢纽和结局，代表星线和诺娃。
- **星线主题**：一段 D 大调八小节旋律（第 2 节），所有曲子都从它变奏出来。玩家在主菜单第一次听到完整版，草原战斗里由圆号吹出小调片段，巫妖王战里由管风琴奏出小调版，结局由合唱唱完大调版，象征星线被重新织起。
- **循环**：每首 50~110 秒，首尾无缝。战斗曲 100~144 BPM，不能盖过战斗音效；界面曲 70~100 BPM。
- **不做**：电子鼓、失真吉他、现代流行和弦进行，人声不唱歌词（只用“啊”“哦”这样的元音）。

## 2. 星线主题

```
D 大调，4/4
| D5. A5 A5-  | G5 F#5 E5 F#5- | D5. B5 A5 G5 | F#5 E5 E5-  |
| D5. A5 A5-  | B5 A5 G5 F#5 D5 | E5 F#5 G5 A5 G5 E5 | D5--- |
和弦：D | Bm | G | A | D | G D | Em A7 | D
```

开头的五度上行是“一颗颗水晶被点亮”，结尾回到主音是“星线连回源头”。小调版把三、六、七级降半音（`synth.to_minor`），和弦换成 Dm | Gm/B♭ | Gm | A | Dm | Gm Dm | E° A7 | Dm。

## 3. 曲目清单

| id | 曲名 | 用在哪里 | 调性 / 速度 | 配器与气氛 | 时长 |
|---|---|---|---|---|---|
| `title` | 星线 | 主菜单 | D 大调 76 | 水晶铃前奏，长笛奏主题，第二遍弦乐加合唱；安静、带点乡愁 | 76 秒 |
| `map` | 沿着星线 | 地图、关卡选择、领奖励 | G 大调 100 | 鲁特琴、竖笛、小提琴、手鼓、铃鼓；出发上路的轻快感，中段是主题 | 77 秒 |
| `deploy` | 推演 | 布阵、战前推演 | A 小调 88 | 低音弦乐固定音型、钟摆般的滴答、水晶提示音；冷静地思考 | 65 秒 |
| `battle_plains` | 灯塔城外 | 城郊草原（第 1 章） | D 小调 128 | 进行曲军鼓、弦乐跳弓、圆号；最后八小节是主题小调版 | 60 秒 |
| `battle_forest` | 林间游侠 | 古木森林（第 2 章） | E 多利亚 6/8 | 长笛、竖琴、鲁特琴、框鼓；游侠在林间穿行 | 69 秒 |
| `battle_bone_waste` | 骸骨要塞 | 骸骨荒原（第 3 章） | C 弗里几亚 100 | 男声合唱“哦”、管风琴、丧钟、太鼓、铁砧；阴沉压迫 | 58 秒 |
| `battle_arcane_court` | 银月回廊 | 奥术庭院（第 4 章） | A 和声小调 3/4 150 | 钢片琴、竖琴、拨弦圆舞曲、单簧管；神秘优雅 | 58 秒 |
| `battle_desert` | 灰沙之城 | 灰沙荒漠（第 5 章） | D 希贾兹 108 | 杜杜克、乌德琴（鲁特琴）、手鼓、铃鼓，D-A 持续音；干燥荒凉 | 71 秒 |
| `battle_lake` | 湖心夜 | 湖心枢纽（第 6 章，夜战） | B 小调 96 | 钢琴琶音加回声、水晶、低音脉冲、长笛；水面倒影 | 60 秒 |
| `battle_mist_marsh` | 雾中之林 | 迷雾沼泽（第 7 章） | 升 F 小调 6/8 | 精灵女声、竖琴、全音阶长笛动机、风铃；方向感模糊 | 69 秒 |
| `battle_night_tundra` | 永夜冰原 | 永夜冰原（第 8 章） | G 小调 112 | 高音弦乐震音、钟琴、低音脉冲、圆号；寒冷空旷 | 69 秒 |
| `battle_abyss` | 永夜深渊 | 永夜深渊（第 9 章） | C 小调 132 | 管风琴、男声合唱、十六分音符弦乐、太鼓与军鼓、铜管断奏；中段主题被扭成小调 | 58 秒 |
| `boss` | 首领战 | 章末 Boss | D 小调 144 | 合唱、铜管、定音鼓与太鼓；最后八小节主题小调版 | 53 秒 |
| `boss_final` | 巫妖王 | 第 9 章巫妖王莫德雷德 | D 小调 120 | 管风琴独奏小调主题开场，合唱和铜管压上 | 64 秒 |
| `victory` | 胜利 | 战斗胜利（短曲，不循环） | D 大调 | 铜管吹主题开头，定音鼓滚奏，水晶铃 | 约 6 秒 |
| `defeat` | 失败 | 战斗失败（短曲，不循环） | D 小调 | 弦乐慢奏小调主题开头，停在属和弦 | 约 15 秒 |
| `ending` | 重织星线 | 通关结局、片尾、远征胜利 | D 大调 70 | 长笛、弦乐、合唱依次唱完主题，竖琴和水晶铃 | 110 秒 |

**以后可以补的曲目**（暂时没有生成，先用上表顶替）：剧情对话的“温馨 / 悲伤 / 紧张”三首短循环（第 6 章雷恩牺牲需要专门一首）、炼金工坊、星辉高塔、各章节第一次进入时的 10 秒开场短曲。

## 4. 客户端怎么选曲

- 选曲表：`Runtime/Resources/StarlineMusic/music.json`，音频是同目录的 `<id>.ogg`。改选曲只改这张表。
- 播放器：`Runtime/Client/Audio/MusicPlayer.cs`，由 `GameApp` 创建，切换界面时淡入淡出（`fadeSeconds`，默认 1.5 秒）。同一首曲子在界面之间切换不会重头播放。
- 战斗选曲顺序：最终 Boss → Boss → 战场环境（`terrains`，关卡带上 `terrain` 后生效）→ 远征的幕（`acts`）→ 默认（`battle.default`）。
- 战斗结束时播放胜利或失败短曲，同时淡出战斗音乐；下一首循环曲在短曲播完前只开到四分之一音量。
- 音量存在 `PlayerPrefs`（`Starline.MusicVolume`），设置界面接上 `MusicPlayer.Volume` 即可。
- 导入设置：编辑器脚本 `Editor/MusicImportSettings.cs` 让循环曲从磁盘流式播放，短曲压缩后放内存。

## 5. 重新生成和替换

```
python3 tools/music/compose.py                  # 全部重新生成（约 2 分钟，需要 numpy 和 ffmpeg）
python3 tools/music/compose.py title boss       # 只生成两首
python3 tools/music/compose.py --preview out/   # 另存一份 mp3 试听
```

- 合成引擎在 `tools/music/synth.py`（弦乐、铜管、长笛、合唱、管风琴、竖琴、鲁特琴、钢琴、钟琴、水晶铃和各种鼓都是加法合成），编曲在 `compose.py`，每首一个函数。随机数全部固定种子，同一份代码每次输出相同。
- 程序化合成的音色接近“好一点的 16 位游戏机”，旋律和结构是对的，但弦乐和合唱听得出是合成的。正式版建议用下面的提示词生成后挑选，或者把这批曲子当作给作曲的样稿（调性、速度、配器、主题都已经定好）。
- 替换时保持文件名和“首尾无缝循环”；AI 工具生成的曲子通常不是无缝的，需要在 Audacity 里找两个相同的小节线裁切，或在 Unity 里用 `AudioSource.timeSamples` 做循环点（以后需要时再加）。

## 6. AI 音乐工具提示词

适用于 Suno、Udio 一类工具（风格栏填英文效果更好，中文说明给自己看）。商用前确认所用工具的付费方案授予商业使用权。通用要求附在每条后面：`instrumental, no vocals lyrics, seamless loop, game soundtrack`。

| id | 提示词 |
|---|---|
| `title` | Gentle fantasy orchestral main theme, D major, 76 BPM, glass crystal bell intro, harp arpeggios, solo flute melody, warm strings and soft wordless choir, nostalgic and hopeful, like a lighthouse in the dark, small chamber orchestra |
| `map` | Light adventurous medieval folk, G major, 100 BPM, lute picking, recorder and fiddle melody, frame drum and tambourine, pizzicato bass, travelling along a road through the countryside, cheerful but not silly |
| `deploy` | Calm tactical planning music, A minor, 88 BPM, low staccato string ostinato, clock-like ticking, sparse glass bell notes, soft synth pad, focused and thoughtful, minimal melody, fantasy strategy game |
| `battle_plains` | Heroic fantasy battle march, D minor, 128 BPM, snare drum march, spiccato strings, French horns melody, timpani hits, knights defending a city at dawn, energetic but leaves room for sound effects |
| `battle_forest` | Celtic fantasy forest battle, E dorian, 6/8, 112 BPM, fast flute melody, harp and lute, bodhran frame drum, shaker, rangers running through ancient woods |
| `battle_bone_waste` | Dark undead fortress battle, C phrygian, 100 BPM, low male choir chanting "oh", pipe organ drones, tolling church bell, taiko drums, low brass, grim and oppressive, no horror screams |
| `battle_arcane_court` | Mysterious magical waltz battle, A harmonic minor, 3/4, 150 BPM, celesta and harp arpeggios, pizzicato strings, clarinet and violin melody, floating runes in a moonlit wizard tower courtyard |
| `battle_desert` | Middle-eastern fantasy desert battle, D hijaz (phrygian dominant), 108 BPM, duduk lead, oud, darbuka and riq, string drone on D and A, dusty ruined city at noon, tense and dry |
| `battle_lake` | Night battle on a crystal lake, B minor, 96 BPM, piano arpeggios with ping-pong delay, glassy bells, deep pulsing bass, soft flute, cold reflections on water, atmospheric but driving |
| `battle_mist_marsh` | Eerie elven marsh battle, F# minor with whole-tone flute motif, 6/8, 84 BPM, ethereal female choir "ah", harp, wind chimes, soft frame drum, thick fog, beautiful but unsettling |
| `battle_night_tundra` | Frozen tundra battle under eternal night, G minor, 112 BPM, high string tremolo, glockenspiel, pulsing low strings, horns melody, sparse taiko, icy and vast |
| `battle_abyss` | Climactic dark fantasy battle at the edge of an abyss, C minor, 132 BPM, pipe organ, male choir, aggressive 16th-note strings, taiko and snare, brass stabs, the hero theme twisted into minor |
| `boss` | Epic fantasy boss battle, D minor, 144 BPM, full choir "ah", brass melody, timpani and taiko, driving string ostinato, cymbal crashes every 8 bars, intense but not chaotic |
| `boss_final` | Final boss: the Lich King, D minor, 120 BPM, solo pipe organ opening with a slow tragic theme, then choir, brass and taiko join, church bells, tragic grandeur rather than pure evil |
| `victory` | Short orchestral victory fanfare, D major, brass fanfare, timpani roll, cymbal, glass bell sparkle, 6 seconds, ends on a held chord |
| `defeat` | Short sad orchestral sting, D minor, slow strings, unresolved ending on the dominant chord, 10 seconds |
| `ending` | Emotional fantasy ending theme, D major, 70 BPM, flute then strings then wordless choir sing the main theme, harp and glass bells, the broken star-line is woven back together, bittersweet and warm, credits music |

**把主题告诉 AI**：Suno / Udio 不能直接读乐谱。想让各首共用星线主题，可以先用 `title` 的提示词生成主菜单，挑一首满意的，再用工具的“延伸 / 翻奏（cover / remix）”功能以它为参考生成其他曲子；或者把 `compose.py` 生成的 mp3 作为参考音频上传（需要工具支持音频参考）。
