# 背景音乐

取经棋局的配乐方向、曲目清单、客户端选曲规则，以及给 AI 音乐工具的提示词。

> v4.0（决策 68）：题材换成“星宿国潮”，配乐从奇幻管弦乐改成**民乐为主、少量管弦垫底**。下面第 1、3、6 节按新方向重写；仓库里现有的 17 首 `.ogg` 还是 `tools/music/compose.py` 生成的管弦乐版，曲目 id 不变，按第 6 节的提示词重做后替换同名文件即可，不用改代码。
>
> v6.0（决策 77）：题材改回《西游记》原著，阵营改成人、仙、魔、兽四族。第 1、3、6 节的场景、阵营配器和提示词跟着改，曲目 id 和选曲规则不变。
>
> v5.0（决策 71）：题材换成“西游后传”，民乐方向不变，只改了几首战斗曲的场景。**不能用、也不能改编 86 版电视剧的《云宫迅音》《敬你一杯》等曲子**，给 AI 工具的提示词里也不要写这些曲名和改编作品名。

## 1. 风格

**一句话**：“小编制的国风民乐，加上灵石的磬声”。参考《江南百景图》《阴风怪》《王国保卫战》那种循环耐听、不抢戏的音乐，以及一段旋律在不同场景反复变奏的做法。

- **核心乐器**：古筝、琵琶、竹笛、二胡、笙，打击乐用堂鼓、板鼓、小锣、木鱼，弦乐组只做铺底。人族偏二胡、竹笛和木鱼（长安街市、田间），仙族偏编钟、古琴、箫和堂鼓号角（天庭和天兵），魔族偏低音埙、大锣和低沉男声吟唱，兽族偏古筝泛音、女声哼唱、风铃和手鼓（龙宫和山野）。
- **灵石的声音**：一种带泛音拍频的磬声 / 编钟声（合成器里的 `crystal` 可以先顶着），出现在主菜单、布阵、通天河和结局，代表灵脉和土地庙。
- **西行主题**：一段五声音阶的八小节旋律（第 2 节），所有曲子都从它变奏出来。主菜单古筝和竹笛奏完整版，长安郊外的战斗里由号角吹出片段，狮驼岭一战由低音埙和大鼓奏出暗色版，结局由二胡和女声唱完，象征取经路走到头。
- **短局优先**：一局战斗不超过 45 秒、默认 2 倍速（决策 69），战斗曲要在开头 4 小节内就有主旋律，不要长前奏。微信小游戏常被静音玩，音乐只是加分项，音效（出手、击中、灵石点亮）要单独就能听懂。
- **循环**：每首 40~90 秒，首尾无缝。战斗曲 100~144 BPM，界面曲 70~100 BPM。小游戏版用 64 kbps 单声道 ogg / mp3，放 CDN 按需下载。
- **不做**：电子鼓、失真吉他、流行和弦进行、戏曲唱腔，人声不唱歌词（只用“啊”“嗯”这样的元音）。

## 2. 西行主题

```
D 宫调式（五声：D E F# A B），4/4
| D5. A5 A5-  | B5 A5 F#5 E5- | D5. B5 A5 F#5 | E5 D5 E5-  |
| D5. A5 A5-  | B5 D6 B5 A5 F#5 | E5 F#5 A5 B5 A5 F#5 | D5--- |
和弦（铺底用）：D | Bm | G | A | D | G D | Em A | D
```

开头的五度上行是“一步步往西走”，结尾回到宫音是“回到长安”。暗色版改用 B 羽调式（同一组音，以 B 为主音），狮驼岭一战和失败短曲用它。v3.0 的 D 大调版（含 G、C# 两个偏音）见 git 历史，`compose.py` 里仍是旧版。

## 3. 曲目清单

| id | 曲名 | 用在哪里 | 调式 / 速度 | 配器与气氛 | 时长 |
|---|---|---|---|---|---|
| `title` | 西行 | 主菜单 | D 宫 76 | 磬声前奏，古筝起、竹笛奏主题，第二遍二胡和女声哼唱加入；安静、带点乡愁 | 70 秒 |
| `map` | 西行路上 | 地图、关卡选择、领奖励 | G 宫 100 | 琵琶轮指、竹笛、小锣、木鱼；出发上路的轻快感，中段是主题 | 60 秒 |
| `deploy` | 推演 | 布阵、战前推演 | A 羽 88 | 古筝低音固定音型、木鱼滴答、磬声提示音；冷静地思考 | 50 秒 |
| `battle_plains` | 长安郊外 | 长安郊外（序章、第 1 章） | D 羽 128 | 堂鼓、号角、二胡快弓；最后八小节是主题暗色版 | 50 秒 |
| `battle_forest` | 深山老林 | 深山老林（第 4、7 章） | E 商 6/8 | 竹笛、琵琶、木鱼、框鼓，加一点调皮的滑音；猴子在林间穿行 | 55 秒 |
| `battle_bone_waste` | 黑风山 | 妖泥荒原（第 3 章） | C 羽 100 | 低音埙、男声吟唱“嗯”、大锣、大鼓；阴沉压迫，但不做恐怖音效 | 50 秒 |
| `battle_arcane_court` | 云海 | 云海（第 11、14 章） | A 徵 3/4 150 | 编钟、古琴泛音、拨弦、箫；神秘优雅 | 50 秒 |
| `battle_desert` | 黄风岭 | 黄风沙地（第 5 章） | D 商 108 | 唢呐（弱奏）、热瓦普、手鼓，D-A 持续音；干燥、风沙大 | 55 秒 |
| `battle_lake` | 通天河 | 水域和通天河冰面（第 2、6、10 章，夜战） | B 羽 96 | 古筝琶音加回声、磬声、低音脉冲、箫；水面倒影 | 50 秒 |
| `battle_mist_marsh` | 平顶山 | 妖洞山岭（第 8 章） | 升 F 羽 6/8 | 女声哼唱、古筝泛音、风铃、竹笛；山雾里方向感模糊 | 55 秒 |
| `battle_night_tundra` | 火焰山 | 火焰山（第 9、12 章） | G 羽 112 | 二胡高音颤弓、唢呐、低音弦乐、大鼓；燥热、喘不过气 | 55 秒 |
| `battle_abyss` | 狮驼岭 | 妖洞山岭（第 13 章） | C 羽 132 | 大鼓、男声吟唱、琵琶扫弦、二胡十六分音符、锣；中段主题被扭成暗色版 | 50 秒 |
| `boss` | 首领战 | 章末 Boss | D 羽 144 | 大鼓与堂鼓、唢呐、琵琶扫弦、男女声“啊”；最后八小节主题暗色版 | 45 秒 |
| `boss_final` | 狮驼三魔 | 第 13 章青狮、白象、大鹏 | D 羽 120 | 低音埙独奏暗色主题开场，大鼓、编钟和合唱压上；悲壮多于邪恶 | 55 秒 |
| `victory` | 胜利 | 战斗胜利（短曲，不循环） | D 宫 | 竹笛吹主题开头，堂鼓滚奏，一声锣和磬声 | 约 5 秒 |
| `defeat` | 失败 | 战斗失败（短曲，不循环） | B 羽 | 二胡慢奏暗色主题开头，停在不稳定的音上 | 约 8 秒 |
| `ending` | 八十一难 | 通关结局、片尾、远征胜利 | D 宫 70 | 竹笛、二胡、女声依次唱完主题，古筝和磬声 | 90 秒 |

曲名里的地名以 [game-design.md](game-design.md) 第 5.5 节的章节和环境为准；`battle_forest` 等 id 沿用旧名，不改。

**以后可以补的曲目**（暂时没有生成，先用上表顶替）：剧情对话的“温馨 / 悲伤 / 紧张”三首短循环、天工坊、镇妖塔、各章节第一次进入时的 5 秒开场短曲。

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

- 合成引擎在 `tools/music/synth.py`（弦乐、铜管、长笛、合唱、管风琴、竖琴、鲁特琴、钢琴、钟琴、水晶铃和各种鼓都是加法合成；还没有古筝、琵琶、二胡、竹笛等民乐音色，加法合成也很难做像，民乐版建议直接用 AI 工具或请作曲），编曲在 `compose.py`，每首一个函数。随机数全部固定种子，同一份代码每次输出相同。
- 程序化合成的音色接近“好一点的 16 位游戏机”，旋律和结构是对的，但弦乐和合唱听得出是合成的。正式版建议用下面的提示词生成后挑选，或者把这批曲子当作给作曲的样稿（调性、速度、配器、主题都已经定好）。
- 替换时保持文件名和“首尾无缝循环”；AI 工具生成的曲子通常不是无缝的，需要在 Audacity 里找两个相同的小节线裁切，或在 Unity 里用 `AudioSource.timeSamples` 做循环点（以后需要时再加）。

## 6. AI 音乐工具提示词

适用于 Suno、Udio、网易天音、海绵音乐一类工具（风格栏填英文效果通常更好，中文说明给自己看）。商用前确认所用工具的付费方案授予商业使用权。通用要求附在每条后面：`instrumental, no lyrics, seamless loop, mobile game soundtrack, Chinese traditional instruments`。

| id | 提示词 |
|---|---|
| `title` | Gentle Chinese traditional main theme, D major pentatonic, 76 BPM, stone chime (bianqing) intro, guzheng arpeggios, solo dizi flute melody, erhu and soft wordless female humming on the second pass, nostalgic and hopeful, a long road to the west in a night sky, small ensemble |
| `map` | Light adventurous Chinese folk, G major pentatonic, 100 BPM, pipa tremolo, dizi melody, small gong and woodblock, plucked bass, travelling along a mountain road, cheerful but not silly |
| `deploy` | Calm tactical planning music, A minor pentatonic, 88 BPM, low guzheng ostinato, woodblock ticking, sparse stone chime notes, soft string pad, focused and thoughtful, minimal melody, strategy game |
| `battle_plains` | Heroic Chinese battle march, D minor pentatonic, 128 BPM, tanggu war drums, horn calls, fast erhu bowing, pipa strums, soldiers defending a city at dawn, energetic but leaves room for sound effects |
| `battle_forest` | Playful Chinese mountain forest battle, E pentatonic, 6/8, 112 BPM, fast dizi melody with slides, pipa, woodblock, frame drum, monkey warriors leaping through the trees |
| `battle_bone_waste` | Dark demon cave battle in a black wind mountain, C minor pentatonic, 100 BPM, low xun ocarina, low male humming, large gong, bass drum, grim and oppressive, no horror screams |
| `battle_arcane_court` | Mysterious Taoist alchemy court waltz battle, A pentatonic, 3/4, 150 BPM, bianzhong bronze bells, guqin harmonics, plucked strings, xiao flute melody, celestial palace above a sea of clouds |
| `battle_desert` | Chinese desert frontier battle, D pentatonic, 108 BPM, soft suona lead, rawap, hand drum, drone on D and A, yellow wind sandstorm in a mountain pass, tense and dry |
| `battle_lake` | Night battle on a wide river, B minor pentatonic, 96 BPM, guzheng arpeggios with delay, stone chimes, deep pulsing bass, soft xiao flute, cold reflections on water, atmospheric but driving |
| `battle_mist_marsh` | Eerie misty mountain battle near a demon cave, F# minor pentatonic, 6/8, 84 BPM, ethereal female humming, guzheng harmonics, wind chimes, soft dizi, thick fog, beautiful but unsettling |
| `battle_night_tundra` | Battle on a burning mountain, G minor pentatonic, 112 BPM, high erhu tremolo, soft suona, low strings pulse, big drums, scorching heat shimmer |
| `battle_abyss` | Climactic Chinese fantasy battle at a mountain pass held by demon kings, C minor pentatonic, 132 BPM, big drums, male chant, pipa strumming, fast erhu 16th notes, gongs, the main theme twisted into a dark mode |
| `boss` | Epic Chinese boss battle, D minor pentatonic, 144 BPM, taiko-like big drums and tanggu, suona melody, pipa strumming, choir "ah", gong crash every 8 bars, intense but not chaotic |
| `boss_final` | Final boss: three demon kings of the lion camel ridge, D minor pentatonic, 120 BPM, solo xun opening with a slow tragic theme, then big drums, bianzhong bells and choir join, tragic grandeur rather than pure evil |
| `victory` | Short Chinese victory fanfare, D major pentatonic, dizi phrase, drum roll, one gong hit, stone chime sparkle, 5 seconds, ends on a held note |
| `defeat` | Short sad Chinese sting, B minor pentatonic, slow erhu, unresolved ending, 8 seconds |
| `ending` | Emotional Chinese ending theme, D major pentatonic, 70 BPM, dizi then erhu then wordless female voice sing the main theme, guzheng and stone chimes, a long journey to the west finally ends, bittersweet and warm, credits music |

**把主题告诉 AI**：这类工具不能直接读乐谱。想让各首共用西行主题，可以先用 `title` 的提示词生成主菜单，挑一首满意的，再用工具的“延伸 / 翻奏（cover / remix）”功能以它为参考生成其他曲子；或者把主题旋律用任意软件弹成 mp3 作为参考音频上传（需要工具支持音频参考）。
