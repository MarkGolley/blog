(() => {
    const quickReplaceInputs = Array.from(
        document.querySelectorAll(
            ".aislepilot-inline-fields input:not([type='hidden']):not([type='checkbox']):not([type='radio'])"
        )
    );
    const getAislePilotForms = () => Array.from(document.querySelectorAll(".aislepilot-app form"));
    const submitLoadingDelayTimers = new WeakMap();

    const selectInputValue = input => {
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        requestAnimationFrame(() => {
            try {
                input.select();
            } catch {
                // Some input types may not support selection.
            }
        });
    };

    quickReplaceInputs.forEach(input => {
        input.addEventListener("focus", () => {
            selectInputValue(input);
        });

        input.addEventListener("click", () => {
            if (document.activeElement === input) {
                selectInputValue(input);
            }
        });
    });

    const getSubmitButton = submitEvent => {
        const submitter = submitEvent.submitter;
        if (submitter instanceof HTMLButtonElement) {
            return submitter;
        }

        const form = submitEvent.currentTarget;
        if (!(form instanceof HTMLFormElement)) {
            return null;
        }

        return form.querySelector("button[type='submit']");
    };

    const clearSubmitLoadingDelay = form => {
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        const timerId = submitLoadingDelayTimers.get(form);
        if (typeof timerId === "number") {
            window.clearTimeout(timerId);
        }

        submitLoadingDelayTimers.delete(form);
    };

    const setSubmitButtonLoadingState = submitButton => {
        const loadingLabel = submitButton.dataset.loadingLabel?.trim();
        if (loadingLabel && loadingLabel.length > 0) {
            submitButton.textContent = loadingLabel;
        } else {
            submitButton.textContent = "Loading...";
        }

        submitButton.classList.add("is-loading");
    };

    const wireSubmitLoading = form => {
        if (!(form instanceof HTMLFormElement) || form.dataset.loadingWired === "true") {
            return;
        }

        form.dataset.loadingWired = "true";
        form.addEventListener("submit", event => {
            if (!(event.currentTarget instanceof HTMLFormElement)) {
                return;
            }
            const targetForm = event.currentTarget;

            const submitButton = getSubmitButton(event);
            if (!(submitButton instanceof HTMLButtonElement)) {
                return;
            }

            if (submitButton.classList.contains("is-loading") || targetForm.getAttribute("data-is-submitting") === "true") {
                event.preventDefault();
                return;
            }

            const originalLabel = submitButton.dataset.originalLabel ?? submitButton.textContent ?? "";
            submitButton.dataset.originalLabel = originalLabel.trim();
            targetForm.setAttribute("data-is-submitting", "true");
            submitButton.disabled = true;
            submitButton.setAttribute("aria-busy", "true");

            const loadingDelayRaw = submitButton.dataset.loadingDelayMs ?? targetForm.dataset.loadingDelayMs ?? "";
            const loadingDelayMs = Number.parseInt(loadingDelayRaw, 10);
            if (Number.isInteger(loadingDelayMs) && loadingDelayMs > 0) {
                clearSubmitLoadingDelay(targetForm);
                const timerId = window.setTimeout(() => {
                    if (targetForm.getAttribute("data-is-submitting") === "true") {
                        setSubmitButtonLoadingState(submitButton);
                    }
                    submitLoadingDelayTimers.delete(targetForm);
                }, loadingDelayMs);
                submitLoadingDelayTimers.set(targetForm, timerId);
                return;
            }

            setSubmitButtonLoadingState(submitButton);
        });
    };

    const wireSubmitLoadingHandlers = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();
        forms.forEach(wireSubmitLoading);
    };

    wireSubmitLoadingHandlers(document);

    const wirePlanBasicsSliders = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        const syncSliderProgress = rangeInput => {
            if (!(rangeInput instanceof HTMLInputElement)) {
                return;
            }

            const min = Number.parseFloat(rangeInput.min ?? "0");
            const max = Number.parseFloat(rangeInput.max ?? "100");
            const value = Number.parseFloat(rangeInput.value ?? "0");
            const span = max - min;
            if (!Number.isFinite(min) || !Number.isFinite(max) || !Number.isFinite(value) || span <= 0) {
                rangeInput.style.setProperty("--slider-progress", "0%");
                return;
            }

            const clamped = Math.min(max, Math.max(min, value));
            const progressPercent = ((clamped - min) / span) * 100;
            rangeInput.style.setProperty("--slider-progress", `${progressPercent}%`);
        };

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const optionSliders = Array.from(form.querySelectorAll("[data-option-slider]"));
            optionSliders.forEach(sliderField => {
                if (!(sliderField instanceof HTMLElement) || sliderField.dataset.sliderWired === "true") {
                    return;
                }

                const rangeInput = sliderField.querySelector("[data-option-slider-range]");
                const hiddenInput = sliderField.querySelector("[data-option-slider-hidden]");
                const valueOutput = sliderField.querySelector("[data-option-slider-value]");

                if (!(rangeInput instanceof HTMLInputElement) || !(hiddenInput instanceof HTMLInputElement)) {
                    return;
                }

                const optionValues = (rangeInput.dataset.optionSliderValues ?? "")
                    .split("|")
                    .map(value => value.trim())
                    .filter(value => value.length > 0);
                if (optionValues.length === 0) {
                    return;
                }

                const applyOptionValue = shouldNotify => {
                    const rawIndex = Number.parseInt(rangeInput.value ?? "0", 10);
                    const safeIndex = Number.isInteger(rawIndex)
                        ? Math.max(0, Math.min(optionValues.length - 1, rawIndex))
                        : 0;
                    const selectedValue = optionValues[safeIndex];

                    rangeInput.value = `${safeIndex}`;
                    syncSliderProgress(rangeInput);
                    rangeInput.setAttribute("aria-valuetext", selectedValue);
                    if (valueOutput instanceof HTMLElement) {
                        valueOutput.textContent = selectedValue;
                    }

                    const previousValue = hiddenInput.value;
                    hiddenInput.value = selectedValue;
                    if (shouldNotify && previousValue !== selectedValue) {
                        hiddenInput.dispatchEvent(new Event("change", { bubbles: true }));
                    }
                };

                const existingHiddenIndex = optionValues.findIndex(option =>
                    option.toLowerCase() === (hiddenInput.value ?? "").trim().toLowerCase());
                if (existingHiddenIndex >= 0) {
                    rangeInput.value = `${existingHiddenIndex}`;
                }

                sliderField.dataset.sliderWired = "true";
                rangeInput.addEventListener("input", () => {
                    applyOptionValue(true);
                });
                rangeInput.addEventListener("change", () => {
                    applyOptionValue(true);
                });

                applyOptionValue(false);
            });

            const numberSliders = Array.from(form.querySelectorAll("[data-number-slider-range]"));
            numberSliders.forEach(rangeInput => {
                if (!(rangeInput instanceof HTMLInputElement) || rangeInput.dataset.sliderWired === "true") {
                    return;
                }

                const sliderField = rangeInput.closest(".aislepilot-slider-field");
                const valueOutput = sliderField?.querySelector("[data-number-slider-value]");
                const prefix = rangeInput.dataset.numberSliderPrefix ?? "";
                const suffix = rangeInput.dataset.numberSliderSuffix ?? "";
                const parsedDecimals = Number.parseInt(rangeInput.dataset.numberSliderDecimals ?? "0", 10);
                const decimals = Number.isInteger(parsedDecimals) ? Math.max(0, Math.min(2, parsedDecimals)) : 0;

                const applyNumberValue = () => {
                    const parsed = Number.parseFloat(rangeInput.value ?? "0");
                    const normalized = Number.isFinite(parsed) ? parsed : 0;
                    const roundedValue = normalized.toFixed(decimals);
                    const displayText = `${prefix}${roundedValue}${suffix}`;

                    syncSliderProgress(rangeInput);
                    rangeInput.setAttribute("aria-valuetext", displayText);
                    if (valueOutput instanceof HTMLElement) {
                        valueOutput.textContent = displayText;
                    }
                };

                rangeInput.dataset.sliderWired = "true";
                rangeInput.addEventListener("input", applyNumberValue);
                rangeInput.addEventListener("change", applyNumberValue);
                applyNumberValue();
            });
        });
    };

    wirePlanBasicsSliders(document);

    const getSelectedSupermarket = form => {
        if (!(form instanceof HTMLFormElement)) {
            return "";
        }

        const supermarketRadioOptions = Array.from(form.querySelectorAll("[data-supermarket-option]"))
            .filter(input => input instanceof HTMLInputElement);
        if (supermarketRadioOptions.length > 0) {
            const selectedOption = supermarketRadioOptions.find(input => input.checked);
            return selectedOption instanceof HTMLInputElement ? selectedOption.value : "";
        }

        const supermarketSelect = form.querySelector("[data-supermarket-select]");
        if (supermarketSelect instanceof HTMLSelectElement || supermarketSelect instanceof HTMLInputElement) {
            return supermarketSelect.value;
        }

        return "";
    };

    const syncCustomAisleFieldVisibility = (selectedSupermarket, customAisleField, storeLayoutSection) => {
        if (!(customAisleField instanceof HTMLElement) && !(storeLayoutSection instanceof HTMLElement)) {
            return;
        }

        const isCustomSupermarket = selectedSupermarket.trim().toLowerCase() === "custom";
        if (isCustomSupermarket) {
            if (storeLayoutSection instanceof HTMLElement) {
                storeLayoutSection.removeAttribute("hidden");
                storeLayoutSection.setAttribute("aria-hidden", "false");
            }
            if (customAisleField instanceof HTMLElement) {
                customAisleField.removeAttribute("hidden");
                customAisleField.setAttribute("aria-hidden", "false");
            }
            return;
        }

        if (storeLayoutSection instanceof HTMLElement) {
            storeLayoutSection.setAttribute("hidden", "hidden");
            storeLayoutSection.setAttribute("aria-hidden", "true");
        }
        if (customAisleField instanceof HTMLElement) {
            customAisleField.setAttribute("hidden", "hidden");
            customAisleField.setAttribute("aria-hidden", "true");
        }
    };

    const wireCustomAisleFieldVisibility = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const customAisleField = form.querySelector("[data-custom-aisle-field]");
            const storeLayoutSection = form.querySelector("[data-store-layout-section]");
            if (!(customAisleField instanceof HTMLElement) && !(storeLayoutSection instanceof HTMLElement)) {
                return;
            }

            const supermarketControls = Array.from(
                form.querySelectorAll("[data-supermarket-option], [data-supermarket-select]"));
            if (supermarketControls.length === 0) {
                return;
            }

            supermarketControls.forEach(control => {
                if (!(control instanceof HTMLInputElement) && !(control instanceof HTMLSelectElement)) {
                    return;
                }

                if (control.dataset.customAisleVisibilityWired === "true") {
                    return;
                }

                control.dataset.customAisleVisibilityWired = "true";
                const syncVisibility = () => {
                    syncCustomAisleFieldVisibility(
                        getSelectedSupermarket(form),
                        customAisleField,
                        storeLayoutSection);
                };

                control.addEventListener("change", syncVisibility);
                if (control instanceof HTMLInputElement) {
                    control.addEventListener("input", syncVisibility);
                }
            });

            syncCustomAisleFieldVisibility(
                getSelectedSupermarket(form),
                customAisleField,
                storeLayoutSection);
        });
    };

    wireCustomAisleFieldVisibility(document);

    const wireNotesExportButtons = scope => {
        const shells = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-notes-export-shell]"))
            : Array.from(document.querySelectorAll("[data-notes-export-shell]"));

        const setTemporaryButtonLabel = (button, label) => {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            const defaultLabel = button.dataset.notesDefaultLabel?.trim()
                || button.dataset.originalLabel?.trim()
                || button.textContent?.trim()
                || "Share shopping list to iPhone Notes";
            button.textContent = label;

            const timeoutRaw = button.dataset.notesLabelTimeoutMs ?? "2400";
            const timeoutMs = Number.parseInt(timeoutRaw, 10);
            const safeTimeout = Number.isInteger(timeoutMs) ? Math.max(800, Math.min(6000, timeoutMs)) : 2400;

            window.setTimeout(() => {
                if (button.isConnected) {
                    button.textContent = defaultLabel;
                }
            }, safeTimeout);
        };

        const copyTextToClipboard = async text => {
            if (typeof text !== "string" || text.length === 0) {
                return false;
            }

            if (navigator.clipboard?.writeText) {
                try {
                    await navigator.clipboard.writeText(text);
                    return true;
                } catch {
                    // Fallback below.
                }
            }

            if (typeof document.execCommand !== "function") {
                return false;
            }

            const tempInput = document.createElement("textarea");
            tempInput.value = text;
            tempInput.setAttribute("readonly", "readonly");
            tempInput.style.position = "fixed";
            tempInput.style.left = "-9999px";
            tempInput.style.top = "0";
            document.body.appendChild(tempInput);
            tempInput.focus({ preventScroll: true });
            tempInput.select();
            const copied = document.execCommand("copy");
            document.body.removeChild(tempInput);
            return copied;
        };

        shells.forEach(shell => {
            if (!(shell instanceof HTMLElement)) {
                return;
            }

            const trigger = shell.querySelector("[data-notes-export-trigger]");
            const contentField = shell.querySelector("[data-notes-export-content]");
            if (!(trigger instanceof HTMLButtonElement) || !(contentField instanceof HTMLTextAreaElement)) {
                return;
            }

            if (trigger.dataset.notesExportWired === "true") {
                return;
            }

            trigger.dataset.notesExportWired = "true";
            const defaultLabel = trigger.dataset.notesDefaultLabel?.trim()
                || trigger.textContent?.trim()
                || "Share shopping list to iPhone Notes";
            trigger.dataset.originalLabel = defaultLabel;
            trigger.textContent = defaultLabel;

            trigger.addEventListener("click", async () => {
                const notesText = contentField.value?.trim() ?? "";
                if (notesText.length === 0) {
                    const failedLabel = trigger.dataset.notesFailedLabel?.trim() || "Could not prepare shopping list.";
                    setTemporaryButtonLabel(trigger, failedLabel);
                    return;
                }

                if (typeof navigator.share === "function") {
                    try {
                        await navigator.share({
                            title: "AislePilot Shopping List",
                            text: notesText
                        });
                        const sharedLabel = trigger.dataset.notesSharedLabel?.trim() || "Share sheet opened. Choose Notes.";
                        setTemporaryButtonLabel(trigger, sharedLabel);
                        return;
                    } catch (error) {
                        if (error instanceof DOMException && error.name === "AbortError") {
                            return;
                        }
                    }
                }

                const copied = await copyTextToClipboard(notesText);
                if (copied) {
                    const copiedLabel = trigger.dataset.notesCopiedLabel?.trim() || "Shopping list copied. Paste into Notes.";
                    setTemporaryButtonLabel(trigger, copiedLabel);
                    return;
                }

                const failedLabel = trigger.dataset.notesFailedLabel?.trim() || "Could not share. Try again.";
                setTemporaryButtonLabel(trigger, failedLabel);
            });
        });
    };

    wireNotesExportButtons(document);

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

    wireSetupModeSwitches(document);

    const resetSubmittingState = () => {
        getAislePilotForms().forEach(form => {
            clearSubmitLoadingDelay(form);
            form.removeAttribute("data-is-submitting");

            const buttons = Array.from(form.querySelectorAll("button[type='submit']"));
            buttons.forEach(button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                if (button.classList.contains("is-loading")) {
                    button.classList.remove("is-loading");
                }

                button.disabled = false;
                button.removeAttribute("aria-busy");

                const originalLabel = button.dataset.originalLabel?.trim();
                if (originalLabel && originalLabel.length > 0) {
                    button.textContent = originalLabel;
                }
            });
        });
    };

    window.addEventListener("pageshow", event => {
        if (event.persisted) {
            resetSubmittingState();
        }
    });

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") {
            requestAnimationFrame(() => {
                resetSubmittingState();
            });
        }
    });

    const swapScrollKey = "aislepilot:swap-scroll";
    const clearRestorePending = () => {
        document.documentElement.classList.remove("aislepilot-restore-pending");
    };
    const root = document.querySelector("[data-aislepilot-window]");
    if (!root) {
        clearRestorePending();
        return;
    }

    const setupPanel = document.querySelector("[data-setup-panel]");

    const viewport = root.querySelector("[data-window-viewport]");
    const track = root.querySelector("[data-window-track]");
    const tabs = Array.from(root.querySelectorAll("[data-window-tab]"));
    const panels = Array.from(root.querySelectorAll(".aislepilot-window-panel"));
    const tabHint = root.querySelector("[data-window-hint]");
    const tabHintSeenStorageKey = "aislepilot:tab-hint-seen";

    if (!viewport || !track || panels.length === 0) {
        return;
    }

    let currentIndex = 0;
    let touchStartX = 0;
    let touchStartY = 0;
    let activePanelResizeObserver = null;

    const hideTabHint = () => {
        if (tabHint instanceof HTMLElement) {
            tabHint.setAttribute("hidden", "hidden");
        }
    };

    const applyTabHintVisibility = () => {
        if (!(tabHint instanceof HTMLElement)) {
            return;
        }

        try {
            if (window.localStorage.getItem(tabHintSeenStorageKey) === "true") {
                hideTabHint();
            }
        } catch {
            // Ignore storage failures in private modes.
        }
    };

    const markTabHintSeen = () => {
        hideTabHint();
        try {
            window.localStorage.setItem(tabHintSeenStorageKey, "true");
        } catch {
            // Ignore storage failures in private modes.
        }
    };

    const scrollInstantly = (x, y) => {
        const rootElement = document.documentElement;
        const previousInlineBehavior = rootElement.style.scrollBehavior;
        rootElement.style.scrollBehavior = "auto";
        window.scrollTo(x, y);

        requestAnimationFrame(() => {
            rootElement.style.scrollBehavior = previousInlineBehavior;
        });
    };

    const persistSwapScrollPosition = form => {
        let targetX = window.scrollX;
        let targetY = window.scrollY;
        let anchorDayIndex = null;
        let anchorTop = null;

        if (form instanceof HTMLFormElement) {
            const capturedX = Number.parseFloat(form.dataset.swapScrollX ?? "");
            const capturedY = Number.parseFloat(form.dataset.swapScrollY ?? "");
            const capturedAt = Number.parseInt(form.dataset.swapScrollCapturedAt ?? "", 10);
            const hasRecentCapture =
                Number.isFinite(capturedX) &&
                Number.isFinite(capturedY) &&
                Number.isFinite(capturedAt) &&
                Date.now() - capturedAt <= 1500;

            if (hasRecentCapture) {
                targetX = capturedX;
                targetY = capturedY;
            }

            const dayInput = form.querySelector("input[name='dayIndex']");
            const parsedDayIndex = Number.parseInt(dayInput?.value ?? "", 10);
            if (Number.isInteger(parsedDayIndex) && parsedDayIndex >= 0) {
                anchorDayIndex = parsedDayIndex;
                anchorTop = Math.round(form.getBoundingClientRect().top);
            }
        }

        const activePanelId = panels[currentIndex]?.id ?? null;
        const payload = {
            x: targetX,
            y: targetY,
            activePanelId,
            anchorDayIndex,
            anchorTop,
            at: Date.now()
        };

        sessionStorage.setItem(swapScrollKey, JSON.stringify(payload));
    };

    const restoreSwapScrollPosition = () => {
        const raw = sessionStorage.getItem(swapScrollKey);
        if (!raw) {
            clearRestorePending();
            return false;
        }

        sessionStorage.removeItem(swapScrollKey);

        try {
            const parsed = JSON.parse(raw);
            if (!parsed || typeof parsed.y !== "number") {
                clearRestorePending();
                return false;
            }

            // Ignore stale restore requests.
            if (typeof parsed.at === "number" && Date.now() - parsed.at > 60_000) {
                clearRestorePending();
                return false;
            }

            if (typeof parsed.activePanelId === "string" && parsed.activePanelId.length > 0) {
                const panelIndex = panels.findIndex(panel => panel.id === parsed.activePanelId);
                if (panelIndex >= 0) {
                    syncUi(panelIndex, false);
                }
            }

            const targetX = typeof parsed.x === "number" ? parsed.x : 0;
            const fallbackTargetY = parsed.y;
            const anchorDayIndex = Number.isInteger(parsed.anchorDayIndex)
                ? parsed.anchorDayIndex
                : null;
            const anchorTop = typeof parsed.anchorTop === "number"
                ? parsed.anchorTop
                : null;
            const resolveTargetY = () => {
                if (anchorDayIndex === null || typeof anchorTop !== "number") {
                    return fallbackTargetY;
                }

                const selector = `.aislepilot-swap-form input[name='dayIndex'][value='${anchorDayIndex}']`;
                const targetDayInput = document.querySelector(selector);
                if (!(targetDayInput instanceof HTMLInputElement)) {
                    return fallbackTargetY;
                }

                const targetForm = targetDayInput.closest("form");
                if (!(targetForm instanceof HTMLFormElement)) {
                    return fallbackTargetY;
                }

                return window.scrollY + (targetForm.getBoundingClientRect().top - anchorTop);
            };
            const restoreIfDrifted = () => {
                const targetY = resolveTargetY();
                const xDrift = Math.abs(window.scrollX - targetX);
                const yDrift = Math.abs(window.scrollY - targetY);
                if (xDrift > 2 || yDrift > 8) {
                    scrollInstantly(targetX, targetY);
                }
            };

            root.classList.add("is-restoring-scroll");
            const restoreDeadline = Date.now() + 700;
            const restoreLoop = () => {
                restoreIfDrifted();
                if (Date.now() < restoreDeadline) {
                    requestAnimationFrame(restoreLoop);
                    return;
                }

                root.classList.remove("is-restoring-scroll");
                clearRestorePending();
            };

            requestAnimationFrame(() => {
                requestAnimationFrame(restoreLoop);
            });
            return true;
        } catch {
            // Ignore malformed session payloads.
            clearRestorePending();
            return false;
        }
    };

    const getSetupToggleButtons = () => Array.from(document.querySelectorAll("[data-setup-toggle]"));

    const syncSetupToggleState = () => {
        if (!setupPanel) {
            return;
        }

        const setupToggleButtons = getSetupToggleButtons();
        if (setupToggleButtons.length === 0) {
            return;
        }

        const isHidden = setupPanel.hasAttribute("hidden");
        setupToggleButtons.forEach(button => {
            button.textContent = isHidden ? "Edit setup" : "Hide setup";
            button.setAttribute("aria-expanded", isHidden ? "false" : "true");
        });
    };

    const wireSetupToggleHandlers = scope => {
        const setupToggleButtons = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-setup-toggle]"))
            : getSetupToggleButtons();

        setupToggleButtons.forEach(button => {
            if (!(button instanceof HTMLButtonElement) || button.dataset.setupToggleWired === "true") {
                return;
            }

            button.dataset.setupToggleWired = "true";
            button.addEventListener("click", () => {
                if (!setupPanel) {
                    return;
                }

                if (setupPanel.hasAttribute("hidden")) {
                    setupPanel.removeAttribute("hidden");
                    setupPanel.scrollIntoView({ behavior: "smooth", block: "start" });
                } else {
                    setupPanel.setAttribute("hidden", "hidden");
                }

                syncSetupToggleState();
            });
        });
    };

    const findIndexFromHash = () => {
        if (!window.location.hash) {
            return -1;
        }

        const lowerHash = window.location.hash.toLowerCase();
        return panels.findIndex(panel => `#${panel.id}`.toLowerCase() === lowerHash);
    };

    const updateViewportHeight = (animated) => {
        const activePanel = panels[currentIndex];
        if (!activePanel) {
            return;
        }

        const targetHeight = Math.ceil(activePanel.scrollHeight);
        if (!Number.isFinite(targetHeight) || targetHeight <= 0) {
            return;
        }

        if (!animated) {
            const previousTransition = viewport.style.transition;
            viewport.style.transition = "none";
            viewport.style.height = `${targetHeight}px`;
            viewport.getBoundingClientRect();
            viewport.style.transition = previousTransition;
            return;
        }

        viewport.style.height = `${targetHeight}px`;
    };

    const observeActivePanelHeight = () => {
        activePanelResizeObserver?.disconnect();
        activePanelResizeObserver = null;

        if (typeof ResizeObserver === "undefined") {
            return;
        }

        const activePanel = panels[currentIndex];
        if (!activePanel) {
            return;
        }

        activePanelResizeObserver = new ResizeObserver(() => {
            updateViewportHeight(true);
        });
        activePanelResizeObserver.observe(activePanel);
    };

    const syncUi = (nextIndex, updateHash) => {
        const panelCount = panels.length;
        currentIndex = ((nextIndex % panelCount) + panelCount) % panelCount;

        track.style.transform = `translateX(-${currentIndex * 100}%)`;

        panels.forEach((panel, index) => {
            panel.setAttribute("aria-hidden", index == currentIndex ? "false" : "true");
        });

        tabs.forEach(tab => {
            const targetId = tab.getAttribute("data-window-tab");
            const isActive = targetId === panels[currentIndex].id;
            tab.classList.toggle("is-active", isActive);
            tab.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        if (updateHash && panels[currentIndex].id) {
            const targetHash = `#${panels[currentIndex].id}`;
            if (window.location.hash !== targetHash) {
                history.replaceState(null, "", `${window.location.pathname}${window.location.search}${targetHash}`);
            }
        }

        updateViewportHeight(true);
        observeActivePanelHeight();
    };

    tabs.forEach(tab => {
        tab.addEventListener("click", () => {
            const targetId = tab.getAttribute("data-window-tab");
            if (!targetId) {
                return;
            }

            const nextIndex = panels.findIndex(panel => panel.id === targetId);
            if (nextIndex >= 0) {
                syncUi(nextIndex, true);
                markTabHintSeen();
            }
        });
    });

    const stripHashFromFormAction = form => {
        const action = form.getAttribute("action");
        if (typeof action !== "string" || action.length === 0) {
            return "";
        }

        if (!action.includes("#")) {
            return action;
        }

        const normalizedAction = action.split("#")[0];
        form.setAttribute("action", normalizedAction);
        return normalizedAction;
    };

    const buildSwapScrollSnapshot = form => {
        let targetX = window.scrollX;
        let targetY = window.scrollY;
        let anchorDayIndex = null;
        let anchorTop = null;

        if (form instanceof HTMLFormElement) {
            const capturedX = Number.parseFloat(form.dataset.swapScrollX ?? "");
            const capturedY = Number.parseFloat(form.dataset.swapScrollY ?? "");
            const capturedAt = Number.parseInt(form.dataset.swapScrollCapturedAt ?? "", 10);
            const hasRecentCapture =
                Number.isFinite(capturedX) &&
                Number.isFinite(capturedY) &&
                Number.isFinite(capturedAt) &&
                Date.now() - capturedAt <= 1500;

            if (hasRecentCapture) {
                targetX = capturedX;
                targetY = capturedY;
            }

            const dayInput = form.querySelector("input[name='dayIndex']");
            const parsedDayIndex = Number.parseInt(dayInput?.value ?? "", 10);
            if (Number.isInteger(parsedDayIndex) && parsedDayIndex >= 0) {
                anchorDayIndex = parsedDayIndex;
                anchorTop = Math.round(form.getBoundingClientRect().top);
            }
        }

        return {
            x: targetX,
            y: targetY,
            anchorDayIndex,
            anchorTop
        };
    };

    const restoreInlineSwapScroll = snapshot => {
        if (!snapshot || typeof snapshot.y !== "number") {
            return;
        }

        const targetX = typeof snapshot.x === "number" ? snapshot.x : 0;
        const fallbackTargetY = snapshot.y;
        const anchorDayIndex = Number.isInteger(snapshot.anchorDayIndex)
            ? snapshot.anchorDayIndex
            : null;
        const anchorTop = typeof snapshot.anchorTop === "number"
            ? snapshot.anchorTop
            : null;

        const resolveTargetY = () => {
            if (anchorDayIndex === null || typeof anchorTop !== "number") {
                return fallbackTargetY;
            }

            const selector = `.aislepilot-swap-form input[name='dayIndex'][value='${anchorDayIndex}']`;
            const targetDayInput = document.querySelector(selector);
            if (!(targetDayInput instanceof HTMLInputElement)) {
                return fallbackTargetY;
            }

            const targetForm = targetDayInput.closest("form");
            if (!(targetForm instanceof HTMLFormElement)) {
                return fallbackTargetY;
            }

            return window.scrollY + (targetForm.getBoundingClientRect().top - anchorTop);
        };

        root.classList.add("is-restoring-scroll");
        const restoreDeadline = Date.now() + 500;
        const restoreLoop = () => {
            const targetY = resolveTargetY();
            const xDrift = Math.abs(window.scrollX - targetX);
            const yDrift = Math.abs(window.scrollY - targetY);
            if (xDrift > 2 || yDrift > 8) {
                scrollInstantly(targetX, targetY);
            }

            if (Date.now() < restoreDeadline) {
                requestAnimationFrame(restoreLoop);
                return;
            }

            root.classList.remove("is-restoring-scroll");
        };

        requestAnimationFrame(() => {
            requestAnimationFrame(restoreLoop);
        });
    };

    const wirePreserveScrollForm = form => {
        if (!(form instanceof HTMLFormElement) || form.dataset.preserveScrollWired === "true") {
            return;
        }

        form.dataset.preserveScrollWired = "true";
        const captureScrollSnapshot = () => {
            form.dataset.swapScrollX = `${Math.round(window.scrollX)}`;
            form.dataset.swapScrollY = `${Math.round(window.scrollY)}`;
            form.dataset.swapScrollCapturedAt = `${Date.now()}`;
        };

        form.addEventListener("pointerdown", captureScrollSnapshot, { passive: true });
        form.addEventListener("touchstart", captureScrollSnapshot, { passive: true });
        form.addEventListener("click", captureScrollSnapshot);

        form.addEventListener("submit", () => {
            stripHashFromFormAction(form);

            if (form.hasAttribute("data-ajax-swap-form")) {
                return;
            }

            persistSwapScrollPosition(form);
        });
    };

    const wirePreserveScrollHandlers = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-preserve-scroll-form]"))
            : Array.from(document.querySelectorAll("[data-preserve-scroll-form]"));
        forms.forEach(wirePreserveScrollForm);
    };

    const replaceSectionContent = (responseDocument, selector) => {
        const currentSection = document.querySelector(selector);
        const nextSection = responseDocument.querySelector(selector);

        if (!(currentSection instanceof HTMLElement) || !(nextSection instanceof HTMLElement)) {
            return false;
        }

        currentSection.innerHTML = nextSection.innerHTML;
        return true;
    };

    const replaceDocumentWithHtml = html => {
        document.open();
        document.write(html);
        document.close();
    };

    const wireLeftoverPlanner = scope => {
        const plannerShells = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-leftover-planner]"))
            : Array.from(document.querySelectorAll("[data-leftover-planner]"));

        plannerShells.forEach(plannerShell => {
            if (!(plannerShell instanceof HTMLElement) || plannerShell.dataset.leftoverPlannerWired === "true") {
                return;
            }

            plannerShell.dataset.leftoverPlannerWired = "true";
            const mealsPanel = plannerShell.closest("#aislepilot-meals");
            const container = mealsPanel instanceof HTMLElement ? mealsPanel : plannerShell.parentElement;
            if (!(container instanceof HTMLElement)) {
                return;
            }

            const leftoverToggleButton = container.querySelector("[data-leftover-toggle]");
            if (leftoverToggleButton instanceof HTMLButtonElement && leftoverToggleButton.dataset.leftoverToggleWired !== "true") {
                leftoverToggleButton.dataset.leftoverToggleWired = "true";
                leftoverToggleButton.addEventListener("click", () => {
                    const isHidden = plannerShell.hasAttribute("hidden");
                    if (isHidden) {
                        plannerShell.removeAttribute("hidden");
                        leftoverToggleButton.setAttribute("aria-expanded", "true");
                        leftoverToggleButton.textContent = "Hide leftover rebalance";
                    } else {
                        plannerShell.setAttribute("hidden", "hidden");
                        leftoverToggleButton.setAttribute("aria-expanded", "false");
                        leftoverToggleButton.textContent = "Rebalance leftovers";
                    }

                    updateViewportHeight(true);
                });
            }

            const leftoverRebalanceForm = container.querySelector("[data-leftover-rebalance-form]");
            const leftoverCsvInput = leftoverRebalanceForm?.querySelector("[data-leftover-csv]");
            const leftoverZones = Array.from(plannerShell.querySelectorAll("[data-leftover-day-zone]"));
            let selectedSourceZone = null;

            const submitLeftoverRebalance = () => {
                if (!(leftoverRebalanceForm instanceof HTMLFormElement) || !(leftoverCsvInput instanceof HTMLInputElement)) {
                    return;
                }

                const requestedDayIndexes = [];
                leftoverZones.forEach(zone => {
                    const dayIndexRaw = zone.getAttribute("data-day-index");
                    const dayIndex = Number.parseInt(dayIndexRaw ?? "", 10);
                    if (!Number.isInteger(dayIndex) || dayIndex < 0) {
                        return;
                    }

                    const countRaw = zone.getAttribute("data-leftover-count");
                    const tokenCount = Number.parseInt(countRaw ?? "", 10);
                    const normalizedCount = Number.isInteger(tokenCount) && tokenCount > 0 ? tokenCount : 0;
                    for (let i = 0; i < normalizedCount; i++) {
                        requestedDayIndexes.push(dayIndex);
                    }
                });

                leftoverCsvInput.value = requestedDayIndexes.join(",");
                persistSwapScrollPosition(leftoverRebalanceForm);
                leftoverRebalanceForm.requestSubmit();
            };

            const getZoneCount = zone => {
                const countRaw = zone.getAttribute("data-leftover-count");
                const count = Number.parseInt(countRaw ?? "", 10);
                return Number.isInteger(count) && count > 0 ? count : 0;
            };

            const setZoneCount = (zone, count) => {
                const normalizedCount = Math.max(0, count);
                zone.setAttribute("data-leftover-count", `${normalizedCount}`);
                zone.classList.toggle("is-leftover-source", normalizedCount > 0);
            };

            const clearSourceSelection = () => {
                if (!selectedSourceZone) {
                    return;
                }

                selectedSourceZone.classList.remove("is-source-selected");
                selectedSourceZone = null;
                leftoverZones.forEach(zone => zone.classList.remove("is-drop-ready"));
            };

            const selectSourceZone = zone => {
                clearSourceSelection();
                selectedSourceZone = zone;
                zone.classList.add("is-source-selected");
                leftoverZones.forEach(targetZone => {
                    if (targetZone !== zone) {
                        targetZone.classList.add("is-drop-ready");
                    }
                });
            };

            const moveOneLeftover = targetZone => {
                if (!selectedSourceZone || selectedSourceZone === targetZone) {
                    return;
                }

                const sourceCount = getZoneCount(selectedSourceZone);
                if (sourceCount <= 0) {
                    clearSourceSelection();
                    return;
                }

                const targetCount = getZoneCount(targetZone);
                setZoneCount(selectedSourceZone, sourceCount - 1);
                setZoneCount(targetZone, targetCount + 1);
                clearSourceSelection();
                submitLeftoverRebalance();
            };

            if (leftoverZones.length > 0) {
                leftoverZones.forEach(zone => {
                    if (!(zone instanceof HTMLButtonElement) || zone.dataset.leftoverZoneWired === "true") {
                        return;
                    }

                    zone.dataset.leftoverZoneWired = "true";
                    zone.addEventListener("click", () => {
                        const zoneCount = getZoneCount(zone);
                        if (!selectedSourceZone) {
                            if (zoneCount > 0) {
                                selectSourceZone(zone);
                            }
                            return;
                        }

                        if (selectedSourceZone === zone) {
                            clearSourceSelection();
                            return;
                        }

                        moveOneLeftover(zone);
                    });
                });
            }
        });
    };

    const applyAjaxSwapResponse = responseText => {
        if (typeof DOMParser === "undefined") {
            return false;
        }

        const responseDocument = new DOMParser().parseFromString(responseText, "text/html");
        const didReplaceMeals = replaceSectionContent(responseDocument, "#aislepilot-meals");
        if (!didReplaceMeals) {
            return false;
        }

        replaceSectionContent(responseDocument, "#aislepilot-overview");
        replaceSectionContent(responseDocument, "#aislepilot-shop");
        replaceSectionContent(responseDocument, "#aislepilot-export");

        wireSubmitLoadingHandlers(document);
        wirePlanBasicsSliders(document);
        wireCustomAisleFieldVisibility(document);
        wirePreserveScrollHandlers(document);
        wireSetupToggleHandlers(document);
        wireLeftoverPlanner(document);
        wireAjaxSwapHandlers(document);
        wireNotesExportButtons(document);
        syncSetupToggleState();
        observeActivePanelHeight();
        updateViewportHeight(true);
        return true;
    };

    const animateSwappedMeal = dayIndex => {
        const mealsGrid = document.querySelector("#aislepilot-meals .aislepilot-meal-grid");
        if (mealsGrid instanceof HTMLElement) {
            mealsGrid.classList.remove("is-swap-fading-in");
            mealsGrid.getBoundingClientRect();
            mealsGrid.classList.add("is-swap-fading-in");
            window.setTimeout(() => {
                mealsGrid.classList.remove("is-swap-fading-in");
            }, 260);
        }

        if (!Number.isInteger(dayIndex) || dayIndex < 0) {
            return;
        }

        const selector = `.aislepilot-swap-form input[name='dayIndex'][value='${dayIndex}']`;
        const dayInput = document.querySelector(selector);
        if (!(dayInput instanceof HTMLInputElement)) {
            return;
        }

        const updatedCard = dayInput.closest(".aislepilot-card");
        if (!(updatedCard instanceof HTMLElement)) {
            return;
        }

        updatedCard.classList.remove("is-swap-updated");
        updatedCard.getBoundingClientRect();
        updatedCard.classList.add("is-swap-updated");
        window.setTimeout(() => {
            updatedCard.classList.remove("is-swap-updated");
        }, 360);
    };

    const wireAjaxSwapForm = form => {
        if (!(form instanceof HTMLFormElement) || form.dataset.ajaxSwapWired === "true") {
            return;
        }

        form.dataset.ajaxSwapWired = "true";
        form.addEventListener("submit", async event => {
            if (!(event.currentTarget instanceof HTMLFormElement) || typeof fetch !== "function") {
                return;
            }

            event.preventDefault();
            const swapForm = event.currentTarget;
            if (swapForm.dataset.ajaxSwapSubmitting === "true") {
                return;
            }

            swapForm.dataset.ajaxSwapSubmitting = "true";
            const scrollSnapshot = buildSwapScrollSnapshot(swapForm);
            const swapDayIndex = Number.isInteger(scrollSnapshot.anchorDayIndex)
                ? scrollSnapshot.anchorDayIndex
                : null;
            const currentCard = swapForm.closest(".aislepilot-card");
            if (currentCard instanceof HTMLElement) {
                currentCard.classList.add("is-swap-fading-out");
            }
            const actionUrl = stripHashFromFormAction(swapForm);
            if (!actionUrl) {
                persistSwapScrollPosition(swapForm);
                HTMLFormElement.prototype.submit.call(swapForm);
                return;
            }

            try {
                const response = await fetch(actionUrl, {
                    method: "POST",
                    body: new FormData(swapForm),
                    credentials: "same-origin",
                    headers: {
                        "Accept": "text/html,application/xhtml+xml",
                        "X-Requested-With": "XMLHttpRequest"
                    }
                });

                const responseText = await response.text();
                const contentType = response.headers.get("content-type") ?? "";
                const isHtmlResponse =
                    contentType.includes("text/html") ||
                    responseText.includes("<html") ||
                    responseText.includes("<!DOCTYPE html");

                if (isHtmlResponse) {
                    if (!applyAjaxSwapResponse(responseText)) {
                        replaceDocumentWithHtml(responseText);
                        return;
                    }

                    restoreInlineSwapScroll(scrollSnapshot);
                    animateSwappedMeal(swapDayIndex);
                    return;
                }

                if (response.ok) {
                    window.location.reload();
                    return;
                }

                resetSubmittingState();
                return;
            } catch {
                persistSwapScrollPosition(swapForm);
                HTMLFormElement.prototype.submit.call(swapForm);
                return;
            } finally {
                if (currentCard instanceof HTMLElement && currentCard.isConnected) {
                    currentCard.classList.remove("is-swap-fading-out");
                }
                clearSubmitLoadingDelay(swapForm);
                delete swapForm.dataset.ajaxSwapSubmitting;
            }
        });
    };

    const wireAjaxSwapHandlers = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-ajax-swap-form]"))
            : Array.from(document.querySelectorAll("[data-ajax-swap-form]"));
        forms.forEach(wireAjaxSwapForm);
    };

    wireSetupToggleHandlers(document);
    wirePreserveScrollHandlers(document);
    wireLeftoverPlanner(document);
    wireAjaxSwapHandlers(document);

    viewport.addEventListener("touchstart", event => {
        const touch = event.changedTouches[0];
        touchStartX = touch.clientX;
        touchStartY = touch.clientY;
    }, { passive: true });

    viewport.addEventListener("touchend", event => {
        const touch = event.changedTouches[0];
        const deltaX = touch.clientX - touchStartX;
        const deltaY = touch.clientY - touchStartY;

        if (Math.abs(deltaX) < 48 || Math.abs(deltaX) <= Math.abs(deltaY)) {
            return;
        }

        if (deltaX < 0) {
            syncUi(currentIndex + 1, true);
        } else {
            syncUi(currentIndex - 1, true);
        }

        markTabHintSeen();
    }, { passive: true });

    window.addEventListener("resize", () => {
        updateViewportHeight(false);
    });

    viewport.addEventListener("toggle", event => {
        const activePanel = panels[currentIndex];
        if (!activePanel || !(event.target instanceof HTMLElement)) {
            return;
        }

        if (activePanel.contains(event.target)) {
            updateViewportHeight(true);
        }
    }, true);

    window.addEventListener("hashchange", () => {
        const hashIndex = findIndexFromHash();
        if (hashIndex >= 0) {
            syncUi(hashIndex, false);
        }
    });

    const initialIndex = findIndexFromHash();
    applyTabHintVisibility();
    syncUi(initialIndex >= 0 ? initialIndex : 0, false);
    updateViewportHeight(false);
    syncSetupToggleState();
    const restored = restoreSwapScrollPosition();
    if (!restored) {
        clearRestorePending();
    }
})();
