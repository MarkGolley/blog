"""Frozen shopping-list task and independently specified expected results."""
STARTER = '''def combine(items):
    totals = {}
    for item in items:
        name = item["name"]
        if name not in totals:
            totals[name] = dict(item)
        else:
            totals[name]["quantity"] += item["quantity"]
    return list(totals.values())
'''

BRIEF = '''Fix the Python function combine(items). This is a small shopping-list exercise
inspired by a meal planner, not production AislePilot code.
Input: a list of dictionaries with name (string), quantity (non-negative finite
number) and unit (string). All inputs satisfy this shape.
Rules:
- Trim and lowercase names and units; collapse internal whitespace in names.
- Merge by normalized name AND compatible unit family. Never merge weight,
  volume, count or distinct unknown units, even when names match.
- g/gram/grams and kg/kilogram/kilograms are weight; return grams as unit g.
- ml/millilitre/millilitres and l/litre/litres are volume; return millilitres as ml.
- each/item/items are count; return unit each. Empty unit also means each.
- Unknown units only merge with the same normalized unknown unit.
- Convert kilograms and litres by multiplying by 1000 BEFORE adding.
- Skip zero quantities. Do not mutate the input or its dictionaries.
- Return a list of dictionaries containing only name, quantity, unit. Sort by
  normalized name then output unit. Round final quantities to six decimal places.
Return JSON with one field, code, containing the full corrected function.
Use only one undecorated function named combine, without imports, annotations,
helper functions, classes, recursion, external tools or I/O. Allowed builtins:
str, float, int, dict, list, tuple, set, sorted, round, len, min, max, sum, abs,
enumerate, range, zip, isinstance. Ordinary string/list/dict methods are allowed.
You cannot change the tests. On a failed attempt you receive the test feedback
and can return a replacement function, up to three attempts total.
'''

def row(name, quantity, unit):
    return {"name": name, "quantity": quantity, "unit": unit}

CASES = [
    ("empty", [], []),
    ("same unit", [row("rice", 200, "g"), row("rice", 300, "g")], [row("rice", 500, "g")]),
    ("mixed weight", [row("rice", 500, "g"), row("rice", 1, "kg")], [row("rice", 1500, "g")]),
    ("weight aliases", [row("flour", .25, "kilograms"), row("flour", 50, "grams"), row("flour", 1, "gram")], [row("flour", 301, "g")]),
    ("volume", [row("milk", .5, "l"), row("milk", 250, "ml")], [row("milk", 750, "ml")]),
    ("volume aliases", [row("oil", 1, "litre"), row("oil", 2, "millilitres"), row("oil", 3, "millilitre"), row("oil", .1, "litres")], [row("oil", 1105, "ml")]),
    ("names and units", [row("  RED   Onion ", 2, " ITEMS "), row("red onion", 1, "item")], [row("red onion", 3, "each")]),
    ("incompatible units", [row("tomato", 2, "each"), row("tomato", 500, "g"), row("tomato", 100, "ml")], [row("tomato", 2, "each"), row("tomato", 500, "g"), row("tomato", 100, "ml")]),
    ("unknown units", [row("stock", 1, " CUBE "), row("stock", 2, "cube"), row("stock", 1, "tsp")], [row("stock", 3, "cube"), row("stock", 1, "tsp")]),
    ("zero and empty unit", [row("eggs", 0, "g"), row("eggs", 2, ""), row("eggs", 1, "each")], [row("eggs", 3, "each")]),
    ("sort order", [row("zest", 2, "g"), row("apple", 100, "g"), row("apple", 1, "each")], [row("apple", 1, "each"), row("apple", 100, "g"), row("zest", 2, "g")]),
    ("round after summing", [row("spice", .0000004, "g"), row("spice", .0000004, "g")], [row("spice", .000001, "g")]),
    ("decimal addition", [row("salt", .1, "g"), row("salt", .2, "g")], [row("salt", .3, "g")]),
    ("conversion order", [row("flour", .001, "kilogram"), row("flour", .5, "g")], [row("flour", 1.5, "g")]),
]

REFERENCE = '''def combine(items):
    aliases = {"g": ("g", 1), "gram": ("g", 1), "grams": ("g", 1),
        "kg": ("g", 1000), "kilogram": ("g", 1000), "kilograms": ("g", 1000),
        "ml": ("ml", 1), "millilitre": ("ml", 1), "millilitres": ("ml", 1),
        "l": ("ml", 1000), "litre": ("ml", 1000), "litres": ("ml", 1000),
        "each": ("each", 1), "item": ("each", 1), "items": ("each", 1), "": ("each", 1)}
    totals = {}
    for item in items:
        if item["quantity"] == 0:
            continue
        name = " ".join(item["name"].lower().split())
        unit = item["unit"].strip().lower()
        unit, scale = aliases.get(unit, (unit, 1))
        key = (name, unit)
        totals[key] = totals.get(key, 0) + item["quantity"] * scale
    return [{"name": name, "unit": unit, "quantity": round(totals[(name, unit)], 6)}
            for name, unit in sorted(totals)]
'''
