# EPG Channel Matching Safety Design

## Goal

Prevent XMLTV programmes from being attached to a different logical channel while preserving EPG fan-out to genuine duplicate streams (for example HD/FHD/backup entries sharing the same exact identity).

## Resolution order

Each XMLTV `<channel>` is resolved once, after all of its display names have been read:

1. Exact XMLTV channel ID / playlist `tvg-id` match. This result is authoritative and display-name candidates are ignored.
2. Country-compatible exact normalized display-name match.
3. Country-compatible fuzzy match only when one candidate set has high confidence and is clearly ahead of the runner-up.
4. No match when display names disagree or confidence is ambiguous.

Results from different evidence levels are never unioned. A selected candidate set may still contain multiple internal channel IDs when those IDs share the same exact playlist identity.

## Country isolation

Playlist channel metadata supplies the country hint in this order: explicit `Channel.Country` / `tvg-country`, an explicit country token in `Name` or `TvgName`, then an explicit group-title country. XMLTV display names prefer their own explicit country token and use explicit source-country metadata second. Broad name patterns such as `Haber`, `ATV`, `TLC`, or `DMAX` are not hard identity evidence. Unknown countries remain neutral rather than defaulting to the United States. Fuzzy comparison rejects conflicting explicit countries; an unknown side remains eligible but cannot resolve a tie between otherwise identical candidates from different countries.

## Fuzzy safety

Fuzzy matching scans every candidate, groups duplicate internal-ID sets, and removes the insertion-order early exit. A result is accepted only above a conservative confidence threshold and with a minimum lead over the second distinct candidate set. Meaningful broadcaster differences such as TRT/TGRT must not match.

## Identity-preserving normalization

The base channel key removes unambiguous delivery and quality markers such as resolution, codec, frame-rate, mobile/web transport, and backup labels. Brand-capable words are not technical noise: `Plus`, `Extra`, `Max`, and `Sat` remain in the key so channels such as HBO/HBO Max, S Sports 1/S Sports 1 Plus, and SAT.1 cannot collapse into one identity. Country tokens may be removed from the base key because explicit country evidence is carried separately by the country-qualified key. Provider-tier labels (`Live`, `VIP`, and `Premium`) remain technical noise in this focused change to preserve current provider compatibility.

Broad country-name patterns remain available only as general playlist heuristics. EPG identity never uses substring patterns such as `Haber`, `ATV`, `TLC`, or `DMAX` as hard country evidence; only explicit metadata and explicit country tokens can constrain an EPG match.

## Import behavior

The importer buffers display-name variants for an XMLTV channel, resolves a single target set, and assigns that set once. Exact `tvg-id` conflicts do not fall back to names. Existing EPG rows must be cleared and re-imported after deployment so previously duplicated programmes disappear.

## Verification

Focused tests cover TRT/TGRT rejection, ambiguous fuzzy rejection, exact `tvg-id` precedence over conflicting display names, country hints for generic channel brands, neutral unknown countries, preservation of exact duplicate-stream fan-out, and distinct normalized identities for `Plus`, `Extra`, `Max`, and `SAT.1` channels. Existing EPG matching and project tests must remain green.
