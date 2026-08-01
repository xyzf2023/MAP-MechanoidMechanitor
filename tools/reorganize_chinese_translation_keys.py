#!/usr/bin/env python3
from __future__ import annotations

from collections import OrderedDict
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
KEYED_DIR = ROOT / "1.6" / "Languages" / "ChineseSimplified" / "Keyed"
BASE = "MAP_MechanoidMechanitor."

SOURCE_FILES = [
    "MAP_MechanoidMechanitor.xml",
    "DataProcessingAllocation.xml",
    "MAP_SymbiosisCovenant.xml",
    "MAP_MechanoidMechanitor_JusticeBoss.xml",
    "MAP_MechanoidMechanitor_JusticeBossSettings.xml",
    "Implants.xml",
    "MAP_MechanoidMechanitor_Odyssey.xml",
    "MAP_MechanoidMechanitor_PurgeDirectiveBossgroups.xml",
]

OUTPUT_FILES = OrderedDict([
    ("Core", "MAP_Core.xml"),
    ("Settings", "MAP_Settings.xml"),
    ("Scenario", "MAP_Scenario.xml"),
    ("Justice", "MAP_Justice.xml"),
    ("Implants", "MAP_Implants.xml"),
    ("Lover", "MAP_Lover.xml"),
    ("DataProcessing", "MAP_DataProcessing.xml"),
    ("PurgeDirective", "MAP_PurgeDirective.xml"),
    ("SymbiosisCovenant", "MAP_SymbiosisCovenant.xml"),
    ("JusticeBoss", "MAP_JusticeBoss.xml"),
    ("MechHiveNode", "MAP_MechHiveNode.xml"),
])

# 仅拆分翻译键末端常见的界面语义，不修改普通英文单词本身。
TERMINAL_TOKENS = [
    ("Description", "Description"),
    ("Tooltip", "Tooltip"),
    ("Desc", "Description"),
    ("Tip", "Tooltip"),
    ("Label", "Label"),
    ("Title", "Title"),
    ("Text", "Text"),
    ("Button", "Button"),
    ("Toggle", "Toggle"),
    ("Gizmo", "Gizmo"),
    ("Letter", "Letter"),
]

PREFIX_REMAPS = [
    ("MechHiveCommunication.", "PurgeDirective.Communication."),
    ("BrainImplant.", "Implants.Brain."),
    ("ProductivityCore.", "Implants.ProductivityCore."),
    ("SyntheticSpouse.", "Lover.Spouse."),
    ("SyntheticPregnancy.", "Lover.Pregnancy."),
    ("MechRecode.", "Justice.Ability.MechRecode."),
    ("MechReconstruction.", "Justice.Ability.MechReconstruction."),
    ("MechHack.", "Justice.Ability.MechHack."),
    ("AbilityUnlockLetter.", "Justice.Unlock.Ability.Letter."),
    ("FeatureUnlockLetter.", "Justice.Unlock.Feature.Letter."),
    ("ConsciousnessTransfer.", "Justice.ConsciousnessTransfer."),
    ("DormantJustice.", "Justice.DormantActivation."),
    ("Ideology.", "Scenario.Ideology."),
    ("NewMechHive.", "MechHiveNode.NewHive."),
    ("Story.PurgeDirective.", "PurgeDirective.Scenario."),
    ("Story.SymbiosisCovenant.", "Symbiosis.Scenario."),
]

KNOWN_SYSTEM_PREFIXES = (
    "Core.",
    "Settings.",
    "Scenario.",
    "Story.",
    "Justice.",
    "JusticeBoss.",
    "Implants.",
    "Lover.",
    "DataProcessing.",
    "PurgeDirective.",
    "Symbiosis.",
    "MechHiveNode.",
)


def split_terminal_tokens(part: str) -> list[str]:
    if not part:
        return []

    suffixes: list[str] = []
    current = part
    changed = True
    while changed and current:
        changed = False
        for raw, normalized in TERMINAL_TOKENS:
            if current != raw and current.endswith(raw):
                current = current[:-len(raw)]
                suffixes.insert(0, normalized)
                changed = True
                break

    result: list[str] = []
    if current:
        result.append(current)
    result.extend(suffixes)
    return result


def normalize_tail(tail: str) -> str:
    raw_parts = [part for part in tail.replace("_", ".").split(".") if part]
    if not raw_parts:
        raise ValueError(f"翻译键缺少主体：{tail!r}")

    normalized: list[str] = []
    for index, part in enumerate(raw_parts):
        if index == len(raw_parts) - 1:
            normalized.extend(split_terminal_tokens(part))
        else:
            normalized.append(part)
    return ".".join(normalized)


def rename_settings_key(key: str) -> str:
    tail = key[len("MAP_Settings_"):]
    replacements = [
        ("JusticeBossDifficulty_", "JusticeBoss."),
        ("ProductivityCoreWorkSpeedOffset_", "ProductivityCore.WorkSpeedOffset."),
        ("EnablePortraitDisplayForAllSaves_", "PortraitDisplayForAllSaves."),
        ("EnableMechanoidMechanitorBrainImplants_", "BrainImplants."),
        ("EnableLoverImplants_", "LoverImplants."),
        ("SyntheticOffspringInheritXenogenes_", "SyntheticOffspring.InheritXenogenes."),
        ("EnablePurgeDirectiveUiLoadingScreen_", "PurgeDirective.UiLoadingScreen."),
    ]
    for old, new in replacements:
        if tail.startswith(old):
            tail = new + tail[len(old):]
            break
    return BASE + "Settings." + normalize_tail(tail)


def rename_key(key: str) -> str:
    if key.startswith("MAP_DataProcessingAllocation_"):
        tail = key[len("MAP_DataProcessingAllocation_"):]
        return BASE + "DataProcessing." + normalize_tail(tail)

    if key.startswith("MAP_Settings_"):
        return rename_settings_key(key)

    if key.startswith("MAP_LoverImplant."):
        tail = key[len("MAP_LoverImplant."):]
        return BASE + "Lover.Implant." + normalize_tail(tail)

    if not key.startswith(BASE):
        raise ValueError(f"发现无法归入统一前缀的翻译键：{key}")

    tail = key[len(BASE):]

    special_exact = {
        "LetterMechsReclaimed": "Core.Reclamation.Letter.Text",
        "MechanoidMechanitorSelfWorkSpeedFeedback": "Core.WorkSpeed.Feedback",
    }
    if tail in special_exact:
        tail = special_exact[tail]
    else:
        for old, new in PREFIX_REMAPS:
            if tail.startswith(old):
                tail = new + tail[len(old):]
                break

        if tail.startswith("Bill."):
            tail = "Core." + tail
        elif tail.startswith("GravshipPilot."):
            tail = "Core.Gravship." + tail[len("GravshipPilot."):]
        elif not tail.startswith(KNOWN_SYSTEM_PREFIXES):
            tail = "Core." + tail

    return BASE + normalize_tail(tail)


def system_for_key(key: str) -> str:
    tail = key[len(BASE):]
    if tail.startswith("Settings."):
        return "Settings"
    if tail.startswith(("Scenario.", "Story.")):
        return "Scenario"
    if tail.startswith("JusticeBoss."):
        return "JusticeBoss"
    if tail.startswith("Justice."):
        return "Justice"
    if tail.startswith("Implants."):
        return "Implants"
    if tail.startswith("Lover."):
        return "Lover"
    if tail.startswith("DataProcessing."):
        return "DataProcessing"
    if tail.startswith("PurgeDirective."):
        return "PurgeDirective"
    if tail.startswith("Symbiosis."):
        return "SymbiosisCovenant"
    if tail.startswith("MechHiveNode."):
        return "MechHiveNode"
    if tail.startswith("Core."):
        return "Core"
    raise ValueError(f"无法确定翻译键所属系统：{key}")


def module_for_key(system: str, key: str) -> str:
    tail = key[len(BASE):]

    if system == "Core":
        if ".Bill." in tail:
            return "账单与工作提示"
        if ".Gravship." in tail:
            return "奥德赛旅行兼容"
        if ".Reclamation." in tail:
            return "机械族脱离监管"
        if ".WorkSpeed." in tail:
            return "工作速度反馈"
        return "基础机械族机械师功能"

    if system == "Settings":
        if ".JusticeBoss." in tail:
            return "正义 BOSS 难度"
        if ".PortraitDisplayForAllSaves." in tail:
            return "头像显示"
        if ".BrainImplants." in tail or ".LoverImplants." in tail:
            return "植入体功能"
        if ".SyntheticOffspring." in tail:
            return "仿生子嗣"
        if ".ProductivityCore." in tail:
            return "效能核心"
        if ".PurgeDirective." in tail:
            return "肃清指令界面"
        return "通用设置"

    if system == "Scenario":
        if ".ReadyPage." in tail:
            return "剧情风格选择"
        if ".CustomizePage." in tail:
            return "自定义剧情"
        if ".OrdinaryFaction" in tail or ".FactionRelation" in tail:
            return "普通派系关系"
        if ".MechHiveRelation" in tail or ".NoMechHive" in tail:
            return "机械巢关系"
        if ".Ideology" in tail:
            return "文化适配"
        if tail.startswith("Scenario.Scenario.") or tail.startswith("Scenario.Editor"):
            return "剧本词条"
        return "剧情组件"

    if system == "Justice":
        if ".Unlock." in tail:
            return "能力与特性解锁"
        if ".MechRecode." in tail:
            return "机体再编码"
        if ".MechReconstruction." in tail:
            return "机体重构"
        if ".MechHack." in tail:
            return "机械体骇入"
        if ".ConsciousnessTransfer." in tail:
            return "意识转移"
        if ".DormantActivation." in tail:
            return "休眠正义启动"
        return "正义通用功能"

    if system == "Implants":
        if ".Brain." in tail:
            return "脑部植入体安装"
        if ".ProductivityCore." in tail:
            return "效能核心游戏效果"
        return "植入体通用功能"

    if system == "Lover":
        if ".Implant." in tail:
            return "恋人植入体安装"
        if ".Pregnancy." in tail:
            return "仿生怀孕与分娩"
        if ".Spouse." in tail:
            return "配偶与婚姻关系"
        if ".Lovin." in tail:
            return "爱爱控制"
        return "恋人通用功能"

    if system == "DataProcessing":
        if ".Matrix" in tail:
            return "意识分配矩阵"
        if ".Dynamic" in tail:
            return "动态分配"
        if ".Specialization." in tail:
            return "特化模式"
        if ".Effect" in tail:
            return "数据处理效果"
        if ".Error." in tail or tail.endswith("Failed") or ".Invalid" in tail:
            return "错误与提示"
        return "基础分配界面"

    if system == "PurgeDirective":
        if ".Letter." in tail and ".Communication." not in tail:
            return "协议校验信件"
        if ".Communication.SpecialProtocols.Cluster." in tail:
            return "集群部署"
        if ".Communication.SpecialProtocols.ForceSupport." in tail:
            return "部队支援"
        if ".Communication.SpecialProtocols." in tail:
            return "特殊协议通用界面"
        if ".Communication.Query." in tail or ".Communication.Response." in tail:
            return "通讯问询"
        if ".Communication.Boot." in tail:
            return "通讯加载流程"
        if ".Communication.Pool." in tail or ".Communication.Dialogue." in tail:
            return "通讯文本"
        if ".Communication.Order." in tail:
            return "订单系统"
        if any(token in tail for token in (".Communication.Goods", ".Communication.Category.", ".Communication.Sort.", ".Communication.Stuff", ".Communication.Quality")):
            return "物资请求"
        if any(token in tail for token in (".Communication.Mech", ".Communication.Weight.")):
            return "机体申领"
        if ".Communication.Nav." in tail or ".Communication.Home." in tail or ".Communication.BackToHome" in tail:
            return "通讯主界面"
        if ".Communication.Status." in tail or ".Communication.Error." in tail:
            return "状态与错误"
        if ".Bossgroup." in tail or ".Scenario." in tail:
            return "路线限制"
        if ".Credits" in tail:
            return "肃清额度"
        return "肃清指令通用功能"

    if system == "SymbiosisCovenant":
        if ".SecretContact." in tail:
            return "秘密接触"
        if ".Declaration." in tail:
            return "脱离声明"
        if ".TrustReason." in tail or ".Trust" in tail:
            return "信任系统"
        if ".Relation" in tail:
            return "派系关系"
        if ".Covenant." in tail:
            return "盟约加入与退出"
        if ".Dev." in tail:
            return "开发者工具"
        if ".Page." in tail:
            return "页面导航"
        if ".Letter." in tail or "Retaliation" in tail:
            return "信件与机械巢报复"
        if ".Scenario." in tail:
            return "路线限制"
        return "通讯入口与主窗口"

    if system == "JusticeBoss":
        if ".Call." in tail:
            return "召唤与确认窗口"
        if ".Letter." in tail:
            return "抵达信件"
        if ".Quest." in tail:
            return "任务与世界事件"
        if ".Message." in tail:
            return "战斗状态与撤退"
        return "正义 BOSS 通用功能"

    if system == "MechHiveNode":
        if ".Frequency." in tail:
            return "自然生成频率"
        if ".Caravan." in tail or ".ProvideMaterials." in tail:
            return "远行队交付"
        if ".Transport." in tail:
            return "运输舱交付"
        if ".Attack." in tail:
            return "进攻限制"
        if ".Dev." in tail:
            return "开发者指令"
        if ".NewHive." in tail:
            return "新机械巢命名"
        if ".MaterialDemand" in tail:
            return "建设材料需求"
        if ".Letter." in tail:
            return "生成、建设与清理信件"
        return "机械巢节点通用功能"

    return "通用功能"


def load_entries() -> tuple[list[tuple[str, str]], dict[str, str]]:
    entries: list[tuple[str, str]] = []
    old_values: dict[str, str] = {}

    for filename in SOURCE_FILES:
        path = KEYED_DIR / filename
        if not path.exists():
            raise FileNotFoundError(f"缺少预期的中文翻译文件：{path.relative_to(ROOT)}")

        root = ET.parse(path).getroot()
        if root.tag != "LanguageData":
            raise ValueError(f"翻译文件根节点不是 LanguageData：{path.relative_to(ROOT)}")

        for child in root:
            if not isinstance(child.tag, str):
                continue
            key = child.tag
            value = child.text or ""
            if key in old_values:
                raise ValueError(f"旧翻译键重复定义：{key}")
            old_values[key] = value
            entries.append((key, value))

    return entries, old_values


def build_mapping(entries: list[tuple[str, str]]) -> tuple[dict[str, str], dict[str, str]]:
    mapping: dict[str, str] = {}
    new_values: dict[str, str] = {}

    for old_key, value in entries:
        new_key = rename_key(old_key)
        mapping[old_key] = new_key
        if new_key in new_values and new_values[new_key] != value:
            raise ValueError(
                f"翻译键整理后发生冲突且文本不同：{old_key} -> {new_key}"
            )
        new_values.setdefault(new_key, value)

    return mapping, new_values


def replace_references(mapping: dict[str, str]) -> None:
    source_paths = [
        path
        for path in ROOT.rglob("*")
        if path.is_file()
        and path.suffix.lower() in {".cs", ".xml"}
        and not path.is_relative_to(KEYED_DIR)
        and ".git" not in path.parts
    ]

    ordered = sorted(
        ((old, new) for old, new in mapping.items() if old != new),
        key=lambda pair: len(pair[0]),
        reverse=True,
    )

    prefix_fallbacks = [
        ("MAP_DataProcessingAllocation_Specialization_", BASE + "DataProcessing.Specialization."),
        ("MAP_DataProcessingAllocation_", BASE + "DataProcessing."),
        ("MAP_Settings_", BASE + "Settings."),
        ("MAP_LoverImplant.", BASE + "Lover.Implant."),
        (BASE + "MechHiveCommunication.", BASE + "PurgeDirective.Communication."),
        (BASE + "BrainImplant.", BASE + "Implants.Brain."),
        (BASE + "ProductivityCore.", BASE + "Implants.ProductivityCore."),
        (BASE + "SyntheticSpouse.", BASE + "Lover.Spouse."),
        (BASE + "SyntheticPregnancy.", BASE + "Lover.Pregnancy."),
        (BASE + "MechRecode.", BASE + "Justice.Ability.MechRecode."),
        (BASE + "MechReconstruction.", BASE + "Justice.Ability.MechReconstruction."),
        (BASE + "MechHack.", BASE + "Justice.Ability.MechHack."),
        (BASE + "ConsciousnessTransfer.", BASE + "Justice.ConsciousnessTransfer."),
        (BASE + "DormantJustice.", BASE + "Justice.DormantActivation."),
        (BASE + "NewMechHive.", BASE + "MechHiveNode.NewHive."),
    ]

    for path in source_paths:
        original = path.read_text(encoding="utf-8-sig")
        updated = original
        for old, new in ordered:
            updated = updated.replace(old, new)
        for old, new in prefix_fallbacks:
            updated = updated.replace(old, new)
        if updated != original:
            path.write_text(updated, encoding="utf-8", newline="\n")


def write_output_files(new_values: dict[str, str]) -> None:
    grouped: dict[str, OrderedDict[str, list[tuple[str, str]]]] = {
        system: OrderedDict() for system in OUTPUT_FILES
    }

    for key, value in new_values.items():
        system = system_for_key(key)
        module = module_for_key(system, key)
        grouped[system].setdefault(module, []).append((key, value))

    output_paths = {KEYED_DIR / filename for filename in OUTPUT_FILES.values()}

    for system, filename in OUTPUT_FILES.items():
        path = KEYED_DIR / filename
        lines = ['<?xml version="1.0" encoding="utf-8"?>', '<LanguageData>', '']
        modules = grouped[system]
        for module_index, (module, module_entries) in enumerate(modules.items()):
            lines.append(f"  <!-- 模块：{module} -->")
            for key, value in module_entries:
                serialized = escape(value)
                lines.append(f"  <{key}>{serialized}</{key}>")
            if module_index != len(modules) - 1:
                lines.append("")
        lines.extend(["", "</LanguageData>", ""])
        path.write_text("\n".join(lines), encoding="utf-8", newline="\n")

    for filename in SOURCE_FILES:
        path = KEYED_DIR / filename
        if path.exists() and path not in output_paths:
            path.unlink()


def validate(mapping: dict[str, str], new_values: dict[str, str]) -> None:
    output_paths = [KEYED_DIR / filename for filename in OUTPUT_FILES.values()]

    parsed_keys: dict[str, Path] = {}
    for path in output_paths:
        if not path.exists():
            raise FileNotFoundError(f"未生成目标翻译文件：{path.relative_to(ROOT)}")
        root = ET.parse(path).getroot()
        for child in root:
            if not isinstance(child.tag, str):
                continue
            if child.tag in parsed_keys:
                raise ValueError(
                    f"新翻译键重复定义：{child.tag}，位于 {parsed_keys[child.tag]} 与 {path}"
                )
            parsed_keys[child.tag] = path

    expected = set(new_values)
    actual = set(parsed_keys)
    if expected != actual:
        missing = sorted(expected - actual)
        unexpected = sorted(actual - expected)
        raise ValueError(f"新翻译键集合不一致；缺失={missing[:10]}，多余={unexpected[:10]}")

    text_paths = [
        path
        for path in ROOT.rglob("*")
        if path.is_file()
        and path.suffix.lower() in {".cs", ".xml"}
        and ".git" not in path.parts
    ]

    changed_old_keys = [old for old, new in mapping.items() if old != new]
    legacy_markers = (
        "MAP_DataProcessingAllocation_",
        "MAP_Settings_",
        "MAP_LoverImplant.",
        BASE + "MechHiveCommunication.",
        BASE + "BrainImplant.",
        BASE + "SyntheticSpouse.",
        BASE + "SyntheticPregnancy.",
        BASE + "MechRecode.",
        BASE + "MechReconstruction.",
        BASE + "MechHack.",
        BASE + "ConsciousnessTransfer.",
        BASE + "DormantJustice.",
        BASE + "NewMechHive.",
    )

    problems: list[str] = []
    for path in text_paths:
        content = path.read_text(encoding="utf-8-sig")
        for marker in legacy_markers:
            if marker in content:
                problems.append(f"{path.relative_to(ROOT)} 仍含旧前缀 {marker}")
        for old_key in changed_old_keys:
            if old_key in content:
                problems.append(f"{path.relative_to(ROOT)} 仍含旧键 {old_key}")
        if len(problems) >= 30:
            break

    if problems:
        raise ValueError("旧翻译键残留：\n" + "\n".join(problems))

    # 本次任务只允许生成简体中文 Keyed 文件，禁止建立英文翻译目录或文件。
    english_dir = ROOT / "1.6" / "Languages" / "English"
    if english_dir.exists():
        raise ValueError("检测到 English 语言目录；本次整理不得创建英文翻译键")

    expected_filenames = set(OUTPUT_FILES.values())
    actual_xml = {path.name for path in KEYED_DIR.glob("*.xml")}
    if actual_xml != expected_filenames:
        raise ValueError(
            f"中文 Keyed 文件集合不符合预期；当前={sorted(actual_xml)}，预期={sorted(expected_filenames)}"
        )


def remove_one_time_files() -> None:
    workflow = ROOT / ".github" / "workflows" / "reorganize-chinese-translation-keys.yml"
    script = Path(__file__).resolve()
    if workflow.exists():
        workflow.unlink()
    if script.exists():
        script.unlink()

    tools_dir = ROOT / "tools"
    if tools_dir.exists() and not any(tools_dir.iterdir()):
        tools_dir.rmdir()

    workflows_dir = ROOT / ".github" / "workflows"
    if workflows_dir.exists() and not any(workflows_dir.iterdir()):
        workflows_dir.rmdir()
    github_dir = ROOT / ".github"
    if github_dir.exists() and not any(github_dir.iterdir()):
        github_dir.rmdir()


def main() -> int:
    entries, _ = load_entries()
    mapping, new_values = build_mapping(entries)
    replace_references(mapping)
    write_output_files(new_values)
    validate(mapping, new_values)
    remove_one_time_files()

    print(f"已整理 {len(entries)} 个旧翻译键。")
    print(f"生成 {len(OUTPUT_FILES)} 个中文系统翻译文件。")
    print(f"其中 {sum(1 for old, new in mapping.items() if old != new)} 个翻译键完成重命名。")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"中文翻译键整理失败：{exc}", file=sys.stderr)
        raise
