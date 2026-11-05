import assert from 'node:assert/strict';
import { test } from 'node:test';
import { grammarFor } from './tag-completions.js';

// matchBefore-style helper: the CodeMirror partial match is the grammar's partialPattern
// anchored at the cursor, so exec on the text-before-cursor reproduces it exactly.
function partialBefore(grammar, textBeforeCursor) {
    const match = grammar.partialPattern.exec(textBeforeCursor);
    return match ? { from: match.index, text: match[0] } : null;
}

function applyCompletion(grammar, textBeforeCursor, name) {
    const partial = partialBefore(grammar, textBeforeCursor);
    assert.ok(partial, `expected a partial tag match in ${JSON.stringify(textBeforeCursor)}`);
    return textBeforeCursor.slice(0, partial.from) + grammar.label(name);
}

test('single grammar labels stay single-braced (every non-agent block)', () => {
    assert.equal(grammarFor('single').label('client.name'), '{client.name}');
});

test('unknown or missing grammar falls back to single-brace', () => {
    assert.equal(grammarFor(undefined).label('a'), '{a}');
    assert.equal(grammarFor('nonsense').label('a'), '{a}');
});

test('double grammar labels are double-braced (agent-block prompts)', () => {
    assert.equal(grammarFor('double').label('client.name'), '{{client.name}}');
});

test('single grammar completion replaces from the opening brace', () => {
    assert.equal(applyCompletion(grammarFor('single'), 'Report for {cli', 'client.name'), 'Report for {client.name}');
});

test('double grammar swallows both typed braces so the insert never stacks a third', () => {
    assert.equal(applyCompletion(grammarFor('double'), 'Report for {{cli', 'client.name'), 'Report for {{client.name}}');
});

test('double grammar completes from a single typed brace too', () => {
    assert.equal(applyCompletion(grammarFor('double'), 'Report for {cli', 'client.name'), 'Report for {{client.name}}');
});

test('double grammar matches an empty partial right after the braces', () => {
    const partial = partialBefore(grammarFor('double'), 'Report for {{');
    assert.ok(partial);
    assert.equal(partial.text, '{{');
});

test('no partial match without an opening brace', () => {
    assert.equal(partialBefore(grammarFor('single'), 'plain text'), null);
    assert.equal(partialBefore(grammarFor('double'), 'plain text'), null);
});

test('validFor keeps the completion open while typing inside the braces', () => {
    assert.ok(grammarFor('single').validFor.test('{client.na'));
    assert.ok(grammarFor('double').validFor.test('{{client.na'));
    assert.ok(grammarFor('double').validFor.test('{{client.name}'));
});
