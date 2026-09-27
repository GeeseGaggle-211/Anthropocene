#!/usr/bin/env python3
"""Reduce Neolithic craft validation outputs using their declared CSV headers.

The runner's CSV/JSONL files remain the detailed evidence. This script validates
their structure and writes compact coverage, scenario, specialist, unresolved-
constraint, and (when exact checkpoint hashes exist) paired summaries.
"""
import argparse
import csv
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict


ROLE_TRADITION = {
    "Toolmaker": "Toolmaking",
    "Potter": "Pottery",
    "Weaver": "Weaving",
    "Carpenter": "Carpentry",
    "Builder": "Construction",
    "Stonecutter": "Toolmaking",
    "ClayMiner": "Pottery",
    "Woodcutter": "Carpentry",
}
ROLE_WORKSTREAM = {
    "Toolmaker": "manufactured-output", "Potter": "manufactured-output",
    "Weaver": "manufactured-output", "Carpenter": "manufactured-output",
    "Builder": "manufactured-output", "Stonecutter": "material-extraction",
    "ClayMiner": "material-extraction", "Woodcutter": "material-extraction",
}
GOOD_NAMES = ("HandTools", "Pots", "Cloth", "FarmTools", "Housing", "Tools",
              "AnimalFabric", "LightHousing", "HerdingTools")
ID_FIELDS = ("seed", "camp", "route", "unlock_rule", "unlock_turn")
SCENARIO_FIELDS = ("cohort_class", "scenario", "shock", "initial_habitat", "unlock_save_sha256")


def read_csv(path):
    if not os.path.isfile(path):
        return [], []
    with open(path, "r", newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        headers = reader.fieldnames or []
        if not headers:
            raise ValueError("CSV has no header: " + path)
        rows = []
        for line_number, row in enumerate(reader, 2):
            if None in row or any(value is None for value in row.values()):
                raise ValueError("CSV row width mismatch at {}:{}".format(path, line_number))
            rows.append(row)
    return headers, rows


def read_json(path, default=None):
    if not os.path.isfile(path):
        return {} if default is None else default
    with open(path, "r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def number(value):
    try:
        result = float(value)
        return result if math.isfinite(result) else None
    except (TypeError, ValueError):
        return None


def mean(values):
    values = [value for value in values if value is not None]
    return sum(values) / len(values) if values else None


def fmt(value):
    if value is None:
        return ""
    return "{:.6g}".format(value)


def require_headers(path, headers, required):
    missing = [name for name in required if name not in headers]
    if missing:
        raise ValueError("{} is missing required headers: {}".format(path, ", ".join(missing)))


def row_key(row):
    return tuple(str(row.get(field, "")) for field in ID_FIELDS)


def stable_key(row, include_hash=True):
    fields = ID_FIELDS + ("branch",) + tuple(field for field in SCENARIO_FIELDS if field != "unlock_save_sha256" and row.get(field, ""))
    if row.get("unlock_save_sha256", ""):
        fields += ("unlock_save_sha256",)
    return tuple(str(row.get(field, "")) for field in fields)


def key_text(key):
    return "/".join(str(value) for value in key)


def parse_pairs(text):
    """Parse Good:value|Good:value map cells without depending on column order."""
    result = {}
    for part in (text or "").split("|"):
        if ":" not in part:
            continue
        name, value = part.split(":", 1)
        parsed = number(value)
        if parsed is not None:
            result[name] = parsed
    return result


def parse_source_counts(text):
    result = {}
    for part in (text or "").split(";"):
        if "=" not in part:
            continue
        name, value = part.split("=", 1)
        try:
            result[name.strip()] = int(value)
        except ValueError:
            continue
    return result


def cohort_for(row, unlock_index):
    explicit = row.get("cohort_class", "").strip()
    if explicit:
        return explicit
    match = unlock_index.get(row_key(row))
    if match:
        candidates = match if isinstance(match, list) else [match]
        wanted_hash = row.get("unlock_save_sha256", "")
        for candidate in candidates:
            if wanted_hash and candidate.get("unlock_save_sha256", "") != wanted_hash:
                continue
            if candidate.get("cohort_class"):
                return candidate["cohort_class"]
    # Compatibility for earlier runner outputs without cohort metadata.
    branch = row.get("branch", "")
    return "natural" if branch == "policy" else "conditioned-control"


def jsonl_records(path):
    if not os.path.isfile(path):
        return []
    records = []
    with open(path, "r", encoding="utf-8-sig") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError as exc:
                raise ValueError("Invalid JSONL at {}:{}: {}".format(path, line_number, exc))
    return records


def load_run(label, directory):
    directory = os.path.abspath(directory)
    manifest = read_json(os.path.join(directory, "manifest.json"))
    if not manifest:
        raise ValueError("Missing manifest.json in " + directory)
    traj_path = os.path.join(directory, "trajectory.csv")
    traj_headers, trajectory = read_csv(traj_path)
    require_headers(traj_path, traj_headers,
                    ["seed", "camp", "route", "unlock_turn", "branch", "window_turn",
                     "qol", "useful_demand_goods", "realized_goods", "constraints"])
    unlock_path = os.path.join(directory, "unlock.csv")
    unlock_headers, unlock = read_csv(unlock_path)
    require_headers(unlock_path, unlock_headers, ["seed", "camp", "route", "unlock_turn"])
    windows_path = os.path.join(directory, "window-summary.csv")
    window_headers, windows = read_csv(windows_path)
    censored_path = os.path.join(directory, "censored.csv")
    _, censored = read_csv(censored_path)
    replay_path = os.path.join(directory, "replay.csv")
    replay_headers, replay = read_csv(replay_path)
    jsonl = jsonl_records(os.path.join(directory, "trajectory.jsonl"))
    # Current and old schema aliases are normalized at the input boundary.
    for row in trajectory:
        if "linked_population" not in row and "population" in row:
            row["linked_population"] = row["population"]
        if "forecast_goods" not in row and "scheduled_goods" in row:
            row["forecast_goods"] = row["scheduled_goods"]
    unlock_index = defaultdict(list)
    for row in unlock:
        unlock_index[row_key(row)].append(row)
    return {
        "label": label or manifest.get("sourceLabel", os.path.basename(directory)),
        "directory": directory,
        "manifest": manifest,
        "trajectory": trajectory,
        "trajectory_headers": traj_headers,
        "unlock": unlock,
        "unlock_headers": unlock_headers,
        "unlock_index": unlock_index,
        "windows": windows,
        "window_headers": window_headers,
        "censored": censored,
        "replay": replay,
        "replay_headers": replay_headers,
        "jsonl": jsonl,
    }


def is_checkpoint_replay(run):
    command_line = run["manifest"].get("commandLine", [])
    if isinstance(command_line, str):
        return "--input-unlock-saves" in command_line
    return any(str(argument).lower() == "--input-unlock-saves" for argument in command_line)


def discovery_censoring_label(run):
    if is_checkpoint_replay(run):
        return "not rerun (unlock-checkpoint replay)"
    return "{} observed".format(len(run["censored"]))


def validate_windows(run):
    grouped = defaultdict(list)
    for row in run["trajectory"]:
        status = row.get("control_action_status")
        if status is not None and not status.startswith("accepted:"):
            raise ValueError("{} has rejected/unverified control action at {} window turn {}: {}".format(
                run["label"], key_text(stable_key(row)), row.get("window_turn", ""), status))
        key = stable_key(row)
        grouped[key].append(row)
    complete = {}
    for key, rows in grouped.items():
        turns = sorted(int(row["window_turn"]) for row in rows)
        if turns != list(range(1, 26)):
            raise ValueError("{} has incomplete/duplicate 25-turn scenario {}: turns={}".format(
                run["label"], key_text(key), turns))
        complete[key] = rows
    return complete


def scenario_summaries(run, trajectories):
    window_rows = {stable_key(row): row for row in run["windows"]}
    detail_by_key = {}
    groups = defaultdict(list)
    details = []
    for key, rows in trajectories.items():
        first = rows[0]
        cohort = cohort_for(first, run["unlock_index"])
        branch = first.get("branch", "")
        statuses = [row.get("control_action_status") for row in rows]
        is_policy = branch == "policy" or branch.endswith("-policy")
        control_validation = ("accepted" if all(value and value.startswith("accepted:") for value in statuses)
                              else "legacy-policy-unverified" if is_policy and all(value is None for value in statuses)
                              else "unavailable-or-unverified-control")
        include_in_aggregate = control_validation == "accepted" or (
            branch == "policy" and cohort.startswith("natural") and
            control_validation == "legacy-policy-unverified")
        # Legacy natural-policy output remains provisional; never interpret an
        # unverified conditioned control's zero output as a policy result.
        if include_in_aggregate:
            group_key = (cohort, first.get("route", ""), branch)
            groups[group_key].append((key, rows))
        qol = [number(row.get("qol")) for row in rows]
        goods_totals = Counter()
        useful_turns = 0
        for row in rows:
            outputs = parse_pairs(row.get("realized_goods", ""))
            goods_totals.update({name: value for name, value in outputs.items() if value > 0})
            if any(value > 0 for value in outputs.values()):
                useful_turns += 1
        wrow = window_rows.get(key, {})
        details.append({
            "source": run["label"], "cohort_class": cohort,
            "seed": first.get("seed", ""), "camp": first.get("camp", ""),
            "player": first.get("player", ""), "route": first.get("route", ""),
            "unlock_rule": first.get("unlock_rule", ""), "unlock_turn": first.get("unlock_turn", ""),
            "unlock_save_sha256": first.get("unlock_save_sha256", ""),
            "branch": first.get("branch", ""), "turns": len(rows), "useful_output_turns": useful_turns,
            "control_validation": control_validation,
            "included_in_policy_summary": "yes" if include_in_aggregate else "no",
            "mean_qol": mean(qol), "qol_start": number(rows[0].get("qol")), "qol_end": number(rows[-1].get("qol")),
            "mean_food_satisfaction": mean(number(row.get("food_satisfaction")) for row in rows),
            "mean_water_satisfaction": mean(number(row.get("water_satisfaction")) for row in rows),
            "mean_clothing_satisfaction": mean(number(row.get("clothing_satisfaction")) for row in rows),
            "mean_housing_satisfaction": mean(number(row.get("housing_satisfaction")) for row in rows),
            "population_end": number(rows[-1].get("linked_population", rows[-1].get("population"))),
            "residents_end": number(rows[-1].get("residents")),
            "field_q_end": number(rows[-1].get("field_q_end")), "field_e_end": number(rows[-1].get("field_e_end")),
            "labor_diagnostic_status": rows[0].get("farm_work_diagnostic_status", "unknown"),
            "labor_audit": wrow.get("labor_budget_audit", rows[0].get("labor_audit", "")),
            "realized_goods": dict(sorted(goods_totals.items())),
            "first_useful_output_turns": wrow.get("first_useful_output_turns", ""),
            "first_mastery_turns": wrow.get("first_mastery_turns", ""),
            "productive_specialist_turns": wrow.get("productive_specialist_turns", ""),
            "longest_sustained_specialist_streak": wrow.get("longest_sustained_specialist_streak", ""),
        })
        detail_by_key[key] = details[-1]
    aggregate = []
    for (cohort, route, branch), members in sorted(groups.items()):
        member_details = [detail_by_key[key] for key, _ in members]
        aggregate.append({
            "source": run["label"], "cohort_class": cohort, "route": route, "branch": branch,
            "windows": len(members), "complete_windows": sum(1 for _, rows in members if len(rows) == 25),
            "mean_qol": mean(row["mean_qol"] for row in member_details),
            "mean_food_satisfaction": mean(row["mean_food_satisfaction"] for row in member_details),
            "mean_water_satisfaction": mean(row["mean_water_satisfaction"] for row in member_details),
            "mean_clothing_satisfaction": mean(row["mean_clothing_satisfaction"] for row in member_details),
            "mean_housing_satisfaction": mean(row["mean_housing_satisfaction"] for row in member_details),
            "mean_useful_output_turns": mean(row["useful_output_turns"] for row in member_details),
            "censored_cohorts": len(run["censored"]),
        })
    return details, aggregate


def specialist_summaries(run):
    grouped = defaultdict(list)
    for item in run["jsonl"]:
        if "seed" not in item or "branch" not in item:
            continue
        key = (str(item.get("seed", "")), str(item.get("camp", "")), str(item.get("route", "")),
               str(item.get("unlock_rule", item.get("unlockRule", ""))),
               str(item.get("unlock_turn", item.get("unlockTurn", ""))), str(item.get("branch", "")),
               str(item.get("cohort_class", item.get("cohortClass", ""))),
               str(item.get("unlock_save_sha256", item.get("unlockSaveSha256", ""))))
        grouped[key].append(item)
    rows = []
    for key, turns in grouped.items():
        turns.sort(key=lambda item: int(item.get("windowTurn", 0)))
        first = turns[0]
        normalized_first = dict(first)
        normalized_first["unlock_rule"] = first.get("unlock_rule", first.get("unlockRule", ""))
        normalized_first["unlock_turn"] = first.get("unlock_turn", first.get("unlockTurn", ""))
        normalized_first["cohort_class"] = first.get("cohort_class", first.get("cohortClass", ""))
        normalized_first["unlock_save_sha256"] = first.get("unlock_save_sha256", first.get("unlockSaveSha256", ""))
        cohort = cohort_for(normalized_first, run["unlock_index"])
        assignments_by_role = defaultdict(list)
        productive_by_role = defaultdict(list)
        for item in turns:
            turn = int(item.get("windowTurn", 0))
            assignments = item.get("assignments") or {}
            productive_value = item.get("specialistRoles", []) or []
            if isinstance(productive_value, str):
                productive = set(filter(None, productive_value.split("|")))
            else:
                productive = set(productive_value)
            materials = item.get("materials") or {}
            extracted = materials.get("extracted") or []
            productive_extraction = {receipt.get("Role", receipt.get("role", "")) for receipt in extracted
                                     if (number(receipt.get("WorkerIntervals", receipt.get("workerIntervals"))) or 0) > 0}
            for role in ROLE_TRADITION:
                count = assignments.get(role, 0)
                if isinstance(count, dict):
                    count = count.get("population", 0)
                try:
                    count = int(count)
                except (TypeError, ValueError):
                    count = 0
                assignments_by_role[role].append((turn, count))
                is_productive = role in (productive_extraction if ROLE_WORKSTREAM[role] == "material-extraction" else productive)
                productive_by_role[role].append((turn, is_productive))
        for role, tradition in ROLE_TRADITION.items():
            assigned_turns = [(turn, count) for turn, count in assignments_by_role[role] if count > 0]
            productive_turns = [turn for turn, active in productive_by_role[role] if active]
            run_length = longest = 0
            prior_turn = None
            for turn, active in productive_by_role[role]:
                if active and (prior_turn is None or turn == prior_turn + 1):
                    run_length += 1
                elif active:
                    run_length = 1
                else:
                    run_length = 0
                longest = max(longest, run_length)
                prior_turn = turn
            rows.append({
                "source": run["label"], "cohort_class": cohort, "seed": key[0], "camp": key[1],
                "route": key[2], "unlock_rule": key[3], "unlock_turn": key[4], "branch": key[5],
                "scenario": first.get("scenario", ""), "shock": first.get("shock", ""),
                "initial_habitat": first.get("initialHabitat", first.get("initial_habitat", "")),
                "unlock_save_sha256": key[7],
                "tradition": tradition, "workstream": ROLE_WORKSTREAM[role], "specialist_role": role,
                "first_assignment_turn": min((turn for turn, _ in assigned_turns), default=""),
                "assigned_turns": len(assigned_turns), "first_productive_turn": min(productive_turns, default=""),
                "productive_turns": len(productive_turns), "longest_productive_streak": longest,
            })
    return rows


def constraint_summaries(run, trajectories):
    counts = Counter()
    for rows in trajectories.values():
        first = rows[0]
        cohort = cohort_for(first, run["unlock_index"])
        for row in rows:
            for token in filter(None, (row.get("constraints", "") or "").split(";")):
                if ":" in token:
                    good, reason = token.split(":", 1)
                else:
                    good, reason = "unknown", token
                counts[(cohort, first.get("route", ""), first.get("branch", ""), good, reason)] += 1
    return [{"source": run["label"], "cohort_class": key[0], "route": key[1], "branch": key[2],
             "good": key[3], "constraint": key[4], "turn_occurrences": value,
             "unresolved": "yes" if "unresolved" in key[4].lower() else "no"}
            for key, value in sorted(counts.items())]


def coverage_summaries(run):
    grouped = defaultdict(list)
    for row in run["unlock"]:
        cohort = row.get("cohort_class") or "natural"
        initial_habitat = row.get("initial_habitat") or row.get("spawn_habitat") or "unknown"
        habitat = row.get("habitat", row.get("unlock_habitat", "unknown"))
        grouped[(cohort, row.get("route", ""), initial_habitat, habitat)].append(row)
    results = []
    for key, members in sorted(grouped.items()):
        value = {"source": run["label"], "cohort_class": key[0], "route": key[1],
                 "initial_habitat": key[2], "unlock_habitat": key[3], "unlock_count": len(members),
                 "stone_known_source_id_camps": 0, "clay_known_source_id_camps": 0,
                 "wood_known_source_id_camps": 0, "stone_positive_accessible_stock_camps": 0,
                 "clay_positive_accessible_stock_camps": 0, "wood_positive_accessible_stock_camps": 0,
                 "mean_stone_known_source_ids": None, "mean_clay_known_source_ids": None,
                 "mean_wood_known_source_ids": None, "mean_stone_accessible_stock_at_unlock": None,
                 "mean_clay_accessible_stock_at_unlock": None, "mean_wood_accessible_stock_at_unlock": None}
        counts_by = {name: [] for name in ("Stone", "Clay", "Wood")}
        qty_by = {name: [] for name in ("Stone", "Clay", "Wood")}
        for row in members:
            source_counts = parse_source_counts(row.get("source_counts", ""))
            for material in counts_by:
                count = number(source_counts.get(material))
                quantity = number(row.get("source_" + material.lower()))
                if count is not None:
                    counts_by[material].append(count)
                    if count > 0:
                        value[material.lower() + "_known_source_id_camps"] += 1
                if quantity is not None:
                    qty_by[material].append(quantity)
                    if quantity > 0:
                        value[material.lower() + "_positive_accessible_stock_camps"] += 1
        for material in counts_by:
            value["mean_" + material.lower() + "_known_source_ids"] = mean(counts_by[material])
            value["mean_" + material.lower() + "_accessible_stock_at_unlock"] = mean(qty_by[material])
        results.append(value)
    # Censored starts are retained as a separate denominator; there is no honest
    # route or unlock habitat to assign to them.
    censored_classes = Counter()
    for row in run["censored"]:
        classification = (row.get("classification", "") or "").lower()
        technology = (row.get("technology", "") or "").lower()
        if technology == "unresolved" or "generated-site" in classification:
            cohort = "conditioned-site-censored"
        elif classification.startswith("dead-before-first-craft-route-unlock") or classification.startswith("unresolved-no-craft-route-unlock"):
            cohort = "natural-censored-before-unlock"
        else:
            cohort = "censored-class-unknown"
        censored_classes[cohort] += 1
    for cohort, count in sorted(censored_classes.items()):
        results.append({"source": run["label"], "cohort_class": cohort,
                        "route": "", "initial_habitat": "unknown", "unlock_habitat": "unknown",
                        "unlock_count": 0, "censored_count": count})
    if is_checkpoint_replay(run):
        results.append({"source": run["label"], "cohort_class": "discovery-not-rerun-checkpoint-replay",
                        "route": "", "initial_habitat": "unknown", "unlock_habitat": "unknown",
                        "unlock_count": 0, "censored_count": "not measured",
                        "discovery_censoring": "not rerun (unlock-checkpoint replay)"})
    return results


def replay_summaries(run):
    """Classify replay.csv evidence without treating every PASS as continuation."""
    counts = Counter()
    for row in run["replay"]:
        details = row.get("details", "") or ""
        if "turns=3; workers=4" in details:
            check_type = "three-turn-four-worker-exact-continuation"
        elif details.startswith("loaded-identical-unlock-save"):
            check_type = "checkpoint-load-roundtrip-only"
        elif "available=" in details:
            check_type = "scarcity-intervention-load-only"
        else:
            check_type = "other-or-unclassified"
        counts[(check_type, row.get("status", "unknown"))] += 1
    return [{"source": run["label"], "check_type": key[0], "status": key[1], "count": value}
            for key, value in sorted(counts.items())]


def aggregate_pair_details(run, trajectories):
    details, _ = scenario_summaries(run, trajectories)
    return {stable_key({**detail, "unlock_rule": detail.get("unlock_rule", "")}, True) + (detail["branch"],): detail
            for detail in details}


def paired_comparisons(candidate, baseline, candidate_trajectories, baseline_trajectories):
    rows = []
    candidate_config = candidate["manifest"].get("configHash")
    baseline_config = baseline["manifest"].get("configHash")
    candidate_config_file = candidate["manifest"].get("configFileHash")
    baseline_config_file = baseline["manifest"].get("configFileHash")
    candidate_resource_file = candidate["manifest"].get("resourceConfigHash")
    baseline_resource_file = baseline["manifest"].get("resourceConfigHash")
    if not all((candidate_config_file, baseline_config_file, candidate_resource_file, baseline_resource_file)):
        return [{"pair_status": "excluded-config-file-hash-unavailable", "candidate": candidate["label"],
                 "baseline": baseline["label"], "candidate_config_hash": candidate_config,
                 "baseline_config_hash": baseline_config,
                 "candidate_config_file_hash": candidate_config_file,
                 "baseline_config_file_hash": baseline_config_file,
                 "candidate_resource_config_hash": candidate_resource_file,
                 "baseline_resource_config_hash": baseline_resource_file}]
    if (str(candidate_config_file).lower() != str(baseline_config_file).lower() or
            str(candidate_resource_file).lower() != str(baseline_resource_file).lower()):
        return [{"pair_status": "excluded-config-file-hash-mismatch", "candidate": candidate["label"],
                 "baseline": baseline["label"], "candidate_config_hash": candidate_config,
                 "baseline_config_hash": baseline_config,
                 "candidate_config_file_hash": candidate_config_file,
                 "baseline_config_file_hash": baseline_config_file,
                 "candidate_resource_config_hash": candidate_resource_file,
                 "baseline_resource_config_hash": baseline_resource_file}]
    cand = {}
    base = {}
    for key, window_rows in candidate_trajectories.items():
        first = window_rows[0]
        match = (first.get("seed", ""), first.get("camp", ""), first.get("route", ""),
                 first.get("unlock_rule", ""), first.get("unlock_turn", ""), first.get("branch", ""),
                 first.get("cohort_class", ""), first.get("scenario", ""), first.get("shock", ""),
                 first.get("initial_habitat", ""))
        cand[match] = window_rows
    for key, window_rows in baseline_trajectories.items():
        first = window_rows[0]
        match = (first.get("seed", ""), first.get("camp", ""), first.get("route", ""),
                 first.get("unlock_rule", ""), first.get("unlock_turn", ""), first.get("branch", ""),
                 first.get("cohort_class", ""), first.get("scenario", ""), first.get("shock", ""),
                 first.get("initial_habitat", ""))
        base[match] = window_rows
    for key in sorted(set(cand) | set(base)):
        left, right = cand.get(key), base.get(key)
        c_hash = (left[0].get("unlock_save_sha256", "") if left else "")
        b_hash = (right[0].get("unlock_save_sha256", "") if right else "")
        item = {"candidate": candidate["label"], "baseline": baseline["label"],
                "seed": key[0], "camp": key[1], "route": key[2], "unlock_rule": key[3],
                "unlock_turn": key[4], "branch": key[5], "cohort_class": key[6],
                "scenario": key[7], "shock": key[8], "initial_habitat": key[9],
                "candidate_unlock_save_sha256": c_hash,
                "baseline_unlock_save_sha256": b_hash,
                "candidate_config_hash": candidate_config,
                "baseline_config_hash": baseline_config,
                "candidate_config_file_hash": candidate_config_file,
                "baseline_config_file_hash": baseline_config_file,
                "candidate_resource_config_hash": candidate_resource_file,
                "baseline_resource_config_hash": baseline_resource_file,
                "configuration_hash_status": "same" if str(candidate_config).lower() == str(baseline_config).lower()
                    else "different-serialization-hash-same-config-files"}
        if left is None or right is None:
            item["pair_status"] = "candidate-only" if right is None else "baseline-only"
        elif not c_hash or not b_hash:
            item["pair_status"] = "matched-boundary-hash-unavailable"
        elif c_hash != b_hash:
            item["pair_status"] = "matched-boundary-different-unlock-save"
        else:
            item["pair_status"] = "exact-same-unlock-save" if item["configuration_hash_status"] == "same" else "exact-same-unlock-save-configschema-hash-diff"
            item["pairing_basis"] = "identical-checkpoint-bytes-and-matching-config/resource-file-hashes"
            def avg(window_rows, field):
                return mean(number(row.get(field)) for row in window_rows)
            c_qol, b_qol = avg(left, "qol"), avg(right, "qol")
            item["candidate_mean_qol"] = fmt(c_qol)
            item["baseline_mean_qol"] = fmt(b_qol)
            item["delta_mean_qol_candidate_minus_baseline"] = fmt(c_qol - b_qol) if c_qol is not None and b_qol is not None else ""
            for field in ("food_satisfaction", "water_satisfaction", "clothing_satisfaction", "housing_satisfaction"):
                cval, bval = avg(left, field), avg(right, field)
                item["candidate_mean_" + field] = fmt(cval)
                item["baseline_mean_" + field] = fmt(bval)
                item["delta_mean_" + field] = fmt(cval - bval) if cval is not None and bval is not None else ""
            c_goods, b_goods = Counter(), Counter()
            for row in left:
                c_goods.update(parse_pairs(row.get("realized_goods", "")))
            for row in right:
                b_goods.update(parse_pairs(row.get("realized_goods", "")))
            for good in GOOD_NAMES:
                item["candidate_output_" + good] = fmt(c_goods.get(good, 0))
                item["baseline_output_" + good] = fmt(b_goods.get(good, 0))
                item["delta_output_" + good] = fmt(c_goods.get(good, 0) - b_goods.get(good, 0))
        rows.append(item)
    return rows


def write_csv(path, rows):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    if not rows:
        with open(path, "w", newline="", encoding="utf-8") as stream:
            stream.write("\n")
        return
    fields = []
    for row in rows:
        for field in row:
            if field not in fields:
                fields.append(field)
    with open(path, "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow({key: fmt(value) if isinstance(value, float) else value
                             for key, value in row.items()})


def write_summary(path, runs, scenario_rows, coverage_rows, constraint_rows, paired_rows, replay_rows):
    lines = ["# Neolithic craft validation summary", "",
             "Detailed raw rows remain in each run directory. This reduction validates complete 25-turn windows and groups outcomes by declared cohort class, route, and branch.", "",
             "| Source | Revision | Config hash | Unlocks | Discovery censoring | Complete windows | Diagnostics |",
             "|---|---|---|---:|---:|---:|---|"]
    for run in runs:
        complete = sum(1 for row in scenario_rows if row["source"] == run["label"] for _ in range(row["complete_windows"]))
        diagnostics = run["manifest"].get("craftWorkDiagnostics",
                      run["manifest"].get("diagnosticsAvailability", run["manifest"].get("diagnostics", "see row status")))
        lines.append("| {} | {} | {} | {} | {} | {} | {} |".format(
            run["label"], run["manifest"].get("simulationRevision", ""),
            run["manifest"].get("configHash", ""), len(run["unlock"]), discovery_censoring_label(run), complete, diagnostics))
    lines.extend(["", "## Scenario outcomes", "",
                  "QoL and satisfaction values are arithmetic means across each 25-turn window, then equally weighted across windows in the compact scenario table. `conditioned-control` and other declared classes remain distinct from natural policy cohorts. Conditioned windows without an accepted-action witness are excluded from policy aggregates; inspect `window-outcomes.csv` for their validation status. Branches named `legal-move-auto-economy-mobile` (including `site-control-` variants) are forced legal-movement controls: the runner selects a deterministic legal Move if policy supplies none, then requests automatic economy. They are not normal-policy outcomes.", "",
                  "| Source | Cohort | Route | Branch | Windows | Mean QoL | Food | Water | Clothing | Housing | Useful-output turns |",
                  "|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|"])
    for row in scenario_rows:
        lines.append("| {source} | {cohort_class} | {route} | {branch} | {windows} | {mean_qol} | {mean_food_satisfaction} | {mean_water_satisfaction} | {mean_clothing_satisfaction} | {mean_housing_satisfaction} | {mean_useful_output_turns} |".format(
            **{key: fmt(value) if isinstance(value, float) else value for key, value in row.items()}))
    lines.extend(["", "## Route, habitat, and source coverage", "",
                  "Source counts are counts of locally known source IDs, including depleted sources; the unlock CSV `source_stone`/`source_clay`/`source_wood` fields are summed accessible stock at unlock. Positive-stock camp counts are separate, so a scarcity shock can retain known IDs while stock is zero.", "",
                  "| Source | Cohort | Route | Initial habitat | Unlock habitat | Unlocks | Stone known-ID camps | Stone positive-stock camps | Stone mean IDs | Stone mean stock | Clay known-ID camps | Clay positive-stock camps | Clay mean IDs | Clay mean stock | Wood known-ID camps | Wood positive-stock camps | Wood mean IDs | Wood mean stock |",
                  "|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"])
    for row in coverage_rows:
        if row.get("unlock_count", 0):
            lines.append("| {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |".format(
                row.get("source", ""), row.get("cohort_class", ""), row.get("route", ""),
                row.get("initial_habitat", ""), row.get("unlock_habitat", ""), row.get("unlock_count", 0),
                row.get("stone_known_source_id_camps", 0), row.get("stone_positive_accessible_stock_camps", 0),
                fmt(row.get("mean_stone_known_source_ids")), fmt(row.get("mean_stone_accessible_stock_at_unlock")),
                row.get("clay_known_source_id_camps", 0), row.get("clay_positive_accessible_stock_camps", 0),
                fmt(row.get("mean_clay_known_source_ids")), fmt(row.get("mean_clay_accessible_stock_at_unlock")),
                row.get("wood_known_source_id_camps", 0), row.get("wood_positive_accessible_stock_camps", 0),
                fmt(row.get("mean_wood_known_source_ids")), fmt(row.get("mean_wood_accessible_stock_at_unlock"))))
    unresolved = [row for row in constraint_rows if row.get("unresolved") == "yes"]
    lines.extend(["", "## Explicit unresolved constraints", ""])
    if unresolved:
        lines.extend(["| Source | Cohort | Route | Branch | Good | Constraint | Turn occurrences |",
                      "|---|---|---|---|---|---|---:|"])
        for row in unresolved:
            lines.append("| {} | {} | {} | {} | {} | {} | {} |".format(
                row.get("source", ""), row.get("cohort_class", ""), row.get("route", ""),
                row.get("branch", ""), row.get("good", ""), row.get("constraint", ""),
                row.get("turn_occurrences", 0)))
    else:
        lines.append("No explicitly unresolved constraints were recorded.")
    lines.extend(["", "## Replay evidence", "",
                  "A `PASS` checkpoint roundtrip only proves that the saved checkpoint loads. Only rows whose details record `turns=3; workers=4` count as the bounded exact continuation check; scarcity intervention rows only record the intervention and saved-state load.", "",
                  "| Source | Check | Status | Count |", "|---|---|---|---:|"])
    for row in replay_rows:
        lines.append("| {} | {} | {} | {} |".format(
            row.get("source", ""), row.get("check_type", ""), row.get("status", ""), row.get("count", 0)))
    lines.extend(["", "## Exact checkpoint comparisons", "",
                  "Paired deltas require identical `unlock_save_sha256` checkpoint bytes and matching `configFileHash` and `resourceConfigHash` manifest values. `SaveCodec.ConfigurationHash` is reported on each side but may differ across serialization/schema revisions; issue #28 added physical-year fields, so a config-hash difference alone does not invalidate an identical saved checkpoint under unchanged configuration/resource files. Other same-key boundaries are listed with an exclusion reason.", "",
                  "| Pair status | Branch | Route | Seed | Camp | Count |", "|---|---|---|---:|---:|---:|"])
    pair_counts = Counter((row.get("pair_status", "unknown"), row.get("branch", ""), row.get("route", "")) for row in paired_rows)
    for (status, branch, route), count in sorted(pair_counts.items()):
        lines.append("| {} | {} | {} | — | — | {} |".format(status, branch, route, count))
    lines.extend(["", "Coverage and per-window specialist/QoL/constraint tables are in the adjacent CSV files. Candidate censored rows are starts that did not unlock within the discovery cap. The baseline was run from candidate unlock-save checkpoints, so it did not rerun discovery and its empty `censored.csv` is not a zero-censor result.", ""])
    with open(path, "w", encoding="utf-8") as stream:
        stream.write("\n".join(lines))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", required=True, help="Candidate run directory")
    parser.add_argument("--baseline", help="Optional baseline run directory")
    parser.add_argument("--out", required=True, help="Summary output directory")
    args = parser.parse_args(argv)
    runs = [load_run("candidate", args.candidate)]
    if args.baseline:
        runs.append(load_run("baseline", args.baseline))
    all_scenarios, all_windows, all_specialists, all_constraints, all_coverage, all_replay = [], [], [], [], [], []
    parsed_runs = []
    for run in runs:
        windows = validate_windows(run)
        details, aggregate = scenario_summaries(run, windows)
        parsed_runs.append((run, windows))
        all_windows.extend(details)
        all_scenarios.extend(aggregate)
        all_specialists.extend(specialist_summaries(run))
        all_constraints.extend(constraint_summaries(run, windows))
        all_coverage.extend(coverage_summaries(run))
        all_replay.extend(replay_summaries(run))
    pairs = []
    if len(parsed_runs) == 2:
        pairs = paired_comparisons(parsed_runs[0][0], parsed_runs[1][0], parsed_runs[0][1], parsed_runs[1][1])
    os.makedirs(args.out, exist_ok=True)
    write_csv(os.path.join(args.out, "scenario-summary.csv"), all_scenarios)
    write_csv(os.path.join(args.out, "window-outcomes.csv"), all_windows)
    write_csv(os.path.join(args.out, "specialist-summary.csv"), all_specialists)
    write_csv(os.path.join(args.out, "unresolved-constraints.csv"), all_constraints)
    write_csv(os.path.join(args.out, "coverage-summary.csv"), all_coverage)
    write_csv(os.path.join(args.out, "replay-summary.csv"), all_replay)
    if len(parsed_runs) == 2:
        write_csv(os.path.join(args.out, "paired-comparison.csv"), pairs)
    write_summary(os.path.join(args.out, "summary.md"), runs, all_scenarios, all_coverage, all_constraints, pairs, all_replay)
    print("SUMMARY_COMPLETE sources={} scenarios={} windows={} paired_rows={} output={}".format(
        len(runs), len(all_scenarios), len(all_windows), len(pairs), os.path.abspath(args.out)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print("SUMMARY_FAILURE {}: {}".format(type(exc).__name__, exc), file=sys.stderr)
        sys.exit(1)
