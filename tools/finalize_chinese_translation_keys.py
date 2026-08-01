#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
KEYED_DIR = ROOT / "1.6" / "Languages" / "ChineseSimplified" / "Keyed"
BASE = "MAP_MechanoidMechanitor."

OUTPUT_PREFIXES = {
    "MAP_Core.xml": BASE + "Core.",
    "MAP_Settings.xml": BASE + "Settings.",
    "MAP_Scenario.xml": BASE + "Scenario.",
    "MAP_Justice.xml": BASE + "Justice.",
    "MAP_Implants.xml": BASE + "Implants.",
    "MAP_Lover.xml": BASE + "Lover.",
    "MAP_DataProcessing.xml": BASE + "DataProcessing.",
    "MAP_PurgeDirective.xml": BASE + "PurgeDirective.",
    "MAP_SymbiosisCovenant.xml": BASE + "Symbiosis.",
    "MAP_JusticeBoss.xml": BASE + "JusticeBoss.",
    "MAP_MechHiveNode.xml": BASE + "MechHiveNode.",
}

KEY_PATTERN = re.compile(r"<(?P<key>MAP_MechanoidMechanitor\.[^>/\s]+)>")


def rename_story_key(key: str) -> str:
    replacements = [
        (BASE + "Story.MechHiveNodeFrequency.", BASE + "MechHiveNode.Scenario.Frequency."),
        (BASE + "Story.OrdinaryFactionRelationsMode.", BASE + "Scenario.Relations.OrdinaryFaction.Mode."),
        (BASE + "Story.FactionRelationOption.", BASE + "Scenario.Relations.Faction.Option."),
        (BASE + "Story.MechHiveRelationMode.", BASE + "Scenario.Relations.MechHive.Mode."),
        (BASE + "Story.MechHiveRelation.", BASE + "Scenario.Relations.MechHive."),
        (BASE + "Story.IdeologyAdaptationLevel.", BASE + "Scenario.Ideology.AdaptationLevel."),
        (BASE + "Story.Option.", BASE + "Scenario.Option."),
    ]
    exact = {
        BASE + "Story.NoOrdinaryFactions": BASE + "Scenario.Relations.OrdinaryFaction.NoneAvailable",
        BASE + "Story.NoMechHive": BASE + "Scenario.Relations.MechHive.NoneAvailable",
    }
    if key in exact:
        return exact[key]
    for old, new in replacements:
        if key.startswith(old):
            return new + key[len(old):]
    if key.startswith(BASE + "Story."):
        return BASE + "Scenario.Story." + key[len(BASE + "Story."):]
    return key


def normalize_segments(key: str) -> str:
    parts = key.split(".")
    parts = ["Description" if part == "Desc" else "Tooltip" if part == "Tip" else part for part in parts]
    return ".".join(parts)


def collect_mapping() -> dict[str, str]:
    mapping: dict[str, str] = {}
    seen_new: dict[str, str] = {}
    for filename in OUTPUT_PREFIXES:
        path = KEYED_DIR / filename
        if not path.exists():
            raise FileNotFoundError(f"缺少目标中文翻译文件：{path.relative_to(ROOT)}")
        content = path.read_text(encoding="utf-8-sig")
        for match in KEY_PATTERN.finditer(content):
            old = match.group("key")
            new = normalize_segments(rename_story_key(old))
            mapping[old] = new
            if new in seen_new and seen_new[new] != old:
                raise ValueError(f"翻译键收口后发生冲突：{seen_new[new]} 与 {old} -> {new}")
            seen_new[new] = old
    return mapping


def replace_all_references(mapping: dict[str, str]) -> None:
    replacements = sorted(
        ((old, new) for old, new in mapping.items() if old != new),
        key=lambda pair: len(pair[0]),
        reverse=True,
    )
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in {".cs", ".xml"} or ".git" in path.parts:
            continue
        original = path.read_text(encoding="utf-8-sig")
        updated = original
        for old, new in replacements:
            updated = updated.replace(old, new)
        if updated != original:
            path.write_text(updated, encoding="utf-8", newline="\n")


def collapse_blank_lines(lines: list[str]) -> list[str]:
    result: list[str] = []
    blank = False
    for line in lines:
        is_blank = not line.strip()
        if is_blank and blank:
            continue
        result.append(line)
        blank = is_blank
    return result


def move_mech_hive_frequency_entries() -> None:
    scenario_path = KEYED_DIR / "MAP_Scenario.xml"
    node_path = KEYED_DIR / "MAP_MechHiveNode.xml"
    prefix = BASE + "MechHiveNode.Scenario.Frequency."

    scenario_lines = scenario_path.read_text(encoding="utf-8").splitlines()
    moved = [line for line in scenario_lines if f"<{prefix}" in line]
    if len(moved) != 4:
        raise ValueError(f"预期移动 4 个机械巢节点频率键，实际找到 {len(moved)} 个")
    scenario_lines = [line for line in scenario_lines if f"<{prefix}" not in line]
    scenario_lines = collapse_blank_lines(scenario_lines)
    scenario_path.write_text("\n".join(scenario_lines) + "\n", encoding="utf-8", newline="\n")

    node_lines = node_path.read_text(encoding="utf-8").splitlines()
    if any(f"<{prefix}" in line for line in node_lines):
        raise ValueError("机械巢节点文件中已存在频率键，拒绝重复插入")

    try:
        language_index = node_lines.index("<LanguageData>")
    except ValueError as exc:
        raise ValueError("机械巢节点翻译文件缺少 LanguageData 根节点") from exc

    insertion = [
        "",
        "  <!-- 模块：自然生成频率 -->",
        *moved,
    ]
    insert_at = language_index + 1
    while insert_at < len(node_lines) and not node_lines[insert_at].strip():
        insert_at += 1
    node_lines[insert_at:insert_at] = insertion + [""]
    node_lines = collapse_blank_lines(node_lines)
    node_path.write_text("\n".join(node_lines) + "\n", encoding="utf-8", newline="\n")


def validate(mapping: dict[str, str]) -> None:
    all_keys: dict[str, str] = {}
    for filename, expected_prefix in OUTPUT_PREFIXES.items():
        path = KEYED_DIR / filename
        root = ET.parse(path).getroot()
        if root.tag != "LanguageData":
            raise ValueError(f"根节点异常：{filename}")
        for child in root:
            if not isinstance(child.tag, str):
                continue
            key = child.tag
            if not key.startswith(expected_prefix):
                raise ValueError(f"{filename} 中存在不属于该系统的翻译键：{key}")
            if key in all_keys:
                raise ValueError(f"翻译键重复定义：{key}，位于 {all_keys[key]} 与 {filename}")
            all_keys[key] = filename
            segments = key.split(".")
            if "Desc" in segments or "Tip" in segments or "Story" in segments[:2]:
                raise ValueError(f"翻译键命名尚未收口：{key}")

    changed_old = [old for old, new in mapping.items() if old != new]
    problems: list[str] = []
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in {".cs", ".xml"} or ".git" in path.parts:
            continue
        content = path.read_text(encoding="utf-8-sig")
        for old in changed_old:
            if old in content:
                problems.append(f"{path.relative_to(ROOT)} 仍含旧键 {old}")
                if len(problems) >= 20:
                    break
        if len(problems) >= 20:
            break
    if problems:
        raise ValueError("旧键残留：\n" + "\n".join(problems))


def remove_one_time_files() -> None:
    workflow = ROOT / ".github" / "workflows" / "finalize-chinese-translation-keys.yml"
    script = Path(__file__).resolve()
    if workflow.exists():
        workflow.unlink()
    if script.exists():
        script.unlink()

    for directory in [ROOT / "tools", ROOT / ".github" / "workflows", ROOT / ".github"]:
        if directory.exists() and not any(directory.iterdir()):
            directory.rmdir()


def main() -> int:
    mapping = collect_mapping()
    replace_all_references(mapping)
    move_mech_hive_frequency_entries()
    validate(mapping)
    remove_one_time_files()
    changed = sum(1 for old, new in mapping.items() if old != new)
    print(f"已完成 {changed} 个中文翻译键的最终命名收口。")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"中文翻译键最终收口失败：{exc}", file=sys.stderr)
        raise
