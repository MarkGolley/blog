export const storageKey = 'myblog:word-garden:v1';
export const day = 86400000;
export const seeds = [
 ['serendipity', 'A fortunate discovery made by chance.', 'Finding my favourite book in a little café was pure serendipity.', 'An unexpected happy discovery.'],
 ['resilient', 'Able to recover after difficulty or change.', 'The resilient team adapted when the plan changed.', 'Picture a spring bouncing back.'],
 ['eloquent', 'Expressing ideas clearly and persuasively.', 'Her eloquent speech made a complex idea feel simple.', 'Words that flow and persuade.'],
 ['ephemeral', 'Lasting for only a short time.', 'The morning mist had an ephemeral beauty.', 'Think of a soap bubble.'],
 ['nuance', 'A subtle difference in meaning, feeling, or expression.', 'The translation missed a nuance of the original joke.', 'A small shade of difference.'],
 ['pragmatic', 'Dealing with things in a practical, realistic way.', 'We took a pragmatic approach and fixed the essentials first.', 'Practical over perfect.'],
 ['inquisitive', 'Curious and eager to learn or understand.', 'The inquisitive child asked how the stars formed.', 'Always asking why.'],
 ['meticulous', 'Showing great care and attention to detail.', 'His meticulous notes made the experiment easy to repeat.', 'Picture checking every tiny detail.'],
 ['lucid', 'Clear and easy to understand.', 'The guide gave a lucid explanation of the process.', 'Like looking through clear glass.'],
 ['tenacious', 'Persistent and unwilling to give up.', 'The tenacious climber kept working towards the summit.', 'Hold on tightly to your goal.'],
 ['ubiquitous', 'Present or found almost everywhere.', 'Smartphones have become ubiquitous.', 'Everywhere you look.'],
 ['juxtapose', 'To place things side by side to highlight their differences.', 'The exhibition juxtaposes old photographs with new ones.', 'Just put them next to each other.']
].map(([term, meaning, example, hint], i) => ({id: `starter-${i}`, term, meaning, example, hint, due: 0, interval: 0, reviews: 0}));

export function schedule(word, rating, now = Date.now()) {
 if (!['again', 'good', 'easy'].includes(rating)) throw new Error('Unknown review rating.');
 const interval = rating === 'again' ? 0 : Math.min(365, rating === 'easy' ? Math.max(3, word.interval * 3) : Math.max(1, word.interval * 2));
 return {...word, interval, reviews: word.reviews + 1, due: now + (rating === 'again' ? 60000 : interval * day)};
}

export function validateBackup(data) {
 if (data?.version !== 1 || !Array.isArray(data.words) || data.words.length > 2000) throw new Error('Choose a WordGarden version 1 backup with up to 2,000 words.');
 const ids = new Set();
 const terms = new Set();
 const words = data.words.map(w => {
  if (!w || typeof w.id !== 'string' || !w.id || w.id.length > 100 || ids.has(w.id)) throw new Error('The backup contains an invalid or duplicate word ID.');
  const result = {id: w.id};
  for (const [key, max] of [['term',100],['meaning',500],['example',500],['hint',300]]) {
   if (typeof w[key] !== 'string' || w[key].length > max || (['term','meaning'].includes(key) && !w[key].trim())) throw new Error('The backup contains an invalid word or meaning.');
   result[key] = w[key].trim();
  }
  const normalized = result.term.toLocaleLowerCase();
  if (terms.has(normalized)) throw new Error('The backup contains duplicate words.');
  for (const [key,max] of [['due',8640000000000000],['interval',365],['reviews',1000000000]]) {
   if (!Number.isSafeInteger(w[key]) || w[key] < 0 || w[key] > max) throw new Error('The backup contains invalid review progress.');
   result[key] = w[key];
  }
  ids.add(w.id); terms.add(normalized);
  return result;
 });
 return words;
}

export function shuffled(items) {
 const result = [...items];
 for (let i = result.length - 1; i > 0; i--) {
  const j = Math.floor(Math.random() * (i + 1));
  [result[i], result[j]] = [result[j], result[i]];
 }
 return result;
}
