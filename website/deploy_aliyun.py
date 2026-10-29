"""通过阿里云云助手把官网部署到香港的 Windows 服务器，不需要远程桌面。
先按 IP 找 ECS 实例，找不到再找轻量应用服务器（SWAS）。

需要环境变量 ALIYUN_ACCESS_KEY_ID、ALIYUN_ACCESS_KEY_SECRET（RAM 子用户，AliyunECSFullAccess
或 AliyunSWASFullAccess），网络能访问 ecs/swas.cn-hongkong.aliyuncs.com。

用法：python3 website/deploy_aliyun.py [公网 IP]
会依次：找到实例 → 防火墙放行 TCP 80 → 把文件分段上传到暂存目录 → 执行部署脚本 → 打印输出。
"""
import base64
import datetime
import hashlib
import hmac
import json
import os
import pathlib
import sys
import time
import urllib.parse
import urllib.request
import uuid

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_deploy_script as mds  # noqa: E402

REGION = "cn-hongkong"
ENDPOINT = f"https://ecs.{REGION}.aliyuncs.com/"
SWAS_ENDPOINT = f"https://swas.{REGION}.aliyuncs.com/"


def swas(action, **params):
    return call(action, _endpoint=SWAS_ENDPOINT, _version="2020-06-01", **params)


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


CHUNK = 12000  # 每条云助手命令带的 base64 字符数，留足余量（单条命令内容上限约 16 KB）


def swas_runner(iid):
    def run(script, name):
        inv = swas("RunCommand", InstanceId=iid, Type="RunPowerShellScript", Name=name,
                   CommandContent=script, Timeout=1800)["InvokeId"]
        while True:
            time.sleep(5)
            res = swas("DescribeInvocationResult", InstanceId=iid, InvokeId=inv)["InvocationResult"]
            if res.get("InvocationStatus") not in ("Pending", "Running", "Scheduled", None):
                out = res.get("Output", "")
                try:
                    out = base64.b64decode(out).decode("utf-8", errors="replace")
                except Exception:
                    pass
                return res["InvocationStatus"], out + (res.get("ErrorInfo") or "")
    return run


def ecs_runner(iid):
    def run(script, name):
        inv = call("RunCommand", Type="RunPowerShellScript", InstanceId_1=iid,
                   CommandContent=base64.b64encode(script.encode("utf-8")).decode(),
                   ContentEncoding="Base64", Timeout=1800, Name=name)["InvokeId"]
        while True:
            time.sleep(5)
            items = call("DescribeInvocationResults", InvokeId=inv, ContentEncoding="PlainText")[
                "Invocation"]["InvocationResults"]["InvocationResult"]
            if items and items[0]["InvocationStatus"] not in ("Pending", "Running", "Scheduled"):
                return items[0]["InvocationStatus"], items[0].get("Output", "") + (items[0].get("ErrorInfo") or "")
    return run


def deploy(run):
    """先把每个文件分段写进服务器暂存目录，再执行部署脚本解码到站点目录。"""
    staging = mds.STAGING_DIR
    status, out = run(f"Remove-Item -Recurse -Force '{staging}' -ErrorAction SilentlyContinue; "
                      f"New-Item -ItemType Directory -Force -Path '{staging}' | Out-Null", "starline-stage")
    if status != "Success":
        raise SystemExit("清空暂存目录失败：" + out)
    for name in mds.FILES:
        data = mds.encode(name)
        target = staging + "\\" + name.replace("/", "__") + ".b64"
        parts = [data[i:i + CHUNK] for i in range(0, len(data), CHUNK)]
        for n, part in enumerate(parts, 1):
            status, out = run(f"[IO.File]::AppendAllText('{target}', '{part}')", "starline-upload")
            if status != "Success":
                raise SystemExit(f"上传 {name} 第 {n} 段失败：" + out)
        print(f"已上传 {name}（{len(parts)} 段）")
    status, out = run(mds.build(inline=False), "starline-website")
    print("状态", status)
    print(out)


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
    deploy(swas_runner(iid))


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
    deploy(ecs_runner(iid))


if __name__ == "__main__":
    main()
