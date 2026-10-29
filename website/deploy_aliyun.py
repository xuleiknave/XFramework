"""通过阿里云云助手把官网部署到香港的 Windows 服务器，不需要远程桌面。
先按 IP 找 ECS 实例，找不到再找轻量应用服务器（SWAS）。

需要环境变量 ALIYUN_ACCESS_KEY_ID、ALIYUN_ACCESS_KEY_SECRET（RAM 子用户，AliyunECSFullAccess
或 AliyunSWASFullAccess），网络能访问 ecs/swas.cn-hongkong.aliyuncs.com。

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
SWAS_ENDPOINT = f"https://swas.{REGION}.aliyuncs.com/"


def swas(action, **params):
    return call(action, _endpoint=SWAS_ENDPOINT, _version="2020-06-01", **params)
HERE = pathlib.Path(__file__).resolve().parent


def call(action, _endpoint=ENDPOINT, _version="2014-05-26", **params):
    key_id = os.environ["ALIYUN_ACCESS_KEY_ID"]
    secret = os.environ["ALIYUN_ACCESS_KEY_SECRET"]
    q = {
        "Action": action, "Version": _version, "Format": "JSON", "RegionId": REGION,
        "AccessKeyId": key_id, "SignatureMethod": "HMAC-SHA1", "SignatureVersion": "1.0",
        "SignatureNonce": uuid.uuid4().hex,
        "Timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    q.update({k: str(v) for k, v in params.items()})
    enc = lambda s: urllib.parse.quote(s, safe="-_.~")
    canon = "&".join(f"{enc(k)}={enc(q[k])}" for k in sorted(q))
    to_sign = "POST&%2F&" + enc(canon)
    q["Signature"] = base64.b64encode(hmac.new((secret + "&").encode(), to_sign.encode(), hashlib.sha1).digest()).decode()
    body = urllib.parse.urlencode(q).encode()
    req = urllib.request.Request(_endpoint, data=body, method="POST",
                                 headers={"Content-Type": "application/x-www-form-urlencoded"})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            raise SystemExit(f"{action} 失败：{e.code} {e.read().decode(errors='replace')}")
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            # 网络偶尔断开：查询类接口重试；签名带时间戳和随机数，重发同一请求即可
            if attempt == 3 or not action.startswith(("Describe", "List")):
                raise
            time.sleep(2 ** attempt)


def build_script():
    out = HERE / "dist" / "deploy-website.ps1"
    subprocess.run([sys.executable, str(HERE / "make_deploy_script.py"), str(out)], check=True)
    return out.read_bytes().lstrip(b"\xef\xbb\xbf").decode("utf-8")


def deploy_swas(ip):
    insts = swas("ListInstances", PublicIpAddresses=json.dumps([ip]))["Instances"]
    if not insts:
        raise SystemExit(f"在 {REGION} 的 ECS 和轻量应用服务器里都找不到公网 IP 为 {ip} 的实例")
    iid = insts[0]["InstanceId"]
    print("轻量应用服务器", iid, insts[0].get("Status"), insts[0].get("Image", {}).get("ImageName"))

    rules = swas("ListFirewallRules", InstanceId=iid, PageSize=100).get("FirewallRules", [])
    if not any(r.get("Port") == "80" and r.get("RuleProtocol", "").upper() in ("TCP", "TCP+UDP") for r in rules):
        swas("CreateFirewallRule", InstanceId=iid, RuleProtocol="TCP", Port="80", Remark="Starline website HTTP")
    print("防火墙已放行 TCP 80")

    r = swas("RunCommand", InstanceId=iid, Type="RunPowerShellScript", Name="starline-website",
             CommandContent=build_script(), Timeout=1800)
    inv = r["InvokeId"]
    print("云助手任务", inv)
    while True:
        time.sleep(10)
        res = swas("DescribeInvocationResult", InstanceId=iid, InvokeId=inv)["InvocationResult"]
        if res.get("InvocationStatus") not in ("Pending", "Running", "Scheduled", None):
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
        print("执行中...")


def main():
    ip = sys.argv[1] if len(sys.argv) > 1 else "47.242.236.216"
    found = call("DescribeInstances", PublicIpAddresses=json.dumps([ip]))["Instances"]["Instance"]
    if not found:  # EIP 不在 PublicIpAddress 里，退回全量查找
        allinst = call("DescribeInstances", PageSize=100)["Instances"]["Instance"]
        found = [i for i in allinst if ip in i.get("PublicIpAddress", {}).get("IpAddress", [])
                 or i.get("EipAddress", {}).get("IpAddress") == ip]
    if not found:
        return deploy_swas(ip)
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

    script = build_script()
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
