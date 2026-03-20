document.addEventListener("DOMContentLoaded", () => {
    initializeQueryLab();
    initializeChunkLab();
    initializePromptRemixLab();
});

function initializeQueryLab() {
    const input = document.getElementById("lab-query-input");
    const mode = document.getElementById("lab-query-mode");
    const runButton = document.getElementById("lab-query-run");
    const randomButton = document.getElementById("lab-query-random");
    const sourceList = document.getElementById("lab-query-sources");
    const answerNode = document.getElementById("lab-query-answer");

    if (
        !(input instanceof HTMLTextAreaElement)
        || !(mode instanceof HTMLSelectElement)
        || !(runButton instanceof HTMLButtonElement)
        || !(randomButton instanceof HTMLButtonElement)
        || !(sourceList instanceof HTMLOListElement)
        || !(answerNode instanceof HTMLElement)
    ) {
        return;
    }

    const corpus = [
        {
            title: "Predeploy checks for ASP.NET Core",
            summary: "Covers test gates, mobile E2E checks, and smoke probes before release.",
            keywords: ["deployment", "checks", "mobile", "e2e", "asp.net", "release", "quality"]
        },
        {
            title: "Designing a cited RAG assistant",
            summary: "Explains ingestion, chunking, retrieval ranking, and citation-first answers.",
            keywords: ["rag", "retrieval", "citation", "chunking", "query", "vector", "ranking"]
        },
        {
            title: "Integration testing patterns",
            summary: "Focuses on stable tests, fixture setup, and production-like behavior checks.",
            keywords: ["integration", "testing", "fixtures", "stability", "reliability"]
        },
        {
            title: "Operational reliability loop",
            summary: "Tracks key SLIs, detects regressions, and keeps rollback steps ready.",
            keywords: ["sli", "latency", "errors", "rollback", "operations", "observability"]
        },
        {
            title: "Writing useful technical explanations",
            summary: "Shows how to tailor explanations for recruiter, junior, and senior audiences.",
            keywords: ["communication", "audience", "recruiter", "learning", "clarity"]
        }
    ];

    const surprisePrompts = [
        "How should I structure a deploy gate for my portfolio app?",
        "What is a simple way to evaluate a RAG system?",
        "How do I explain testing strategy to a recruiter?",
        "What tradeoffs matter most for retrieval quality?"
    ];

    const run = () => {
        const rawQuestion = input.value.trim();
        const question = rawQuestion.length > 0 ? rawQuestion : "How do we improve release reliability?";
        const selectedMode = mode.value;
        const tokens = tokenize(question);
        const ranked = rankSources(tokens, corpus, selectedMode).slice(0, 3);

        sourceList.innerHTML = "";
        ranked.forEach((entry) => {
            const li = document.createElement("li");
            const title = document.createElement("span");
            title.className = "ai-source-title";
            title.innerHTML = `<span>${escapeHtml(entry.source.title)}</span><span class="ai-score-chip">Score ${entry.score}</span>`;

            const meta = document.createElement("span");
            meta.className = "ai-source-meta";
            meta.textContent = entry.source.summary;

            li.appendChild(title);
            li.appendChild(meta);
            sourceList.appendChild(li);
        });

        answerNode.textContent = synthesizeAnswer(question, ranked, selectedMode);
    };

    runButton.addEventListener("click", run);
    randomButton.addEventListener("click", () => {
        const pick = surprisePrompts[Math.floor(Math.random() * surprisePrompts.length)];
        input.value = pick;
        run();
    });

    run();
}

function initializeChunkLab() {
    const textInput = document.getElementById("lab-chunk-input");
    const sizeSlider = document.getElementById("lab-chunk-size");
    const overlapSlider = document.getElementById("lab-chunk-overlap");
    const sizeValue = document.getElementById("lab-chunk-size-value");
    const overlapValue = document.getElementById("lab-chunk-overlap-value");
    const summary = document.getElementById("lab-chunk-summary");
    const results = document.getElementById("lab-chunk-results");

    if (
        !(textInput instanceof HTMLTextAreaElement)
        || !(sizeSlider instanceof HTMLInputElement)
        || !(overlapSlider instanceof HTMLInputElement)
        || !(sizeValue instanceof HTMLElement)
        || !(overlapValue instanceof HTMLElement)
        || !(summary instanceof HTMLElement)
        || !(results instanceof HTMLElement)
    ) {
        return;
    }

    const render = () => {
        const documentText = textInput.value.trim();
        const chunkSize = Number.parseInt(sizeSlider.value, 10);
        const overlap = Number.parseInt(overlapSlider.value, 10);
        const safeOverlap = Math.min(overlap, chunkSize - 20);

        sizeValue.textContent = String(chunkSize);
        overlapValue.textContent = String(safeOverlap);
        overlapSlider.value = String(safeOverlap);

        const chunks = splitIntoChunks(documentText, chunkSize, safeOverlap);
        const tokenEstimate = Math.max(1, Math.round(documentText.length / 4));
        summary.textContent =
            `${chunks.length} chunks from ${documentText.length} characters (~${tokenEstimate} tokens estimated).`;

        results.innerHTML = "";
        chunks.forEach((chunk, index) => {
            const card = document.createElement("article");
            card.className = "ai-chunk-item";
            card.innerHTML = `
                <p class="ai-chunk-label">Chunk ${index + 1} (${chunk.start}-${chunk.end})</p>
                <p class="ai-chunk-copy">${escapeHtml(chunk.text)}</p>
            `;
            results.appendChild(card);
        });
    };

    textInput.addEventListener("input", render);
    sizeSlider.addEventListener("input", render);
    overlapSlider.addEventListener("input", render);
    render();
}

function initializePromptRemixLab() {
    const intentInput = document.getElementById("lab-remix-intent");
    const audienceSelect = document.getElementById("lab-remix-audience");
    const toneSelect = document.getElementById("lab-remix-tone");
    const constraintToggle = document.getElementById("lab-remix-constraint");
    const generateButton = document.getElementById("lab-remix-generate");
    const results = document.getElementById("lab-remix-results");

    if (
        !(intentInput instanceof HTMLInputElement)
        || !(audienceSelect instanceof HTMLSelectElement)
        || !(toneSelect instanceof HTMLSelectElement)
        || !(constraintToggle instanceof HTMLInputElement)
        || !(generateButton instanceof HTMLButtonElement)
        || !(results instanceof HTMLElement)
    ) {
        return;
    }

    const openers = {
        recruiter: "Frame the impact and measurable outcomes.",
        junior: "Teach this with plain language and one concrete example.",
        lead: "Describe tradeoffs, failure modes, and rollout considerations."
    };

    const toneHints = {
        direct: "Keep it concise and skip filler.",
        playful: "Use clear language with a light, energetic tone.",
        analytical: "Use structured reasoning with explicit assumptions."
    };

    const render = () => {
        const intent = intentInput.value.trim().length > 0
            ? intentInput.value.trim()
            : "Explain the concept clearly.";
        const audience = audienceSelect.value;
        const tone = toneSelect.value;
        const includeConstraint = constraintToggle.checked;

        const hardConstraint = includeConstraint
            ? "Constraint: include one real failure case and one measurable metric."
            : "";

        const prompts = [
            `${openers[audience]} ${toneHints[tone]} Task: ${intent} ${hardConstraint}`.trim(),
            `You are helping a ${audience}. ${toneHints[tone]} Goal: ${intent} ${hardConstraint}`.trim(),
            `Rewrite this for a ${audience} audience. ${intent} ${toneHints[tone]} ${hardConstraint}`.trim()
        ];

        results.innerHTML = "";
        prompts.forEach((prompt, index) => {
            const item = document.createElement("article");
            item.className = "ai-remix-item";
            item.innerHTML = `<p><strong>Variant ${index + 1}:</strong> ${escapeHtml(prompt)}</p>`;
            results.appendChild(item);
        });
    };

    generateButton.addEventListener("click", render);
    intentInput.addEventListener("input", render);
    audienceSelect.addEventListener("change", render);
    toneSelect.addEventListener("change", render);
    constraintToggle.addEventListener("change", render);
    render();
}

function tokenize(input) {
    return input
        .toLowerCase()
        .replace(/[^a-z0-9\s]/g, " ")
        .split(/\s+/)
        .filter((token) => token.length > 2);
}

function rankSources(tokens, corpus, mode) {
    const broadBonus = mode === "broad" ? 2 : 0;
    const preciseBoost = mode === "precise" ? 2 : 1;

    return corpus
        .map((source) => {
            const haystack = `${source.title} ${source.summary} ${source.keywords.join(" ")}`.toLowerCase();
            let score = 0;

            tokens.forEach((token) => {
                if (source.keywords.includes(token)) {
                    score += 3 * preciseBoost;
                }

                if (haystack.includes(token)) {
                    score += 2;
                }
            });

            if (tokens.length === 0) {
                score += 1;
            }

            score += broadBonus + Math.floor(Math.random() * (mode === "broad" ? 2 : 1));
            return { source, score };
        })
        .sort((a, b) => b.score - a.score);
}

function synthesizeAnswer(question, ranked, mode) {
    const top = ranked[0]?.source;
    const second = ranked[1]?.source;
    if (!top) {
        return "Add a question to generate a retrieval preview.";
    }

    const modeNote = mode === "precise"
        ? "Prioritizing exact matches and tighter grounding."
        : mode === "broad"
            ? "Widening recall to surface adjacent sources."
            : "Balancing precision and recall for a practical first pass.";

    const secondTitle = second ? ` and ${second.title}` : "";
    return `${modeNote} For "${question}", start with ${top.title}${secondTitle}. ` +
        `Draft an answer from these sources, then verify each claim with citation checks before final output.`;
}

function splitIntoChunks(input, size, overlap) {
    if (!input) {
        return [];
    }

    const step = Math.max(1, size - overlap);
    const chunks = [];
    let start = 0;

    while (start < input.length) {
        const end = Math.min(input.length, start + size);
        const chunkText = input.slice(start, end).trim();
        if (chunkText.length > 0) {
            chunks.push({
                start,
                end,
                text: chunkText
            });
        }

        if (end >= input.length) {
            break;
        }

        start += step;
    }

    return chunks;
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
}
