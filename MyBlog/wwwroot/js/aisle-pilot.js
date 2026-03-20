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

    if (!viewport || !track || panels.length === 0) {
        return;
    }

    let currentIndex = 0;
    let touchStartX = 0;
    let touchStartY = 0;
    let activePanelResizeObserver = null;

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
        wirePreserveScrollHandlers(document);
        wireSetupToggleHandlers(document);
        wireLeftoverPlanner(document);
        wireAjaxSwapHandlers(document);
        syncSetupToggleState();
        observeActivePanelHeight();
        updateViewportHeight(true);
        return true;
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
    syncUi(initialIndex >= 0 ? initialIndex : 0, false);
    updateViewportHeight(false);
    syncSetupToggleState();
    const restored = restoreSwapScrollPosition();
    if (!restored) {
        clearRestorePending();
    }
})();
