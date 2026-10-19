"""Combine the static catalog with explicitly reviewed game observations; never infer a pass."""
import csv
import json
from collections import Counter
from pathlib import Path

root = Path(__file__).resolve().parents[1]
evidence = root / "docs/cs2-feasibility"
catalog = json.loads((evidence / "catalog.json").read_text(encoding="utf-8-sig"))["Parameters"]
observations = json.loads((evidence / "runtime-observations.json").read_text(encoding="utf-8-sig"))
by_id = {o["Id"]: o for o in observations}
if len(by_id) != len(observations) or set(by_id) - {p["Id"] for p in catalog}:
    raise ValueError("Duplicate or unknown observation ID")
allowed = {"验证通过", "部分通过", "验证失败", "待实机验证", "不适用"}
rows = []
for p in catalog:
    o = by_id.get(p["Id"])
    if o and (o["Status"] not in allowed or not o["GameActuallyStarted"]):
        raise ValueError("Invalid observation status or game session")
    if o:
        for path in o["Evidence"]:
            resolved = (evidence / path).resolve()
            if not resolved.is_relative_to(evidence.resolve()) or not resolved.is_file():
                raise ValueError("Missing or external evidence path")
    rows.append({
        "ID": p["Id"], "名称": p["Name"], "文件": p["File"], "节点": p["Section"],
        "应用类型与范围": p["Type"] + "; " + p["Range"],
        "游戏观察值": o["InGameValue"] if o else "未部署测试",
        "重启观察值": o["AfterRestartValue"] if o else "未验证",
        "验证状态": o["Status"] if o else "待实机验证",
        "依据": "; ".join(o["Evidence"]) if o else "仅静态目录；不是游戏生效证据",
        "限制": o["Notes"] if o else "应用未开放或条件未满足；未宣称已实现或不受支持",
    })
with (evidence / "runtime-parameter-results.csv").open("w", encoding="utf-8-sig", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=rows[0].keys())
    writer.writeheader()
    writer.writerows(rows)
lines = ["# CS2 全量参数实机状态", "",
         "静态目录与人工复核的真实游戏观察合并。部分通过仅表示读取/档位与重启保留有证据，尚未完成该参数的客观效果验收。完整限制、范围及证据路径见同名 CSV 和 runtime-observations.json。", "",
         "| ID | 名称 | 文件 / 节点 | 游戏观察 | 重启观察 | 状态 |", "| --- | --- | --- | --- | --- | --- |"]
for row in rows:
    cells = [row["ID"], row["名称"], row["文件"] + " / " + row["节点"], row["游戏观察值"], row["重启观察值"], row["验证状态"]]
    lines.append("| " + " | ".join(str(v).replace("|", "\\|").replace("\n", " ") for v in cells) + " |")
(evidence / "runtime-parameter-results.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(json.dumps({"rows": len(rows), "statuses": Counter(row["验证状态"] for row in rows)}, ensure_ascii=False))
