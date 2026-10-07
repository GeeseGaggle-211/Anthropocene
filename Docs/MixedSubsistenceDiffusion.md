# Mixed subsistence diffusion

This work extends the Elder-RL substrate so Agriculture, Husbandry and resident Fishing can coexist and spread without assigning a technology-defined economic identity.

## Contract

New worlds enable resident Neolithic Fishing, corrected aquatic seasonal pressure, bilateral trade technology diffusion, Husbandry proximity exposure, and physical taming. Older saves preserve their historical feature state.

Technology knowledge and productive proficiency are separate. Activity continues to accrue Hunting, Gathering and Fishing practice. Trade and proximity accrue Society.TechnologyExposure, which contributes to secondary discovery without improving hunting, gathering or fishing performance. Social exposure is post-breakthrough only.

A settled barter is bilateral contact. Each side can learn opening technologies held by the counterparty and not already held locally, scaled by realized delivered value. The mechanism is commodity-neutral.

Nearby domesticable animals generate Husbandry exposure at the end boundary. Wild animals provide ordinary exposure; foreign managed animals are a stronger demonstration; own managed herds do not reteach their owner. Species familiarity is recorded separately.

Learning Husbandry never creates livestock. Taming requires Husbandry knowledge, species familiarity and a local hunter interaction with a wild herd. It transfers actual animals above the wild breeding reserve into a managed herd, capped initially by InitialDomesticAnimals. The transfer commits after current-turn production, so captured animals cannot produce or breed retroactively.

The resident Fishing implementation already existed behind gates. This integration enables it for new production worlds while retaining DemandControl and corrected aquatic-pressure prerequisites.

## RL boundary

The Elder sees capabilities and physical holdings separately. Knowing Husbandry does not imply owning animals; knowing Watercraft does not imply having a productive fishery. Trade and proximity change capabilities without selecting the production portfolio.
