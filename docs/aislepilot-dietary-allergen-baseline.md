# AislePilot dietary and allergen compatibility baseline

Date: 2026-08-22

## Enforced selection rules

Template, pooled, and newly generated meals pass through the same compatibility filter before selection.

- Vegetarian excludes explicit meat, fish, crustacean, and mollusc ingredients.
- Vegan applies the vegetarian exclusions and also excludes explicit milk/dairy, egg, and honey ingredients.
- Pescatarian excludes explicit land-meat ingredients.
- Gluten-Free excludes explicit wheat, flour, bread, wraps, pasta, couscous, noodles, pastry, barley, rye, oats, and ordinary soy sauce. Ingredients explicitly named gluten-free and tamari are not rejected by name alone.
- Recognised UK allergen-group notes expand common user terms such as `dairy`, `gluten`, `tree nuts`, `soy`, and `shellfish` to ingredient-name aliases.
- Free-text dislikes continue to exclude direct meal-name and ingredient-name matches.
- Optional dessert templates use the same ingredient compatibility checks and are omitted when no compatible dessert exists.

The allergen groups follow the UK Food Standards Agency's 14-allergen categories and common ingredient examples: <https://www.food.gov.uk/print/pdf/node/176>.

## Safety boundary

This is conservative ingredient-name screening, not an allergen certification system. AislePilot does not currently hold manufacturer labels, recipe-brand composition, factory cross-contamination statements, or “may contain” data. Users with allergies must verify every product label and preparation environment. Product wording must not claim that a generated plan is guaranteed allergen-free.

## Regression coverage

`AislePilotServiceTests.FallbackCompatibility.cs` verifies the template path across Vegan, Vegetarian, Pescatarian, Gluten-Free, and Vegan + Gluten-Free modes; common dairy/tree-nut aliases; ordinary soy sauce versus tamari; plant-based milk false positives; and compatible optional-dessert behavior.

