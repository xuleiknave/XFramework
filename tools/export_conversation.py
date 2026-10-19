#!/usr/bin/env python3
"""把 Claude Code 会话记录（.jsonl）导出为 docs/conversation-log.md。

用法：
    python3 tools/export_conversation.py <会话记录.jsonl> [更多 .jsonl ...] [-o docs/conversation-log.md]

会话记录在 ~/.claude/projects/<项目目录>/<会话 ID>.jsonl。只导出双方的文字内容和
选择框里的回答，不导出工具调用；多个会话按时间顺序合并。
"""
import argparse
import json
import re

HEADER = [
    "# 开发对话记录",
    "",
    "> 这是 Starline 立项和开发过程中，开发者与 Claude（Claude Code 云端会话）的对话整理。",
    "> 只保留双方的文字内容，省略了工具调用（读写文件、运行命令等）的细节；开发者在选择框里的回答也已列出。时间为 UTC。",
    "> 会话中途因上下文过长做过自动压缩，压缩前后的对话都已包含。",
    "> 关键决策和理由的摘要见 [decisions.md](decisions.md)。",
    "",
]


def read_session(p):
    out = []
    for line in open(p, encoding="utf-8"):
        d=json.loads(line)
        t=d.get('type')
        if t not in ('user','assistant') or d.get('isCompactSummary') or d.get('isMeta') or d.get('isSidechain'): continue
        m=d.get('message',{}); content=m.get('content')
        texts=[]
        if isinstance(content,str): texts=[content]
        elif isinstance(content,list):
            for b in content:
                if b.get('type')=='text': texts.append(b['text'])
                elif b.get('type')=='tool_result':
                    c=b.get('content'); c=c if isinstance(c,str) else ' '.join(x.get('text','') for x in (c or []) if isinstance(x,dict))
                    if 'Your questions have been answered' in c:
                        pairs=re.findall(r'"([^"]+)"="([^"]+)"', c)
                        texts.append('（在选择框中回答）\n'+'\n'.join('- '+q+' → '+a for q,a in pairs))
        text='\n'.join(texts)
        text=re.sub(r'<system-reminder>.*?</system-reminder>','',text,flags=re.S)
        text=re.sub(r'<(local-command|command)[^>]*>.*?</\1[^>]*>','',text,flags=re.S)
        text=text.strip()
        if not text or text.startswith('[Request interrupted') : continue
        if t=='user' and ('Stop hook feedback' in text or text.startswith('Caveat:')): 
            text='（系统提示：Stop hook 要求提交未跟踪的文件）' if 'Stop hook' in text else None
            if not text: continue
        out.append((t,d.get('timestamp',''),text))
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("sessions", nargs="+")
    parser.add_argument("-o", "--output", default="docs/conversation-log.md")
    args = parser.parse_args()

    messages = []
    for path in args.sessions:
        messages.extend(read_session(path))
    messages.sort(key=lambda m: m[1])

    merged = []
    for role, ts, text in messages:
        if "task-notification" in text:
            continue
        if merged and merged[-1][0] == role == "assistant":
            merged[-1] = (role, merged[-1][1], merged[-1][2] + "\n\n" + text)
        else:
            merged.append((role, ts, text))

    lines = list(HEADER)
    for role, ts, text in merged:
        stamp = ts[:16].replace("T", " ")
        if role == "user":
            quoted = "\n".join("> " + l if l else ">" for l in text.split("\n"))
            lines += ["---", "### **开发者** · " + stamp, "", quoted, ""]
        else:
            lines += ["", "**Claude**：", "", text, ""]
    with open(args.output, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("已导出 %d 条消息到 %s" % (len(merged), args.output))


if __name__ == "__main__":
    main()
