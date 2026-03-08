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
