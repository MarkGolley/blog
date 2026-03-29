(() => {
    const setupModeStorageKey = "aislepilot:setup-mode";

    const wireSetupModeSwitches = scope => {
        const switches = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-setup-mode-switch]"))
            : Array.from(document.querySelectorAll("[data-setup-mode-switch]"));

        switches.forEach(modeSwitch => {
            if (!(modeSwitch instanceof HTMLElement) || modeSwitch.dataset.setupModeWired === "true") {
                return;
            }

            const buttons = Array.from(modeSwitch.querySelectorAll("[data-setup-mode-toggle]"))
                .filter(button => button instanceof HTMLButtonElement);
            if (buttons.length === 0) {
                return;
            }

            const ownerForm = modeSwitch.closest("form");
            const panelScope = ownerForm instanceof HTMLFormElement ? ownerForm : document;
            const panels = Array.from(panelScope.querySelectorAll("[data-setup-mode-panel]"))
                .filter(panel => panel instanceof HTMLElement);
            if (panels.length === 0) {
                return;
            }
            const modeSubmitButtons = Array.from(panelScope.querySelectorAll("button[type='submit'][data-setup-mode-submit]"))
                .filter(button => button instanceof HTMLButtonElement);
            const modeValueInput = panelScope.querySelector("[data-setup-mode-value]");
            const modeIds = buttons
                .map(button => button.dataset.setupModeToggle?.trim() ?? "")
                .filter(mode => mode.length > 0);

            const resolveMode = mode => {
                const normalizedMode = typeof mode === "string" ? mode.trim() : "";
                if (normalizedMode.length > 0 && panels.some(panel => panel.dataset.setupModePanel === normalizedMode)) {
                    return normalizedMode;
                }

                const fallbackMode = buttons[0]?.dataset.setupModeToggle?.trim() ?? "";
                return fallbackMode;
            };

            const applyMode = (mode, options = {}) => {
                const nextMode = resolveMode(mode);
                if (!nextMode) {
                    return;
                }
                const shouldPersist = options.persist !== false;

                buttons.forEach(button => {
                    if (!(button instanceof HTMLButtonElement)) {
                        return;
                    }

                    const isActive = button.dataset.setupModeToggle === nextMode;
                    button.classList.toggle("is-active", isActive);
                    button.setAttribute("aria-selected", isActive ? "true" : "false");
                    button.setAttribute("tabindex", isActive ? "0" : "-1");
                });

                panels.forEach(panel => {
                    if (!(panel instanceof HTMLElement)) {
                        return;
                    }

                    const isActive = panel.dataset.setupModePanel === nextMode;
                    if (isActive) {
                        panel.removeAttribute("hidden");
                        panel.setAttribute("aria-hidden", "false");
                    } else {
                        panel.setAttribute("hidden", "hidden");
                        panel.setAttribute("aria-hidden", "true");
                    }
                });

                if (modeValueInput instanceof HTMLInputElement) {
                    modeValueInput.value = nextMode;
                }

                if (shouldPersist) {
                    try {
                        window.localStorage.setItem(setupModeStorageKey, nextMode);
                    } catch {
                        // Ignore storage failures in private modes.
                    }
                }

                modeSwitch.dispatchEvent(new CustomEvent("aislepilot:setup-mode-change", {
                    bubbles: true,
                    detail: { mode: nextMode }
                }));
            };

            buttons.forEach(button => {
                if (!(button instanceof HTMLButtonElement) || button.dataset.setupModeToggleWired === "true") {
                    return;
                }

                button.dataset.setupModeToggleWired = "true";
                button.addEventListener("click", () => {
                    applyMode(button.dataset.setupModeToggle);
                });
                button.addEventListener("keydown", event => {
                    const currentIndex = modeIds.findIndex(mode => mode === button.dataset.setupModeToggle);
                    if (currentIndex < 0) {
                        return;
                    }

                    let targetIndex = -1;
                    switch (event.key) {
                        case "ArrowLeft":
                        case "ArrowUp":
                            targetIndex = (currentIndex - 1 + modeIds.length) % modeIds.length;
                            break;
                        case "ArrowRight":
                        case "ArrowDown":
                            targetIndex = (currentIndex + 1) % modeIds.length;
                            break;
                        case "Home":
                            targetIndex = 0;
                            break;
                        case "End":
                            targetIndex = modeIds.length - 1;
                            break;
                        default:
                            break;
                    }

                    if (targetIndex < 0 || targetIndex === currentIndex) {
                        return;
                    }

                    event.preventDefault();
                    const targetMode = modeIds[targetIndex];
                    const targetButton = buttons.find(candidate => candidate.dataset.setupModeToggle === targetMode);
                    applyMode(targetMode);
                    if (targetButton instanceof HTMLButtonElement) {
                        targetButton.focus();
                    }
                });
            });

            if (ownerForm instanceof HTMLFormElement && ownerForm.dataset.setupModeSubmitWired !== "true") {
                ownerForm.dataset.setupModeSubmitWired = "true";
                const skippedInputTypes = new Set(["submit", "button", "checkbox", "radio", "range", "file"]);
                ownerForm.addEventListener("keydown", event => {
                    if (event.defaultPrevented || event.key !== "Enter") {
                        return;
                    }

                    const target = event.target;
                    if (!(target instanceof HTMLElement)) {
                        return;
                    }

                    if (
                        target instanceof HTMLTextAreaElement ||
                        target instanceof HTMLButtonElement ||
                        target instanceof HTMLSelectElement
                    ) {
                        return;
                    }

                    if (target instanceof HTMLInputElement) {
                        if (skippedInputTypes.has(target.type.toLowerCase())) {
                            return;
                        }
                    }

                    const activePanel = panels.find(panel => !panel.hasAttribute("hidden"));
                    if (!(activePanel instanceof HTMLElement) || !activePanel.contains(target)) {
                        return;
                    }

                    const activeMode = activePanel.dataset.setupModePanel?.trim();
                    if (!activeMode) {
                        return;
                    }

                    const activeSubmitButton = modeSubmitButtons.find(button =>
                        button.dataset.setupModeSubmit?.trim() === activeMode);
                    if (!(activeSubmitButton instanceof HTMLButtonElement)) {
                        return;
                    }

                    event.preventDefault();
                    activeSubmitButton.click();
                });
            }

            const visibleMode = panels
                .find(panel => panel instanceof HTMLElement && !panel.hasAttribute("hidden"))
                ?.dataset.setupModePanel;
            const defaultMode = modeSwitch.dataset.setupModeDefault;
            const shouldForceDefaultMode = modeSwitch.dataset.setupModeForceDefault === "true";
            let storedMode = null;
            if (!shouldForceDefaultMode) {
                try {
                    storedMode = window.localStorage.getItem(setupModeStorageKey);
                } catch {
                    storedMode = null;
                }
            }

            applyMode(visibleMode ?? storedMode ?? defaultMode, { persist: false });
            modeSwitch.dataset.setupModeWired = "true";
        });
    };

    window.AislePilotSetupMode = Object.freeze({
        wireSetupModeSwitches
    });
})();
