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
        if (!(toggle instanceof HTMLDetailsElement)) {
            return;
        }

        toggle.open = action === "expand";
    });
});

document.addEventListener("submit", async (event) => {
    const target = event.target;
    if (!(target instanceof HTMLFormElement)) {
        return;
    }

    if (target.dataset.likeAsync !== "true") {
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
            symbol.textContent = payload.isLiked ? "♥" : "♡";
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
