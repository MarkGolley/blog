"""Explicit, bounded API experiment. No API call occurs on import or page view."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from task import BRIEF, STARTER, CASES

ROOT = Path(__file__).resolve().parent
CONDITIONS = [('gpt-6-sol', 'low'), ('gpt-6-astra', 'low'), ('gpt-6-sol', 'high')]
# USD per million tokens, official model pages checked 2026-09-27.
RATES = {'gpt-6-sol': {'input': 2, 'cached': .2, 'write': 2.5, 'output': 10},
         'gpt-6-astra': {'input': 10, 'cached': 1, 'write': 12.5, 'output': 50}}
MAX_OUTPUT = 8192

def estimate(usage, model):
    if not usage or 'input_tokens' not in usage or 'output_tokens' not in usage:
        return None
    details = usage.get('input_tokens_details') or {}
    cached = details.get('cached_tokens', 0)
    writes = details.get('cache_write_tokens', 0)
    ordinary = max(0, usage['input_tokens'] - cached - writes)
    r = RATES[model]
    return (ordinary*r['input'] + cached*r['cached'] + writes*r['write'] + usage['output_tokens']*r['output'])/1e6

def judge(code):
    env = {k: v for k, v in os.environ.items() if k.upper() in {'SYSTEMROOT', 'WINDIR', 'TEMP', 'TMP'}}
    try:
        result = subprocess.run([sys.executable, '-E', '-s', str(ROOT / 'judge.py')],
            input=json.dumps({'code': code}), capture_output=True, text=True, timeout=5,
            cwd=ROOT, env=env)
        if result.returncode:
            raise ValueError('Candidate evaluation process failed')
        return json.loads(result.stdout)
    except (subprocess.TimeoutExpired, ValueError):
        return {'passed': False, 'passed_count': 0, 'total': len(CASES),
                'failures': [{'test': 'evaluation', 'message': 'Invalid result or five-second evaluation timeout'}]}

def make_payload(model, effort, history):
    return {'model': model, 'reasoning': {'effort': effort}, 'input': history,
            'max_output_tokens': MAX_OUTPUT, 'store': False, 'service_tier': 'default',
            'text': {'format': {'type': 'json_schema', 'name': 'solution', 'strict': True,
                     'schema': {'type': 'object', 'properties': {'code': {'type': 'string'}},
                                'required': ['code'], 'additionalProperties': False}}}}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--max-usd', type=float, default=5)
    args = parser.parse_args()
    if args.output.exists():
        raise SystemExit('Output exists; use a new file to preserve the original experiment')
    key = os.environ.get('OPENAI_API_KEY')
    if not key:
        raise SystemExit('Set OPENAI_API_KEY locally; never put it in the repo or browser')
    data = {'schema_version': 1, 'status': 'running', 'started_at': datetime.now(timezone.utc).isoformat(),
            'task': BRIEF, 'starter': STARTER, 'cases': CASES, 'rates_usd_per_million': RATES,
            'pricing_checked': '2026-09-27', 'max_output_tokens': MAX_OUTPUT,
            'max_attempts': 3, 'repetitions': 3, 'budget_usd': args.max_usd,
            'task_sha256': hashlib.sha256((BRIEF+STARTER+json.dumps(CASES)).encode()).hexdigest(),
            'harness_sha256': {name: hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
                               for name in ['run.py', 'judge.py', 'task.py']},
            'trials': [], 'estimated_cost_usd': 0}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    def save():
        temp = args.output.with_suffix('.tmp')
        temp.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding='utf-8')
        temp.replace(args.output)
    save()
    # Rotate order across repetitions to reduce systematic order/cache bias.
    for repeat in range(3):
        order = CONDITIONS[repeat:] + CONDITIONS[:repeat]
        for model, effort in order:
            trial = {'id': f'{model}-{effort}-{repeat+1}', 'model': model, 'effort': effort,
                     'repetition': repeat+1, 'attempts': [], 'passed': False}
            data['trials'].append(trial)
            history = [{'role': 'user', 'content': BRIEF + '\nBroken function:\n' + STARTER}]
            for attempt in range(1, 4):
                payload = make_payload(model, effort, history)
                # Conservative reserve: UTF-8 bytes upper-bound text tokens, plus schema overhead.
                reserve = ((len(json.dumps(payload).encode()) + 4096)*RATES[model]['write']
                           + MAX_OUTPUT*RATES[model]['output'])/1e6
                if data['estimated_cost_usd'] + reserve > args.max_usd:
                    data['status'] = 'budget_stopped'; save(); return
                started = time.perf_counter()
                request = urllib.request.Request('https://api.openai.com/v1/responses',
                    data=json.dumps(payload).encode(), headers={'Authorization': 'Bearer '+key,
                    'Content-Type': 'application/json'})
                try:
                    with urllib.request.urlopen(request, timeout=240) as response:
                        body = json.load(response)
                except (urllib.error.URLError, TimeoutError) as exc:
                    # No automatic HTTP retry: an uncertain response may already incur usage.
                    data['status'] = 'api_error'
                    trial['error'] = f'API request failed ({getattr(exc, "code", "network/timeout")}); not retried'
                    save(); print(trial['error'], flush=True); return
                elapsed = round(time.perf_counter()-started, 3)
                text = ''.join(c.get('text', '') for o in body.get('output', [])
                               if o.get('type') == 'message' for c in o.get('content', []) if c.get('type') == 'output_text')
                try:
                    code = json.loads(text)['code']
                    if not isinstance(code, str): raise ValueError()
                except (ValueError, KeyError, TypeError):
                    code = ''
                result = judge(code)
                usage = body.get('usage')
                cost = estimate(usage, model)
                entry = {'attempt': attempt, 'response_id': body.get('id'),
                    'returned_model': body.get('model'), 'response_status': body.get('status'),
                    'incomplete_details': body.get('incomplete_details'), 'elapsed_seconds': elapsed,
                    'usage': usage, 'estimated_cost_usd': cost, 'code': code, 'result': result}
                trial['attempts'].append(entry)
                data['estimated_cost_usd'] += cost if cost is not None else reserve
                trial['passed'] = result['passed'] and body.get('status') == 'completed'
                save()
                print(f'{trial["id"]} attempt {attempt}: {result["passed_count"]}/{result["total"]}; '
                      f'{elapsed}s; usage={usage}; estimated USD={cost}', flush=True)
                if cost is None:
                    data['status'] = 'missing_usage'; save(); return
                if trial['passed']: break
                history += [{'role': 'assistant', 'content': text or '(No usable code returned)'},
                            {'role': 'user', 'content': 'Test feedback: '+json.dumps(result)+'\nReturn corrected code using the same rules.'}]
    data['status'] = 'complete'
    data['completed_at'] = datetime.now(timezone.utc).isoformat()
    save()

if __name__ == '__main__':
    main()
