"""通过阿里云云助手把留言板部署到香港轻量服务器：服务器从 GitHub 拉取指定提交的源码并编译，
不需要把文件塞进命令里。要先把改动推送到 GitHub。

用法：python3 website/chat-server/deploy_chat.py [提交或分支，默认当前 HEAD]
"""
import base64
import json
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE.parent))
from deploy_aliyun import swas  # noqa: E402

IP = "47.242.236.216"


def main():
    ref = sys.argv[1] if len(sys.argv) > 1 else subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    files = subprocess.check_output(["git", "ls-files", "website/chat-server", "website/chat"], cwd=ROOT, text=True).split()
    files = [f for f in files if not f.endswith((".py", ".ps1"))]
    script = (HERE / "deploy-chat.ps1").read_text(encoding="utf-8")
    script = script.replace("__REF__", ref).replace("__FILES__", "\n".join(f"    '{f}'" for f in files))
    print("部署提交", ref, "，文件", len(files), "个，脚本", len(script.encode()), "字节")

    iid = swas("ListInstances", PublicIpAddresses=json.dumps([IP]))["Instances"][0]["InstanceId"]
    inv = swas("RunCommand", InstanceId=iid, Type="RunPowerShellScript", Name="starline-chat",
               CommandContent=script, Timeout=1800)["InvokeId"]
    print("云助手任务", inv)
    while True:
        time.sleep(15)
        res = swas("DescribeInvocationResult", InstanceId=iid, InvokeId=inv)["InvocationResult"]
        if res.get("InvocationStatus") in ("Pending", "Running", "Scheduled", None):
            print("执行中...")
            continue
        print("状态", res["InvocationStatus"], "退出码", res.get("ExitCode"))
        out = res.get("Output", "")
        try:
            out = base64.b64decode(out).decode("utf-8", errors="replace")
        except Exception:
            pass
        print(out)
        if res.get("ErrorInfo"):
            print("错误", res["ErrorInfo"])
        break


if __name__ == "__main__":
    main()
