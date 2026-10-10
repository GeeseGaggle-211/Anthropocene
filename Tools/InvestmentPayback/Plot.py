#!/usr/bin/env python3
"""Reduce and plot C# investment-payback outputs.

This script does not model production, ecology, demographics, or reward.  It
only summarizes the physical trajectories and exact discounted integrals that
Program.cs already emitted.
"""

from __future__ import annotations

import argparse
import json
from itertools import combinations
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


DOMAINS = ("field", "herd", "fishing")
HORIZONS = ("100", "200", "400", "undiscounted")
INTENSITIES = ("low", "medium", "high")
COLORS = {
    "100": "#d95f02",
    "200": "#7570b3",
    "400": "#1b9e77",
    "undiscounted": "#222222",
}
TOLERANCE = 1e-8


def save_figure(fig: plt.Figure, output: Path, stem: str) -> None:
    fig.savefig(output / f"{stem}.svg")
    fig.savefig(output / f"{stem}.png", dpi=180)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def b(series: pd.Series) -> pd.Series:
    if series.dtype == bool:
        return series
    return series.astype(str).str.lower().eq("true")


def ensure_inputs(root: Path) -> dict[str, pd.DataFrame]:
    names = {
        "states": "initial_states.csv",
        "trajectories": "trajectories.csv",
        "paired": "paired_trajectories.csv",
        "discounts": "discount_summary.csv",
    }
    missing = [name for name in names.values() if not (root / name).exists()]
    if missing:
        raise SystemExit("Missing sweep output: " + ", ".join(missing))
    return {key: pd.read_csv(root / value) for key, value in names.items()}


def discount_aggregate(d: pd.DataFrame) -> pd.DataFrame:
    data = d.copy()
    data["long_run_improves"] = b(data["long_run_improves"])
    data["correct_sign"] = b(data["correct_sign"])
    data["false_negative"] = data["sign_classification"].eq("false-negative")
    data["false_positive"] = data["sign_classification"].eq("false-positive")

    def one(group: pd.DataFrame) -> pd.Series:
        positive = group[group.long_run_improves]
        paid = positive[positive.durable_break_even_years >= 0]
        return pd.Series({
            "n": len(group),
            "long_run_positive": int(group.long_run_improves.sum()),
            "long_run_positive_share": group.long_run_improves.mean(),
            "correct_sign": int(group.correct_sign.sum()),
            "correct_sign_share": group.correct_sign.mean(),
            "false_negatives": int(group.false_negative.sum()),
            "false_positives": int(group.false_positive.sum()),
            "median_discounted_welfare": group.value.median(),
            "median_undiscounted_welfare": group.undiscounted_value.median(),
            "positive_paid_back": len(paid),
            "positive_censored": len(positive) - len(paid),
            "median_durable_break_even_years": (
                paid.durable_break_even_years.median() if len(paid) else np.nan
            ),
            "median_durable_positive_years": (
                positive[positive.durable_positive_years >= 0]
                .durable_positive_years.median() if len(positive) else np.nan
            ),
            "median_worst_drawdown": group.minimum_cumulative_value.median(),
        })

    result = (
        data.groupby(["horizon", "domain", "intensity"], sort=False)
        .apply(one, include_groups=False)
        .reset_index()
    )
    result["horizon"] = pd.Categorical(
        result.horizon, HORIZONS, ordered=True
    )
    return result.sort_values(["horizon", "domain", "intensity"])


def ranking_analysis(d: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    u = d[d.horizon.eq("undiscounted")][
        ["state_id", "run_id", "domain", "intensity", "value"]
    ].rename(columns={"value": "undiscounted"})
    cases: list[dict] = []
    inversions: list[dict] = []
    for horizon in ("100", "200", "400"):
        h = d[d.horizon.eq(horizon)][
            ["state_id", "run_id", "value"]
        ].rename(columns={"value": "discounted"})
        merged = u.merge(h, on=["state_id", "run_id"], validate="one_to_one")
        for state_id, state in merged.groupby("state_id", sort=True):
            positive = state[state.undiscounted > TOLERANCE].copy()
            if positive.empty:
                cases.append({
                    "state_id": state_id,
                    "horizon": horizon,
                    "positive_investments": 0,
                    "long_run_best_run": "",
                    "discounted_best_run": "",
                    "top_choice_match": np.nan,
                    "comparable_positive_pairs": 0,
                    "concordant_pairs": 0,
                    "discordant_pairs": 0,
                    "discount_ties": 0,
                    "pairwise_agreement": np.nan,
                })
                continue
            best_u = positive.sort_values(
                ["undiscounted", "run_id"], ascending=[False, True]
            ).iloc[0]
            best_h = positive.sort_values(
                ["discounted", "run_id"], ascending=[False, True]
            ).iloc[0]
            concordant = discordant = ties = comparable = 0
            for (_, left), (_, right) in combinations(positive.iterrows(), 2):
                du = left.undiscounted - right.undiscounted
                if abs(du) <= TOLERANCE:
                    continue
                comparable += 1
                dh = left.discounted - right.discounted
                if abs(dh) <= TOLERANCE:
                    ties += 1
                elif np.sign(du) == np.sign(dh):
                    concordant += 1
                else:
                    discordant += 1
                    inversions.append({
                        "state_id": state_id,
                        "horizon": horizon,
                        "preferred_long_run": (
                            left.run_id if du > 0 else right.run_id
                        ),
                        "preferred_discounted": (
                            left.run_id if dh > 0 else right.run_id
                        ),
                        "left_run": left.run_id,
                        "right_run": right.run_id,
                        "left_undiscounted": left.undiscounted,
                        "right_undiscounted": right.undiscounted,
                        "left_discounted": left.discounted,
                        "right_discounted": right.discounted,
                    })
            cases.append({
                "state_id": state_id,
                "horizon": horizon,
                "positive_investments": len(positive),
                "long_run_best_run": best_u.run_id,
                "long_run_best_domain": best_u.domain,
                "long_run_best_intensity": best_u.intensity,
                "discounted_best_run": best_h.run_id,
                "discounted_best_domain": best_h.domain,
                "discounted_best_intensity": best_h.intensity,
                "top_choice_match": best_u.run_id == best_h.run_id,
                "comparable_positive_pairs": comparable,
                "concordant_pairs": concordant,
                "discordant_pairs": discordant,
                "discount_ties": ties,
                "pairwise_agreement": (
                    concordant / comparable if comparable else np.nan
                ),
            })
    return pd.DataFrame(cases), pd.DataFrame(inversions)


def ranking_summary(cases: pd.DataFrame) -> pd.DataFrame:
    rows = []
    for horizon, group in cases.groupby("horizon", sort=False):
        eligible = group[group.positive_investments > 0]
        comparable = int(group.comparable_positive_pairs.sum())
        rows.append({
            "horizon": horizon,
            "states": len(group),
            "states_with_positive_investment": len(eligible),
            "top_choice_matches": int(eligible.top_choice_match.eq(True).sum()),
            "top_choice_match_share": (
                eligible.top_choice_match.mean() if len(eligible) else np.nan
            ),
            "comparable_positive_pairs": comparable,
            "concordant_pairs": int(group.concordant_pairs.sum()),
            "discordant_pairs": int(group.discordant_pairs.sum()),
            "discount_ties": int(group.discount_ties.sum()),
            "pairwise_agreement": (
                group.concordant_pairs.sum() / comparable
                if comparable else np.nan
            ),
        })
    return pd.DataFrame(rows)


def physical_summary(t: pd.DataFrame, p: pd.DataFrame) -> pd.DataFrame:
    treatment = t[(t.arm == "treatment") & (t.turn == 1)].copy()
    first = p[p.turn == 1][[
        "run_id", "delta_field_build_worker_years",
        "delta_fishing_build_worker_years", "delta_field_q_mean",
        "delta_fishing_q_mean", "delta_herd_animals",
        "delta_food_produced", "delta_gatherers",
    ]]
    treatment = treatment.merge(first, on="run_id", validate="one_to_one")
    final = p.sort_values("turn").groupby("run_id", as_index=False).tail(1)[[
        "run_id", "cumulative_undiscounted", "delta_herd_animals",
        "delta_field_q_mean", "delta_fishing_q_mean",
        "delta_food_reserves", "delta_fissions", "delta_daughter_population",
    ]].rename(columns={column: "final_" + column for column in [
        "cumulative_undiscounted", "delta_herd_animals",
        "delta_field_q_mean", "delta_fishing_q_mean",
        "delta_food_reserves", "delta_fissions", "delta_daughter_population",
    ]})
    treatment = treatment.merge(final, on="run_id", validate="one_to_one")
    columns = [
        "pulse_requested_people", "pulse_accepted_people",
        "delta_gatherers", "delta_field_build_worker_years",
        "delta_fishing_build_worker_years", "delta_field_q_mean",
        "delta_fishing_q_mean", "delta_herd_animals",
        "newborn_animals", "herd_starvation_deaths", "delta_food_produced",
        "final_cumulative_undiscounted", "final_delta_field_q_mean",
        "final_delta_fishing_q_mean", "final_delta_herd_animals",
        "final_delta_food_reserves", "final_delta_fissions",
        "final_delta_daughter_population",
    ]
    return (
        treatment.groupby(["domain", "intensity"], sort=False)[columns]
        .agg(["median", "min", "max"])
        .reset_index()
    )


def sensitivity_summary(d: pd.DataFrame) -> pd.DataFrame:
    data = d[d.horizon.isin(["100", "200", "400"])].copy()
    data["long_run_improves"] = b(data.long_run_improves)
    data["correct_sign"] = b(data.correct_sign)
    rows: list[dict] = []
    factors = [
        "seed", "capital_level", "population_level", "support_level",
        "reserve_level", "intensity",
    ]
    for factor in factors:
        for keys, group in data.groupby(["horizon", "domain", factor], sort=False):
            horizon, domain, level = keys
            rows.append({
                "factor": factor,
                "level": level,
                "horizon": horizon,
                "domain": domain,
                "n": len(group),
                "long_run_positive_share": group.long_run_improves.mean(),
                "correct_sign_share": group.correct_sign.mean(),
                "false_negatives": group.sign_classification.eq(
                    "false-negative"
                ).sum(),
                "false_positives": group.sign_classification.eq(
                    "false-positive"
                ).sum(),
                "median_undiscounted_welfare": group.undiscounted_value.median(),
                "median_discounted_welfare": group.value.median(),
            })
    return pd.DataFrame(rows)


def endpoint_sensitivity(d: pd.DataFrame) -> pd.DataFrame:
    data = d[d.horizon.eq("undiscounted")].copy()
    data["positive_400"] = data.undiscounted_400_value > TOLERANCE
    data["positive_800"] = data.undiscounted_value > TOLERANCE
    data["same_sign"] = data.positive_400.eq(data.positive_800)
    return (
        data.groupby(["domain", "intensity"], sort=False)
        .agg(
            n=("run_id", "size"),
            positive_400=("positive_400", "sum"),
            positive_800=("positive_800", "sum"),
            same_sign=("same_sign", "sum"),
            same_sign_share=("same_sign", "mean"),
            median_value_400=("undiscounted_400_value", "median"),
            median_value_800=("undiscounted_value", "median"),
        )
        .reset_index()
    )


def plot_cumulative(p: pd.DataFrame, output: Path) -> None:
    columns = {
        "100": "cumulative_100", "200": "cumulative_200",
        "400": "cumulative_400", "undiscounted": "cumulative_undiscounted",
    }
    fig, axes = plt.subplots(1, 3, figsize=(12, 3.7), sharex=True)
    for ax, domain in zip(axes, DOMAINS):
        data = p[p.domain.eq(domain)]
        for horizon in HORIZONS:
            by_year = data.groupby("end_year")[columns[horizon]]
            median = by_year.median()
            low = by_year.quantile(.25)
            high = by_year.quantile(.75)
            ax.plot(median.index, median.values, color=COLORS[horizon],
                    label=horizon, linewidth=1.8)
            ax.fill_between(median.index, low.values, high.values,
                            color=COLORS[horizon], alpha=.10)
        ax.axhline(0, color="#888888", linewidth=.7)
        ax.set_title(domain.capitalize())
        ax.set_xlabel("Years after pulse")
        ax.grid(alpha=.18)
    axes[0].set_ylabel("Median cumulative incremental\nperson-QoL-years (IQR)")
    axes[-1].legend(title="e-fold years", frameon=False, fontsize=8)
    fig.tight_layout()
    save_figure(fig, output, "cumulative-welfare")
    plt.close(fig)


def plot_classification(aggregate: pd.DataFrame,
                        ranks: pd.DataFrame, output: Path) -> None:
    # Re-aggregate over intensity from counts so this graphic is not weighted
    # by a ratio-of-ratios.
    data = aggregate.groupby(["horizon", "domain"], observed=True).agg(
        positives=("long_run_positive", "sum"),
        false_negatives=("false_negatives", "sum"),
        n=("n", "sum"), correct=("correct_sign", "sum"),
    ).reset_index()
    data["positive_recall"] = np.where(
        data.positives > 0,
        1 - data.false_negatives / data.positives,
        np.nan,
    )
    fig, axes = plt.subplots(1, 2, figsize=(9, 3.7))
    horizons = ["100", "200", "400"]
    x = np.arange(len(horizons))
    width = .23
    for index, domain in enumerate(DOMAINS):
        values = [
            data[(data.horizon.astype(str) == horizon) &
                 data.domain.eq(domain)].positive_recall.iloc[0]
            for horizon in horizons
        ]
        axes[0].bar(x + (index - 1) * width, values, width,
                    label=domain.capitalize())
    axes[0].set_xticks(x, horizons)
    axes[0].set_ylim(0, 1.05)
    axes[0].set_ylabel("Recall of 800-year-positive investments")
    axes[0].set_xlabel("Discount e-folding horizon (years)")
    axes[0].legend(frameon=False, fontsize=8)
    axes[0].grid(axis="y", alpha=.18)

    axes[1].bar(ranks.horizon.astype(str), ranks.pairwise_agreement,
                color=[COLORS[str(value)] for value in ranks.horizon])
    axes[1].set_ylim(0, 1.05)
    axes[1].set_ylabel("Pairwise agreement among positive investments")
    axes[1].set_xlabel("Discount e-folding horizon (years)")
    axes[1].grid(axis="y", alpha=.18)
    fig.tight_layout()
    save_figure(fig, output, "discount-ranking")
    plt.close(fig)


def plot_break_even(d: pd.DataFrame, output: Path) -> None:
    data = d[(d.horizon.eq("undiscounted")) & b(d.long_run_improves)].copy()
    fig, axes = plt.subplots(1, 3, figsize=(11, 3.7), sharey=True)
    for ax, domain in zip(axes, DOMAINS):
        subset = data[data.domain.eq(domain)]
        positions = np.arange(len(INTENSITIES))
        for pos, intensity in zip(positions, INTENSITIES):
            values = subset[(subset.intensity.eq(intensity)) &
                            (subset.durable_break_even_years >= 0)]\
                .durable_break_even_years.to_numpy()
            if len(values):
                ax.boxplot(values, positions=[pos], widths=.55,
                           showfliers=True, patch_artist=True,
                           boxprops={"facecolor": "#8da0cb", "alpha": .65},
                           medianprops={"color": "#222222"})
            censored = subset[(subset.intensity.eq(intensity)) &
                              (subset.durable_break_even_years < 0)].shape[0]
            total = subset[subset.intensity.eq(intensity)].shape[0]
            ax.text(pos, .98, f"{censored}/{total} cens.", ha="center",
                    va="top", transform=ax.get_xaxis_transform(), fontsize=7)
        ax.set_title(domain.capitalize())
        ax.set_xticks(positions, INTENSITIES)
        ax.set_xlabel("Pulse intensity")
        ax.grid(axis="y", alpha=.18)
    axes[0].set_ylabel("Durable break-even (years)\n800-year continuation")
    fig.tight_layout()
    save_figure(fig, output, "durable-break-even")
    plt.close(fig)


def plot_physical(p: pd.DataFrame, output: Path) -> None:
    metrics = {
        "field": ("delta_field_q_mean", "Δ mean field q"),
        "herd": ("delta_herd_animals", "Δ herd animals"),
        "fishing": ("delta_fishing_q_mean", "Δ mean fishing q"),
    }
    fig, axes = plt.subplots(2, 3, figsize=(12, 6), sharex="col")
    for column, domain in enumerate(DOMAINS):
        data = p[p.domain.eq(domain)]
        metric, label = metrics[domain]
        for intensity, style in zip(INTENSITIES, (":", "--", "-")):
            item = data[data.intensity.eq(intensity)]
            physical = item.groupby("end_year")[metric].median()
            production = item.groupby("end_year").delta_food_produced.median()
            axes[0, column].plot(physical.index, physical.values,
                                 linestyle=style, label=intensity)
            axes[1, column].plot(production.index, production.values,
                                 linestyle=style, label=intensity)
        axes[0, column].axhline(0, color="#888888", linewidth=.7)
        axes[1, column].axhline(0, color="#888888", linewidth=.7)
        axes[0, column].set_title(domain.capitalize())
        axes[0, column].set_ylabel(label)
        axes[1, column].set_ylabel("Median Δ food / turn")
        axes[1, column].set_xlabel("Years after pulse")
        axes[0, column].grid(alpha=.18)
        axes[1, column].grid(alpha=.18)
    axes[0, -1].legend(frameon=False, fontsize=8, title="pulse")
    fig.tight_layout()
    save_figure(fig, output, "physical-response")
    plt.close(fig)


def main() -> None:
    args = parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    tables = ensure_inputs(args.input)
    discounts = tables["discounts"]
    paired = tables["paired"]
    trajectories = tables["trajectories"]

    aggregate = discount_aggregate(discounts)
    cases, inversions = ranking_analysis(discounts)
    ranks = ranking_summary(cases)
    physical = physical_summary(trajectories, paired)
    sensitivity = sensitivity_summary(discounts)
    endpoints = endpoint_sensitivity(discounts)

    aggregate.to_csv(args.output / "discount-aggregate.csv", index=False)
    cases.to_csv(args.output / "ranking-cases.csv", index=False)
    inversions.to_csv(args.output / "ranking-inversions.csv", index=False)
    ranks.to_csv(args.output / "ranking-summary.csv", index=False)
    physical.to_csv(args.output / "physical-summary.csv", index=False)
    sensitivity.to_csv(args.output / "sensitivity-summary.csv", index=False)
    endpoints.to_csv(args.output / "endpoint-sensitivity.csv", index=False)

    plot_cumulative(paired, args.output)
    plot_classification(aggregate, ranks, args.output)
    plot_break_even(discounts, args.output)
    plot_physical(paired, args.output)

    summary = {
        "states": int(tables["states"].shape[0]),
        "trajectories": int(trajectories.run_id.nunique()),
        "turn_rows": int(trajectories.shape[0]),
        "positive_800_year_investments": int(
            b(discounts[discounts.horizon.eq("undiscounted")]
              .long_run_improves).sum()
        ),
        "ranking": ranks.to_dict(orient="records"),
        "tolerance_person_qol_years": TOLERANCE,
        "note": "All simulator physics and discounted welfare integrals are read from C# output; this reducer performs grouping, rank comparison, and plotting only.",
    }
    (args.output / "summary.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
