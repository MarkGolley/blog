import unittest
from judge import evaluate
from run import estimate, judge, make_payload
from task import STARTER, REFERENCE, CASES

class ExperimentTests(unittest.TestCase):
    def test_reference_passes_every_case_in_subprocess(self):
        result = judge(REFERENCE)
        self.assertTrue(result['passed'], result)
        self.assertEqual(len(CASES), result['passed_count'])

    def test_starter_fails_conversion_and_separation(self):
        failures = {f['test'] for f in evaluate(STARTER)['failures']}
        self.assertTrue({'mixed weight', 'incompatible units', 'names and units'} <= failures)

    def test_input_mutation_is_rejected(self):
        mutant = REFERENCE.replace('    totals = {}', '    items.append({"name": "x", "quantity": 0, "unit": "g"})\n    totals = {}')
        result = evaluate(mutant)
        self.assertFalse(result['passed'])
        self.assertTrue(all(f.get('input_unchanged') is False for f in result['failures']))

    def test_statement_calls_and_docstrings_are_allowed(self):
        code = REFERENCE.replace('    totals = {}', '    """Combine ingredients."""\n    totals = {}')
        code = code.replace('    return [{"name": name, "unit": unit, "quantity": round(totals[(name, unit)], 6)}\n            for name, unit in sorted(totals)]',
            '    result = []\n    for name, unit in sorted(totals):\n        result.append({"name": name, "unit": unit, "quantity": round(totals[(name, unit)], 6)})\n    return result')
        self.assertTrue(evaluate(code)['passed'])

    def test_wrong_conversion_is_detected(self):
        self.assertFalse(evaluate(REFERENCE.replace('1000', '100'))['passed'])

    def test_rounding_each_input_is_detected(self):
        mutant = REFERENCE.replace('item["quantity"] * scale', 'round(item["quantity"] * scale, 6)')
        self.assertFalse(evaluate(mutant)['passed'])

    def test_no_io_imports_or_reflection(self):
        for code in ['import os\ndef combine(items): return []',
                     'def combine(items): return open("secret")',
                     'def combine(items): return items.__class__',
                     'def combine(items): return eval("1")']:
            self.assertFalse(evaluate(code)['passed'])

    def test_usage_cost_does_not_double_count_reasoning(self):
        usage = {'input_tokens': 1000, 'input_tokens_details': {'cached_tokens': 200, 'cache_write_tokens': 100},
                 'output_tokens': 500, 'output_tokens_details': {'reasoning_tokens': 300}}
        self.assertAlmostEqual((700*2+200*.2+100*2.5+500*10)/1e6, estimate(usage, 'gpt-6-sol'))
        self.assertIsNone(estimate(None, 'gpt-6-sol'))

    def test_only_model_and_effort_differ_in_requests(self):
        a = make_payload('gpt-6-sol', 'low', [{'role':'user', 'content':'same task'}])
        b = make_payload('gpt-6-astra', 'low', [{'role':'user', 'content':'same task'}])
        a.pop('model'); b.pop('model')
        self.assertEqual(a, b)

if __name__ == '__main__':
    unittest.main()
