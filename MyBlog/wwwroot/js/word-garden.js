import {storageKey, seeds, schedule, validateBackup, shuffled} from './word-garden-core.mjs';

const $ = id => document.getElementById(`wg-${id}`);
let words = seeds.map(w => ({...w}));
let current = null;
let reviewed = 0;
let mode = 'review';
let storageBlocked = false;
function storageWarning(text) { $('storage').hidden = false; $('storage').textContent = text; }
try {
 const raw = localStorage.getItem(storageKey);
 if (raw !== null) words = validateBackup(JSON.parse(raw));
} catch {
 storageBlocked = true;
 storageWarning('Saved data could not be read. You can practise with starter words, but changes will not be saved. Export a backup of this session; import a valid backup to replace unreadable data.');
}
function persist() {
 if (storageBlocked) return;
 try { localStorage.setItem(storageKey, JSON.stringify({version: 1, words})); }
 catch { storageWarning('Your browser could not save these changes. Keep this tab open and export a backup from My words.'); }
}
function message(text) { $('message').textContent = text; }
function stats() {
 $('due').textContent = words.filter(w => w.due <= Date.now()).length;
 $('total').textContent = words.length;
 $('learned').textContent = words.filter(w => w.interval >= 7).length;
}
function showCard() {
 stats();
 current = words.filter(w => w.due <= Date.now()).sort((a,b) => a.due - b.due)[0] || null;
 $('answer').hidden = true; $('ratings').hidden = true;
 $('reveal').hidden = !current;
 $('session').textContent = `${reviewed} reviewed this session`;
 $('card-label').textContent = current ? 'YOUR NEXT WORD' : 'ROOM TO GROW';
 $('word').textContent = current?.term || (words.length ? 'All caught up.' : 'Your garden starts here.');
 $('prompt').hidden = false;
 $('prompt').textContent = current ? 'Say the meaning to yourself, then turn the card over.' : words.length ? `Next review: ${new Date(Math.min(...words.map(w => w.due))).toLocaleString()}. Try Memory match or add a new word.` : 'Open My words to add your first word and meaning.';
}
$('reveal').addEventListener('click', () => {
 if (!current) return;
 $('meaning').textContent = current.meaning;
 $('example').textContent = current.example;
 $('example').hidden = !current.example;
 $('memory').textContent = current.hint ? `Memory cue: ${current.hint}` : '';
 $('answer').hidden = false; $('ratings').hidden = false;
 $('reveal').hidden = true; $('prompt').hidden = true;
 for (const rating of ['good','easy']) $('' + rating + '-time').textContent = `${schedule(current, rating).interval} day${schedule(current, rating).interval === 1 ? '' : 's'}`;
 document.querySelector('[data-rating="again"]').focus();
});
document.querySelectorAll('[data-rating]').forEach(button => button.addEventListener('click', () => {
 if (!current || $('ratings').hidden) return;
 const next = schedule(current, button.dataset.rating);
 words = words.map(w => w.id === current.id ? next : w);
 reviewed++; persist(); showCard();
 message('Review saved for this session.');
 if (current) $('reveal').focus();
}));
document.querySelectorAll('[data-mode]').forEach(button => button.addEventListener('click', () => {
 mode = button.dataset.mode;
 document.querySelectorAll('[data-mode]').forEach(b => b.setAttribute('aria-pressed', String(b === button)));
 for (const name of ['review','match','library']) $(name).hidden = name !== mode;
 message('');
 if (mode === 'review') showCard();
 if (mode === 'library') renderLibrary();
 if (mode === 'match') newGame();
}));
function renderLibrary() {
 stats();
 const query = $('search').value.trim().toLocaleLowerCase();
 const filtered = words.filter(w => `${w.term} ${w.meaning}`.toLocaleLowerCase().includes(query));
 $('word-list').replaceChildren();
 if (!filtered.length) $('word-list').textContent = words.length ? 'No matching words. Try another search.' : 'No words yet. Add your first one using the form.';
 for (const word of filtered) {
  const row = document.createElement('article'); row.className = 'wg-word-row';
  const content = document.createElement('div');
  const title = document.createElement('strong'); title.textContent = word.term;
  const definition = document.createElement('p'); definition.textContent = word.meaning;
  const status = document.createElement('p'); status.className = 'wg-muted';
  status.textContent = word.due <= Date.now() ? 'Ready to review' : `Review ${new Date(word.due).toLocaleDateString()}`;
  const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = 'Remove'; remove.setAttribute('aria-label', `Remove ${word.term}`);
  remove.addEventListener('click', () => {
   if (!confirm(`Remove “${word.term}” and its review progress?`)) return;
   words = words.filter(w => w.id !== word.id); persist(); renderLibrary(); message('Word removed.'); $('search').focus();
  });
  content.append(title,definition,status); row.append(content,remove); $('word-list').append(row);
 }
}
$('search').addEventListener('input', renderLibrary);
$('add').addEventListener('submit', event => {
 event.preventDefault();
 const term = $('term').value.trim(), meaning = $('definition').value.trim();
 if (!term || !meaning) { message('Enter both a word and its meaning.'); return; }
 if (words.length >= 2000) { message('Your collection is full (2,000 words). Export a backup before removing words.'); return; }
 if (words.some(w => w.term.toLocaleLowerCase() === term.toLocaleLowerCase())) { message('That word is already in your collection.'); return; }
 words.push({id: crypto.randomUUID(), term, meaning, example: $('sentence').value.trim(), hint: $('hint').value.trim(), due: 0, interval: 0, reviews: 0});
 persist(); $('add').reset(); $('search').value = ''; renderLibrary(); message(`Added “${term}”. It is ready for review.`); $('term').focus();
});
$('export').addEventListener('click', () => {
 const url = URL.createObjectURL(new Blob([JSON.stringify({version: 1, words}, null, 2)], {type: 'application/json'}));
 const link = document.createElement('a'); link.href = url; link.download = `word-garden-${new Date().toISOString().slice(0,10)}.json`; link.click();
 setTimeout(() => URL.revokeObjectURL(url), 1000); message('Backup exported. Keep it somewhere safe.');
});
$('import').addEventListener('change', async event => {
 const file = event.target.files[0]; if (!file) return;
 try {
  if (file.size > 5 * 1024 * 1024) throw new Error('Choose a backup smaller than 5 MB.');
  const imported = validateBackup(JSON.parse(await file.text()));
  if (!confirm(`Replace your current ${words.length} words and progress with ${imported.length} words from this backup? Export first if you want to keep the current collection.`)) return;
  words = imported; storageBlocked = false; $('storage').hidden = true; persist(); renderLibrary(); message(`Imported ${words.length} words.`);
 } catch (error) { message(`Import failed. ${error instanceof SyntaxError ? 'The file is not valid JSON.' : error.message} Your collection is unchanged.`); }
 finally { event.target.value = ''; }
});
let selected = null, matched = 0, attempts = 0, pairs = 0;
function newGame() {
 selected = null; matched = 0; attempts = 0;
 const sample = shuffled(words).slice(0,6); pairs = sample.length;
 $('board').replaceChildren();
 $('match-status').textContent = pairs ? `0 of ${pairs} pairs found · 0 attempts` : 'Add some words in My words to start a round.';
 const cards = shuffled(sample.flatMap(w => [{id: w.id, side: 'word', text: w.term},{id: w.id, side: 'meaning', text: w.meaning}]));
 for (const card of cards) {
  const button = document.createElement('button'); button.type = 'button'; button.textContent = card.text; button.setAttribute('aria-pressed','false');
  button.addEventListener('click', () => {
   if (selected?.button === button) { button.setAttribute('aria-pressed','false'); selected = null; return; }
   if (!selected) { selected = {button,card}; button.setAttribute('aria-pressed','true'); return; }
   attempts++;
   const success = selected.card.id === card.id && selected.card.side !== card.side;
   selected.button.setAttribute('aria-pressed','false');
   if (success) {
    for (const b of [selected.button,button]) { b.disabled = true; b.classList.add('matched'); b.textContent = `✓ ${b.textContent}`; }
    matched++;
   }
   selected = null;
   $('match-status').textContent = `${success ? 'Match found! ' : 'Not a pair. Try again. '}${matched} of ${pairs} pairs found · ${attempts} attempts${matched === pairs ? ' · Round complete! Start a new round to keep practising.' : ''}`;
  });
  $('board').append(button);
 }
}
$('new-game').addEventListener('click', newGame);
setInterval(() => { stats(); if (mode === 'review' && !current) showCard(); }, 15000);
showCard();
