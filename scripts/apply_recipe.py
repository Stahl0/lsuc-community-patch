import json
import os
import re
import sys


def load(path):
    with open(path, encoding='utf-8') as f:
        return f.read().replace('\r\n', '\n')


def main():
    export_dir, mod_dir, compiled_dir, out_dir = sys.argv[1:5]
    recipe = json.loads(load(os.path.join(mod_dir, 'recipe.json')))
    scripts = {}
    for step in recipe:
        name = step['script']
        if name not in scripts:
            scripts[name] = load(os.path.join(export_dir, name))
        text = scripts[name]
        if 'append_compiled' in step:
            compiled = load(os.path.join(compiled_dir, os.path.splitext(step['append_compiled'])[0] + '.pcode'))
            compiled = re.sub(r'(?<![\w"])loc([0-9a-f]+)\b', r'lscploc\1', compiled)
            if not text.endswith('\n'):
                text += '\n'
            text += compiled
        elif 'after' in step:
            anchor = step['after']
            count = text.count(anchor)
            if count != 1:
                sys.exit('anchor found %d times in %s: %s' % (count, name, step['what']))
            insert = load(os.path.join(mod_dir, step['insert']))
            pos = text.index(anchor) + len(anchor)
            text = text[:pos] + insert + text[pos:]
        else:
            pattern = re.compile(step['regex'])
            matches = pattern.findall(text)
            if len(matches) != 1:
                sys.exit('pattern matched %d times in %s: %s' % (len(matches), name, step['what']))
            text = pattern.sub(lambda m: step['replace'], text)
        scripts[name] = text
        print('applied:', step['what'])
    for name, text in scripts.items():
        if name.endswith('.pcode'):
            labels = re.findall(r'(?m)^(\w+):', text)
            dupes = sorted(set(l for l in labels if labels.count(l) > 1))
            if dupes:
                sys.exit('duplicate labels in %s: %s' % (name, ', '.join(dupes)))
            targets = set(re.findall(r'(?m)(?:^|:)(?:If|Jump) (\w+)\s*$', text))
            missing = sorted(targets - set(labels))
            if missing:
                sys.exit('jumps to undefined labels in %s: %s' % (name, ', '.join(missing)))
        path = os.path.join(out_dir, name)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(text)
        print('wrote', path)


main()
