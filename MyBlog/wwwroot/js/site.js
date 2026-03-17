document.addEventListener("DOMContentLoaded", () => {
    const navToggle = document.querySelector("[data-nav-toggle]");
    const navMenu = document.querySelector("[data-nav-menu]");
    if (navToggle instanceof HTMLButtonElement && navMenu instanceof HTMLElement) {
        const closeNav = () => {
            navMenu.classList.remove("is-open");
            navToggle.setAttribute("aria-expanded", "false");
        };

        navToggle.addEventListener("click", () => {
            const willOpen = !navMenu.classList.contains("is-open");
            navMenu.classList.toggle("is-open", willOpen);
            navToggle.setAttribute("aria-expanded", String(willOpen));
        });

        document.addEventListener("click", (event) => {
            if (!(event.target instanceof Node)) {
                return;
            }

            if (navMenu.contains(event.target) || navToggle.contains(event.target)) {
                return;
            }

            closeNav();
        });

        window.addEventListener("resize", () => {
            if (window.innerWidth > 800) {
                closeNav();
            }
        });
    }

    const navCapsule = document.querySelector("[data-nav-capsule]");
    if (navCapsule instanceof HTMLDetailsElement) {
        const todayContent = navCapsule.querySelector("[data-nav-capsule-content='today']");
        const yesterdayContent = navCapsule.querySelector("[data-nav-capsule-content='yesterday']");
        const prevButton = navCapsule.querySelector("[data-nav-capsule-prev]");
        const nextButton = navCapsule.querySelector("[data-nav-capsule-next]");
        const dayLabel = navCapsule.querySelector("[data-nav-capsule-day-label]");

        if (
            todayContent instanceof HTMLElement
            && yesterdayContent instanceof HTMLElement
            && prevButton instanceof HTMLButtonElement
            && nextButton instanceof HTMLButtonElement
            && dayLabel instanceof HTMLElement
        ) {
            const showDay = (day) => {
                const showYesterday = day === "yesterday";
                todayContent.hidden = showYesterday;
                yesterdayContent.hidden = !showYesterday;
                prevButton.hidden = showYesterday;
                nextButton.hidden = !showYesterday;
                dayLabel.textContent = showYesterday ? "Yesterday" : "Today";
            };

            prevButton.addEventListener("click", (event) => {
                event.preventDefault();
                showDay("yesterday");
            });

            nextButton.addEventListener("click", (event) => {
                event.preventDefault();
                showDay("today");
            });

            navCapsule.addEventListener("toggle", () => {
                if (!navCapsule.open) {
                    showDay("today");
                }
            });
        }

        document.addEventListener("click", (event) => {
            if (!(event.target instanceof Node)) {
                return;
            }

            if (navCapsule.contains(event.target)) {
                return;
            }

            navCapsule.open = false;
        });

        window.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                navCapsule.open = false;
            }
        });
    }

    const progressBar = document.querySelector("[data-scroll-progress]");
    if (progressBar instanceof HTMLElement) {
        const updateProgress = () => {
            const documentHeight = document.documentElement.scrollHeight - window.innerHeight;
            const progress = documentHeight > 0 ? (window.scrollY / documentHeight) * 100 : 0;
            progressBar.style.width = `${Math.min(Math.max(progress, 0), 100)}%`;
        };

        window.addEventListener("scroll", updateProgress, { passive: true });
        window.addEventListener("resize", updateProgress);
        updateProgress();
    }

    const revealItems = Array.from(document.querySelectorAll("[data-reveal]"));
    if (revealItems.length > 0) {
        const reducedMotion =
            typeof window.matchMedia === "function"
            && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        const canUseIntersectionObserver = typeof window.IntersectionObserver === "function";

        if (reducedMotion || !canUseIntersectionObserver) {
            revealItems.forEach((item) => item.classList.add("is-visible"));
        } else {
            document.documentElement.classList.add("reveal-ready");

            try {
                const observer = new IntersectionObserver(
                    (entries, revealObserver) => {
                        entries.forEach((entry) => {
                            if (!entry.isIntersecting) {
                                return;
                            }

                            entry.target.classList.add("is-visible");
                            revealObserver.unobserve(entry.target);
                        });
                    },
                    {
                        // Keep reveal usable for very tall sections (e.g., long blog posts on mobile).
                        threshold: 0,
                        rootMargin: "0px 0px -10% 0px"
                    }
                );

                revealItems.forEach((item) => observer.observe(item));
            } catch {
                document.documentElement.classList.remove("reveal-ready");
                revealItems.forEach((item) => item.classList.add("is-visible"));
            }
        }
    }

    const capsuleCountdown = document.querySelector("[data-daily-capsule-countdown='true']");
    if (capsuleCountdown instanceof HTMLElement) {
        const nextResetUtcIso = capsuleCountdown.dataset.nextResetUtc;
        const nextResetUtcMs = typeof nextResetUtcIso === "string" ? Date.parse(nextResetUtcIso) : Number.NaN;

        if (!Number.isNaN(nextResetUtcMs)) {
            let intervalHandle = 0;
            const renderCountdown = () => {
                const millisecondsRemaining = nextResetUtcMs - Date.now();
                if (millisecondsRemaining <= 0) {
                    capsuleCountdown.textContent = "refreshing...";
                    if (intervalHandle !== 0) {
                        window.clearInterval(intervalHandle);
                    }

                    window.setTimeout(() => window.location.reload(), 1400);
                    return;
                }

                const totalSeconds = Math.floor(millisecondsRemaining / 1000);
                const hours = Math.floor(totalSeconds / 3600);
                const minutes = Math.floor((totalSeconds % 3600) / 60);
                const seconds = totalSeconds % 60;
                capsuleCountdown.textContent =
                    `${String(hours).padStart(2, "0")}h ${String(minutes).padStart(2, "0")}m ${String(seconds).padStart(2, "0")}s`;
            };

            renderCountdown();
            intervalHandle = window.setInterval(renderCountdown, 1000);
        } else {
            capsuleCountdown.textContent = "unavailable";
        }
    }
});

document.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
        return;
    }

    const action = target.getAttribute("data-thread-action");
    if (!action) {
        return;
    }

    const threadToggles = document.querySelectorAll("details[data-thread-toggle]");
    threadToggles.forEach((toggle) => {
        if (toggle instanceof HTMLDetailsElement) {
            toggle.open = action === "expand";
        }
    });
});

document.addEventListener("submit", async (event) => {
    const target = event.target;
    if (!(target instanceof HTMLFormElement) || target.dataset.refreshAntiforgery !== "true") {
        return;
    }

    if (target.dataset.antiforgeryRefreshed === "true") {
        return;
    }

    const refreshUrl = target.dataset.antiforgeryRefreshUrl;
    if (!refreshUrl) {
        return;
    }

    event.preventDefault();

    const submitter = event.submitter instanceof HTMLElement ? event.submitter : target.querySelector("button[type='submit']");
    const disableTarget =
        submitter instanceof HTMLButtonElement || submitter instanceof HTMLInputElement
            ? submitter
            : null;

    let didSubmit = false;
    if (disableTarget) {
        disableTarget.disabled = true;
    }

    try {
        const response = await fetch(refreshUrl, {
            method: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            credentials: "same-origin",
            cache: "no-store"
        });

        if (response.ok) {
            const payload = await response.json();
            const token = typeof payload?.token === "string" ? payload.token : "";

            if (token.length > 0) {
                let tokenInput = target.querySelector("input[name='__RequestVerificationToken']");
                if (!(tokenInput instanceof HTMLInputElement)) {
                    tokenInput = document.createElement("input");
                    tokenInput.type = "hidden";
                    tokenInput.name = "__RequestVerificationToken";
                    target.appendChild(tokenInput);
                }

                tokenInput.value = token;
                target.dataset.antiforgeryRefreshed = "true";
            }
        }

        didSubmit = true;
        target.submit();
    } catch {
        // Fall back to the original submit path if token refresh fails.
        didSubmit = true;
        target.submit();
    } finally {
        if (!didSubmit && disableTarget) {
            disableTarget.disabled = false;
        }
    }
});

document.addEventListener("submit", async (event) => {
    const target = event.target;
    if (!(target instanceof HTMLFormElement) || target.dataset.likeAsync !== "true") {
        return;
    }

    event.preventDefault();

    const button = target.querySelector(".like-btn");
    if (!(button instanceof HTMLButtonElement)) {
        return;
    }

    try {
        button.disabled = true;

        const response = await fetch(target.action, {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: new FormData(target),
            credentials: "same-origin"
        });

        if (!response.ok) {
            return;
        }

        const payload = await response.json();
        if (!payload || payload.success !== true) {
            return;
        }

        const symbol = button.querySelector(".like-symbol");
        const count = button.querySelector(".like-count");

        if (symbol instanceof HTMLElement) {
            symbol.textContent = payload.isLiked ? "\u2665" : "\u2661";
        }

        if (count instanceof HTMLElement) {
            count.textContent = String(payload.count ?? 0);
        }

        button.classList.toggle("liked", payload.isLiked === true);
        button.setAttribute("aria-label", payload.isLiked ? "Unlike this post" : "Like this post");
    } finally {
        button.disabled = false;
    }
});
