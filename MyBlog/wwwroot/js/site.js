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
