"""The references the AI host's Jinja engine is tested against, rendered by transformers' own Jinja environment:

  chat-templates.json  each shipped model family's chat template, and conversations rendered with it — plain, with a
                       system turn, tools, calls and what answered them, media, thinking on and off
  jinja-snippets.json  short templates, one feature each, and what Python rendered or that it refused

Rerun when a model family is added to the catalog or its template changes upstream, and commit both files:

  python -m venv .venv && .venv\\Scripts\\pip install transformers==5.14.1 jinja2==3.1.6 huggingface_hub
  .venv\\Scripts\\python -I render_references.py src/Nexaflow.Tests/Nexaflow.Tests.AiHost/Jinja
"""
import json
import sys
from pathlib import Path

from huggingface_hub import HfApi, snapshot_download
from transformers import AutoTokenizer
from transformers.utils.chat_template_utils import _compile_jinja_template

# Each family's template, pinned by revision — from the model's own repository, where it is the template its GGUF carries.
FAMILIES = {
    "gemma-4":  ("google/gemma-4-E4B-it", "ee0ef6023621cff504d758262d4e04895a5af4a2", [{}, {"enable_thinking": True}]),
    "qwen-3.5": ("Qwen/Qwen3.5-9B", "c202236235762e1c871ad0ccb60c8ee5ba337b9a", [{"enable_thinking": False}, {}]),
    "qwen-3.6": ("Qwen/Qwen3.6-27B", "6a9e13bd6fc8f0983b9b99948120bc37f49c13e9", [{"enable_thinking": False}, {}]),
    "qwen-3.8": ("Qwen/Qwen3.8-27B", "1d4bf0f2ff6012fd82039f2fa52739d0dd7c60c0",
                 [{"enable_thinking": False}, {}, {"enable_thinking": False, "preserve_thinking": False}]),
}

# Families whose GGUF carries a template of its own, read from the GGUF as the Hub reports it, pinned by the GGUF
# repository's revision — with the conversations a model of text alone is never given.
GGUF_FAMILIES = {
    "gemma-4-large":    ("google/gemma-4-26B-A4B-it-qat-q4_0-gguf", "d1c082be9cf3c8a514acf63b8761f4b41935842e", [{}, {"enable_thinking": True}], set()),
    "qwen-3.5-unsloth": ("unsloth/Qwen3.5-9B-GGUF", "3885219b6810b007914f3a7950a8d1b469d598a5", [{}, {"enable_thinking": True}], set()),
    "mimo-2.6":         ("bartowski/MiMo-V2.6-Distill-Qwen-9B-GGUF", "4371da10c84fb26da3592d4cf312d24aa82b7b65", [{"enable_thinking": False}, {}], set()),
    "phi-4":            ("microsoft/phi-4-gguf", "6edc2ef6664b739a8e11e62f2672ff6afe0c15ac", [{}], {"media"}),
}

TOOLS = [
    {"type": "function", "function": {"name": "open_folder", "description": "Opens a folder in the file browser.",
     "parameters": {"type": "object", "properties": {"path": {"type": "string", "description": "The folder's path."},
                                                       "new_tab": {"type": "boolean", "description": "In a new tab."}},
                    "required": ["path"]}}},
    {"type": "function", "function": {"name": "set_mode", "description": "Sets the view mode.",
     "parameters": {"type": "object", "properties": {"mode": {"type": "string", "enum": ["list", "grid"]},
                                                       "sizes": {"type": "array", "items": {"type": "integer"}}},
                    "required": ["mode"]}}},
]


def call(id, name, args):
    return {"id": id, "type": "function", "function": {"name": name, "arguments": args}}


DOCS = "D:\\docs"
CONVERSATIONS = {
    "plain": dict(messages=[{"role": "user", "content": "Hello there"}]),
    "system": dict(messages=[{"role": "system", "content": "  You are Nexaflow.  "}, {"role": "user", "content": "Hi"},
                             {"role": "assistant", "content": "Hello! How can I help?"}, {"role": "user", "content": "Open my docs."}]),
    "tools": dict(messages=[{"role": "system", "content": "You are Nexaflow."}, {"role": "user", "content": "Open " + DOCS + " in a new tab"}],
                  tools=TOOLS),
    "call": dict(messages=[{"role": "system", "content": "You are Nexaflow."}, {"role": "user", "content": "Open " + DOCS},
                           {"role": "assistant", "content": "", "tool_calls": [call("c1", "open_folder", {"path": DOCS, "new_tab": True})]},
                           {"role": "tool", "tool_call_id": "c1", "content": "Opened " + DOCS + "."},
                           {"role": "assistant", "content": "Done \u2014 it is open."}, {"role": "user", "content": "Thanks"}], tools=TOOLS),
    "two_calls": dict(messages=[{"role": "user", "content": "Grid view of " + DOCS},
                                {"role": "assistant", "content": "On it.",
                                 "tool_calls": [call("a", "open_folder", {"path": DOCS}), call("b", "set_mode", {"mode": "grid", "sizes": [1, 2.5]})]},
                                {"role": "tool", "tool_call_id": "a", "content": "Opened."}, {"role": "tool", "tool_call_id": "b", "content": "Grid."}],
                      tools=TOOLS),
    "pending": dict(messages=[{"role": "user", "content": "Open " + DOCS},
                              {"role": "assistant", "content": "", "tool_calls": [call("c1", "open_folder", {"path": DOCS})]},
                              {"role": "tool", "tool_call_id": "c1", "content": "Opened."}], tools=TOOLS),
    "media": dict(messages=[{"role": "user", "content": [{"type": "image"}, {"type": "text", "text": "What is this?"}]},
                            {"role": "assistant", "content": "A report."},
                            {"role": "user", "content": [{"type": "text", "text": "<__media__>"}, {"type": "text", "text": "And this?"}]}]),
    "nogen": dict(messages=[{"role": "user", "content": "Hi"}, {"role": "assistant", "content": "Hello."}], add_generation_prompt=False),
}

SNIPPET_DATA = {"d": {"b": 2, "a": 1, "Z": 3}, "items": [3, 1, 2], "words": ["Banana", "apple", "cherry"], "s": "Hello, World",
                "n": None, "t": True, "f": 2.5,
                "users": [{"name": "Ann", "age": 31, "tags": ["x"]}, {"name": "bob", "age": 25, "tags": []}],
                "nested": {"x": {"y": [1, {"z": "deep"}]}}, "uni": "caf\u00e9 \u2014 \u2615", "empty": [], "zero": 0}

SNIPPETS = [
    # whitespace control: trim_blocks, lstrip_blocks, '-' and '+', comments, raw, the trailing newline
    "a\n{% if t %}\nyes\n{% endif %}\nb", "  {% if t %}x{% endif %}  \n  y", "a  {%- if t -%}  x  {%- endif -%}  b",
    "{{ 'x' -}}   \n  {{- 'y' }}", "line\n    {%+ if t %}kept{% endif %}\n", "{# comment #}\nafter", "a {#- c -#} b",
    "{% raw %}{{ not rendered }}{% endraw %}", "x\n  {{ 'out' }}\n",
    # literals and operators
    "{{ 1 + 2 }} {{ 7 / 2 }} {{ 7 // 2 }} {{ -7 // 2 }} {{ 7 % 3 }} {{ -7 % 3 }} {{ 2 ** 10 }} {{ 2.0 * 3 }}",
    "{{ 1.0 }} {{ 0.1 + 0.2 }} {{ 1e20 }} {{ 1e-5 }} {{ 123456789.0 }} {{ 3 / 3 }} {{ 1_000 }}",
    "{{ 'a' + 'b' }} {{ 'a' ~ 1 ~ none }} {{ 'ab' * 3 }} {{ '%s-%d' % ('x', 4) }}",
    "{{ 'tab\\there' }}|{{ \"q'uote\" }}|{{ 'u\\u00e9' }}|{{ 'a' 'b' }}",
    "{{ 1 < 2 < 3 }} {{ 3 > 2 > 2 }} {{ 1 == 1.0 }} {{ 'a' != 'b' }} {{ 'b' > 'a' }}",
    "{{ 'ell' in s }} {{ 2 in items }} {{ 'a' in d }} {{ 'q' not in d }}",
    "{{ n or 'x' }} {{ t and 'y' }} {{ zero or empty or 'z' }} {{ not t }}",
    "{{ 'big' if f > 2 else 'small' }}|{{ 'no' if false }}|",
    "{{ [1, 'a', none, true, 2.5] }} {{ {'k': 'v', 'n': [1]} }} {{ (1, 2) }}",
    # attributes, items, slices
    "{{ d.a }} {{ d['b'] }} {{ d.missing }}|{{ items[0] }} {{ items[-1] }} {{ items.1 }} {{ s[0] }} {{ s[-5:] }}",
    "{{ items[::-1] }} {{ items[1:] }} {{ s[:5] }} {{ s[::2] }} {{ items[5:9] }} {{ uni[1:4] }}",
    "{{ nested.x.y[1].z }} {{ users[0]['tags'][0] }}",
    # methods
    "{{ s.upper() }} {{ s.lower() }} {{ s.split(', ') }} {{ '  pad '.strip() }}|{{ 'xxhixx'.strip('x') }} {{ s.startswith('He') }} {{ s.endswith(('x', 'ld')) }}",
    "{{ s.replace('l', 'L') }} {{ s.replace('l', 'L', 1) }} {{ s.find('o') }} {{ s.count('l') }} {{ '-'.join(['a', 'b']) }} {{ 'a b  c'.split() }}",
    "{{ '{} and {}'.format('x', 'y') }} {{ 'n={n}'.format(n=3) }} {{ 'hello world'.title() }} {{ 'hELLO'.capitalize() }}",
    "{{ d.get('a') }} {{ d.get('q', 'dflt') }} {{ d.get('q') }} {{ d.keys() | list }} {{ d.values() | list }} {{ d.items() | list }}",
    "{{ 'a\\nb\\n'.splitlines() }} {{ 'a,b,c'.split(',', 1) }} {{ 'a,b,c'.rsplit(',', 1) }} {{ '\\n x \\n'.lstrip('\\n') }}|",
    # filters
    "{{ items | sort }} {{ words | sort }} {{ words | sort(case_sensitive=true) }} {{ items | sort(reverse=true) }} {{ users | sort(attribute='age') | map(attribute='name') | list }}",
    "{% for k, v in d | dictsort %}{{ k }}={{ v }};{% endfor %} {% for k, v in d | dictsort(true) %}{{ k }};{% endfor %} {{ d | dictsort(by='value', reverse=true) }}",
    "{{ words | map('upper') | list }} {{ words | join(', ') }} {{ users | map(attribute='name') | join('/') }} {{ users | join(',', attribute='age') }}",
    "{{ items | first }} {{ items | last }} {{ items | length }} {{ s | length }} {{ uni | length }} {{ d | length }} {{ items | sum }} {{ items | min }} {{ items | max }}",
    "{{ users | selectattr('age', 'gt', 30) | map(attribute='name') | list }} {{ users | rejectattr('tags') | map(attribute='name') | list }} {{ items | select('odd') | list }} {{ items | reject('even') | list }}",
    "{{ missing | default('dflt') }} {{ '' | default('e', true) }} {{ n | default('x') }} {{ zero | d(5) }}",
    "{{ '  x  ' | trim }}|{{ 'abc' | upper }} {{ 'ABC' | lower }} {{ 'hello' | capitalize }} {{ 'a b' | title }} {{ 3.7 | int }} {{ '42' | int }} {{ 'x' | int }} {{ '2.5' | float }} {{ 7 | float }}",
    "{{ 2.567 | round(2) }} {{ 2.5 | round }} {{ -3 | abs }} {{ -2.5 | abs }} {{ [1, 2, 2, 'A', 'a'] | unique | list }} {{ 'abc' | reverse }} {{ items | reverse | list }}",
    "{{ d | tojson }} {{ users | tojson }} {{ uni | tojson }} {{ {'a': [1, 2.5, none, true]} | tojson(indent=2) }} {{ d | tojson(sort_keys=true) }} {{ uni | tojson(ensure_ascii=true) }}",
    "{{ d | items | list }} {{ 'a\\nb' | indent(2) }}|{{ 'a\\nb' | indent(2, true) }}|{{ 'x' | center(5) }}|{{ 'a b c' | wordcount }} {{ items | batch(2) | list }} {{ 'x' | string }} {{ 5 | string }}",
    "{{ 'hello %s' | format('you') }} {{ '<a href=\"x\">' | e }} {{ items | list }} {{ 'ab' | list }} {{ d | list }} {{ users | map(attribute='nope', default='?') | list }}",
    # tests
    "{{ d is mapping }} {{ items is mapping }} {{ items is sequence }} {{ s is sequence }} {{ d is sequence }} {{ s is string }} {{ 5 is string }} {{ items is iterable }}",
    "{{ n is none }} {{ missing is defined }} {{ missing is undefined }} {{ t is boolean }} {{ 1 is boolean }} {{ 1 is integer }} {{ t is integer }} {{ f is float }} {{ t is number }}",
    "{{ 4 is even }} {{ 3 is odd }} {{ 9 is divisibleby 3 }} {{ 9 is divisibleby(4) }} {{ 1 is eq 1 }} {{ 'a' is in 'cat' }} {{ n is sameas none }} {{ 'up' is lower }} {{ 'UP' is upper }} {{ t is true }} {{ zero is false }}",
    "{{ 'upper' is filter }} {{ 'nope' is filter }} {{ 'odd' is test }} {{ s is not none }} {{ d is not mapping }}",
    # loops
    "{% for i in items %}{{ loop.index }}/{{ loop.index0 }}/{{ loop.revindex }}/{{ loop.first }}/{{ loop.last }}/{{ loop.length }};{% endfor %}",
    "{% for i in items %}[{{ loop.previtem }}<{{ i }}>{{ loop.nextitem }}]{% endfor %} {% for i in items %}{{ loop.cycle('a', 'b') }}{% endfor %}",
    "{% for i in items if i > 1 %}{{ i }}:{{ loop.index }} {% endfor %}| {% for i in empty %}x{% else %}none{% endfor %}",
    "{% for i in range(10) %}{% if i == 2 %}{% continue %}{% endif %}{% if i == 5 %}{% break %}{% endif %}{{ i }}{% endfor %} {{ range(3) | list }} {{ range(1, 9, 3) | list }} {{ range(5, 0, -2) | list }}",
    "{% for k in d %}{{ k }}{% endfor %} {% for c in 'abc' %}{{ c }}-{% endfor %} {% for a, b in [[1, 2], [3, 4]] %}{{ a + b }}{% endfor %}",
    "{% set x = 1 %}{% for i in items %}{% set x = i %}{% endfor %}{{ x }} {% set ns = namespace(v=0) %}{% for i in items %}{% set ns.v = ns.v + i %}{% endfor %}{{ ns.v }}",
    "{% for a in [1, 2] %}{% for b in [3, 4] %}{{ loop.index }}{{ a }}{{ b }} {% endfor %}{% endfor %}",
    # set and macros
    "{% set a, b = 1, 2 %}{{ a }}{{ b }} {% set c %}  block {{ a }}  {% endset %}[{{ c }}] {% set u | upper %}shout{% endset %}{{ u }}",
    "{% macro greet(name, punct='!') %}Hi {{ name }}{{ punct }}{% endmacro %}{{ greet('Ann') }} {{ greet('Bob', '?') }} {{ greet(name='Cy', punct='.') }} {{ greet('x') | upper }}",
    "{% macro fact(n) %}{% if n <= 1 %}1{% else %}{{ n * (fact(n - 1) | int) }}{% endif %}{% endmacro %}{{ fact(5) }}",
    "{% macro m() %}{{ outer }}{% endmacro %}{% set outer = 'seen' %}{{ m() }}",
    "{% set ns = namespace(found=false, list=[]) %}{{ ns.found }} {{ ns.list }} {{ namespace(a=1).a }} {{ dict(x=1, y='z') }}",
    # undefined
    "[{{ missing }}] {{ missing | length }} {% for x in missing %}x{% endfor %}{{ missing is defined }} {{ (missing or 'fallback') }}",
    "{% generation %}assistant text{% endgeneration %}",
]

REFUSED = ["{{ missing.attr }}", "{{ raise_exception('boom') }}", "{{ items.append(4) }}", "{{ 1 / 0 }}", "{% if %}", "{{ none.x }}"]


def chat_templates():
    out = {}
    for family, (repo, revision, variables) in FAMILIES.items():
        folder = snapshot_download(repo, revision=revision,
                                   allow_patterns=["chat_template.jinja", "tokenizer*", "vocab.json", "merges.txt", "special_tokens_map.json"])
        tokenizer = AutoTokenizer.from_pretrained(folder)
        renders = []
        for name, conversation in CONVERSATIONS.items():
            generation_prompt = conversation.get("add_generation_prompt", True)
            for extra in variables:
                renders.append({
                    "case": name, "variables": extra, "messages": conversation["messages"], "tools": conversation.get("tools"),
                    "add_generation_prompt": generation_prompt, "bos_token": tokenizer.bos_token, "eos_token": tokenizer.eos_token,
                    "rendered": tokenizer.apply_chat_template(conversation["messages"], tools=conversation.get("tools"), tokenize=False,
                                                              add_generation_prompt=generation_prompt, **extra),
                })
        out[family] = {"repo": repo, "revision": revision, "template": Path(folder, "chat_template.jinja").read_text(encoding="utf-8"),
                       "renders": renders}
    for family, (repo, revision, variables, unread) in GGUF_FAMILIES.items():
        gguf = HfApi().model_info(repo, revision=revision, expand=["gguf"]).gguf
        template = _compile_jinja_template(gguf["chat_template"])
        bos, eos = gguf.get("bos_token"), gguf.get("eos_token")
        renders = []
        for name, conversation in CONVERSATIONS.items():
            if name in unread: continue
            generation_prompt = conversation.get("add_generation_prompt", True)
            for extra in variables:
                renders.append({
                    "case": name, "variables": extra, "messages": conversation["messages"], "tools": conversation.get("tools"),
                    "add_generation_prompt": generation_prompt, "bos_token": bos, "eos_token": eos,
                    "rendered": template.render(messages=conversation["messages"], tools=conversation.get("tools"),
                                                add_generation_prompt=generation_prompt, bos_token=bos, eos_token=eos, **extra),
                })
        out[family] = {"repo": repo, "revision": revision, "template": gguf["chat_template"], "renders": renders}
    return out


def snippets():
    out = {"data": SNIPPET_DATA, "snippets": [], "refused": []}
    for source in SNIPPETS:
        out["snippets"].append({"source": source, "output": _compile_jinja_template(source).render(**SNIPPET_DATA)})
    for source in REFUSED:
        try:
            _compile_jinja_template(source).render(**SNIPPET_DATA)
            out["refused"].append({"source": source, "raised": False})
        except Exception as ex:
            out["refused"].append({"source": source, "raised": True, "message": str(ex)})
    return out


def main():
    target = Path(sys.argv[1])
    target.mkdir(parents=True, exist_ok=True)
    for name, data in (("chat-templates.json", chat_templates()), ("jinja-snippets.json", snippets())):
        (target / name).write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print("wrote", target / name)


if __name__ == "__main__":
    main()
