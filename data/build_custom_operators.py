"""Build the current season's selectable operator catalogue from PRTS game data.

The catalogue has one entry per operator; tiers V/VI are not split. Tier 5 is
the earliest shop level at which these operators can appear. Selected skills
and modules belong to the player's in-game deck and are not guessed here.
"""
from __future__ import annotations

import argparse
import json
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from fetch_spdatabase import TORAPPU_ENDPOINT, SP_CHAR_REDIRECT
from build_operator_profiles import PROFESSION, HEALER_SUBPROFESSIONS


def build_catalogue(chars: dict, season: dict, bonds: dict, subprofessions: dict, ranges: dict) -> tuple[list, dict]:
    preset = {entry["charId"] for entry in season["charShopChessDatas"].values()
              if entry["chessType"] != "DIY" and not entry["isHidden"]}
    system = set(season["constData"]["systemCustomChar"])
    operators, profiles, used_ranges = [], {}, set()
    for char_id, char in chars.items():
        if not char_id.startswith("char_") or char_id in preset:
            continue
        if char_id not in system and (char["rarity"] != "TIER_6" or char["isNotObtainable"]):
            continue
        powers = {char[key] for key in ("nationId", "groupId", "teamId") if char[key] is not None}
        if char["subPower"] is not None:
            for power in char["subPower"]:
                powers.update(value for value in power.values() if value is not None)
        own = [bond for bond_id, bond in bonds.items() if bond_id in season["bondInfoDict"]
               and bond["isPower"] and powers.intersection(bond["powerIdList"])]
        if not own:
            own = [bonds[season["constData"]["fallbackBondId"]]]
        name = SP_CHAR_REDIRECT.get(char_id, char["name"])
        operators.append({"name": name, "sourceCharId": char_id, "isCustom": True, "tier": 5,
                          "iconPath": f"icons/operators/{name}.png",
                          "coreCovenants": [b["name"] for b in own if b["bondType"] == "SEASON"],
                          "additionalCovenants": [b["name"] for b in own if b["bondType"] == "REGULAR"],
                          "normal": {"traits": []}, "elite": {"traits": []}})
        phase = char["phases"][2]
        range_id = phase["rangeId"]
        if range_id is not None:
            used_ranges.add(range_id)
        profession = PROFESSION[char["profession"]]
        sub = subprofessions[char["subProfessionId"]]["subProfessionName"]
        roles = ["盾位"] if profession == "重装" else []
        if profession == "医疗" or sub in HEALER_SUBPROFESSIONS:
            roles.append("奶位")
        profiles[name] = {"profession": profession, "subProfession": sub,
                          "position": "近战" if char["position"] == "MELEE" else "远程",
                          "blockCount": phase["attributesKeyFrames"][-1]["data"]["blockCnt"],
                          "normalSkill": None, "eliteSkill": None,
                          "normalRange": {"base": range_id, "skill": None},
                          "eliteRange": {"base": range_id, "skill": None},
                          "roles": roles, "stack": None, "consumes": []}
    operators.sort(key=lambda op: op["name"])
    return operators, {"operators": profiles, "ranges": {
        rid: sorted([cell["col"], cell["row"]] for cell in ranges[rid]["grids"]) for rid in sorted(used_ranges)}}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gamedata", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--skip-icons", action="store_true")
    args = parser.parse_args()
    tables = {name: json.loads((args.gamedata / f"{name}.json").read_text(encoding="utf-8-sig"))
              for name in ("activity_table", "character_table", "uniequip_table", "range_table")}
    activity = tables["activity_table"]
    operators, profiles = build_catalogue(tables["character_table"], activity["activity"]["AUTOCHESS"]["act3autochess"]["V3_1"],
                                         activity["autoChessData"]["bondInfoDict"],
                                         tables["uniequip_table"]["subProfDict"], tables["range_table"])
    args.output.mkdir(parents=True, exist_ok=True)
    if not args.skip_icons:
        icons = args.output / "icons" / "operators"
        icons.mkdir(parents=True, exist_ok=True)
        def download(op: dict) -> None:
            url = f"{TORAPPU_ENDPOINT}/assets/char_avatar/{op['sourceCharId']}.png"
            with urllib.request.urlopen(url, timeout=30) as response:
                content = response.read()
            if not content.startswith(b"\x89PNG\r\n\x1a\n"):
                raise ValueError(f"PRTS avatar is not PNG: {op['name']}")
            (icons / f"{op['name']}.png").write_bytes(content)
        with ThreadPoolExecutor(max_workers=4) as pool:
            list(pool.map(download, operators))
    notice = {"source": f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/", "season": "act3autochess/V3_1",
              "notice": "PRTS 原始游戏数据；自选干员无特质，技能和模组由游戏内配置决定；Ⅴ/Ⅵ阶合并为一个候选。"}
    (args.output / "custom_operators.json").write_text(
        json.dumps({**notice, "operators": operators}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.output / "custom_operator_profiles.json").write_text(
        json.dumps({**notice, **profiles}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(operators)} selectable operators")


if __name__ == "__main__":
    main()
