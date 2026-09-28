# Shopping-list model usage experiment

Recorded 27 September 2026. This is an API function-repair exercise, not a Plus
allowance measurement or an IDE/agent benchmark. Python keeps the small exercise
portable; it is inspired by AislePilot, not copied from its production C# code.

## Fixed protocol

- GPT-6 Sol Low, GPT-6 Astra Low, GPT-6 Sol High; three fresh trials each.
- Identical brief, deliberately broken function, structured JSON response format,
  no external model tools, 8,192 maximum output tokens, Standard service tier.
- Fourteen fixed cases, including no input mutation. Cases are not in the initial
  prompt; every requirement is. Failed cases and expected results are returned
  after a failed attempt. No human hints or edits during a valid trial.
- At most three attempts, stopping at the first fully passing completed response.
- Fresh text history for each trial; within retries, previous code and feedback
  are sent back. Hidden reasoning items are not replayed across attempts.
- Sequential requests, rotated condition order across rounds. No API error retries.
- Record all attempts, exact requested and returned models, response IDs, status,
  timing, reported usage, generated code, and test feedback. Timing covers the API
  request, not local judging. Record hashes identify the frozen runner and cases.
- Costs include all attempts. Reasoning tokens are a subset of output tokens.
  Cached input and cache writes are accounted for separately when reported.
  Unknown usage stops the experiment; it is never treated as free.

## Results and limits

`results/2026-09-27.json` is the complete valid dataset. All nine trials passed on
their first attempt. Estimated total API cost: USD 0.111844. This is an estimate
from dated rates and recorded usage, not a billing invoice.

`results/2026-09-27-invalid-pilot.json` is excluded: the initial AST evaluator
rejected valid expression statements such as list.append. After correcting it and
adding a regression test, all nine trials restarted. The pilot was interrupted;
one in-flight request may have been charged without captured usage. Do not count
pilot failures as model failures or add them to the comparison.

Three trials of one small task do not establish statistical significance or
general model superiority. None needed a retry, so this does not resolve whether
Astra could save retries on a harder task. Zero reported reasoning tokens does
not prove no internal reasoning occurred. Cached tokens were zero in the valid
dataset. Aliases may change over time; returned IDs are preserved.

## Reproduce (paid API calls)

Requires Python 3.10+ and an `OPENAI_API_KEY` supplied through the environment.
No third-party Python packages. Never put credentials in a file or browser.

```powershell
python -m unittest discover -s experiments/model-usage -p test_experiment.py -v
python experiments/model-usage/run.py --output experiments/model-usage/results/new-run.json --max-usd 4.5
```

Execution was capped at USD 5. After the invalid pilot the valid
runner reserved a conservative maximum before each request within USD 4.50.
The ceiling is estimated from published rates; a network interruption can leave
an uncertain charge, so the runner stops rather than retrying automatically.

The candidate runs in a separate five-second process with a minimal environment,
restricted builtins and an AST allowlist. No file/network API is exposed. This is
a narrow local experiment evaluator, not a public arbitrary-code execution service.

## Publish the recording

```powershell
python experiments/model-usage/publish.py
```

This publishes only the complete original dataset to static website assets and
replaces the marked experiment section of the draft. No model is called by the
website. The UI offers two comparisons, three trials, play/pause, seek, reset,
final results, submitted code and feedback. Counts update at recorded response
completion boundaries. The parallel-looking timelines represent sequential runs,
aligned to time zero and replayed at 4x speed. Nothing pretends to be a live token
stream. A complete HTML result table remains when JS or the recording fails.

Rates checked on the official GPT-6 Sol and GPT-6 Astra model pages on 2026-09-27:
Sol USD 2 / .20 / 2.50 / 10; Astra USD 10 / 1 / 12.50 / 50 per million ordinary
input / cached input / cache-write / output tokens. These are API rates, not
subscription credit rates. See the source links embedded in the article.
