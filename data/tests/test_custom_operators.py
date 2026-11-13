import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_custom_operators import build_catalogue


def character(name, **changes):
    return {"name": name, "rarity": "TIER_6", "isNotObtainable": False,
            "nationId": "yan", "groupId": None, "teamId": None, "subPower": None,
            "position": "MELEE", "profession": "WARRIOR", "subProfessionId": "lord",
            "phases": [{}, {}, {"rangeId": "r", "attributesKeyFrames": [{"data": {"blockCnt": 2}}]}], **changes}


class CustomCatalogueTests(unittest.TestCase):
    def test_filters_fixed_and_non_operators_but_keeps_alters_and_system_members(self):
        chars = {"char_1": character("普通自选"), "char_2": character("异格自选", isSpChar=True),
                 "char_3": character("固定"), "char_4": character("特殊", isNotObtainable=True),
                 "char_5": character("不可获取", isNotObtainable=True), "token_1": character("召唤物")}
        season = {"charShopChessDatas": {"fixed": {"charId": "char_3", "chessType": "NORMAL", "isHidden": False}},
                  "constData": {"systemCustomChar": ["char_4"], "fallbackBondId": "empty"}, "bondInfoDict": {"yan": {}}}
        bonds = {"yan": {"name": "炎", "bondType": "SEASON", "isPower": True, "powerIdList": ["yan"]}}
        operators, profiles = build_catalogue(chars, season, bonds, {"lord": {"subProfessionName": "领主"}},
                                             {"r": {"grids": [{"col": 1, "row": 0}]}})
        self.assertEqual({o["name"] for o in operators}, {"普通自选", "异格自选", "特殊"})
        self.assertTrue(all(o["normal"]["traits"] == [] and o["elite"]["traits"] == [] for o in operators))
        self.assertIsNone(profiles["operators"]["异格自选"]["normalSkill"])
        self.assertEqual(profiles["ranges"]["r"], [[1, 0]])

    def test_secondary_power_is_used_and_unmatched_operators_get_support(self):
        chars = {"char_1": character("隐藏势力", nationId=None, subPower=[{"nationId": "yan", "groupId": None}]),
                 "char_2": character("无匹配势力", nationId=None)}
        season = {"charShopChessDatas": {}, "constData": {"systemCustomChar": [], "fallbackBondId": "empty"},
                  "bondInfoDict": {"yan": {}}}
        bonds = {"yan": {"name": "炎", "bondType": "SEASON", "isPower": True, "powerIdList": ["yan"]},
                 "empty": {"name": "协防干员", "bondType": "REGULAR", "isPower": False, "powerIdList": []}}
        operators, _ = build_catalogue(chars, season, bonds, {"lord": {"subProfessionName": "领主"}}, {"r": {"grids": []}})
        by_name = {o["name"]: o for o in operators}
        self.assertEqual(by_name["隐藏势力"]["coreCovenants"], ["炎"])
        self.assertEqual(by_name["无匹配势力"]["additionalCovenants"], ["协防干员"])


if __name__ == "__main__":
    unittest.main()
