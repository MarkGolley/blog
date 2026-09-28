"""Publish only a complete measured experiment, with a static no-JS fallback."""
import html
import json
from pathlib import Path
import re
import statistics

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent
SOURCE = ROOT / 'results/2026-09-27.json'
DEST = REPO / 'MyBlog/wwwroot/experiments/model-usage/2026-09-27.json'
POST = REPO / 'MyBlog/wwwroot/BlogStorage/Why_Does_My_AI_Allowance_Disappear_So_Quickly.html'

def totals(trial):
    return {'tokens': sum(a['usage']['total_tokens'] for a in trial['attempts']),
            'seconds': sum(a['elapsed_seconds'] for a in trial['attempts']),
            'cost': sum(a['estimated_cost_usd'] for a in trial['attempts'])}

def main():
    data = json.loads(SOURCE.read_text(encoding='utf-8'))
    expected = {(m, e, r) for m, e in [('gpt-6-sol', 'low'), ('gpt-6-astra', 'low'), ('gpt-6-sol', 'high')] for r in range(1, 4)}
    actual = {(t['model'], t['effort'], t['repetition']) for t in data['trials']}
    if data['status'] != 'complete' or len(data['trials']) != 9 or actual != expected:
        raise ValueError('Refusing to publish an incomplete experiment')
    for trial in data['trials']:
        if not trial['attempts'] or any(a['usage'] is None or a['estimated_cost_usd'] is None for a in trial['attempts']):
            raise ValueError('Missing measured usage')
    DEST.parent.mkdir(parents=True, exist_ok=True)
    DEST.write_text(json.dumps(data, indent=2), encoding='utf-8')
    rows = ''
    for t in sorted(data['trials'], key=lambda t: (t['repetition'], t['model'], t['effort'])):
        total = totals(t)
        rows += f'<tr><th scope="row">{html.escape(t["model"])} / {t["effort"]}</th><td>{t["repetition"]}</td><td>{"Pass" if t["passed"] else "Fail"}</td><td>{len(t["attempts"])}</td><td>{total["tokens"]:,}</td><td>{total["seconds"]:.2f}s</td><td>${total["cost"]:.5f}</td></tr>\n'
    panels = ''.join('''<article class="replay-panel" data-replay-panel>
<h4 data-model></h4><p data-state></p><p class="replay-total" data-tokens></p><p>Total tokens so far</p>
<dl><dt>Input</dt><dd data-input></dd><dt>Output</dt><dd data-output></dd><dt>Reasoning (inside output)</dt><dd data-reasoning></dd><dt>Estimated API cost</dt><dd data-cost></dd></dl>
<details><summary>Inspect the submitted fix</summary><pre><code data-code></code></pre></details>
<details><summary>Inspect test feedback</summary><pre data-feedback></pre></details></article>''' for _ in range(2))
    summary_rows = ''
    for model, effort in [('gpt-6-sol','low'), ('gpt-6-astra','low'), ('gpt-6-sol','high')]:
        trials = [t for t in data['trials'] if t['model']==model and t['effort']==effort]
        values = [totals(t) for t in trials]
        summary_rows += f'<li><strong>{model} / {effort}:</strong> {sum(t["passed"] for t in trials)}/3 passed; median {statistics.median(v["tokens"] for v in values):,.0f} tokens and ${statistics.median(v["cost"] for v in values):.5f} per trial.</li>\n'
    conclusion = ''
    if all(t['passed'] and len(t['attempts']) == 1 for t in data['trials']):
        conclusion = '''<p><strong>Every model passed on its first attempt.</strong> Sol Low was enough for this problem. Astra Low used a similar number of tokens, but cost more at its higher rate. Sol High used more tokens without improving the test result.</p>
<p>That supports starting with a lower setting for a small, clearly explained job. It does not show whether Astra saves retries on harder work, because none of these valid trials needed a retry.</p>'''
    fragment = f'''<!-- MODEL_USAGE_EXPERIMENT_START -->
<h2 id="model-usage-experiment">I gave both models the same broken shopping list</h2>
<p>I used a small Python function inspired by AislePilot. Its job was to combine ingredients: 500 g of rice plus 1 kg should become 1,500 g. Two tomatoes and 500 g of tomatoes should stay separate. The starting code got those cases wrong.</p>
<p>I compared Sol Low, Astra Low and Sol High, with three fresh trials each. Every trial received the same instructions and starting function. A fixed set of 14 checks covered units, names, sorting, rounding and leaving the input unchanged. Each model could submit up to three fixes, receiving the same kind of test feedback after a failure.</p>
<p>This was a small API code-fixing exercise, not an IDE session or a measurement of Plus limits. The models had no browsing or file tools. Requests ran one at a time, with the order rotated between rounds. All nine results are below.</p>
<ul>{summary_rows}</ul>
<p>Median means the middle result of the three trials, so one unusually large or small run does not set the headline number.</p>
{conclusion}
<details class="model-experiment-disclosure">
<summary>
<span class="experiment-disclosure-title">Open the recorded experiment</span>
<span class="experiment-disclosure-note">Replay all nine trials, inspect the submitted code and see the full results.</span>
</summary>
<div class="experiment-disclosure-body">
<section class="model-replay" data-model-replay="/experiments/model-usage/2026-09-27.json" aria-labelledby="replay-title">
<p class="replay-label">RECORDED EXPERIMENT / 27 SEPTEMBER 2026</p>
<h3 id="replay-title">Watch the comparison</h3>
<p>Replay real responses at 4x speed, or skip straight to the results. This does not call an AI model or use your allowance. Counts appear when each response finished; there is no live token stream.</p>
<p class="replay-note">The two timelines start together for comparison. The actual API requests ran sequentially. Times cover the API request, not the local test run.</p>
<p role="status" data-replay-status>Loading the recording. All results are also in the table below.</p>
<div data-replay-controls hidden>
<div class="replay-controls">
<label>Compare<select data-comparison><option value="models">Sol Low vs Astra Low</option><option value="effort">Sol Low vs Sol High</option></select></label>
<label>Trial<select data-repetition><option value="1">1</option><option value="2">2</option><option value="3">3</option></select></label>
<button type="button" data-play>Play recording</button><button type="button" data-finish>Show results</button><button type="button" data-reset>Reset</button></div>
<label class="replay-seek">Recording position<input type="range" data-seek min="0" max="1" step="0.001" value="0"></label>
<p data-clock></p></div>
<div class="replay-panels" data-replay-panels hidden>{panels}</div>
<noscript><p>The interactive replay needs JavaScript. The complete results remain available below.</p></noscript>
<div class="replay-table-wrap" tabindex="0" role="region" aria-label="All nine experiment results">
<table><caption>Every trial, including all attempts</caption><thead><tr><th scope="col">Model / effort</th><th scope="col">Trial</th><th scope="col">Result</th><th scope="col">Attempts</th><th scope="col">Tokens</th><th scope="col">API time</th><th scope="col">Est. USD</th></tr></thead><tbody>{rows}</tbody></table></div>
<p class="replay-note">Reasoning is already included in output tokens. Costs use recorded input, cached input, cache writes and output at the dated Standard API rates. They are estimates, not an invoice or a conversion to Plus allowance.</p>
<p><a href="/experiments/model-usage/2026-09-27.json" download>Download the full recording, task and test cases (JSON)</a></p>
</section>
<h3>How much can this tell us?</h3>
<p>Three trials on one small problem are a useful example, not a verdict on either model. Passing these checks does not prove the code is right for every possible input. A harder task could give a different result.</p>
<p>I also caught a mistake in my own test runner during an initial pilot: it rejected valid Python list operations. I fixed it, added a regression check and restarted every condition. That invalid pilot is kept in the experiment folder and excluded from these results.</p>
<p>The <a href="https://developers.openai.com/api/docs/guides/reasoning">API reports reasoning usage</a> separately within output tokens. Prices came from the <a href="https://developers.openai.com/api/docs/models/gpt-6-sol">Sol</a> and <a href="https://developers.openai.com/api/docs/models/gpt-6-astra">Astra</a> model pages. The downloadable record includes exact model IDs, settings, all submitted fixes and test feedback.</p>
</div>
</details>
<!-- MODEL_USAGE_EXPERIMENT_END -->'''
    post = POST.read_text(encoding='utf-8-sig')
    if '<!-- MODEL_USAGE_EXPERIMENT_START -->' in post:
        post = re.sub(r'<!-- MODEL_USAGE_EXPERIMENT_START -->.*?<!-- MODEL_USAGE_EXPERIMENT_END -->', lambda _: fragment, post, flags=re.S)
    else:
        post = re.sub(r'<h2>Does it actually save anything\?</h2>.*?(?=<h2>What I would change first</h2>)', lambda _: fragment+'\n\n', post, flags=re.S)
    if 'model-usage-replay.js' not in post:
        post = post.replace('</body>', '<link rel="stylesheet" href="/css/model-usage-replay.css">\n<script src="/js/model-usage-replay.js" defer></script>\n</body>')
    POST.write_text(post, encoding='utf-8')
    print('Published nine measured trials and static fallback to the draft.')

if __name__ == '__main__':
    main()
