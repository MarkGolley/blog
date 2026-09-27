(() => {
    'use strict';
    const root = document.querySelector('[data-model-replay]');
    if (!root) return;
    const status = root.querySelector('[data-replay-status]');
    const controls = root.querySelector('[data-replay-controls]');
    const comparison = root.querySelector('[data-comparison]');
    const repeat = root.querySelector('[data-repetition]');
    const seek = root.querySelector('[data-seek]');
    const play = root.querySelector('[data-play]');
    const panels = [...root.querySelectorAll('[data-replay-panel]')];
    const number = value => Number(value).toLocaleString('en-GB');
    let data, pair = [], duration = 0, position = 0, frame = 0, last = 0, running = false;

    function pause() {
        running = false;
        cancelAnimationFrame(frame);
        play.textContent = 'Play recording';
    }

    function render() {
        seek.value = position;
        root.querySelector('[data-clock]').textContent = `${position.toFixed(1)} / ${duration.toFixed(1)} recorded seconds`;
        pair.forEach((trial, index) => {
            const panel = panels[index];
            panel.querySelector('[data-model]').textContent = `${trial.model} / ${trial.effort}`;
            let elapsed = 0;
            const done = trial.attempts.filter(attempt => {
                elapsed += attempt.elapsed_seconds;
                return elapsed <= position + 0.001;
            });
            const latest = done.at(-1);
            panel.querySelector('[data-state]').textContent = latest
                ? `${done.length} attempt${done.length === 1 ? '' : 's'} recorded; ${latest.result.passed_count}/${latest.result.total} checks passed`
                : 'Waiting for the first recorded response';
            const sum = field => done.reduce((total, a) => total + (a.usage?.[field] ?? 0), 0);
            panel.querySelector('[data-tokens]').textContent = latest ? number(sum('total_tokens')) : 'Not reported yet';
            panel.querySelector('[data-input]').textContent = latest ? number(sum('input_tokens')) : '-';
            panel.querySelector('[data-output]').textContent = latest ? number(sum('output_tokens')) : '-';
            panel.querySelector('[data-reasoning]').textContent = latest
                ? number(done.reduce((total, a) => total + (a.usage?.output_tokens_details?.reasoning_tokens ?? 0), 0)) : '-';
            panel.querySelector('[data-cost]').textContent = latest
                ? `$${done.reduce((total, a) => total + a.estimated_cost_usd, 0).toFixed(5)}` : '-';
            panel.querySelector('[data-code]').textContent = latest?.code || 'The submitted fix appears when its response completes.';
            panel.querySelector('[data-feedback]').textContent = latest
                ? (latest.result.passed ? 'All fixed tests passed; input was unchanged.' : JSON.stringify(latest.result.failures, null, 2))
                : 'No test result recorded at this point.';
        });
    }

    function choose() {
        pause();
        const repetition = Number(repeat.value);
        const ids = comparison.value === 'models'
            ? [['gpt-6-sol', 'low'], ['gpt-6-astra', 'low']]
            : [['gpt-6-sol', 'low'], ['gpt-6-sol', 'high']];
        pair = ids.map(([model, effort]) => data.trials.find(t => t.model === model && t.effort === effort && t.repetition === repetition));
        if (pair.some(t => !t)) throw new Error('Incomplete recording');
        duration = Math.max(...pair.map(t => t.attempts.reduce((total, a) => total + a.elapsed_seconds, 0)));
        seek.max = duration;
        position = 0;
        render();
        status.textContent = 'Recording ready. Both timelines start at zero; the original requests ran sequentially.';
    }

    function tick(now) {
        if (!running) return;
        position = Math.min(duration, position + (now - last) / 1000 * 4);
        last = now;
        render();
        if (position >= duration) {
            pause();
            status.textContent = 'Recording complete. Results include every attempt in these two trials.';
        } else frame = requestAnimationFrame(tick);
    }

    play.addEventListener('click', () => {
        if (running) { pause(); status.textContent = 'Recording paused.'; return; }
        if (position >= duration) position = 0;
        running = true;
        last = performance.now();
        play.textContent = 'Pause recording';
        status.textContent = 'Playing at 4x speed. Token totals update only when recorded responses finish.';
        frame = requestAnimationFrame(tick);
    });
    root.querySelector('[data-finish]').addEventListener('click', () => {
        pause(); position = duration; render(); status.textContent = 'Showing complete results.';
    });
    root.querySelector('[data-reset]').addEventListener('click', () => {
        pause(); position = 0; render(); status.textContent = 'Recording reset.';
    });
    seek.addEventListener('input', () => {
        pause(); position = Number(seek.value); render();
        status.textContent = `Recording paused at ${position.toFixed(1)} recorded seconds.`;
    });
    [comparison, repeat].forEach(select => select.addEventListener('change', choose));
    fetch(root.dataset.modelReplay).then(response => {
        if (!response.ok) throw new Error('Recording could not be loaded');
        return response.json();
    }).then(recording => {
        if (recording.status !== 'complete' || recording.trials?.length !== 9 ||
            recording.trials.some(t => !t.attempts?.length || t.attempts.some(a => !a.usage || a.estimated_cost_usd == null))) {
            throw new Error('Recording is incomplete');
        }
        data = recording;
        choose();
        controls.hidden = false;
        root.querySelector('[data-replay-panels]').hidden = false;
    }).catch(() => {
        controls.hidden = true;
        status.textContent = 'The replay could not load. The recorded results and downloadable data below are still available.';
    });
})();
