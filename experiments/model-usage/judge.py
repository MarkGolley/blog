"""Restricted pure-function evaluator. Candidate has no imports, I/O or credentials.

This is a narrow AST allowlist for this experiment, not a general Python sandbox.
The runner executes this trusted judge in a separate isolated, timed process.
"""
import ast
import builtins
import copy
import json
import sys
from task import CASES

BUILTINS = 'str float int dict list tuple set sorted round len min max sum abs enumerate range zip isinstance'.split()
METHODS = set('strip lower split join get items keys values append extend setdefault copy sort'.split())
NODES = set('Module FunctionDef arguments arg Expr Return Assign AnnAssign AugAssign Name Load Store Constant Dict List Tuple Set Subscript Slice For If Compare Eq NotEq Lt LtE Gt GtE In NotIn Is IsNot BoolOp And Or UnaryOp Not USub UAdd BinOp Add Sub Mult Div FloorDiv Mod Pow Call Attribute ListComp DictComp SetComp GeneratorExp comprehension IfExp Continue Break Pass keyword Lambda'.split())

def validate(code):
    if len(code) > 24000:
        raise ValueError('Code exceeds 24000 characters')
    tree = ast.parse(code)
    if len(tree.body) != 1 or not isinstance(tree.body[0], ast.FunctionDef):
        raise ValueError('Return exactly one function')
    fn = tree.body[0]
    if fn.name != 'combine' or fn.decorator_list or fn.returns or fn.args.defaults or fn.args.kw_defaults:
        raise ValueError('Expected an undecorated combine(items) function without defaults or annotations')
    if len(fn.args.args) != 1 or fn.args.args[0].arg != 'items' or fn.args.vararg or fn.args.kwarg or fn.args.kwonlyargs or fn.args.posonlyargs:
        raise ValueError('Expected combine(items) signature')
    for node in ast.walk(tree):
        if type(node).__name__ not in NODES:
            raise ValueError('Unsupported syntax: ' + type(node).__name__)
        if isinstance(node, ast.FunctionDef) and node is not fn:
            raise ValueError('No helper functions')
        if isinstance(node, ast.arg) and node.annotation:
            raise ValueError('No annotations')
        if isinstance(node, ast.Name) and node.id.startswith('_'):
            raise ValueError('Private names are not allowed')
        if isinstance(node, ast.Attribute) and node.attr not in METHODS:
            raise ValueError('Unsupported method: ' + node.attr)
        if isinstance(node, ast.Call):
            if not ((isinstance(node.func, ast.Name) and node.func.id in BUILTINS)
                    or (isinstance(node.func, ast.Attribute) and node.func.attr in METHODS)):
                raise ValueError('Unsupported function call')
    return tree

def evaluate(code):
    try:
        tree = validate(code)
        scope = {'__builtins__': {name: getattr(builtins, name) for name in BUILTINS}}
        exec(compile(tree, '<candidate>', 'exec'), scope)
    except Exception as exc:
        return {'passed': False, 'passed_count': 0, 'total': len(CASES), 'failures': [{'test': 'code format', 'message': str(exc)[:500]}]}
    failures = []
    for name, inputs, expected in CASES:
        actual_input = copy.deepcopy(inputs)
        try:
            actual = scope['combine'](actual_input)
            if actual != expected or actual_input != inputs:
                failures.append({'test': name, 'input': inputs, 'expected': expected,
                                 'actual': repr(actual)[:1000], 'input_unchanged': actual_input == inputs})
        except Exception as exc:
            failures.append({'test': name, 'message': type(exc).__name__ + ': ' + str(exc)[:300]})
    return {'passed': not failures, 'passed_count': len(CASES) - len(failures), 'total': len(CASES), 'failures': failures}

if __name__ == '__main__':
    print(json.dumps(evaluate(json.load(sys.stdin)['code'])))
