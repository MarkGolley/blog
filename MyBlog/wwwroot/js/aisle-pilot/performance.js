(() => {
    const endpoint = "/projects/aisle-pilot/client-performance";
    const submitKey = "aislepilot:performance:submit-started";
    const setupKey = "aislepilot:performance:setup-started";
    const pantryKey = "aislepilot:performance:pantry-started";
    const hasResult = document.querySelector("[data-aislepilot-window]") instanceof HTMLElement;
    const navigation = performance.getEntriesByType("navigation")[0];
    const navigationType = navigation?.type ?? "unknown";
    const send = payload => {
        const body = JSON.stringify({ ...payload, navigationType, hasResult });
        if (navigator.sendBeacon?.(endpoint, new Blob([body], { type: "application/json" }))) return;
        void fetch(endpoint, { method: "POST", credentials: "same-origin", keepalive: true,
            headers: { "Content-Type": "application/json" }, body }).catch(() => {});
    };
    const report = (metric, valueMilliseconds) => {
        if (Number.isFinite(valueMilliseconds) && valueMilliseconds >= 0) send({ metric, valueMilliseconds });
    };
    const emitEvent = event => send({ event });
    const elapsed = key => {
        const started = Number(sessionStorage.getItem(key));
        return Number.isFinite(started) && started > 0 ? Math.max(0, Date.now() - started) : null;
    };
    window.AislePilotPerformance = Object.freeze({
        reportDuration(metric, startedAt) {
            if (Number.isFinite(startedAt)) report(metric, Math.max(0, performance.now() - startedAt));
        }
    });
    const pantryMs = elapsed(pantryKey);
    if (document.querySelector("#aislepilot-pantry-title") && pantryMs !== null) {
        report("pantry_latency", pantryMs);
        sessionStorage.removeItem(pantryKey);
    }
    document.addEventListener("submit", event => {
        const action = (event.submitter?.formAction || event.target?.action || "").toLowerCase();
        if (action.includes("suggest-from-pantry") || action.includes("swap-pantry-suggestion")) {
            sessionStorage.setItem(pantryKey, Date.now());
        }
    });
    if (navigation) {
        report("ttfb", navigation.responseStart);
        report("page_usable", performance.now());
    }
    const submitMs = elapsed(submitKey);
    if (hasResult && submitMs !== null) {
        report("submit_to_plan_visible", submitMs);
        sessionStorage.removeItem(submitKey);
        sessionStorage.removeItem(setupKey);
    }
    const form = document.querySelector("#aislepilot-setup-form");
    if (form instanceof HTMLFormElement) {
        const markStarted = () => {
            if (!sessionStorage.getItem(setupKey)) {
                sessionStorage.setItem(setupKey, Date.now());
                emitEvent("setup_started");
            }
        };
        form.addEventListener("input", markStarted, { once: true });
        form.addEventListener("change", markStarted, { once: true });
        form.addEventListener("submit", () => {
            const setupMs = elapsed(setupKey);
            if (setupMs !== null) report("setup_to_submit", setupMs);
            sessionStorage.setItem(submitKey, Date.now());
            emitEvent("setup_submitted");
        });
    }
    let reported = false;
    addEventListener("error", () => {
        if (!reported) {
            reported = true;
            emitEvent("client_error");
        }
    });
    addEventListener("unhandledrejection", () => emitEvent("unhandled_rejection"), { once: true });
    if (typeof PerformanceObserver !== "function") return;
    let cls = 0;
    let lcp = 0;
    let inp = 0;
    const observe = (type, callback) => {
        try {
            const observer = new PerformanceObserver(list => callback(list.getEntries()));
            observer.observe({ type, buffered: true });
        } catch { }
    };
    observe("largest-contentful-paint", entries => { lcp = entries.at(-1)?.startTime ?? lcp; });
    observe("layout-shift", entries => entries.forEach(entry => { if (!entry.hadRecentInput) cls += entry.value; }));
    observe("event", entries => entries.forEach(entry => { inp = Math.max(inp, entry.duration || 0); }));
    const reportVitals = () => {
        if (lcp > 0) report("lcp", lcp);
        report("cls", cls);
        if (inp > 0) report("inp", inp);
    };
    addEventListener("pagehide", () => {
        reportVitals();
        if (!hasResult && sessionStorage.getItem(setupKey) && !sessionStorage.getItem(submitKey)) {
            emitEvent("setup_abandoned");
            sessionStorage.removeItem(setupKey);
        }
    }, { once: true });
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "hidden") reportVitals();
    }, { once: true });
})();
