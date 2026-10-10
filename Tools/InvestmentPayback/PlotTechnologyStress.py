#!/usr/bin/env python3
"""Reduce and plot C# technology-stress outputs.

All production, welfare integration, discounting, payback, regret, and ranking
choices are emitted by TechnologyStress.cs. This script only groups those
measurements and plots physical trajectories.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


def args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def boolean(series: pd.Series) -> pd.Series:
    if series.dtype == bool:
        return series
    return series.astype(str).str.lower().eq("true")


def summarize_groups(
    data: pd.DataFrame, keys: list[str], reducer
) -> pd.DataFrame:
    rows: list[dict] = []
    for values, group in data.groupby(keys, sort=True):
        if not isinstance(values, tuple):
            values = (values,)
        row = dict(zip(keys, values))
        row.update(reducer(group).to_dict())
        rows.append(row)
    return pd.DataFrame(rows)


def save(fig: plt.Figure, output: Path, name: str) -> None:
    fig.tight_layout()
    fig.savefig(output / f"{name}.svg")
    fig.savefig(output / f"{name}.png", dpi=180)
    plt.close(fig)


def timing_summary(timing: pd.DataFrame) -> pd.DataFrame:
    data = timing.copy()
    for column in (
        "h400_inversion",
        "h800_inversion",
        "h800_strictly_lower_regret",
    ):
        data[column] = boolean(data[column])

    def one(group: pd.DataFrame) -> pd.Series:
        return pd.Series(
            {
                "cases": len(group),
                "long_run_invest_now": group.long_run_choice.eq(
                    "invest-now"
                ).sum(),
                "long_run_invest_after": group.long_run_choice.eq(
                    "invest-after"
                ).sum(),
                "long_run_no_development": group.long_run_choice.eq(
                    "no-development"
                ).sum(),
                "h400_inversions": group.h400_inversion.sum(),
                "h800_inversions": group.h800_inversion.sum(),
                "h400_median_long_run_regret": group.h400_long_run_regret.median(),
                "h800_median_long_run_regret": group.h800_long_run_regret.median(),
                "h400_mean_long_run_regret": group.h400_long_run_regret.mean(),
                "h800_mean_long_run_regret": group.h800_long_run_regret.mean(),
                "h800_strictly_lower_regret_cases": (
                    group.h800_strictly_lower_regret.sum()
                ),
            }
        )

    return summarize_groups(
        data,
        ["scope", "target_q_k", "unlock_year"],
        one,
    )


def payoff_summary(payoffs: pd.DataFrame) -> pd.DataFrame:
    data = payoffs.copy()
    data["long_run_improves"] = boolean(data.long_run_improves)
    data["correct_sign"] = boolean(data.correct_sign)

    def one(group: pd.DataFrame) -> pd.Series:
        paid = group[
            group.long_run_improves & (group.durable_break_even_years >= 0)
        ]
        return pd.Series(
            {
                "cases": len(group),
                "positive_1600y": group.long_run_improves.sum(),
                "correct_sign": group.correct_sign.sum(),
                "median_value": group.value.median(),
                "median_undiscounted_value": group.undiscounted_value.median(),
                "paid_back_cases": len(paid),
                "median_durable_break_even_years": (
                    paid.durable_break_even_years.median()
                    if len(paid)
                    else np.nan
                ),
                "median_durable_positive_years": (
                    paid.durable_positive_years.median()
                    if len(paid)
                    else np.nan
                ),
                "median_field_build_worker_years": (
                    group.incremental_field_build_worker_years.median()
                ),
                "median_field_maintenance_worker_years": (
                    group.incremental_field_maintenance_worker_years.median()
                ),
                "median_fishing_build_worker_years": (
                    group.incremental_fishing_build_worker_years.median()
                ),
                "median_fishing_maintenance_worker_years": (
                    group.incremental_fishing_maintenance_worker_years.median()
                ),
                "median_final_delta_field_q": group.final_delta_field_q.median(),
                "median_final_delta_fishing_q": (
                    group.final_delta_fishing_q.median()
                ),
                "worst_land_condition_delta": (
                    group.minimum_delta_land_condition.min()
                ),
                "worst_aquatic_condition_delta": (
                    group.minimum_delta_aquatic_condition.min()
                ),
            }
        )

    return summarize_groups(
        data,
        [
            "mode",
            "scope",
            "target_q_k",
            "unlock_year",
            "arm",
            "horizon",
        ],
        one,
    )


def select_severe_shared(trajectories: pd.DataFrame) -> pd.DataFrame:
    scheduled = trajectories[
        trajectories["mode"].eq("scheduled")
        & trajectories["scope"].eq("shared")
        & trajectories["target_q_k"].eq(0.8)
        & ~trajectories["arm"].eq("invest-after-unannounced")
    ]
    if scheduled.empty:
        return scheduled
    unlock = scheduled.unlock_year.max()
    return scheduled[scheduled.unlock_year.eq(unlock)]


def plot_regret(summary: pd.DataFrame, output: Path) -> None:
    ordered = summary.sort_values(["unlock_year", "target_q_k", "scope"])
    labels = [
        f"y{int(row.unlock_year)} q{row.target_q_k:g}\n{row.scope}"
        for row in ordered.itertuples()
    ]
    x = np.arange(len(ordered))
    fig, ax = plt.subplots(figsize=(max(9, len(ordered) * 0.8), 5))
    ax.bar(
        x - 0.19,
        ordered.h400_mean_long_run_regret,
        width=0.38,
        label="H=400",
        color="#d95f02",
    )
    ax.bar(
        x + 0.19,
        ordered.h800_mean_long_run_regret,
        width=0.38,
        label="H=800",
        color="#1b9e77",
    )
    ax.set_xticks(x, labels, rotation=30, ha="right")
    ax.set_ylabel("Mean 1,600-year welfare regret")
    ax.set_title("Discount-selected timing regret")
    ax.legend()
    ax.axhline(0, color="black", linewidth=0.8)
    save(fig, output, "discount_timing_regret")


def plot_physical_paths(data: pd.DataFrame, output: Path) -> None:
    if data.empty:
        return
    labels = {
        "no-development": "No Development",
        "invest-now": "Invest now",
        "invest-after-known": "Invest after unlock",
    }
    colors = {
        "no-development": "#444444",
        "invest-now": "#7570b3",
        "invest-after-known": "#1b9e77",
    }
    grouped = (
        data.groupby(["arm", "end_year"], sort=True)
        .agg(
            field_q=("field_q_mean", "median"),
            fishing_q=("fishing_q_mean", "median"),
            field_maintenance=("field_maintenance_worker_years", "median"),
            fishing_maintenance=("fishing_maintenance_worker_years", "median"),
            land_condition=("land_condition", "median"),
            aquatic_condition=("aquatic_condition", "median"),
            land_condition_min=("land_condition", "min"),
            aquatic_condition_min=("aquatic_condition", "min"),
        )
        .reset_index()
    )
    unlock = int(data.unlock_year.iloc[0])

    fig, axes = plt.subplots(1, 2, figsize=(11, 4.5), sharex=True)
    for arm, arm_data in grouped.groupby("arm"):
        axes[0].plot(
            arm_data.end_year,
            arm_data.field_q,
            label=labels.get(arm, arm),
            color=colors.get(arm),
        )
        axes[1].plot(
            arm_data.end_year,
            arm_data.fishing_q,
            label=labels.get(arm, arm),
            color=colors.get(arm),
        )
    for ax, title in zip(axes, ("Field q", "Fishery q")):
        ax.axvline(unlock, color="#cc0000", linestyle="--", linewidth=1)
        ax.set_title(title)
        ax.set_xlabel("Year")
        ax.set_ylabel("Median physical intensity")
    axes[1].legend()
    fig.suptitle(f"Severe shared q_k unlock at year {unlock}")
    save(fig, output, "severe_shared_capital_paths")

    fig, axes = plt.subplots(1, 2, figsize=(11, 4.5), sharex=True)
    for arm, arm_data in grouped.groupby("arm"):
        axes[0].plot(
            arm_data.end_year,
            arm_data.field_maintenance,
            label=labels.get(arm, arm),
            color=colors.get(arm),
        )
        axes[1].plot(
            arm_data.end_year,
            arm_data.fishing_maintenance,
            label=labels.get(arm, arm),
            color=colors.get(arm),
        )
    for ax, title in zip(
        axes, ("Field maintenance", "Fishery maintenance")
    ):
        ax.axvline(unlock, color="#cc0000", linestyle="--", linewidth=1)
        ax.set_title(title)
        ax.set_xlabel("Year")
        ax.set_ylabel("Median worker-years per turn")
    axes[1].legend()
    fig.suptitle(f"Maintenance response to severe unlock at year {unlock}")
    save(fig, output, "severe_shared_maintenance_paths")

    fig, axes = plt.subplots(1, 2, figsize=(11, 4.5), sharex=True)
    for arm, arm_data in grouped.groupby("arm"):
        axes[0].plot(
            arm_data.end_year,
            arm_data.land_condition,
            label=labels.get(arm, arm),
            color=colors.get(arm),
        )
        axes[1].plot(
            arm_data.end_year,
            arm_data.aquatic_condition,
            label=labels.get(arm, arm),
            color=colors.get(arm),
        )
        axes[0].plot(
            arm_data.end_year,
            arm_data.land_condition_min,
            color=colors.get(arm),
            linestyle=":",
            linewidth=1,
            alpha=0.8,
        )
        axes[1].plot(
            arm_data.end_year,
            arm_data.aquatic_condition_min,
            color=colors.get(arm),
            linestyle=":",
            linewidth=1,
            alpha=0.8,
        )
    for ax, title in zip(axes, ("Land condition", "Aquatic condition")):
        ax.axvline(unlock, color="#cc0000", linestyle="--", linewidth=1)
        ax.set_title(title)
        ax.set_xlabel("Year")
        ax.set_ylabel("Condition (solid median; dotted minimum)")
        ax.set_ylim(0.985, 1.002)
    axes[1].legend()
    fig.suptitle(f"Ecological sustainability at severe unlock year {unlock}")
    save(fig, output, "severe_shared_ecology_paths")


def plot_payback(payoffs: pd.DataFrame, output: Path) -> None:
    data = payoffs[
        payoffs.horizon.eq("undiscounted")
        & payoffs["mode"].eq("scheduled")
        & payoffs.arm.isin(["invest-now", "invest-after-known"])
        & boolean(payoffs.long_run_improves)
        & (payoffs.durable_break_even_years >= 0)
    ].copy()
    if data.empty:
        return
    fig, ax = plt.subplots(figsize=(8, 5))
    markers = {"invest-now": "o", "invest-after-known": "^"}
    for (arm, scope), group in data.groupby(["arm", "scope"]):
        ax.scatter(
            group.unlock_year + (group.target_q_k - 0.65) * 40,
            group.durable_break_even_years,
            alpha=0.65,
            marker=markers[arm],
            label=f"{arm} / {scope}",
        )
    ax.set_xlabel("Unlock year (q_k severity slightly offset)")
    ax.set_ylabel("Durable undiscounted payback duration (years)")
    ax.set_title("Payback duration among trajectories that paid back")
    ax.legend(fontsize=8, ncol=2)
    save(fig, output, "payback_duration")


def main() -> None:
    parsed = args()
    parsed.output.mkdir(parents=True, exist_ok=True)
    required = {
        "trajectories": parsed.input / "technology_trajectories.csv",
        "payoffs": parsed.input / "technology_payoffs.csv",
        "timing": parsed.input / "technology_timing.csv",
    }
    missing = [path.name for path in required.values() if not path.exists()]
    if missing:
        raise SystemExit("Missing technology-stress output: " + ", ".join(missing))
    trajectories = pd.read_csv(required["trajectories"])
    payoffs = pd.read_csv(required["payoffs"])
    timing = pd.read_csv(required["timing"])

    timing_aggregate = timing_summary(timing)
    payoff_aggregate = payoff_summary(payoffs)
    timing_aggregate.to_csv(parsed.output / "timing_aggregate.csv", index=False)
    payoff_aggregate.to_csv(parsed.output / "payoff_aggregate.csv", index=False)
    plot_regret(timing_aggregate, parsed.output)
    plot_physical_paths(select_severe_shared(trajectories), parsed.output)
    plot_payback(payoffs, parsed.output)


if __name__ == "__main__":
    main()
