import test from 'node:test';
import assert from 'node:assert/strict';
import {seeds, schedule, validateBackup, day, shuffled} from '../MyBlog/wwwroot/js/word-garden-core.mjs';

test('review ratings schedule exact intervals without mutating the word', () => {
 const word = {...seeds[0]};
 assert.equal(schedule(word, 'again', 1000).due, 61000);
 assert.equal(schedule(word, 'good', 1000).due, 1000 + day);
 assert.equal(schedule(word, 'easy', 1000).due, 1000 + 3 * day);
 assert.equal(schedule({...word, interval: 4}, 'good').interval, 8);
 assert.equal(schedule({...word, interval: 200}, 'easy').interval, 365);
 assert.equal(schedule({...word, interval: 20}, 'again').interval, 0);
 assert.equal(word.reviews, 0);
 assert.equal(schedule(word, 'good').reviews, 1);
});
test('backup round trips including an intentionally empty collection', () => {
 assert.deepEqual(validateBackup(JSON.parse(JSON.stringify({version: 1, words: seeds}))), seeds);
 assert.deepEqual(validateBackup({version: 1, words: []}), []);
});
test('rejects malformed, duplicate, oversized and unsafe progress data', () => {
 for (const words of [[null], [{...seeds[0], meaning: ''}], [{...seeds[0], due: -1}], [{...seeds[0], due: Infinity}], [{...seeds[0], interval: 500}], [{...seeds[0], reviews: 1.5}], [{...seeds[0], term: 'a'.repeat(101)}], [seeds[0], seeds[0]], [seeds[0], {...seeds[0], id: 'other'}]]) {
  assert.throws(() => validateBackup({version: 1, words}));
 }
 assert.throws(() => validateBackup({version: 2, words: []}));
 assert.throws(() => validateBackup({version: 1, words: Array(2001).fill(seeds[0])}));
});
test('shuffle preserves each card and leaves input untouched', () => {
 const original = seeds.map(w => w.id);
 assert.deepEqual(shuffled(original).sort(), [...original].sort());
 assert.deepEqual(original, seeds.map(w => w.id));
});
