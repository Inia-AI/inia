"""Generates unit_conversions.json beside units_dictionary.json from CLDR's
supplemental conversion data (convertUnit + unitConstants), pinned to CLDR_TAG.

Run:  python3 generate_unit_conversions.py [path-to-units.xml]
Without an argument the pinned tag is downloaded from GitHub. Output is
deterministic for a given tag. Factors and offsets are exact integer rationals
(numerator/denominator strings); consumers must cross-multiply, never divide,
so zero-tolerance comparisons (12 months vs 1 year) stay exact. Supplemental
subtype ids mirror SupplementalLexicon.cs.
"""

import json
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET
from collections import Counter
from decimal import Decimal
from fractions import Fraction
from functools import cache
from pathlib import Path

CLDR_TAG = "release-48-2"
CLDR_URL = f"https://raw.githubusercontent.com/unicode-org/cldr/{CLDR_TAG}/common/supplemental/units.xml"

SI_PREFIXES = {
    "quetta": Fraction(10**30), "ronna": Fraction(10**27), "yotta": Fraction(10**24),
    "zetta": Fraction(10**21), "exa": Fraction(10**18), "peta": Fraction(10**15),
    "tera": Fraction(10**12), "giga": Fraction(10**9), "mega": Fraction(10**6),
    "kilo": Fraction(10**3), "hecto": Fraction(10**2), "deka": Fraction(10),
    "deca": Fraction(10), "deci": Fraction(1, 10), "centi": Fraction(1, 10**2),
    "milli": Fraction(1, 10**3), "micro": Fraction(1, 10**6), "nano": Fraction(1, 10**9),
    "pico": Fraction(1, 10**12), "femto": Fraction(1, 10**15), "atto": Fraction(1, 10**18),
    "zepto": Fraction(1, 10**21), "yocto": Fraction(1, 10**24), "ronto": Fraction(1, 10**27),
    "quecto": Fraction(1, 10**30),
    "kibi": Fraction(2**10), "mebi": Fraction(2**20), "gibi": Fraction(2**30),
    "tebi": Fraction(2**40), "pebi": Fraction(2**50), "exbi": Fraction(2**60),
    "zebi": Fraction(2**70), "yobi": Fraction(2**80),
}

EXPRESSION_OVERRIDES = {
    "concentr-permillion": "part-per-1000000",
    "concentr-portion-per-1e9": "part-per-1000000000",
    "concentr-basis-point": "permyriad",
}

SEMANTICALLY_DISTINCT = {
    "speed-beaufort": "CLDR marks its convertUnit special=: the Beaufort scale is non-linear, no linear factor exists",
    "concentr-percentage-point": "a change in a rate is a different claim than a level; never converts to percent",
    "concentr-karat": "a purity scale, not a generic portion; 18 karat must not match 75%",
    "light-lumen": "luminous flux; CLDR's algebra drops the steradian and would equate it with candela",
    "graphics-dot": "print dots are not screen pixels, although CLDR aliases them",
    "force-kilowatt-hour-per-100-kilometer": "a consumption figure; dimensional algebra would equate it with newtons",
}

SUPPLEMENTAL_SUBTYPES = [
    "mass-megatonne",
    "mass-kilotonne",
    "mass-gigatonne",
    "concentr-basis-point",
    "concentr-percentage-point",
]

EXCLUDED = {
    "temperature-generic": "display-only degree sign with no defined conversion",
}

DECIMAL_MAX = 79228162514264337593543950335
DECIMAL_RANGE_REASON = "conversion rational exceeds System.Decimal range"

CURRENCY_ROWS = {
    "USD": ("USD", Fraction(1)),
    "USD-cent": ("USD", Fraction(1, 100)),
}


def parse_number(token):
    return Fraction(Decimal(token))


def load_units_xml():
    if len(sys.argv) > 1:
        return ET.parse(sys.argv[1]).getroot()
    with urllib.request.urlopen(CLDR_URL) as response:
        return ET.fromstring(response.read())


root = load_units_xml()

constants = {}


def evaluate(expression):
    # CLDR's expression grammar is fractional: every term after the first "/"
    # multiplies into the denominator, so ft3_to_m3/12*12*12 is ft3/1728.
    numerator, denominator = Fraction(1), Fraction(1)
    in_denominator = False
    for token in re.findall(r"[^*/]+|[*/]", expression.replace(" ", "")):
        if token == "*":
            continue
        if token == "/":
            in_denominator = True
            continue
        value = constants.get(token)
        if value is None:
            value = parse_number(token)
        if in_denominator:
            denominator *= value
        else:
            numerator *= value
    return numerator / denominator


for node in root.iter("unitConstant"):
    constants[node.get("constant")] = evaluate(node.get("value"))

convert = {}
for node in root.iter("convertUnit"):
    if node.get("special"):
        # special= marks a non-arithmetic conversion (Beaufort); reading its
        # missing factor as 1 would fabricate a linear identity.
        continue
    convert[node.get("source")] = {
        "baseUnit": node.get("baseUnit"),
        "factor": node.get("factor"),
        "offset": node.get("offset"),
    }


@cache
def try_resolve_component(name):
    if re.fullmatch(r"[0-9]+", name):
        return (Fraction(int(name)), Counter())
    if name in convert:
        entry = convert[name]
        factor = evaluate(entry["factor"]) if entry["factor"] else Fraction(1)
        if entry["baseUnit"] == name:
            return (factor, Counter({name: 1}))
        base_factor, base_dims = resolve_expression(entry["baseUnit"])
        return (factor * base_factor, base_dims)
    for prefix, multiplier in SI_PREFIXES.items():
        if name.startswith(prefix) and name[len(prefix):] in convert:
            inner_factor, inner_dims = try_resolve_component(name[len(prefix):])
            return (multiplier * inner_factor, inner_dims)
    return None


def resolve_side(side):
    tokens = side.split("-")
    factor, dims = Fraction(1), Counter()
    i = 0
    while i < len(tokens):
        power = 1
        if tokens[i] == "square":
            power, i = 2, i + 1
        elif tokens[i] == "cubic":
            power, i = 3, i + 1
        resolved = None
        for j in range(len(tokens), i, -1):
            if (candidate := try_resolve_component("-".join(tokens[i:j]))) is not None:
                resolved = candidate
                i = j
                break
        if resolved is None:
            resolved = (Fraction(1), Counter({tokens[i]: 1}))
            i += 1
        component_factor, component_dims = resolved
        factor *= component_factor**power
        for unit, exponent in component_dims.items():
            dims[unit] += exponent * power
    return factor, dims


def resolve_expression(expression):
    sides = expression.split("-per-")
    if len(sides) > 2:
        raise ValueError(f"more than one -per- in {expression!r}")
    factor, dims = resolve_side(sides[0])
    if len(sides) == 2:
        denominator_factor, denominator_dims = resolve_side(sides[1])
        factor /= denominator_factor
        for unit, exponent in denominator_dims.items():
            dims[unit] -= exponent
    return factor, dims


def dimension_key(dims):
    parts = [f"{unit}^{exponent}" for unit, exponent in sorted(dims.items()) if exponent != 0]
    return "|".join(parts) if parts else "dimensionless"


dictionary_path = Path(__file__).parent / "units_dictionary.json"
dictionary = json.load(open(dictionary_path))
unit_ids = [uid for uid in dictionary["units"] if uid not in EXCLUDED] + SUPPLEMENTAL_SUBTYPES

conversions = {}
excluded = dict(EXCLUDED)
for uid in sorted(set(unit_ids)):
    if uid in SEMANTICALLY_DISTINCT:
        conversions[uid] = {"dimension": f"distinct:{uid}", "factorNum": "1", "factorDen": "1"}
        continue
    expression = EXPRESSION_OVERRIDES.get(uid, uid.split("-", 1)[1])
    factor, dims = resolve_expression(expression)
    offset = Fraction(0)
    direct = convert.get(expression)
    if direct and direct["offset"]:
        if dims != Counter({direct["baseUnit"]: 1}):
            raise ValueError(f"offset on non-simple unit {uid}")
        offset = evaluate(direct["offset"])
    rational_parts = [factor.numerator, factor.denominator, offset.numerator, offset.denominator]
    if any(abs(part) > DECIMAL_MAX for part in rational_parts):
        excluded[uid] = DECIMAL_RANGE_REASON
        continue
    entry = {
        "dimension": dimension_key(dims),
        "factorNum": str(factor.numerator),
        "factorDen": str(factor.denominator),
    }
    if offset != 0:
        entry["offsetNum"] = str(offset.numerator)
        entry["offsetDen"] = str(offset.denominator)
    conversions[uid] = entry

for code, (family, factor) in CURRENCY_ROWS.items():
    conversions[code] = {
        "dimension": family,
        "currency": family,
        "factorNum": str(factor.numerator),
        "factorDen": str(factor.denominator),
    }


def fraction_of(uid):
    entry = conversions[uid]
    return Fraction(int(entry["factorNum"]), int(entry["factorDen"]))


def dimension_of(uid):
    return conversions[uid]["dimension"]


assert dimension_of("energy-kilowatt-hour") == dimension_of("energy-joule")
assert fraction_of("energy-kilowatt-hour") == Fraction(3_600_000)
assert len({dimension_of(u) for u in conversions if u.startswith("mass-")}) == 1
assert fraction_of("mass-megatonne") == Fraction(10**9)
assert fraction_of("mass-kilotonne") == Fraction(10**6)
assert fraction_of("mass-pound") == Fraction(45359237, 10**8)
assert len({dimension_of(u) for u in conversions if u.startswith("length-")}) == 1
assert fraction_of("length-kilometer") == Fraction(1000)
assert fraction_of("length-mile") == Fraction(1609344, 1000)
seconds_based = {"duration-second", "duration-minute", "duration-hour", "duration-day", "duration-week"}
year_based = {"duration-month", "duration-year", "duration-decade", "duration-century"}
assert len({dimension_of(u) for u in seconds_based & conversions.keys()}) == 1
assert len({dimension_of(u) for u in year_based & conversions.keys()}) == 1
assert dimension_of("duration-day") != dimension_of("duration-month")
assert fraction_of("duration-month") == Fraction(1, 12)
portion_family = {
    "concentr-percent": Fraction(1, 100),
    "concentr-permille": Fraction(1, 1000),
    "concentr-permyriad": Fraction(1, 10000),
    "concentr-basis-point": Fraction(1, 10000),
    "concentr-permillion": Fraction(1, 10**6),
    "concentr-portion-per-1e9": Fraction(1, 10**9),
}
for uid, expected in portion_family.items():
    assert fraction_of(uid) == expected, uid
    assert dimension_of(uid) == dimension_of("concentr-percent"), uid
assert dimension_of("concentr-percent") != dimension_of("concentr-milligram-ofglucose-per-deciliter")
for uid in SEMANTICALLY_DISTINCT:
    assert dimension_of(uid) == f"distinct:{uid}", uid
assert dimension_of("concentr-percentage-point") != dimension_of("concentr-percent")
assert dimension_of("concentr-karat") != dimension_of("concentr-percent")
assert dimension_of("speed-beaufort") != dimension_of("speed-meter-per-second")
assert dimension_of("light-lumen") != dimension_of("light-candela")
assert dimension_of("graphics-dot") != dimension_of("graphics-pixel")
assert dimension_of("force-kilowatt-hour-per-100-kilometer") != dimension_of("force-newton")
assert dimension_of("consumption-liter-per-100-kilometer") != dimension_of("consumption-mile-per-gallon")
assert dimension_of("digital-byte") == dimension_of("digital-bit")
assert fraction_of("digital-byte") == Fraction(8)
GALLON_M3 = Fraction(3785411784, 10**12)
assert fraction_of("volume-liter") == Fraction(1, 1000)
assert fraction_of("volume-gallon") == GALLON_M3
assert fraction_of("volume-pinch") == GALLON_M3 / (128 * 128)
assert fraction_of("volume-drop") == GALLON_M3 / (128 * 576)
assert fraction_of("consumption-mile-per-gallon") == Fraction(1609344, 1000) / GALLON_M3
assert fraction_of("angle-arc-minute") == Fraction(1, 21600)
assert fraction_of("angle-radian") == Fraction(1, 2) / constants["PI"]
assert fraction_of("speed-knot") == Fraction(1852, 3600)
assert Fraction(int(conversions["temperature-fahrenheit"]["offsetNum"]), int(conversions["temperature-fahrenheit"]["offsetDen"])) == Fraction(Decimal("2298.35")) / 9
assert fraction_of("temperature-fahrenheit") == Fraction(5, 9)
assert dimension_of("USD-cent") == dimension_of("USD")
assert fraction_of("USD-cent") == Fraction(1, 100)
assert conversions["USD"]["currency"] == "USD" and conversions["USD-cent"]["currency"] == "USD"
assert all("currency" not in entry for uid, entry in conversions.items() if uid not in CURRENCY_ROWS)
assert len(conversions) == len(dictionary["units"]) - len(excluded) + len(SUPPLEMENTAL_SUBTYPES) + len(CURRENCY_ROWS)
assert {uid for uid, reason in excluded.items() if reason == DECIMAL_RANGE_REASON} == {"mass-dalton", "mass-solar-mass"}

output = {
    "_meta": {
        "source": CLDR_URL,
        "cldr_version": CLDR_TAG,
        "generator": "generate_unit_conversions.py",
        "notes": [
            "Keyed by the subtype ids used in units_dictionary.json and SupplementalLexicon.cs.",
            "factorNum/factorDen (and offsetNum/offsetDen for affine temperature units) are exact "
            "integer rationals; canonical = value * factor + offset. Consumers compare via "
            "cross-multiplication, never division, so non-terminating rationals (month = year/12) stay exact.",
            "Two quantities convert only when their dimension strings are equal. Consumption's "
            "reciprocal pair (mile-per-gallon vs liter-per-100-kilometer) lands on reciprocal "
            "dimensions and is deliberately not cross-converted.",
            "Only USD carries a subunit pair (USD-cent); all other currencies have no entries, so "
            "cross-currency pairs never convert.",
            "concentr-permillion and concentr-portion-per-1e9 are legacy ids from the babel-era "
            "dictionary, resolved as part-per-1000000 and part-per-1000000000.",
            "semantically_distinct entries carry a distinct: dimension of their own: CLDR's "
            "dimensional algebra or aliasing would equate them with a different physical claim, "
            "so they keep same-subtype matching but never cross-convert.",
        ],
        "excluded": excluded,
        "semantically_distinct": SEMANTICALLY_DISTINCT,
    },
    "conversions": conversions,
}

output_path = Path(__file__).parent / "unit_conversions.json"
with open(output_path, "w") as handle:
    json.dump(output, handle, indent=1)
    handle.write("\n")

dimensions = {entry["dimension"] for entry in conversions.values()}
offsets = [uid for uid, entry in conversions.items() if "offsetNum" in entry]
print(
    f"wrote {output_path.name}: {len(conversions)} conversions, {len(dimensions)} dimensions, "
    f"offsets: {offsets}, excluded: {sorted(excluded)}")
