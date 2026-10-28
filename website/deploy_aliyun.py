"""通过阿里云云助手把官网部署到香港 ECS（Windows），不需要远程桌面。

需要环境变量 ALIYUN_ACCESS_KEY_ID、ALIYUN_ACCESS_KEY_SECRET（RAM 子用户，AliyunECSFullAccess），
网络能访问 ecs.cn-hongkong.aliyuncs.com。

用法：python3 website/deploy_aliyun.py [公网 IP]
会依次：找到实例 → 安全组放行 TCP 80 → 生成部署脚本并用云助手执行 → 打印执行输出。
"""
import base64
import datetime
import hashlib
import hmac
import json
import os
import pathlib
import subprocess
import sys
import time
import urllib.parse
import urllib.request
import uuid

REGION = "cn-hongkong"
ENDPOINT = f"https://ecs.{REGION}.aliyuncs.com/"
HERE = pathlib.Path(__file__).resolve().parent


def call(action, **params):
    key_id = os.environ["ALIYUN_ACCESS_KEY_ID"]
    secret = os.environ["ALIYUN_ACCESS_KEY_SECRET"]
    q = {
        "Action": action, "Version": "2014-05-26", "Format": "JSON", "RegionId": REGION,
        "AccessKeyId": key_id, "SignatureMethod": "HMAC-SHA1", "SignatureVersion": "1.0",
        "SignatureNonce": uuid.uuid4().hex,
        "Timestamp": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    q.update({k: str(v) for k, v in params.items()})
    enc = lambda s: urllib.parse.quote(s, safe="-_.~")
    canon = "&".join(f"{enc(k)}={enc(q[k])}" for k in sorted(q))
    to_sign = "POST&%2F&" + enc(canon)
    q["Signature"] = base64.b64encode(hmac.new((secret + "&").encode(), to_sign.encode(), hashlib.sha1).digest()).decode()
    body = urllib.parse.urlencode(q).encode()
    req = urllib.request.Request(ENDPOINT, data=body, method="POST",
                                 headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        raise SystemExit(f"{action} 失败：{e.code} {e.read().decode(errors='replace')}")


def main():
    ip = sys.argv[1] if len(sys.argv) > 1 else "47.242.236.216"
    found = call("DescribeInstances", PublicIpAddresses=json.dumps([ip]))["Instances"]["Instance"]
    if not found:  # EIP 不在 PublicIpAddress 里，退回全量查找
        allinst = call("DescribeInstances", PageSize=100)["Instances"]["Instance"]
        found = [i for i in allinst if ip in i.get("PublicIpAddress", {}).get("IpAddress", [])
                 or i.get("EipAddress", {}).get("IpAddress") == ip]
    if not found:
        raise SystemExit(f"在 {REGION} 找不到公网 IP 为 {ip} 的实例")
    inst = found[0]
    iid = inst["InstanceId"]
    print("实例", iid, inst.get("OSName"), inst.get("Status"))

    for sg in inst["SecurityGroupIds"]["SecurityGroupId"]:
        try:
            call("AuthorizeSecurityGroup", SecurityGroupId=sg, IpProtocol="tcp", PortRange="80/80",
                 SourceCidrIp="0.0.0.0/0", Policy="accept", Description="Starline website HTTP")
        except SystemExit as e:
            if "Duplicate" not in str(e):
                raise
        print("安全组", sg, "已放行 TCP 80")

    out = HERE / "dist" / "deploy-website.ps1"
    subprocess.run([sys.executable, str(HERE / "make_deploy_script.py"), str(out)], check=True)
    script = out.read_bytes().lstrip(b"\xef\xbb\xbf").decode("utf-8")
    r = call("RunCommand", Type="RunPowerShellScript", InstanceId_1=iid,
             CommandContent=base64.b64encode(script.encode("utf-8")).decode(),
             ContentEncoding="Base64", Timeout=1800, Name="starline-website")
    inv = r["InvokeId"]
    print("云助手任务", inv)
    while True:
        time.sleep(10)
        res = call("DescribeInvocationResults", InvokeId=inv, ContentEncoding="PlainText")
        items = res["Invocation"]["InvocationResults"]["InvocationResult"]
        if items and items[0]["InvocationStatus"] not in ("Pending", "Running", "Scheduled"):
            it = items[0]
            print("状态", it["InvocationStatus"], "退出码", it.get("ExitCode"))
            print(it.get("Output", ""))
            if it.get("ErrorInfo"):
                print("错误", it["ErrorInfo"])
            break
        print("执行中...")


if __name__ == "__main__":
    main()
