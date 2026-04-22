(() => {
    if (window.__aislePilotScriptWired === true) {
        return;
    }
    window.__aislePilotScriptWired = true;

    const aislePilotCore = window.AislePilotCore;
    if (!aislePilotCore) {
        return;
    }

    const {
        clearPersistedSwapScroll,
        clearRestorePending,
        clearSubmitLoadingDelay,
        hidePlanLoadingShell,
        resetFormSubmittingState,
        schedulePlanBasicsSliderRefresh,
        showToast,
        startMealImagePolling,
        syncMobileContextOffset,
        wireCustomAisleFieldVisibility,
        wireExportThemeForms,
        wireMealTypeSelectors,
        wirePlanBasicsSliders,
        wireSubmitLoadingHandlers
    } = aislePilotCore;

    const isLocalSwapDebugEnabled = (() => {
        try {
            const host = (window.location.hostname ?? "").trim().toLowerCase();
            return host === "localhost" || host === "127.0.0.1" || host === "0.0.0.0";
        } catch {
            return false;
        }
    })();
    const swapDebugEndpoint = "/projects/aisle-pilot/debug-client-log";

    const sendSwapDebugToServer = payload => {
        if (!isLocalSwapDebugEnabled || !payload || typeof payload !== "object") {
            return;
        }

        try {
            const body = JSON.stringify(payload);
            if (typeof navigator !== "undefined" && typeof navigator.sendBeacon === "function") {
                const blob = new Blob([body], { type: "application/json" });
                navigator.sendBeacon(swapDebugEndpoint, blob);
                return;
            }

            if (typeof fetch === "function") {
                void fetch(swapDebugEndpoint, {
                    method: "POST",
                    body,
                    keepalive: true,
                    credentials: "same-origin",
                    headers: {
                        "Content-Type": "application/json"
                    }
                });
            }
        } catch {
            // Ignore debug transport failures.
        }
    };

    const writeSwapDebug = (stage, details = {}) => {
        if (!isLocalSwapDebugEnabled) {
            return;
        }

        try {
            const payload = {
                stage,
                details,
                href: window.location.href,
                userAgent: typeof navigator !== "undefined" ? navigator.userAgent : "",
                timestampUtc: new Date().toISOString()
            };
            console.info("[AislePilot swap debug]", stage, details);
            sendSwapDebugToServer(payload);
        } catch {
            // Ignore console write issues.
        }
    };

    const root = document.querySelector("[data-aislepilot-window]");
    if (!root) {
        const savedWeeksPanel = document.querySelector("[data-saved-weeks-panel]");
        const savedWeeksToggleButtons = Array.from(document.querySelectorAll("[data-saved-weeks-toggle]"));
        const headMenus = Array.from(document.querySelectorAll("[data-head-menu]"));

        const syncSavedWeeksToggleStateWithoutWindow = () => {
            if (!(savedWeeksPanel instanceof HTMLElement)) {
                return;
            }

            const isHidden = savedWeeksPanel.hasAttribute("hidden");
            savedWeeksToggleButtons.forEach(button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                const visibleLabel = button.querySelector("[data-saved-weeks-toggle-label]");
                const collapsedLabel = button.dataset.savedWeeksToggleCollapsedLabel || "Saved weeks";
                const expandedLabel = button.dataset.savedWeeksToggleExpandedLabel || "Hide saved weeks";
                const nextLabel = isHidden ? collapsedLabel : expandedLabel;
                if (visibleLabel instanceof HTMLElement) {
                    visibleLabel.textContent = nextLabel;
                }

                button.setAttribute("aria-expanded", isHidden ? "false" : "true");
                button.setAttribute("aria-label", nextLabel);
                if (button.hasAttribute("title")) {
                    button.setAttribute("title", nextLabel);
                }
            });
        };

        const closeOpenHeadMenusWithoutWindow = () => {
            headMenus.forEach(menu => {
                if (menu instanceof HTMLDetailsElement && menu.open) {
                    menu.open = false;
                }
            });
        };

        savedWeeksToggleButtons.forEach(button => {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            button.addEventListener("click", () => {
                if (!(savedWeeksPanel instanceof HTMLElement)) {
                    return;
                }

                if (savedWeeksPanel.hasAttribute("hidden")) {
                    savedWeeksPanel.removeAttribute("hidden");
                    savedWeeksPanel.scrollIntoView({ behavior: "smooth", block: "start" });
                } else {
                    savedWeeksPanel.setAttribute("hidden", "hidden");
                }

                const headMenu = button.closest("[data-head-menu]");
                if (headMenu instanceof HTMLDetailsElement) {
                    headMenu.open = false;
                }

                syncSavedWeeksToggleStateWithoutWindow();
            });
        });

        headMenus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement)) {
                return;
            }

            menu.addEventListener("toggle", () => {
                const trigger = menu.querySelector("summary");
                if (trigger instanceof HTMLElement) {
                    trigger.setAttribute("aria-expanded", menu.open ? "true" : "false");
                }
            });
        });

        document.addEventListener("click", event => {
            if (!(event.target instanceof Element)) {
                return;
            }

            if (event.target.closest("[data-head-menu]")) {
                return;
            }

            closeOpenHeadMenusWithoutWindow();
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeOpenHeadMenusWithoutWindow();
            }
        });

        syncSavedWeeksToggleStateWithoutWindow();
        clearRestorePending();
        return;
    }

    const setupPanel = document.querySelector("[data-setup-panel]");
    const setupLayout = setupPanel instanceof HTMLElement
        ? setupPanel.closest(".aislepilot-layout")
        : null;
    const savedWeeksPanel = document.querySelector("[data-saved-weeks-panel]");
    const getOverviewContent = () => document.querySelector("[data-overview-content]");

    const viewport = root.querySelector("[data-window-viewport]");
    const track = root.querySelector("[data-window-track]");
    const tabs = Array.from(root.querySelectorAll("[data-window-tab]"));
    const panels = Array.from(root.querySelectorAll(".aislepilot-window-panel"));
    const tabHint = root.querySelector("[data-window-hint]");
    const tabHintSeenStorageKey = "aislepilot:tab-hint-seen";
    const shoppingItemStateStorageKey = "aislepilot:shopping-item-state";
    const customShoppingItemsStorageKey = "aislepilot:custom-shopping-items";
    let shoppingItemStateCache = null;
    let customShoppingItemsCache = null;

    const readShoppingItemState = () => {
        if (shoppingItemStateCache && typeof shoppingItemStateCache === "object") {
            return shoppingItemStateCache;
        }

        try {
            const raw = window.localStorage.getItem(shoppingItemStateStorageKey);
            if (!raw) {
                shoppingItemStateCache = {};
                return shoppingItemStateCache;
            }

            const parsed = JSON.parse(raw);
            shoppingItemStateCache = parsed && typeof parsed === "object" ? parsed : {};
        } catch {
            shoppingItemStateCache = {};
        }

        return shoppingItemStateCache;
    };

    const writeShoppingItemState = () => {
        try {
            const state = readShoppingItemState();
            window.localStorage.setItem(shoppingItemStateStorageKey, JSON.stringify(state));
        } catch {
            // Ignore storage failures in private modes.
        }
    };

    const syncShoppingItemVisualState = (label, isChecked) => {
        if (!(label instanceof HTMLElement)) {
            return;
        }

        label.classList.toggle("is-checked", isChecked);
    };

    const readCustomShoppingItems = () => {
        if (Array.isArray(customShoppingItemsCache)) {
            return customShoppingItemsCache;
        }

        try {
            const raw = window.localStorage.getItem(customShoppingItemsStorageKey);
            if (!raw) {
                customShoppingItemsCache = [];
                return customShoppingItemsCache;
            }

            const parsed = JSON.parse(raw);
            customShoppingItemsCache = Array.isArray(parsed)
                ? parsed.filter(item =>
                    item &&
                    typeof item === "object" &&
                    typeof item.id === "string" &&
                    typeof item.text === "string")
                : [];
        } catch {
            customShoppingItemsCache = [];
        }

        return customShoppingItemsCache;
    };

    const writeCustomShoppingItems = items => {
        customShoppingItemsCache = Array.isArray(items) ? items : [];

        try {
            window.localStorage.setItem(customShoppingItemsStorageKey, JSON.stringify(customShoppingItemsCache));
        } catch {
            // Ignore storage failures in private modes.
        }
    };

    if (!viewport || !track || panels.length === 0) {
        return;
    }

    let currentIndex = 0;
    let touchStartX = 0;
    let touchStartY = 0;
    let viewportSwipeStartedInsideProtectedRegion = false;
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

    const isEventWithinSelector = (event, selector) => {
        if (!(event instanceof Event) || typeof selector !== "string" || selector.length === 0) {
            return false;
        }

        const target = event.target;
        if (target instanceof Element && target.closest(selector)) {
            return true;
        }

        if (typeof event.composedPath !== "function") {
            return false;
        }

        const eventPath = event.composedPath();
        return eventPath.some(node => node instanceof Element && (node.matches(selector) || node.closest(selector)));
    };

    const isEventWithinDayMealCard = event => isEventWithinSelector(event, "[data-day-meal-card]");
    const isEventWithinDayCarouselPagination = event => isEventWithinSelector(event, "[data-day-carousel-pagination]");

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
        let anchorPanelId = null;
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
                const panel = form.closest("[data-day-meal-panel]");
                if (panel instanceof HTMLElement && panel.id) {
                    anchorPanelId = panel.id;
                    anchorTop = Math.round(panel.getBoundingClientRect().top);
                } else {
                    anchorTop = Math.round(form.getBoundingClientRect().top);
                }
            }
        }

        const activePanelId = panels[currentIndex]?.id ?? null;
        const payload = {
            x: targetX,
            y: targetY,
            activePanelId,
            anchorDayIndex,
            anchorPanelId,
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
            const anchorPanelId = typeof parsed.anchorPanelId === "string" && parsed.anchorPanelId.length > 0
                ? parsed.anchorPanelId
                : null;
            const anchorTop = typeof parsed.anchorTop === "number"
                ? parsed.anchorTop
                : null;
            const resolveTargetY = () => {
                if (anchorDayIndex === null || typeof anchorTop !== "number") {
                    return fallbackTargetY;
                }

                if (anchorPanelId) {
                    const targetPanel = document.getElementById(anchorPanelId);
                    if (targetPanel instanceof HTMLElement) {
                        return window.scrollY + (targetPanel.getBoundingClientRect().top - anchorTop);
                    }
                }

                const selector = `.aislepilot-swap-form[action*='/swap-meal'] input[name='dayIndex'][value='${anchorDayIndex}']`;
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
            const restoreDeadline = Date.now() + swapScrollRestoreDurationMs;
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
    const getOverviewToggleButtons = () => Array.from(document.querySelectorAll("[data-overview-toggle]"));
    const getSavedWeeksToggleButtons = () => Array.from(document.querySelectorAll("[data-saved-weeks-toggle]"));

    const syncOverviewToggleState = () => {
        const overviewContent = getOverviewContent();
        if (!(overviewContent instanceof HTMLElement)) {
            return;
        }

        const overviewToggleButtons = getOverviewToggleButtons();
        if (overviewToggleButtons.length === 0) {
            return;
        }

        const isExpanded = !overviewContent.hasAttribute("hidden");
        overviewToggleButtons.forEach(button => {
            const srOnlyLabel = button.querySelector(".sr-only");
            const visibleLabel = button.querySelector("[data-overview-toggle-label]");
            const collapsedLabel = button.dataset.overviewToggleCollapsedLabel
                || srOnlyLabel?.textContent?.trim()
                || button.getAttribute("aria-label")
                || button.textContent?.trim()
                || "Expand overview";
            const expandedLabel = button.dataset.overviewToggleExpandedLabel || "Collapse overview";
            const nextLabel = isExpanded ? expandedLabel : collapsedLabel;

            button.dataset.overviewToggleCollapsedLabel = collapsedLabel;
            button.dataset.overviewToggleExpandedLabel = expandedLabel;

            if (srOnlyLabel) {
                srOnlyLabel.textContent = nextLabel;
            }

            if (visibleLabel instanceof HTMLElement) {
                visibleLabel.textContent = nextLabel;
            }

            if (!srOnlyLabel && !(visibleLabel instanceof HTMLElement)) {
                button.textContent = nextLabel;
            }

            button.setAttribute("aria-label", nextLabel);
            if (button.hasAttribute("title")) {
                button.setAttribute("title", nextLabel);
            }
            button.setAttribute("aria-expanded", isExpanded ? "true" : "false");
        });
    };

    const syncSetupToggleState = () => {
        if (!setupPanel) {
            return;
        }

        const setupToggleButtons = getSetupToggleButtons();
        if (setupToggleButtons.length === 0) {
            return;
        }

        const isHidden = setupPanel.hasAttribute("hidden");
        if (setupLayout instanceof HTMLElement) {
            setupLayout.classList.toggle("has-visible-setup", !isHidden);
        }

        setupToggleButtons.forEach(button => {
            const srOnlyLabel = button.querySelector(".sr-only");
            const visibleLabel = button.querySelector("[data-setup-toggle-label]");
            const collapsedLabel = button.dataset.setupToggleCollapsedLabel
                || srOnlyLabel?.textContent?.trim()
                || button.getAttribute("aria-label")
                || button.textContent?.trim()
                || "Edit settings";
            const expandedLabel = button.dataset.setupToggleExpandedLabel || "Hide settings";
            const nextLabel = isHidden ? collapsedLabel : expandedLabel;

            button.dataset.setupToggleCollapsedLabel = collapsedLabel;
            button.dataset.setupToggleExpandedLabel = expandedLabel;

            if (srOnlyLabel) {
                srOnlyLabel.textContent = nextLabel;
            }

            if (visibleLabel instanceof HTMLElement) {
                visibleLabel.textContent = nextLabel;
            }

            if (!srOnlyLabel && !(visibleLabel instanceof HTMLElement)) {
                button.textContent = nextLabel;
            }

            button.setAttribute("aria-label", nextLabel);
            if (button.hasAttribute("title")) {
                button.setAttribute("title", nextLabel);
            }
            button.setAttribute("aria-expanded", isHidden ? "false" : "true");
        });
    };

    const syncSavedWeeksToggleState = () => {
        if (!(savedWeeksPanel instanceof HTMLElement)) {
            return;
        }

        const savedWeeksToggleButtons = getSavedWeeksToggleButtons();
        if (savedWeeksToggleButtons.length === 0) {
            return;
        }

        const isHidden = savedWeeksPanel.hasAttribute("hidden");
        savedWeeksToggleButtons.forEach(button => {
            const srOnlyLabel = button.querySelector(".sr-only");
            const visibleLabel = button.querySelector("[data-saved-weeks-toggle-label]");
            const collapsedLabel = button.dataset.savedWeeksToggleCollapsedLabel
                || srOnlyLabel?.textContent?.trim()
                || button.getAttribute("aria-label")
                || button.textContent?.trim()
                || "My weeks";
            const expandedLabel = button.dataset.savedWeeksToggleExpandedLabel || "Hide weeks";
            const nextLabel = isHidden ? collapsedLabel : expandedLabel;

            button.dataset.savedWeeksToggleCollapsedLabel = collapsedLabel;
            button.dataset.savedWeeksToggleExpandedLabel = expandedLabel;

            if (srOnlyLabel) {
                srOnlyLabel.textContent = nextLabel;
            }

            if (visibleLabel instanceof HTMLElement) {
                visibleLabel.textContent = nextLabel;
            }

            if (!srOnlyLabel && !(visibleLabel instanceof HTMLElement)) {
                button.textContent = nextLabel;
            }

            button.setAttribute("aria-label", nextLabel);
            if (button.hasAttribute("title")) {
                button.setAttribute("title", nextLabel);
            }
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
                    schedulePlanBasicsSliderRefresh(setupPanel);
                    setupPanel.scrollIntoView({ behavior: "smooth", block: "start" });
                } else {
                    setupPanel.setAttribute("hidden", "hidden");
                }

                syncSetupToggleState();
            });
        });
    };

    const wireOverviewToggleHandlers = scope => {
        const overviewToggleButtons = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-overview-toggle]"))
            : getOverviewToggleButtons();

        overviewToggleButtons.forEach(button => {
            if (!(button instanceof HTMLButtonElement) || button.dataset.overviewToggleWired === "true") {
                return;
            }

            button.dataset.overviewToggleWired = "true";
            button.addEventListener("click", () => {
                const overviewContent = getOverviewContent();
                if (!(overviewContent instanceof HTMLElement)) {
                    return;
                }

                if (overviewContent.hasAttribute("hidden")) {
                    overviewContent.removeAttribute("hidden");
                } else {
                    overviewContent.setAttribute("hidden", "hidden");
                }

                syncOverviewToggleState();
            });
        });
    };

    const wireSavedWeeksToggleHandlers = scope => {
        const savedWeeksToggleButtons = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-saved-weeks-toggle]"))
            : getSavedWeeksToggleButtons();

        savedWeeksToggleButtons.forEach(button => {
            if (!(button instanceof HTMLButtonElement) || button.dataset.savedWeeksToggleWired === "true") {
                return;
            }

            button.dataset.savedWeeksToggleWired = "true";
            button.addEventListener("click", () => {
                if (!(savedWeeksPanel instanceof HTMLElement)) {
                    return;
                }

                if (savedWeeksPanel.hasAttribute("hidden")) {
                    savedWeeksPanel.removeAttribute("hidden");
                    savedWeeksPanel.scrollIntoView({ behavior: "smooth", block: "start" });
                } else {
                    savedWeeksPanel.setAttribute("hidden", "hidden");
                }

                const headMenu = button.closest("[data-head-menu]");
                if (headMenu instanceof HTMLDetailsElement) {
                    headMenu.open = false;
                }

                syncSavedWeeksToggleState();
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
            const isActive = index === currentIndex;
            panel.setAttribute("aria-hidden", isActive ? "false" : "true");
            panel.setAttribute("tabindex", isActive ? "0" : "-1");
        });

        tabs.forEach(tab => {
            const targetId = tab.getAttribute("data-window-tab");
            const isActive = targetId === panels[currentIndex].id;
            tab.classList.toggle("is-active", isActive);
            tab.setAttribute("aria-selected", isActive ? "true" : "false");
            tab.setAttribute("aria-current", isActive ? "page" : "false");
            tab.setAttribute("tabindex", isActive ? "0" : "-1");
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

    const activateWindowTab = tab => {
        if (!(tab instanceof HTMLElement)) {
            return;
        }

        const targetId = tab.getAttribute("data-window-tab");
        if (!targetId) {
            return;
        }

        const nextIndex = panels.findIndex(panel => panel.id === targetId);
        if (nextIndex < 0) {
            return;
        }

        syncUi(nextIndex, true);
        tab.focus();
        markTabHintSeen();
    };

    tabs.forEach(tab => {
        tab.addEventListener("click", () => {
            activateWindowTab(tab);
        });

        tab.addEventListener("keydown", event => {
            const tabContainer = tab.parentElement;
            if (!(tabContainer instanceof HTMLElement)) {
                return;
            }

            const siblingTabs = Array.from(tabContainer.querySelectorAll("[data-window-tab]"));
            const currentTabIndex = siblingTabs.indexOf(tab);
            if (currentTabIndex < 0) {
                return;
            }

            if (event.key === "ArrowRight" || event.key === "ArrowDown") {
                event.preventDefault();
                const nextTabIndex = (currentTabIndex + 1) % siblingTabs.length;
                const nextTab = siblingTabs[nextTabIndex];
                activateWindowTab(nextTab);
                return;
            }

            if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
                event.preventDefault();
                const nextTabIndex = (currentTabIndex - 1 + siblingTabs.length) % siblingTabs.length;
                const nextTab = siblingTabs[nextTabIndex];
                activateWindowTab(nextTab);
                return;
            }

            if (event.key === "Home") {
                event.preventDefault();
                activateWindowTab(siblingTabs[0]);
                return;
            }

            if (event.key === "End") {
                event.preventDefault();
                activateWindowTab(siblingTabs[siblingTabs.length - 1]);
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
        let anchorPanelId = null;
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
                const panel = form.closest("[data-day-meal-panel]");
                if (panel instanceof HTMLElement && panel.id) {
                    anchorPanelId = panel.id;
                    anchorTop = Math.round(panel.getBoundingClientRect().top);
                } else {
                    anchorTop = Math.round(form.getBoundingClientRect().top);
                }
            }
        }

        return {
            x: targetX,
            y: targetY,
            anchorDayIndex,
            anchorPanelId,
            anchorTop
        };
    };

    const resolveCardMoreActionsMenuForForm = form => {
        if (!(form instanceof HTMLFormElement)) {
            return null;
        }

        const inlineMenu = form.closest("[data-card-more-actions]");
        if (inlineMenu instanceof HTMLDetailsElement) {
            return inlineMenu;
        }

        const actionsPanel = form.closest("[data-card-more-actions-panel]");
        if (!(actionsPanel instanceof HTMLElement) || !actionsPanel.id) {
            return null;
        }

        const menus = Array.from(document.querySelectorAll("[data-card-more-actions]"));
        const matchingMenuByPanel = menus.find(menu =>
            menu instanceof HTMLDetailsElement &&
            getCardMoreActionsPanel(menu) instanceof HTMLElement &&
            getCardMoreActionsPanel(menu).contains(form));
        if (matchingMenuByPanel instanceof HTMLDetailsElement) {
            return matchingMenuByPanel;
        }

        const portalState = cardMoreActionsPanelPortalState.get(actionsPanel);
        if (portalState?.ownerMenu instanceof HTMLDetailsElement) {
            return portalState.ownerMenu;
        }

        const escapedPanelId = typeof CSS !== "undefined" && typeof CSS.escape === "function"
            ? CSS.escape(actionsPanel.id)
            : actionsPanel.id.replace(/["\\]/g, "\\$&");
        const trigger = document.querySelector(`summary[aria-controls="${escapedPanelId}"]`);
        const matchingMenu = trigger?.closest("[data-card-more-actions]");
        return matchingMenu instanceof HTMLDetailsElement ? matchingMenu : null;
    };

    const findMealCardBySlotIndex = (scope, slotIndex) => {
        if (!(scope instanceof Document || scope instanceof Element) || !Number.isInteger(slotIndex) || slotIndex < 0) {
            return null;
        }

        let matchingCard = null;
        let matchingCardDayIndex = -1;
        const cards = Array.from(scope.querySelectorAll("[data-day-meal-card][data-day-index]"));
        cards.forEach(card => {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const cardDayIndex = Number.parseInt(card.dataset.dayIndex ?? "", 10);
            if (!Number.isInteger(cardDayIndex) || cardDayIndex > slotIndex || cardDayIndex < matchingCardDayIndex) {
                return;
            }

            matchingCard = card;
            matchingCardDayIndex = cardDayIndex;
        });

        return matchingCard instanceof HTMLElement ? matchingCard : null;
    };

    const resolveSwapTargetCard = form => {
        if (!(form instanceof HTMLFormElement)) {
            return null;
        }

        const inlineCard = form.closest(".aislepilot-card");
        if (inlineCard instanceof HTMLElement) {
            return inlineCard;
        }

        const parentActionsMenu = resolveCardMoreActionsMenuForForm(form);
        const menuCard = parentActionsMenu?.closest("[data-day-meal-card]");
        if (menuCard instanceof HTMLElement) {
            return menuCard;
        }

        const dayInput = form.querySelector("input[name='dayIndex']");
        const parsedDayIndex = Number.parseInt(dayInput?.value ?? "", 10);
        if (!Number.isInteger(parsedDayIndex) || parsedDayIndex < 0) {
            return null;
        }

        return findMealCardBySlotIndex(document, parsedDayIndex);
    };

    const restoreInlineSwapScroll = snapshot => {
        if (!snapshot || typeof snapshot.y !== "number") {
            return;
        }

        const targetX = typeof snapshot.x === "number" ? snapshot.x : 0;
        const targetY = snapshot.y;

        root.classList.add("is-restoring-scroll");
        const restoreDeadline = Date.now() + swapScrollRestoreDurationMs;
        const restoreLoop = () => {
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

    const dayMealSlotState = new Map();

    const readCardDayKey = card => {
        if (!(card instanceof HTMLElement)) {
            return "";
        }

        return (card.dataset.dayIndex ?? "").trim();
    };

    const readActiveDayMealSlotIndex = card => {
        if (!(card instanceof HTMLElement)) {
            return 0;
        }

        const tabs = Array.from(card.querySelectorAll("[data-day-meal-tab]"));
        if (tabs.length <= 1) {
            return 0;
        }

        const activeIndex = tabs.findIndex(tab => {
            if (!(tab instanceof HTMLElement)) {
                return false;
            }

            return tab.classList.contains("is-active") || tab.getAttribute("aria-selected") === "true";
        });

        return activeIndex >= 0 ? activeIndex : 0;
    };

    const rememberDayMealSlots = scope => {
        const cards = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-day-meal-card]"))
            : Array.from(document.querySelectorAll("[data-day-meal-card]"));

        cards.forEach(card => {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const dayKey = readCardDayKey(card);
            if (!dayKey) {
                return;
            }

            dayMealSlotState.set(dayKey, readActiveDayMealSlotIndex(card));
        });
    };

    const readRememberedDayMealSlot = card => {
        const dayKey = readCardDayKey(card);
        if (!dayKey || !dayMealSlotState.has(dayKey)) {
            return 0;
        }

        const stored = dayMealSlotState.get(dayKey);
        return Number.isInteger(stored) ? stored : 0;
    };

    const wireLeftoverPlanner = scope => {
        const leftoverRebalanceForms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-leftover-rebalance-form]"))
            : Array.from(document.querySelectorAll("[data-leftover-rebalance-form]"));

        leftoverRebalanceForms.forEach(leftoverRebalanceForm => {
            if (!(leftoverRebalanceForm instanceof HTMLFormElement) || leftoverRebalanceForm.dataset.leftoverPlannerWired === "true") {
                return;
            }

            leftoverRebalanceForm.dataset.leftoverPlannerWired = "true";
            const mealsPanel = leftoverRebalanceForm.closest("#aislepilot-meals");
            const container = mealsPanel instanceof HTMLElement ? mealsPanel : leftoverRebalanceForm.parentElement;
            if (!(container instanceof HTMLElement)) {
                return;
            }

            const leftoverCsvInput = leftoverRebalanceForm?.querySelector("[data-leftover-csv]");
            const leftoverZones = Array.from(container.querySelectorAll("[data-leftover-day-zone]"));
            const maxExtraRaw = Number.parseInt(
                leftoverRebalanceForm.getAttribute("data-leftover-max-extra") ?? "",
                10);
            const maxExtraAllocations = Number.isInteger(maxExtraRaw) && maxExtraRaw >= 0
                ? maxExtraRaw
                : Math.max(0, leftoverZones.length - 1);

            const toCsv = dayIndexes => dayIndexes.join(",");
            const getZoneDayIndex = zone => {
                const dayIndexRaw = zone.getAttribute("data-day-index");
                const dayIndex = Number.parseInt(dayIndexRaw ?? "", 10);
                return Number.isInteger(dayIndex) && dayIndex >= 0 ? dayIndex : -1;
            };
            const getZoneCount = zone => {
                const countRaw = zone.getAttribute("data-leftover-count");
                const count = Number.parseInt(countRaw ?? "", 10);
                return Number.isInteger(count) && count > 0 ? count : 0;
            };
            const getZoneToggleButtons = zone => {
                if (!(zone instanceof HTMLElement)) {
                    return [];
                }

                const dayIndex = getZoneDayIndex(zone);
                if (dayIndex < 0) {
                    return [];
                }

                const selector = `[data-leftover-toggle-sign][data-leftover-day-index="${dayIndex}"]`;
                return Array.from(container.querySelectorAll(selector))
                    .filter(button => button instanceof HTMLButtonElement);
            };
            const buildRequestedDayIndexes = () => {
                const requestedDayIndexes = [];
                leftoverZones.forEach(zone => {
                    const dayIndex = getZoneDayIndex(zone);
                    if (dayIndex < 0) {
                        return;
                    }

                    const normalizedCount = getZoneCount(zone);
                    for (let i = 0; i < normalizedCount; i += 1) {
                        requestedDayIndexes.push(dayIndex);
                    }
                });
                return requestedDayIndexes;
            };
            const getAssignedCount = () => buildRequestedDayIndexes().length;

            const syncSubmitPayload = () => {
                if (!(leftoverCsvInput instanceof HTMLInputElement)) {
                    return;
                }

                leftoverCsvInput.value = toCsv(buildRequestedDayIndexes());
            };

            const syncZoneCountBadge = (zone, normalizedCount) => {
                const countBadge = zone.querySelector("[data-leftover-day-count]");
                if (!(countBadge instanceof HTMLElement)) {
                    return;
                }

                const dayCount = Math.max(0, normalizedCount);
                if (dayCount > 1) {
                    countBadge.textContent = `x${dayCount}`;
                    countBadge.removeAttribute("hidden");
                } else {
                    countBadge.textContent = "";
                    countBadge.setAttribute("hidden", "hidden");
                }
            };

            const syncZoneAriaLabel = (zone, normalizedCount) => {
                const dayName = (zone.getAttribute("data-day-name") ?? "").trim();
                if (dayName.length === 0) {
                    return;
                }

                const dayCount = Math.max(0, normalizedCount);
                const nextLabel = dayCount > 0
                    ? `${dayName}: cook-extra x${dayCount}`
                    : `${dayName}: no cook-extra`;
                zone.setAttribute("aria-label", nextLabel);
            };

            const setZoneCount = (zone, count) => {
                const normalizedCount = Math.max(0, count);
                zone.setAttribute("data-leftover-count", `${normalizedCount}`);
                zone.classList.toggle("is-leftover-source", normalizedCount > 0);
                syncZoneCountBadge(zone, normalizedCount);
                syncZoneAriaLabel(zone, normalizedCount);
            };

            const syncZoneControlButtons = () => {
                const assignedCount = getAssignedCount();
                const canAssignMore = assignedCount < maxExtraAllocations;
                leftoverZones.forEach(zone => {
                    if (!(zone instanceof HTMLElement)) {
                        return;
                    }

                    const zoneCount = getZoneCount(zone);
                    const dayName = (zone.getAttribute("data-day-name") ?? "").trim();
                    const isAssigned = zoneCount > 0;
                    const canAssign = !isAssigned && canAssignMore;
                    const nextActionLabel = isAssigned ? "Remove extra" : "Cook extra";
                    zone.classList.toggle("is-leftover-locked", !isAssigned && !canAssignMore);
                    getZoneToggleButtons(zone).forEach(toggleButton => {
                        if (isAssigned) {
                            toggleButton.hidden = false;
                            toggleButton.disabled = false;
                            if (dayName.length > 0) {
                                toggleButton.setAttribute("aria-label", `Remove cook-extra from ${dayName}`);
                            }
                        } else if (canAssign && canAssignMore) {
                            toggleButton.hidden = false;
                            toggleButton.disabled = false;
                            if (dayName.length > 0) {
                                toggleButton.setAttribute("aria-label", `Add cook-extra to ${dayName}`);
                            }
                        } else {
                            toggleButton.hidden = true;
                            toggleButton.disabled = true;
                        }

                        toggleButton.setAttribute("title", nextActionLabel);
                        toggleButton.dataset.leftoverToggleMode = isAssigned ? "remove" : "add";
                        const buttonText = toggleButton.querySelector("[data-leftover-toggle-text]");
                        if (buttonText instanceof HTMLElement) {
                            buttonText.textContent = nextActionLabel;
                        }
                    });
                });
            };

            const syncPlannerState = () => {
                syncSubmitPayload();
                syncZoneControlButtons();
            };

            const submitLeftoverRebalance = () => {
                if (!(leftoverRebalanceForm instanceof HTMLFormElement) || !(leftoverCsvInput instanceof HTMLInputElement)) {
                    return;
                }

                syncSubmitPayload();
                persistSwapScrollPosition(leftoverRebalanceForm);
                leftoverRebalanceForm.requestSubmit();
            };

            if (leftoverZones.length > 0) {
                leftoverZones.forEach(zone => {
                    if (!(zone instanceof HTMLElement)) {
                        return;
                    }

                    setZoneCount(zone, getZoneCount(zone));
                });

                syncPlannerState();

                leftoverZones.forEach(zone => {
                    if (!(zone instanceof HTMLElement) || zone.dataset.leftoverZoneWired === "true") {
                        return;
                    }

                    zone.dataset.leftoverZoneWired = "true";
                    getZoneToggleButtons(zone).forEach(toggleButton => {
                        if (!(toggleButton instanceof HTMLButtonElement) || toggleButton.dataset.leftoverZoneWired === "true") {
                            return;
                        }

                        toggleButton.dataset.leftoverZoneWired = "true";
                        toggleButton.addEventListener("click", () => {
                            const zoneCount = getZoneCount(zone);
                            if (zoneCount > 0) {
                                setZoneCount(zone, zoneCount - 1);
                                submitLeftoverRebalance();
                                return;
                            }

                            if (getAssignedCount() >= maxExtraAllocations) {
                                return;
                            }

                            setZoneCount(zone, zoneCount + 1);
                            submitLeftoverRebalance();
                        });
                    });
                });
            }
        });
    };

    let cardMoreActionsBackdrop = null;
    let cardMoreActionsGlobalWired = false;
    let cardMoreActionsLastOpenedAt = 0;
    const cardMoreActionsCloseAnimationMs = 420;
    const cardMoreActionsCloseTimers = new WeakMap();
    const cardMoreActionsAnimationFrames = new WeakMap();
    const cardMoreActionsPanelPortalState = new WeakMap();
    const shouldUseMobileCardActionsSheet = () =>
        typeof window.matchMedia === "function" && window.matchMedia("(max-width: 760px)").matches;
    const getVisibleCardMoreActionsMenus = () => Array.from(
        document.querySelectorAll("[data-card-more-actions][open], [data-card-more-actions].is-closing")
    );
    const getCardMoreActionsPanel = menu => {
        if (!(menu instanceof HTMLDetailsElement)) {
            return null;
        }

        const trigger = menu.querySelector("summary");
        const panelId = trigger instanceof HTMLElement ? trigger.getAttribute("aria-controls") : null;
        if (typeof panelId === "string" && panelId.length > 0) {
            const panelById = document.getElementById(panelId);
            if (panelById instanceof HTMLElement) {
                return panelById;
            }
        }

        const panel = menu.querySelector(".aislepilot-card-more-actions-menu");
        return panel instanceof HTMLElement ? panel : null;
    };

    const clearCardMoreActionsCloseTimer = menu => {
        const closeTimer = cardMoreActionsCloseTimers.get(menu);
        if (typeof closeTimer === "number") {
            window.clearTimeout(closeTimer);
        }

        cardMoreActionsCloseTimers.delete(menu);
    };

    const stopCardMoreActionsAnimation = menu => {
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(actionsMenu instanceof HTMLElement)) {
            return;
        }

        const activeFrame = cardMoreActionsAnimationFrames.get(actionsMenu);
        if (typeof activeFrame === "number") {
            window.cancelAnimationFrame(activeFrame);
        }

        cardMoreActionsAnimationFrames.delete(actionsMenu);
        actionsMenu.style.removeProperty("transition");
    };

    const clearCardMoreActionsMenuInlineStyles = menu => {
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(actionsMenu instanceof HTMLElement)) {
            return;
        }

        actionsMenu.style.removeProperty("max-height");
        actionsMenu.style.removeProperty("overflow-y");
        actionsMenu.style.removeProperty("position");
        actionsMenu.style.removeProperty("left");
        actionsMenu.style.removeProperty("top");
        actionsMenu.style.removeProperty("right");
        actionsMenu.style.removeProperty("bottom");
    };

    const moveCardMoreActionsPanelToBody = menu => {
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(actionsMenu instanceof HTMLElement)) {
            return null;
        }

        if (!shouldUseMobileCardActionsSheet()) {
            return actionsMenu;
        }

        if (actionsMenu.parentElement !== document.body) {
            cardMoreActionsPanelPortalState.set(actionsMenu, {
                parent: actionsMenu.parentNode,
                nextSibling: actionsMenu.nextSibling,
                ownerMenu: menu
            });
            document.body.appendChild(actionsMenu);
        }

        actionsMenu.classList.add("is-mobile-sheet");
        actionsMenu.classList.remove("is-closing");
        actionsMenu.style.visibility = "hidden";
        actionsMenu.style.opacity = "1";
        actionsMenu.style.transform = "translate3d(0, 110%, 0)";
        actionsMenu.style.transition = "none";
        return actionsMenu;
    };

    const restoreCardMoreActionsPanelFromBody = menu => {
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(actionsMenu instanceof HTMLElement)) {
            return;
        }

        actionsMenu.classList.remove("is-mobile-sheet", "is-closing");
        actionsMenu.style.removeProperty("visibility");
        actionsMenu.style.removeProperty("opacity");
        actionsMenu.style.removeProperty("transform");
        actionsMenu.style.removeProperty("transition");
        const portalState = cardMoreActionsPanelPortalState.get(actionsMenu);
        if (!portalState || actionsMenu.parentElement !== document.body) {
            return;
        }

        const parentNode = portalState.parent instanceof Node ? portalState.parent : null;
        if (parentNode === null) {
            cardMoreActionsPanelPortalState.delete(actionsMenu);
            return;
        }

        const nextSibling = portalState.nextSibling instanceof Node &&
            portalState.nextSibling.parentNode === parentNode
            ? portalState.nextSibling
            : null;
        parentNode.insertBefore(actionsMenu, nextSibling);
        cardMoreActionsPanelPortalState.delete(actionsMenu);
    };

    const syncDayMealPanelOpenActionsState = menu => {
        const dayMealPanel = menu instanceof HTMLElement
            ? menu.closest("[data-day-meal-panel]")
            : null;
        if (!(dayMealPanel instanceof HTMLElement)) {
            return;
        }

        const hasVisibleMenu = dayMealPanel.querySelector("[data-card-more-actions][open], [data-card-more-actions].is-closing")
            instanceof HTMLDetailsElement;
        dayMealPanel.classList.toggle("has-open-actions", hasVisibleMenu);
    };

    const ensureCardMoreActionsBackdrop = () => {
        if (cardMoreActionsBackdrop instanceof HTMLDivElement) {
            return cardMoreActionsBackdrop;
        }

        const backdrop = document.createElement("div");
        backdrop.className = "aislepilot-mobile-meal-sheet-backdrop";
        backdrop.setAttribute("hidden", "hidden");
        backdrop.addEventListener("click", () => {
            if (Date.now() - cardMoreActionsLastOpenedAt < 260) {
                return;
            }

            closeOpenCardMoreActions(null);
        });
        document.body.appendChild(backdrop);
        cardMoreActionsBackdrop = backdrop;
        return backdrop;
    };

    const syncCardMoreActionsSheetState = () => {
        const hasVisibleMobileSheet = shouldUseMobileCardActionsSheet() &&
            document.querySelector("[data-card-more-actions][open], [data-card-more-actions].is-closing") instanceof HTMLDetailsElement;
        const backdrop = ensureCardMoreActionsBackdrop();
        if (hasVisibleMobileSheet) {
            if (document.querySelector("[data-card-more-actions][open]") instanceof HTMLDetailsElement) {
                cardMoreActionsLastOpenedAt = Date.now();
            }

            backdrop.removeAttribute("hidden");
            backdrop.classList.add("is-active");
            document.body.classList.add("aislepilot-mobile-meal-sheet-open");
        } else {
            backdrop.setAttribute("hidden", "hidden");
            backdrop.classList.remove("is-active");
            document.body.classList.remove("aislepilot-mobile-meal-sheet-open");
        }
    };

    const animateCardMoreActionsOpen = menu => {
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(actionsMenu instanceof HTMLElement) || !shouldUseMobileCardActionsSheet()) {
            return;
        }

        stopCardMoreActionsAnimation(menu);
        actionsMenu.style.transition = "none";
        actionsMenu.style.visibility = "hidden";
        actionsMenu.style.transform = "translate3d(0, 110%, 0)";
        void actionsMenu.offsetHeight;

        const beginAnimation = () => {
            if (!menu.open) {
                return;
            }

            actionsMenu.style.visibility = "visible";
            actionsMenu.style.transition = `transform ${cardMoreActionsCloseAnimationMs}ms cubic-bezier(0.16, 1, 0.3, 1)`;
            actionsMenu.style.transform = "translate3d(0, 0, 0)";
        };

        const frameId = window.requestAnimationFrame(beginAnimation);
        cardMoreActionsAnimationFrames.set(actionsMenu, frameId);
    };

    const finishClosingCardMoreActionsMenu = (menu, options = {}) => {
        if (!(menu instanceof HTMLDetailsElement)) {
            return;
        }

        clearCardMoreActionsCloseTimer(menu);
        stopCardMoreActionsAnimation(menu);
        menu.classList.remove("is-closing");
        clearCardMoreActionsMenuInlineStyles(menu);
        restoreCardMoreActionsPanelFromBody(menu);
        syncDayMealPanelOpenActionsState(menu);
        syncCardMoreActionsSheetState();
        updateViewportHeight(true);

        if (options.restoreFocus === true) {
            const trigger = menu.querySelector("summary");
            if (trigger instanceof HTMLElement) {
                trigger.focus({ preventScroll: true });
            }
        }
    };

    const closeCardMoreActionsMenu = (menu, options = {}) => {
        if (!(menu instanceof HTMLDetailsElement)) {
            return;
        }

        const restoreFocus = options.restoreFocus === true;
        if (menu.classList.contains("is-closing")) {
            finishClosingCardMoreActionsMenu(menu, { restoreFocus });
            return;
        }

        if (!menu.open) {
            finishClosingCardMoreActionsMenu(menu, { restoreFocus });
            return;
        }

        if (!shouldUseMobileCardActionsSheet()) {
            menu.open = false;
            finishClosingCardMoreActionsMenu(menu, { restoreFocus });
            return;
        }

        clearCardMoreActionsCloseTimer(menu);
        stopCardMoreActionsAnimation(menu);
        menu.classList.add("is-closing");
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(actionsMenu instanceof HTMLElement)) {
            menu.open = false;
            syncDayMealPanelOpenActionsState(menu);
            syncCardMoreActionsSheetState();
            updateViewportHeight(true);
            const closeTimerWithoutPanel = window.setTimeout(() => {
                finishClosingCardMoreActionsMenu(menu, { restoreFocus });
            }, cardMoreActionsCloseAnimationMs);
            cardMoreActionsCloseTimers.set(menu, closeTimerWithoutPanel);
            return;
        }

        if (actionsMenu instanceof HTMLElement) {
            actionsMenu.classList.add("is-closing");
            actionsMenu.style.visibility = "visible";
            actionsMenu.style.opacity = "1";
        }
        menu.open = false;
        syncDayMealPanelOpenActionsState(menu);
        syncCardMoreActionsSheetState();
        updateViewportHeight(true);
        actionsMenu.style.transition = `transform ${cardMoreActionsCloseAnimationMs}ms cubic-bezier(0.4, 0, 0.2, 1)`;
        void actionsMenu.offsetHeight;
        actionsMenu.style.transform = "translate3d(0, 110%, 0)";

        const closeTimer = window.setTimeout(() => {
            finishClosingCardMoreActionsMenu(menu, { restoreFocus });
        }, cardMoreActionsCloseAnimationMs);
        cardMoreActionsCloseTimers.set(menu, closeTimer);
    };

    const closeCardMoreActionsMenuImmediately = menu => {
        if (!(menu instanceof HTMLDetailsElement)) {
            return;
        }

        menu.open = false;
        finishClosingCardMoreActionsMenu(menu);
    };

    const restoreAllCardMoreActionsPanelsToMenus = () => {
        const menus = Array.from(document.querySelectorAll("[data-card-more-actions]"));
        menus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement)) {
                return;
            }

            const actionsMenu = getCardMoreActionsPanel(menu);
            const isPortaled = actionsMenu instanceof HTMLElement && actionsMenu.parentElement === document.body;
            if (!menu.open && !menu.classList.contains("is-closing") && !isPortaled) {
                return;
            }

            closeCardMoreActionsMenuImmediately(menu);
        });
    };

    const closeOpenCardMoreActions = except => {
        getVisibleCardMoreActionsMenus().forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement) || menu === except) {
                return;
            }

            closeCardMoreActionsMenu(menu);
        });

        window.requestAnimationFrame(() => {
            syncCardMoreActionsSheetState();
        });
    };

    const positionCardMoreActionsMenu = menu => {
        if (!(menu instanceof HTMLDetailsElement) || !menu.open) {
            return;
        }

        const trigger = menu.querySelector("summary");
        const actionsMenu = getCardMoreActionsPanel(menu);
        if (!(trigger instanceof HTMLElement) || !(actionsMenu instanceof HTMLElement)) {
            return;
        }

        const viewportPadding = 8;
        clearCardMoreActionsMenuInlineStyles(menu);
        menu.classList.remove("is-drop-up", "is-drop-down", "is-align-left");
        if (!shouldUseMobileCardActionsSheet()) {
            restoreCardMoreActionsPanelFromBody(menu);
            syncCardMoreActionsSheetState();
            return;
        }

        const mobileMaxHeight = Math.max(220, Math.floor((window.innerHeight - viewportPadding) * 0.72));
        actionsMenu.style.maxHeight = `${mobileMaxHeight}px`;
        actionsMenu.style.overflowY = "auto";
        syncCardMoreActionsSheetState();
    };

    const wireCardMoreActions = scope => {
        const menus = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-card-more-actions]"))
            : Array.from(document.querySelectorAll("[data-card-more-actions]"));

        menus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement) || menu.dataset.cardMoreActionsWired === "true") {
                return;
            }

            menu.dataset.cardMoreActionsWired = "true";
            const trigger = menu.querySelector("summary");
            if (trigger instanceof HTMLElement) {
                trigger.addEventListener("click", event => {
                    if (!menu.open || !shouldUseMobileCardActionsSheet()) {
                        return;
                    }

                    event.preventDefault();
                    closeCardMoreActionsMenu(menu, { restoreFocus: true });
                });
            }

            menu.addEventListener("toggle", () => {
                if (trigger instanceof HTMLElement) {
                    trigger.setAttribute("aria-expanded", menu.open ? "true" : "false");
                }

                if (!menu.open && !menu.classList.contains("is-closing")) {
                    clearCardMoreActionsMenuInlineStyles(menu);
                    restoreCardMoreActionsPanelFromBody(menu);
                }

                syncDayMealPanelOpenActionsState(menu);
                if (!menu.open && !menu.classList.contains("is-closing")) {
                    syncCardMoreActionsSheetState();
                }

                updateViewportHeight(true);

                if (menu.open) {
                    clearCardMoreActionsCloseTimer(menu);
                    stopCardMoreActionsAnimation(menu);
                    menu.classList.remove("is-closing");
                    const actionsMenu = moveCardMoreActionsPanelToBody(menu);
                    if (actionsMenu instanceof HTMLElement) {
                        actionsMenu.classList.remove("is-closing");
                    }
                    closeOpenCardMoreActions(menu);
                    positionCardMoreActionsMenu(menu);
                    window.requestAnimationFrame(() => {
                        positionCardMoreActionsMenu(menu);
                        animateCardMoreActionsOpen(menu);
                        if (!shouldUseMobileCardActionsSheet()) {
                            return;
                        }

                        const closeButton = actionsMenu instanceof HTMLElement
                            ? actionsMenu.querySelector("[data-card-more-actions-close]")
                            : null;
                        if (closeButton instanceof HTMLElement) {
                            closeButton.focus({ preventScroll: true });
                        }
                    });
                }
            });

            const closeButtons = Array.from(menu.querySelectorAll("[data-card-more-actions-close]"));
            closeButtons.forEach(closeButton => {
                if (!(closeButton instanceof HTMLButtonElement)) {
                    return;
                }

                closeButton.addEventListener("click", event => {
                    event.preventDefault();
                    closeCardMoreActionsMenu(menu, { restoreFocus: true });
                });
            });

            const actionButtons = Array.from(menu.querySelectorAll("button[type='submit']"));
            actionButtons.forEach(button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                button.addEventListener("click", event => {
                    const form = button.closest("form");
                    const dayInput = form instanceof HTMLFormElement
                        ? form.querySelector("input[name='dayIndex']")
                        : null;
                    writeSwapDebug("menu-submit-click", {
                        ariaLabel: button.getAttribute("aria-label") ?? "",
                        formAction: form instanceof HTMLFormElement ? form.getAttribute("action") ?? "" : "",
                        dayIndex: dayInput instanceof HTMLInputElement ? dayInput.value : "",
                        portaled: shouldUseMobileCardActionsSheet()
                    });

                    if (!(form instanceof HTMLFormElement)) {
                        return;
                    }

                    event.preventDefault();
                    if (form.hasAttribute("data-ajax-swap-form")) {
                        writeSwapDebug("menu-submit-direct-handler", {
                            formAction: form.getAttribute("action") ?? "",
                            dayIndex: dayInput instanceof HTMLInputElement ? dayInput.value : ""
                        });
                        void handleAjaxSwapFormSubmit(form, button);
                        return;
                    }

                    if (typeof form.requestSubmit === "function") {
                        writeSwapDebug("menu-submit-request-submit", {
                            formAction: form.getAttribute("action") ?? "",
                            dayIndex: dayInput instanceof HTMLInputElement ? dayInput.value : ""
                        });
                        form.requestSubmit(button);
                        return;
                    }

                    writeSwapDebug("menu-submit-native-submit-fallback", {
                        formAction: form.getAttribute("action") ?? "",
                        dayIndex: dayInput instanceof HTMLInputElement ? dayInput.value : ""
                    });
                    HTMLFormElement.prototype.submit.call(form);
                });
            });
        });

        if (cardMoreActionsGlobalWired) {
            return;
        }

        cardMoreActionsGlobalWired = true;
        document.addEventListener("click", event => {
            if (!(event.target instanceof Element)) {
                return;
            }

            if (event.target.closest("[data-card-more-actions]") || event.target.closest("[data-card-more-actions-panel]")) {
                return;
            }

            closeOpenCardMoreActions(null);
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeOpenCardMoreActions(null);
            }
        });

        window.addEventListener("resize", () => {
            const visibleMenus = getVisibleCardMoreActionsMenus();
            visibleMenus.forEach(openMenu => {
                if (!(openMenu instanceof HTMLDetailsElement)) {
                    return;
                }

                if (!openMenu.open) {
                    finishClosingCardMoreActionsMenu(openMenu);
                    return;
                }

                positionCardMoreActionsMenu(openMenu);
            });
            updateViewportHeight(true);
        });

        syncCardMoreActionsSheetState();
    };

    const wireInlineDetailsPanels = scope => {
        const toggles = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-inline-details-toggle]"))
            : Array.from(document.querySelectorAll("[data-inline-details-toggle]"));

        toggles.forEach(toggle => {
            if (!(toggle instanceof HTMLDetailsElement) || toggle.dataset.inlineDetailsWired === "true") {
                return;
            }

            const mealPanel = toggle.closest("[data-day-meal-panel]");
            if (!(mealPanel instanceof HTMLElement)) {
                return;
            }

            const detailsPanel = mealPanel.querySelector("[data-inline-details-panel]");
            if (!(detailsPanel instanceof HTMLElement)) {
                return;
            }

            toggle.dataset.inlineDetailsWired = "true";

            const syncDetailsPanel = () => {
                const summary = toggle.querySelector("summary");
                const isExpanded = toggle.open;
                if (summary instanceof HTMLElement) {
                    summary.setAttribute("aria-expanded", isExpanded ? "true" : "false");
                }

                if (isExpanded) {
                    detailsPanel.removeAttribute("hidden");
                    detailsPanel.setAttribute("aria-hidden", "false");
                } else {
                    detailsPanel.setAttribute("hidden", "hidden");
                    detailsPanel.setAttribute("aria-hidden", "true");
                }

                updateViewportHeight(true);
            };

            toggle.addEventListener("toggle", syncDetailsPanel);
            syncDetailsPanel();
        });
    };

    const wireDayMealCards = scope => {
        const cards = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-day-meal-card]"))
            : Array.from(document.querySelectorAll("[data-day-meal-card]"));

        cards.forEach(card => {
            if (!(card instanceof HTMLElement) || card.dataset.dayMealCardWired === "true") {
                return;
            }

            const tabs = Array.from(card.querySelectorAll("[data-day-meal-tab]"));
            const panels = Array.from(card.querySelectorAll("[data-day-meal-panel]"));
            const track = card.querySelector("[data-day-meal-track]");
            if (!(track instanceof HTMLElement) || tabs.length <= 1 || panels.length <= 1) {
                return;
            }

            card.dataset.dayMealCardWired = "true";
            let currentSlotIndex = 0;

            const syncSlot = nextIndex => {
                const slotCount = Math.min(tabs.length, panels.length);
                if (slotCount <= 0) {
                    return;
                }

                currentSlotIndex = ((nextIndex % slotCount) + slotCount) % slotCount;
                track.style.transform = `translateX(-${currentSlotIndex * 100}%)`;

                tabs.forEach((tab, index) => {
                    if (!(tab instanceof HTMLButtonElement)) {
                        return;
                    }

                    const isActive = index === currentSlotIndex;
                    tab.classList.toggle("is-active", isActive);
                    tab.setAttribute("aria-selected", isActive ? "true" : "false");
                    tab.setAttribute("tabindex", isActive ? "0" : "-1");
                });

                panels.forEach((panel, index) => {
                    if (!(panel instanceof HTMLElement)) {
                        return;
                    }

                    const isActive = index === currentSlotIndex;
                    panel.setAttribute("aria-hidden", isActive ? "false" : "true");
                    panel.setAttribute("tabindex", isActive ? "0" : "-1");
                });

                const summaryLabel = card.querySelector("[data-day-card-summary]");
                if (summaryLabel instanceof HTMLElement) {
                    const activeTab = tabs[currentSlotIndex];
                    const summaryValue = activeTab instanceof HTMLButtonElement
                        ? (activeTab.dataset.dayCardSummaryValue ?? "").trim()
                        : "";
                    const defaultSummaryValue = (summaryLabel.dataset.dayCardSummaryDefault ?? "").trim();
                    const nextSummaryValue = summaryValue.length > 0 ? summaryValue : defaultSummaryValue;
                    if (nextSummaryValue.length > 0) {
                        summaryLabel.textContent = nextSummaryValue;
                    }
                }

                const expanderMealLabel = card.querySelector("[data-day-card-expander-meal]");
                const expanderMetaLabel = card.querySelector("[data-day-card-expander-meta]");
                const expanderImage = card.querySelector("[data-day-card-expander-image]");
                const activeTab = tabs[currentSlotIndex];
                if (expanderMealLabel instanceof HTMLElement) {
                    const mealValue = activeTab instanceof HTMLButtonElement
                        ? (activeTab.dataset.dayCardMealName ?? "").trim()
                        : "";
                    const defaultMealValue = (expanderMealLabel.dataset.dayCardExpanderMealDefault ?? "").trim();
                    const nextMealValue = mealValue.length > 0 ? mealValue : defaultMealValue;
                    if (nextMealValue.length > 0) {
                        expanderMealLabel.textContent = nextMealValue;
                    }
                }

                if (expanderMetaLabel instanceof HTMLElement) {
                    const metaValue = activeTab instanceof HTMLButtonElement
                        ? (activeTab.dataset.dayCardSummaryValue ?? "").trim()
                        : "";
                    const defaultMetaValue = (expanderMetaLabel.dataset.dayCardExpanderMetaDefault ?? "").trim();
                    const nextMetaValue = metaValue.length > 0 ? metaValue : defaultMetaValue;
                    if (nextMetaValue.length > 0) {
                        expanderMetaLabel.textContent = nextMetaValue;
                    }
                }

                if (expanderImage instanceof HTMLImageElement) {
                    const imageValue = activeTab instanceof HTMLButtonElement
                        ? (activeTab.dataset.dayCardMealImageUrl ?? "").trim()
                        : "";
                    const defaultImageValue = (expanderImage.dataset.dayCardExpanderImageDefault ?? "").trim();
                    const nextImageValue = imageValue.length > 0 ? imageValue : defaultImageValue;
                    const currentImageValue = (expanderImage.getAttribute("src") ?? "").trim();
                    if (nextImageValue.length > 0 && currentImageValue !== nextImageValue) {
                        expanderImage.src = nextImageValue;
                    }
                }

                const headerActions = Array.from(card.querySelectorAll("[data-day-card-header-actions]"));
                headerActions.forEach(actions => {
                    if (!(actions instanceof HTMLElement)) {
                        return;
                    }

                    const slotIndex = Number.parseInt(actions.dataset.slotIndex ?? "-1", 10);
                    const isActive = slotIndex === currentSlotIndex;
                    actions.classList.toggle("is-active", isActive);
                    actions.setAttribute("aria-hidden", isActive ? "false" : "true");

                    if (!isActive) {
                        const openMenu = actions.querySelector("[data-card-more-actions][open]");
                        if (openMenu instanceof HTMLDetailsElement) {
                            openMenu.open = false;
                        }
                    }
                });

                const dayKey = readCardDayKey(card);
                if (dayKey) {
                    dayMealSlotState.set(dayKey, currentSlotIndex);
                }

                updateViewportHeight(true);
            };

            tabs.forEach((tab, index) => {
                if (!(tab instanceof HTMLButtonElement)) {
                    return;
                }

                tab.addEventListener("click", () => {
                    syncSlot(index);
                });

                tab.addEventListener("keydown", event => {
                    if (event.key === "ArrowRight" || event.key === "ArrowDown") {
                        event.preventDefault();
                        const nextIndex = (currentSlotIndex + 1) % tabs.length;
                        syncSlot(nextIndex);
                        const nextTab = tabs[nextIndex];
                        if (nextTab instanceof HTMLButtonElement) {
                            nextTab.focus();
                        }
                        return;
                    }

                    if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
                        event.preventDefault();
                        const nextIndex = (currentSlotIndex - 1 + tabs.length) % tabs.length;
                        syncSlot(nextIndex);
                        const nextTab = tabs[nextIndex];
                        if (nextTab instanceof HTMLButtonElement) {
                            nextTab.focus();
                        }
                        return;
                    }

                    if (event.key === "Home") {
                        event.preventDefault();
                        syncSlot(0);
                        const firstTab = tabs[0];
                        if (firstTab instanceof HTMLButtonElement) {
                            firstTab.focus();
                        }
                        return;
                    }

                    if (event.key === "End") {
                        event.preventDefault();
                        const lastIndex = tabs.length - 1;
                        syncSlot(lastIndex);
                        const lastTab = tabs[lastIndex];
                        if (lastTab instanceof HTMLButtonElement) {
                            lastTab.focus();
                        }
                    }
                });
            });

            syncSlot(readRememberedDayMealSlot(card));
        });
    };

    const parseDayCardJsonArray = (serializedValue, itemMapper) => {
        if (typeof serializedValue !== "string" || serializedValue.trim().length === 0) {
            return [];
        }

        try {
            const parsed = JSON.parse(serializedValue);
            if (!Array.isArray(parsed)) {
                return [];
            }

            return typeof itemMapper === "function"
                ? parsed.map(itemMapper)
                : parsed;
        } catch {
            return [];
        }
    };

    const readDayCardMealNames = card => {
        if (!(card instanceof HTMLElement)) {
            return [];
        }

        return parseDayCardJsonArray(card.dataset.dayCardMealNames, value =>
            typeof value === "string" ? value.trim() : "").filter(value => value.length > 0);
    };

    const readDayCardIgnoredFlags = card => {
        if (!(card instanceof HTMLElement)) {
            return [];
        }

        const mealNames = readDayCardMealNames(card);
        const parsedFlags = parseDayCardJsonArray(card.dataset.dayCardIgnoredFlags, value => value === true);
        if (parsedFlags.length === mealNames.length) {
            return parsedFlags;
        }

        return mealNames.map(() => false);
    };

    const getReorderableDayCards = scope => {
        const root = scope instanceof Element ? scope : document;
        return Array.from(root.querySelectorAll("[data-day-meal-card][data-day-card-meal-names]"))
            .filter(card => card instanceof HTMLElement);
    };

    const buildDayCardCurrentPlanMealNames = cards => {
        const mealNames = [];
        cards.forEach(card => {
            readDayCardMealNames(card).forEach(mealName => {
                if (mealName.length > 0) {
                    mealNames.push(mealName);
                }
            });
        });
        return mealNames;
    };

    const buildDayCardIgnoredIndexesCsv = cards => {
        const ignoredIndexes = [];
        let slotIndex = 0;
        cards.forEach(card => {
            readDayCardIgnoredFlags(card).forEach(isIgnored => {
                if (isIgnored) {
                    ignoredIndexes.push(slotIndex);
                }

                slotIndex += 1;
            });
        });

        return ignoredIndexes.join(",");
    };

    const buildDayCardLeftoverSourceIndexesCsv = cards => {
        const sourceIndexes = [];
        let weekDayIndex = 0;
        cards.forEach(card => {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const leftoverCount = Number.parseInt(card.dataset.dayCardLeftoverCount ?? "0", 10);
            const safeLeftoverCount = Number.isInteger(leftoverCount)
                ? Math.max(0, leftoverCount)
                : 0;
            for (let index = 0; index < safeLeftoverCount; index += 1) {
                sourceIndexes.push(weekDayIndex);
            }

            weekDayIndex += Math.max(1, safeLeftoverCount + 1);
        });

        return sourceIndexes.join(",");
    };

    const syncDayReorderFormState = form => {
        if (!(form instanceof HTMLFormElement)) {
            return false;
        }

        const cards = getReorderableDayCards(form.parentElement ?? document);
        const reorderedMealNames = buildDayCardCurrentPlanMealNames(cards);
        if (reorderedMealNames.length <= 1) {
            return false;
        }

        const leftoverInput = form.querySelector("input[name='Request.LeftoverCookDayIndexesCsv']");
        if (leftoverInput instanceof HTMLInputElement) {
            leftoverInput.value = buildDayCardLeftoverSourceIndexesCsv(cards);
        }

        const ignoredInput = form.querySelector("input[name='Request.IgnoredMealSlotIndexesCsv']");
        if (ignoredInput instanceof HTMLInputElement) {
            ignoredInput.value = buildDayCardIgnoredIndexesCsv(cards);
        }

        const swapHistoryInput = form.querySelector("input[name='Request.SwapHistoryState']");
        if (swapHistoryInput instanceof HTMLInputElement) {
            swapHistoryInput.value = "";
        }

        const specialTreatInput = form.querySelector("input[name='Request.SelectedSpecialTreatCookDayIndex']");
        if (specialTreatInput instanceof HTMLInputElement) {
            const specialTreatCardIndex = cards.findIndex(card =>
                card instanceof HTMLElement && card.dataset.dayCardHasSpecialTreat === "true");
            if (specialTreatCardIndex >= 0) {
                specialTreatInput.value = `${specialTreatCardIndex}`;
            }
        }

        Array.from(form.querySelectorAll("input[name='currentPlanMealNames']")).forEach(input => {
            input.remove();
        });

        reorderedMealNames.forEach(mealName => {
            const hiddenInput = document.createElement("input");
            hiddenInput.type = "hidden";
            hiddenInput.name = "currentPlanMealNames";
            hiddenInput.value = mealName;
            form.append(hiddenInput);
        });

        return true;
    };

    const wireDayCardReorder = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-day-reorder-form]"))
            : Array.from(document.querySelectorAll("[data-day-reorder-form]"));

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement) || form.dataset.dayReorderWired === "true") {
                return;
            }

            const root = form.parentElement;
            if (!(root instanceof Element)) {
                return;
            }

            const handles = Array.from(root.querySelectorAll("[data-day-reorder-handle]"));
            if (handles.length === 0) {
                return;
            }

            form.dataset.dayReorderWired = "true";
            let activePointerId = null;
            let activeHandle = null;
            let activeCard = null;
            let pointerStartY = 0;
            let lastSwapY = 0;
            let hasMoved = false;
            let activationTimerId = null;
            let reorderActivated = false;

            const clearActivationTimer = () => {
                if (typeof activationTimerId === "number") {
                    window.clearTimeout(activationTimerId);
                }

                activationTimerId = null;
            };

            const activateReorder = () => {
                if (!(activeCard instanceof HTMLElement) || !(activeHandle instanceof HTMLElement) || reorderActivated) {
                    return;
                }

                reorderActivated = true;
                activeCard.classList.add("is-reorder-active");
                activeHandle.classList.add("is-reorder-active");
            };

            const cleanupReorder = shouldSubmit => {
                clearActivationTimer();
                if (activeCard instanceof HTMLElement) {
                    activeCard.classList.remove("is-reorder-active");
                }

                if (activeHandle instanceof HTMLElement) {
                    activeHandle.classList.remove("is-reorder-active");
                }

                const reorderChanged = hasMoved;
                activePointerId = null;
                activeHandle = null;
                activeCard = null;
                hasMoved = false;
                reorderActivated = false;

                if (!shouldSubmit || !reorderChanged || form.dataset.ajaxSwapSubmitting === "true") {
                    return;
                }

                if (!syncDayReorderFormState(form)) {
                    return;
                }

                persistSwapScrollPosition(form);
                form.requestSubmit();
            };

            const moveCardInDirection = direction => {
                if (!(activeCard instanceof HTMLElement)) {
                    return false;
                }

                const cards = getReorderableDayCards(root);
                const currentIndex = cards.indexOf(activeCard);
                if (currentIndex < 0) {
                    return false;
                }

                if (direction < 0 && currentIndex > 0) {
                    cards[currentIndex - 1].before(activeCard);
                    return true;
                }

                if (direction > 0 && currentIndex < cards.length - 1) {
                    cards[currentIndex + 1].after(activeCard);
                    return true;
                }

                return false;
            };

            handles.forEach(handle => {
                if (!(handle instanceof HTMLElement) || handle.dataset.dayReorderHandleWired === "true") {
                    return;
                }

                handle.dataset.dayReorderHandleWired = "true";
                handle.addEventListener("click", event => {
                    event.preventDefault();
                    event.stopPropagation();
                });

                handle.addEventListener("pointerdown", event => {
                    if (form.dataset.ajaxSwapSubmitting === "true") {
                        return;
                    }

                    if (event.pointerType === "mouse" && event.button !== 0) {
                        return;
                    }

                    const card = handle.closest("[data-day-meal-card][data-day-card-meal-names]");
                    if (!(card instanceof HTMLElement)) {
                        return;
                    }

                    activePointerId = event.pointerId;
                    activeHandle = handle;
                    activeCard = card;
                    pointerStartY = event.clientY;
                    lastSwapY = event.clientY;
                    hasMoved = false;
                    reorderActivated = false;
                    clearActivationTimer();
                    activationTimerId = window.setTimeout(() => {
                        activateReorder();
                    }, 140);
                    handle.setPointerCapture(event.pointerId);
                    event.preventDefault();
                    event.stopPropagation();
                });

                handle.addEventListener("pointermove", event => {
                    if (activePointerId === null || event.pointerId !== activePointerId) {
                        return;
                    }

                    if (!reorderActivated && Math.abs(event.clientY - pointerStartY) >= 8) {
                        activateReorder();
                    }

                    if (!reorderActivated) {
                        event.preventDefault();
                        return;
                    }

                    const movementThreshold = Math.max(
                        32,
                        activeCard instanceof HTMLElement ? Math.round(activeCard.getBoundingClientRect().height * 0.22) : 32);
                    const deltaY = event.clientY - lastSwapY;
                    if (Math.abs(deltaY) < movementThreshold) {
                        event.preventDefault();
                        return;
                    }

                    const moved = moveCardInDirection(deltaY < 0 ? -1 : 1);
                    if (moved) {
                        hasMoved = true;
                        lastSwapY = event.clientY;
                    }

                    event.preventDefault();
                });

                handle.addEventListener("pointerup", event => {
                    if (activePointerId === null || event.pointerId !== activePointerId) {
                        return;
                    }

                    try {
                        handle.releasePointerCapture(event.pointerId);
                    } catch {
                        // Pointer capture can already be released if the browser cancels the gesture.
                    }
                    event.preventDefault();
                    cleanupReorder(true);
                });

                handle.addEventListener("pointercancel", event => {
                    if (activePointerId === null || event.pointerId !== activePointerId) {
                        return;
                    }

                    try {
                        handle.releasePointerCapture(event.pointerId);
                    } catch {
                        // Pointer capture can already be released if the browser cancels the gesture.
                    }
                    cleanupReorder(false);
                });
            });
        });
    };

    const wireDayCardCarousel = scope => {
        const carousels = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-day-card-carousel]"))
            : Array.from(document.querySelectorAll("[data-day-card-carousel]"));

        carousels.forEach(carousel => {
            if (!(carousel instanceof HTMLElement) || carousel.dataset.dayCardCarouselWired === "true") {
                return;
            }

            const viewport = carousel.querySelector("[data-day-carousel-viewport]");
            const track = carousel.querySelector("[data-day-carousel-track]");
            const status = carousel.querySelector("[data-day-carousel-status]");
            const previousButton = carousel.querySelector("[data-day-carousel-prev]");
            const nextButton = carousel.querySelector("[data-day-carousel-next]");
            const pagination = carousel.querySelector("[data-day-carousel-pagination]");
            const dots = Array.from(carousel.querySelectorAll("[data-day-carousel-dot]"));
            if (!(viewport instanceof HTMLElement) || !(track instanceof HTMLElement)) {
                return;
            }

            carousel.dataset.dayCardCarouselWired = "true";
            let activeIndex = 0;
            let scrollSyncFrame = 0;
            let motionTimer = 0;
            let scrollSettleTimer = 0;
            let suppressScrollChromeSync = false;
            const prefersReducedMotion = typeof window.matchMedia === "function"
                && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

            const getSlides = () => Array.from(track.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"))
                .filter(slide => slide instanceof HTMLElement);

            const createGhostSlide = side => {
                const ghost = document.createElement("article");
                ghost.className = "aislepilot-card aislepilot-day-card is-carousel-ghost";
                ghost.setAttribute("data-day-card-slide", "");
                ghost.setAttribute("data-day-carousel-ghost", "true");
                ghost.setAttribute("data-day-carousel-ghost-side", side);
                ghost.setAttribute("data-day-carousel-position", "far");
                ghost.setAttribute("aria-hidden", "true");
                ghost.setAttribute("inert", "");

                const body = document.createElement("div");
                body.className = "aislepilot-day-card-body aislepilot-day-card-body--ghost";
                body.setAttribute("aria-hidden", "true");

                const placeholder = document.createElement("div");
                placeholder.className = "aislepilot-day-card-ghost-shell";
                placeholder.innerHTML = `
                    <span class="aislepilot-day-card-ghost-band is-top"></span>
                    <span class="aislepilot-day-card-ghost-band is-mid"></span>
                    <span class="aislepilot-day-card-ghost-panel"></span>
                `;

                body.appendChild(placeholder);
                ghost.appendChild(body);
                return ghost;
            };

            const ensureGhostSlides = () => {
                Array.from(track.querySelectorAll("[data-day-carousel-ghost='true']")).forEach(ghost => {
                    ghost.remove();
                });

                const slides = getSlides();
                if (slides.length <= 1) {
                    return;
                }

                const firstSlide = slides[0];
                const lastSlide = slides[slides.length - 1];
                if (!(firstSlide instanceof HTMLElement) || !(lastSlide instanceof HTMLElement)) {
                    return;
                }

                const leadingGhost = createGhostSlide("leading");
                const trailingGhost = createGhostSlide("trailing");
                track.insertBefore(leadingGhost, firstSlide);
                track.appendChild(trailingGhost);
            };

            const clampIndex = nextIndex => {
                const slides = getSlides();
                if (slides.length === 0) {
                    return 0;
                }

                if (!Number.isInteger(nextIndex)) {
                    return activeIndex;
                }

                return Math.max(0, Math.min(slides.length - 1, nextIndex));
            };

            const clearMotionState = () => {
                if (motionTimer > 0) {
                    window.clearTimeout(motionTimer);
                    motionTimer = 0;
                }

                delete carousel.dataset.dayCarouselMotion;
                getSlides().forEach(slide => {
                    if (slide instanceof HTMLElement) {
                        delete slide.dataset.dayCarouselSettling;
                    }
                });

                dots.forEach(dot => {
                    if (dot instanceof HTMLElement) {
                        delete dot.dataset.dayCarouselSettling;
                    }
                });
            };

            const pulseActiveState = (nextIndex, motion) => {
                clearMotionState();

                const slides = getSlides();
                const targetSlide = slides[clampIndex(nextIndex)];
                const targetDot = dots[clampIndex(nextIndex)];
                if (targetSlide instanceof HTMLElement) {
                    targetSlide.dataset.dayCarouselSettling = "true";
                }

                if (targetDot instanceof HTMLElement) {
                    targetDot.dataset.dayCarouselSettling = "true";
                }

                if (typeof motion === "string" && motion.length > 0) {
                    carousel.dataset.dayCarouselMotion = motion;
                }

                motionTimer = window.setTimeout(() => {
                    clearMotionState();
                }, 320);
            };

            const clearScrollSettleTimer = () => {
                if (scrollSettleTimer > 0) {
                    window.clearTimeout(scrollSettleTimer);
                    scrollSettleTimer = 0;
                }
            };

            const computeTargetScrollLeft = targetSlide => {
                if (!(targetSlide instanceof HTMLElement)) {
                    return 0;
                }

                const viewportRect = viewport.getBoundingClientRect();
                const targetRect = targetSlide.getBoundingClientRect();
                const viewportCenter = viewportRect.left + (viewportRect.width / 2);
                const targetCenter = targetRect.left + (targetRect.width / 2);
                const maxScrollLeft = Math.max(0, viewport.scrollWidth - viewport.clientWidth);
                const centeredLeft = viewport.scrollLeft + (targetCenter - viewportCenter);
                return Math.max(0, Math.min(maxScrollLeft, Math.round(centeredLeft)));
            };

            const scrollPaginationToActiveDot = behavior => {
                if (!(pagination instanceof HTMLElement)) {
                    return;
                }

                const activeDot = dots[activeIndex];
                if (!(activeDot instanceof HTMLElement)) {
                    return;
                }

                const maxScrollLeft = Math.max(0, pagination.scrollWidth - pagination.clientWidth);
                if (maxScrollLeft <= 0) {
                    return;
                }

                const targetLeft = activeDot.offsetLeft - ((pagination.clientWidth - activeDot.offsetWidth) / 2);
                pagination.scrollTo({
                    left: Math.max(0, Math.min(maxScrollLeft, Math.round(targetLeft))),
                    behavior
                });
            };

            const updateChrome = (nextIndex, options = {}) => {
                const slides = getSlides();
                if (slides.length === 0) {
                    return;
                }

                const previousActiveIndex = activeIndex;
                activeIndex = clampIndex(nextIndex);
                slides.forEach((slide, index) => {
                    if (!(slide instanceof HTMLElement)) {
                        return;
                    }

                    const isActive = index === activeIndex;
                    const offset = index - activeIndex;
                    let position = "far";
                    if (offset === 0) {
                        position = "active";
                    } else if (offset === -1) {
                        position = "prev";
                    } else if (offset === 1) {
                        position = "next";
                    }

                    slide.setAttribute("aria-hidden", isActive ? "false" : "true");
                    slide.dataset.dayCarouselPosition = position;
                });

                const leadingGhost = track.querySelector("[data-day-carousel-ghost-side='leading']");
                if (leadingGhost instanceof HTMLElement) {
                    leadingGhost.dataset.dayCarouselPosition = activeIndex === 0 ? "prev" : "far";
                }

                const trailingGhost = track.querySelector("[data-day-carousel-ghost-side='trailing']");
                if (trailingGhost instanceof HTMLElement) {
                    trailingGhost.dataset.dayCarouselPosition = activeIndex === slides.length - 1 ? "next" : "far";
                }

                dots.forEach(dot => {
                    if (!(dot instanceof HTMLButtonElement)) {
                        return;
                    }

                    const dotIndex = Number.parseInt(dot.dataset.dayCarouselTarget ?? "-1", 10);
                    const isActive = dotIndex === activeIndex;
                    dot.classList.toggle("is-active", isActive);
                    dot.setAttribute("aria-selected", isActive ? "true" : "false");
                    dot.setAttribute("tabindex", isActive ? "0" : "-1");
                    dot.setAttribute("aria-current", isActive ? "true" : "false");
                });

                if (previousButton instanceof HTMLButtonElement) {
                    previousButton.disabled = slides.length <= 1;
                }

                if (nextButton instanceof HTMLButtonElement) {
                    nextButton.disabled = slides.length <= 1;
                }

                if (status instanceof HTMLElement) {
                    const activeSlide = slides[activeIndex];
                    const dayName = activeSlide instanceof HTMLElement
                        ? (activeSlide.dataset.dayCardDayName ?? "").trim()
                        : "";
                    const dayPositionRaw = activeSlide instanceof HTMLElement
                        ? Number.parseInt(activeSlide.dataset.dayCardDayPosition ?? "", 10)
                        : Number.NaN;
                    const totalDaysRaw = activeSlide instanceof HTMLElement
                        ? Number.parseInt(activeSlide.dataset.dayCardTotalDays ?? "", 10)
                        : Number.NaN;
                    const dayPosition = Number.isInteger(dayPositionRaw) && dayPositionRaw > 0 ? dayPositionRaw : activeIndex + 1;
                    const totalDays = Number.isInteger(totalDaysRaw) && totalDaysRaw > 0 ? totalDaysRaw : slides.length;
                    status.textContent = dayName.length > 0
                        ? `${dayName}, ${dayPosition} of ${totalDays}`
                        : `Day ${dayPosition} of ${totalDays}`;
                }

                if (previousButton instanceof HTMLButtonElement) {
                    const previousIndex = activeIndex === 0 ? slides.length - 1 : activeIndex - 1;
                    const previousDayName = slides[previousIndex] instanceof HTMLElement
                        ? (slides[previousIndex].dataset.dayCardDayName ?? "").trim()
                        : "";
                    previousButton.setAttribute("aria-label", previousDayName.length > 0
                        ? `Show ${previousDayName}`
                        : "Show previous day");
                }

                if (nextButton instanceof HTMLButtonElement) {
                    const nextWrappedIndex = activeIndex === slides.length - 1 ? 0 : activeIndex + 1;
                    const nextDayName = slides[nextWrappedIndex] instanceof HTMLElement
                        ? (slides[nextWrappedIndex].dataset.dayCardDayName ?? "").trim()
                        : "";
                    nextButton.setAttribute("aria-label", nextDayName.length > 0
                        ? `Show ${nextDayName}`
                        : "Show next day");
                }

                if (options.forcePaginationSync === true || activeIndex !== previousActiveIndex) {
                    scrollPaginationToActiveDot(options.paginationBehavior === "smooth" ? "smooth" : "auto");
                }
            };

            const scrollToIndex = (nextIndex, behavior, motion = "") => {
                const slides = getSlides();
                if (slides.length === 0) {
                    return;
                }

                activeIndex = clampIndex(nextIndex);
                const resolvedBehavior = prefersReducedMotion ? "auto" : behavior;
                if (scrollSyncFrame > 0) {
                    window.cancelAnimationFrame(scrollSyncFrame);
                    scrollSyncFrame = 0;
                }

                // Keep the status label and day pills locked to the requested day
                // while smooth scrolling passes over intermediate slides.
                suppressScrollChromeSync = resolvedBehavior === "smooth" && slides.length > 1;
                clearScrollSettleTimer();
                updateChrome(activeIndex, {
                    paginationBehavior: resolvedBehavior,
                    forcePaginationSync: true
                });
                const targetSlide = slides[activeIndex];
                if (!(targetSlide instanceof HTMLElement)) {
                    return;
                }

                pulseActiveState(activeIndex, motion);
                const targetScrollLeft = computeTargetScrollLeft(targetSlide);
                viewport.scrollTo({
                    left: targetScrollLeft,
                    behavior: resolvedBehavior
                });

                if (suppressScrollChromeSync) {
                    scrollSettleTimer = window.setTimeout(() => {
                        suppressScrollChromeSync = false;
                        clearScrollSettleTimer();
                        updateChrome(findClosestSlideIndex(), { forcePaginationSync: true });
                        updateViewportHeight(true);
                    }, 180);
                }

                updateViewportHeight(true);
            };

            const navigateBySwipeDelta = deltaX => {
                const slides = getSlides();
                if (slides.length <= 1 || Math.abs(deltaX) < 48) {
                    return false;
                }

                if (deltaX < 0) {
                    const isWrapping = activeIndex === slides.length - 1;
                    const nextWrappedIndex = isWrapping ? 0 : activeIndex + 1;
                    scrollToIndex(nextWrappedIndex, isWrapping ? "auto" : "smooth", "next");
                    return true;
                }

                const isWrapping = activeIndex === 0;
                const previousIndex = isWrapping ? slides.length - 1 : activeIndex - 1;
                scrollToIndex(previousIndex, isWrapping ? "auto" : "smooth", "prev");
                return true;
            };

            const wireMealImageSwipeSurface = swipeSurface => {
                if (!(swipeSurface instanceof HTMLElement) || swipeSurface.dataset.dayMealSwipeSurfaceWired === "true") {
                    return;
                }

                swipeSurface.dataset.dayMealSwipeSurfaceWired = "true";
                let touchStartX = 0;
                let touchStartY = 0;
                let suppressClickUntil = 0;

                swipeSurface.addEventListener("touchstart", event => {
                    const touch = event.changedTouches[0];
                    if (!touch) {
                        return;
                    }

                    touchStartX = touch.clientX;
                    touchStartY = touch.clientY;
                }, { passive: true });

                swipeSurface.addEventListener("touchmove", event => {
                    const touch = event.changedTouches[0];
                    if (!touch) {
                        return;
                    }

                    const deltaX = touch.clientX - touchStartX;
                    const deltaY = touch.clientY - touchStartY;
                    if (Math.abs(deltaX) >= 12 && Math.abs(deltaX) > Math.abs(deltaY)) {
                        event.preventDefault();
                    }
                }, { passive: false });

                swipeSurface.addEventListener("touchend", event => {
                    const touch = event.changedTouches[0];
                    if (!touch) {
                        return;
                    }

                    const deltaX = touch.clientX - touchStartX;
                    const deltaY = touch.clientY - touchStartY;
                    if (Math.abs(deltaX) <= Math.abs(deltaY)) {
                        return;
                    }

                    const didNavigate = navigateBySwipeDelta(deltaX);
                    if (!didNavigate) {
                        return;
                    }

                    suppressClickUntil = Date.now() + 400;
                    event.preventDefault();
                    event.stopPropagation();
                }, { passive: false });

                swipeSurface.addEventListener("touchcancel", () => {
                    touchStartX = 0;
                    touchStartY = 0;
                }, { passive: true });

                swipeSurface.addEventListener("click", event => {
                    if (Date.now() <= suppressClickUntil) {
                        event.preventDefault();
                        event.stopPropagation();
                    }
                });
            };

            const findClosestSlideIndex = () => {
                const slides = getSlides();
                if (slides.length === 0) {
                    return 0;
                }

                const viewportRect = viewport.getBoundingClientRect();
                const viewportCenter = viewportRect.left + (viewportRect.width / 2);
                let closestIndex = activeIndex;
                let closestDistance = Number.POSITIVE_INFINITY;

                slides.forEach((slide, index) => {
                    if (!(slide instanceof HTMLElement)) {
                        return;
                    }

                    const slideRect = slide.getBoundingClientRect();
                    const slideCenter = slideRect.left + (slideRect.width / 2);
                    const distance = Math.abs(slideCenter - viewportCenter);
                    if (distance < closestDistance) {
                        closestDistance = distance;
                        closestIndex = index;
                    }
                });

                return closestIndex;
            };

            const syncFromScroll = () => {
                scrollSyncFrame = 0;
                updateChrome(findClosestSlideIndex());
            };

            viewport.addEventListener("scroll", () => {
                if (suppressScrollChromeSync) {
                    clearScrollSettleTimer();
                    scrollSettleTimer = window.setTimeout(() => {
                        suppressScrollChromeSync = false;
                        clearScrollSettleTimer();
                        updateChrome(findClosestSlideIndex(), { forcePaginationSync: true });
                        updateViewportHeight(true);
                    }, 180);
                    return;
                }

                if (scrollSyncFrame > 0) {
                    return;
                }

                scrollSyncFrame = window.requestAnimationFrame(syncFromScroll);
            }, { passive: true });

            if (previousButton instanceof HTMLButtonElement) {
                previousButton.addEventListener("click", () => {
                    const slides = getSlides();
                    if (slides.length <= 1) {
                        return;
                    }

                    const isWrapping = activeIndex === 0;
                    const previousIndex = isWrapping ? slides.length - 1 : activeIndex - 1;
                    scrollToIndex(previousIndex, isWrapping ? "auto" : "smooth", "prev");
                });
            }

            if (nextButton instanceof HTMLButtonElement) {
                nextButton.addEventListener("click", () => {
                    const slides = getSlides();
                    if (slides.length <= 1) {
                        return;
                    }

                    const isWrapping = activeIndex === slides.length - 1;
                    const nextWrappedIndex = isWrapping ? 0 : activeIndex + 1;
                    scrollToIndex(nextWrappedIndex, isWrapping ? "auto" : "smooth", "next");
                });
            }

            dots.forEach(dot => {
                if (!(dot instanceof HTMLButtonElement) || dot.dataset.dayCarouselDotWired === "true") {
                    return;
                }

                dot.dataset.dayCarouselDotWired = "true";
                dot.addEventListener("click", () => {
                    const targetIndex = Number.parseInt(dot.dataset.dayCarouselTarget ?? "-1", 10);
                    if (!Number.isInteger(targetIndex) || targetIndex < 0) {
                        return;
                    }

                    scrollToIndex(targetIndex, "smooth", "jump");
                });
            });

            Array.from(carousel.querySelectorAll("[data-day-meal-swipe-surface]")).forEach(wireMealImageSwipeSurface);

            ensureGhostSlides();
            const initialSlides = getSlides();
            const initialActiveIndex = initialSlides.findIndex(slide =>
                slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
            updateChrome(initialActiveIndex >= 0 ? initialActiveIndex : 0, { forcePaginationSync: true });

            const alignAfterLayout = () => {
                window.requestAnimationFrame(() => {
                    scrollToIndex(activeIndex, "auto");
                });
            };

            alignAfterLayout();
            window.addEventListener("resize", alignAfterLayout);
        });
    };

    const closeCardMoreActionsWithinCard = card => {
        if (!(card instanceof HTMLElement)) {
            return;
        }

        const menus = Array.from(card.querySelectorAll("[data-card-more-actions]"));
        menus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement)) {
                return;
            }

            finishClosingCardMoreActionsMenu(menu);
        });
    };

    const preserveReplacementCardUiState = (currentCard, replacementCard) => {
        if (!(currentCard instanceof HTMLElement) || !(replacementCard instanceof HTMLElement)) {
            return;
        }

        const dayKey = readCardDayKey(currentCard);
        if (dayKey) {
            dayMealSlotState.set(dayKey, readActiveDayMealSlotIndex(currentCard));
        }

        replacementCard.setAttribute("aria-hidden", currentCard.getAttribute("aria-hidden") === "false" ? "false" : "true");
    };

    const replaceSwappedMealCard = (responseDocument, slotIndex) => {
        const currentCard = findMealCardBySlotIndex(document, slotIndex);
        const nextCard = findMealCardBySlotIndex(responseDocument, slotIndex);
        if (!(currentCard instanceof HTMLElement) || !(nextCard instanceof HTMLElement)) {
            return false;
        }

        const replacement = nextCard.cloneNode(true);
        if (!(replacement instanceof HTMLElement)) {
            return false;
        }

        closeCardMoreActionsWithinCard(currentCard);
        preserveReplacementCardUiState(currentCard, replacement);
        currentCard.replaceWith(replacement);
        return true;
    };

    const readAjaxSwapFormSignature = form => {
        if (!(form instanceof HTMLFormElement)) {
            return "";
        }

        const dayInput = form.querySelector("input[name='dayIndex']");
        if (!(dayInput instanceof HTMLInputElement)) {
            return "";
        }

        const dayIndex = dayInput.value.trim();
        if (!dayIndex) {
            return "";
        }

        const formType = form.classList.contains("aislepilot-ignore-form")
            ? "ignore"
            : form.classList.contains("aislepilot-favorite-form")
                ? "favorite"
                : "swap";
        return `${formType}:${dayIndex}`;
    };

    const syncAjaxSwapFormsFromResponse = responseDocument => {
        if (!(responseDocument instanceof Document)) {
            return false;
        }

        const responseForms = Array.from(responseDocument.querySelectorAll("#aislepilot-meals [data-ajax-swap-form]"));
        if (responseForms.length === 0) {
            return false;
        }

        const responseFormsBySignature = new Map();
        responseForms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const signature = readAjaxSwapFormSignature(form);
            if (!signature || responseFormsBySignature.has(signature)) {
                return;
            }

            responseFormsBySignature.set(signature, form);
        });

        const currentForms = Array.from(document.querySelectorAll("#aislepilot-meals [data-ajax-swap-form]"));
        let replacements = 0;
        currentForms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const signature = readAjaxSwapFormSignature(form);
            if (!signature) {
                return;
            }

            const nextForm = responseFormsBySignature.get(signature);
            if (!(nextForm instanceof HTMLFormElement)) {
                return;
            }

            const replacement = nextForm.cloneNode(true);
            if (!(replacement instanceof HTMLFormElement)) {
                return;
            }

            form.replaceWith(replacement);
            replacements += 1;
        });

        return replacements > 0;
    };

    const syncHiddenInputValueFromResponse = (responseDocument, inputName) => {
        if (!(responseDocument instanceof Document) || typeof inputName !== "string" || inputName.length === 0) {
            return false;
        }

        const escapedInputName = typeof CSS !== "undefined" && typeof CSS.escape === "function"
            ? CSS.escape(inputName)
            : inputName.replace(/["\\]/g, "\\$&");
        const selector = `input[name="${escapedInputName}"]`;
        const nextInput = responseDocument.querySelector(selector);
        if (!(nextInput instanceof HTMLInputElement)) {
            return false;
        }

        const nextValue = nextInput.value;
        const currentInputs = Array.from(document.querySelectorAll(selector))
            .filter(input => input instanceof HTMLInputElement);
        currentInputs.forEach(input => {
            input.value = nextValue;
        });
        return currentInputs.length > 0;
    };

    const readSavedMealNamesFromHiddenState = () => {
        const stateInput = document.querySelector("input[name='Request.SavedEnjoyedMealNamesState']");
        if (!(stateInput instanceof HTMLInputElement)) {
            return [];
        }

        const serializedState = stateInput.value?.trim() ?? "";
        if (!serializedState) {
            return [];
        }

        try {
            const parsed = JSON.parse(serializedState);
            if (!Array.isArray(parsed)) {
                return [];
            }

            return parsed
                .filter(name => typeof name === "string")
                .map(name => name.trim())
                .filter(name => name.length > 0)
                .filter((name, index, values) =>
                    values.findIndex(candidate => candidate.localeCompare(name, undefined, { sensitivity: "accent" }) === 0) === index);
        } catch {
            return [];
        }
    };

    const syncFavoriteButtonsFromSavedState = () => {
        const savedMealNames = readSavedMealNamesFromHiddenState();
        const savedMealNameSet = new Set(savedMealNames.map(name => name.toLowerCase()));
        const favoriteForms = Array.from(document.querySelectorAll("#aislepilot-meals .aislepilot-favorite-form"));
        let syncedButtons = 0;

        favoriteForms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const mealNameInput = form.querySelector("input[name='mealName']");
            const submitButton = form.querySelector("button[type='submit']");
            const label = submitButton?.querySelector(".aislepilot-swap-action-label");
            if (!(mealNameInput instanceof HTMLInputElement) || !(submitButton instanceof HTMLButtonElement) || !(label instanceof HTMLElement)) {
                return;
            }

            const mealName = mealNameInput.value.trim();
            const isSavedMeal = mealName.length > 0 && savedMealNameSet.has(mealName.toLowerCase());
            submitButton.classList.toggle("is-saved-meal", isSavedMeal);
            submitButton.setAttribute("aria-label", isSavedMeal ? "Unsave meal" : "Save meal");
            submitButton.setAttribute("title", isSavedMeal ? "Unsave meal" : "Save meal");
            label.textContent = isSavedMeal ? "Unsave" : "Save";
            syncedButtons += 1;
        });

        return syncedButtons > 0;
    };

    const createSavedMealRemoveGlyph = () => {
        const glyph = document.createElement("span");
        glyph.className = "aislepilot-symbol-glyph";
        glyph.setAttribute("aria-hidden", "true");
        glyph.innerHTML = "<svg viewBox='0 0 24 24' focusable='false'><path d='M4 7h16'></path><path d='M9 7V4h6v3'></path><path d='M7 7l1 12h8l1-12'></path><path d='M10 11v6'></path><path d='M14 11v6'></path></svg>";
        return glyph;
    };

    const syncSavedMealsMenuFromHiddenState = () => {
        const section = document.querySelector("[data-saved-meals-menu-section]");
        if (!(section instanceof HTMLElement)) {
            return false;
        }

        const savedMealNames = readSavedMealNamesFromHiddenState();
        const title = section.querySelector(".aislepilot-head-menu-section-title");
        section.replaceChildren();
        if (title instanceof HTMLElement) {
            section.appendChild(title);
        } else {
            const nextTitle = document.createElement("p");
            nextTitle.className = "aislepilot-head-menu-section-title";
            nextTitle.textContent = "Saved meals";
            section.appendChild(nextTitle);
        }

        if (savedMealNames.length === 0) {
            const emptyState = document.createElement("p");
            emptyState.className = "aislepilot-head-menu-item is-disabled";
            emptyState.setAttribute("aria-disabled", "true");
            emptyState.textContent = "No saved meals yet";
            section.appendChild(emptyState);
            return true;
        }

        const antiForgeryTokenInput = document.querySelector("input[name='__RequestVerificationToken']");
        const antiForgeryToken = antiForgeryTokenInput instanceof HTMLInputElement ? antiForgeryTokenInput.value : "";
        const returnUrlInput = document.querySelector("input[name='returnUrl']");
        const returnUrl = returnUrlInput instanceof HTMLInputElement ? returnUrlInput.value : "";
        const list = document.createElement("ul");
        list.className = "aislepilot-head-saved-meal-list";

        savedMealNames.slice(0, 20).forEach(savedMealName => {
            const row = document.createElement("li");
            row.className = "aislepilot-head-saved-meal-row";

            const name = document.createElement("span");
            name.className = "aislepilot-head-saved-meal-name";
            name.textContent = savedMealName;
            row.appendChild(name);

            const form = document.createElement("form");
            form.method = "post";
            form.action = "/projects/aisle-pilot/remove-saved-meal";
            form.className = "aislepilot-head-week-form";

            if (antiForgeryToken.length > 0) {
                const tokenInput = document.createElement("input");
                tokenInput.type = "hidden";
                tokenInput.name = "__RequestVerificationToken";
                tokenInput.value = antiForgeryToken;
                form.appendChild(tokenInput);
            }

            const mealNameInput = document.createElement("input");
            mealNameInput.type = "hidden";
            mealNameInput.name = "mealName";
            mealNameInput.value = savedMealName;
            form.appendChild(mealNameInput);

            const returnUrlHiddenInput = document.createElement("input");
            returnUrlHiddenInput.type = "hidden";
            returnUrlHiddenInput.name = "returnUrl";
            returnUrlHiddenInput.value = returnUrl;
            form.appendChild(returnUrlHiddenInput);

            const button = document.createElement("button");
            button.type = "submit";
            button.className = "aislepilot-head-week-action-btn is-danger";
            button.setAttribute("data-loading-label", "Removing meal...");
            button.setAttribute("aria-label", `Remove ${savedMealName} from saved meals`);
            button.setAttribute("title", "Remove saved meal");
            button.appendChild(createSavedMealRemoveGlyph());

            const srOnly = document.createElement("span");
            srOnly.className = "sr-only";
            srOnly.textContent = "Remove saved meal";
            button.appendChild(srOnly);

            form.appendChild(button);
            row.appendChild(form);
            list.appendChild(row);
        });

        section.appendChild(list);
        return true;
    };

    const normalizeMealImageName = value => {
        if (typeof value !== "string") {
            return "";
        }

        return value.trim().toLowerCase();
    };

    const captureRenderedMealImageSources = scope => {
        if (!(scope instanceof Document || scope instanceof Element)) {
            return new Map();
        }

        const imageElements = Array.from(scope.querySelectorAll("img[data-meal-image][data-meal-name]"));
        const imageSrcByMealName = new Map();
        imageElements.forEach(imageElement => {
            if (!(imageElement instanceof HTMLImageElement)) {
                return;
            }

            const mealNameKey = normalizeMealImageName(imageElement.dataset.mealName ?? "");
            if (!mealNameKey) {
                return;
            }

            const imageSrc = (imageElement.getAttribute("src") ?? imageElement.currentSrc ?? "").trim();
            if (!imageSrc) {
                return;
            }

            const preservedEntries = imageSrcByMealName.get(mealNameKey);
            const nextEntry = {
                imageElement,
                imageSrc
            };
            if (Array.isArray(preservedEntries)) {
                preservedEntries.push(nextEntry);
                return;
            }

            imageSrcByMealName.set(mealNameKey, [nextEntry]);
        });

        return imageSrcByMealName;
    };

    const restoreRenderedMealImageSources = (scope, imageSrcByMealName) => {
        if (!(scope instanceof Document || scope instanceof Element) || !(imageSrcByMealName instanceof Map) || imageSrcByMealName.size === 0) {
            return;
        }

        const imageElements = Array.from(scope.querySelectorAll("img[data-meal-image][data-meal-name]"));
        imageElements.forEach(imageElement => {
            if (!(imageElement instanceof HTMLImageElement)) {
                return;
            }

            const mealNameKey = normalizeMealImageName(imageElement.dataset.mealName ?? "");
            if (!mealNameKey) {
                return;
            }

            const preservedEntries = imageSrcByMealName.get(mealNameKey);
            if (!Array.isArray(preservedEntries) || preservedEntries.length === 0) {
                return;
            }

            const preservedEntry = preservedEntries.shift();
            const preservedImageSrc = typeof preservedEntry?.imageSrc === "string"
                ? preservedEntry.imageSrc.trim()
                : "";
            if (!preservedImageSrc) {
                return;
            }

            const preservedImageElement = preservedEntry?.imageElement;
            if (preservedImageElement instanceof HTMLImageElement) {
                const currentPreservedSrc = (preservedImageElement.getAttribute("src") ?? preservedImageElement.currentSrc ?? "").trim();
                if (currentPreservedSrc !== preservedImageSrc) {
                    preservedImageElement.src = preservedImageSrc;
                }

                preservedImageElement.alt = imageElement.alt;
                const incomingMealName = imageElement.dataset.mealName ?? "";
                if (incomingMealName.length > 0) {
                    preservedImageElement.dataset.mealName = incomingMealName;
                }

                imageElement.replaceWith(preservedImageElement);
                return;
            }

            const currentImageSrc = (imageElement.getAttribute("src") ?? "").trim();
            if (currentImageSrc !== preservedImageSrc) {
                imageElement.src = preservedImageSrc;
            }
        });
    };

    const applyAjaxSwapResponse = (responseText, slotIndex) => {
        if (typeof DOMParser === "undefined") {
            return false;
        }

        rememberDayMealSlots(document);
        restoreAllCardMoreActionsPanelsToMenus();
        const preservedMealImageSources = captureRenderedMealImageSources(document);
        const wasOverviewExpanded = (() => {
            const currentOverviewContent = getOverviewContent();
            return currentOverviewContent instanceof HTMLElement && !currentOverviewContent.hasAttribute("hidden");
        })();
        const responseDocument = new DOMParser().parseFromString(responseText, "text/html");
        const didReplaceMeals =
            replaceSectionContent(responseDocument, "#aislepilot-meals") ||
            replaceSwappedMealCard(responseDocument, slotIndex);
        if (!didReplaceMeals) {
            return false;
        }

        syncAjaxSwapFormsFromResponse(responseDocument);
        replaceSectionContent(responseDocument, "#aislepilot-overview");
        replaceSectionContent(responseDocument, "#aislepilot-shop");
        replaceSectionContent(responseDocument, "#aislepilot-export");
        restoreRenderedMealImageSources(document, preservedMealImageSources);
        if (wasOverviewExpanded) {
            const nextOverviewContent = getOverviewContent();
            if (nextOverviewContent instanceof HTMLElement) {
                nextOverviewContent.removeAttribute("hidden");
            }
        }

        wireModulesAfterAjaxSwap(document);
        startMealImagePolling();
        syncSetupToggleState();
        syncOverviewToggleState();
        syncSavedWeeksToggleState();
        observeActivePanelHeight();
        updateViewportHeight(true);
        return true;
    };

    const applyAjaxFavoriteResponse = responseText => {
        if (typeof DOMParser === "undefined") {
            return false;
        }

        const responseDocument = new DOMParser().parseFromString(responseText, "text/html");
        const didSyncForms = syncAjaxSwapFormsFromResponse(responseDocument);
        const didSyncSavedState = syncHiddenInputValueFromResponse(
            responseDocument,
            "Request.SavedEnjoyedMealNamesState"
        );
        const didSyncFavoriteButtons = syncFavoriteButtonsFromSavedState();
        const didSyncSavedMealsMenu = syncSavedMealsMenuFromHiddenState();
        if (!didSyncForms && !didSyncSavedState && !didSyncFavoriteButtons && !didSyncSavedMealsMenu) {
            return false;
        }

        wireModulesAfterAjaxFavorite(document);
        return true;
    };

    const parseHtmlDocument = html => {
        if (typeof DOMParser === "undefined" || typeof html !== "string" || html.length === 0) {
            return null;
        }

        return new DOMParser().parseFromString(html, "text/html");
    };

    const hasAislePilotWindowRoot = responseDocument => {
        if (!(responseDocument instanceof Document)) {
            return false;
        }

        return responseDocument.querySelector("[data-aislepilot-window]") instanceof HTMLElement;
    };

    const readAjaxFailureMessage = responseDocument => {
        if (!(responseDocument instanceof Document)) {
            return "";
        }

        const alertItem = responseDocument.querySelector(".aislepilot-alert-list li");
        if (alertItem instanceof HTMLElement) {
            const text = alertItem.textContent?.trim();
            if (typeof text === "string" && text.length > 0) {
                return text;
            }
        }

        const summary = responseDocument.querySelector(".text-danger");
        if (summary instanceof HTMLElement) {
            const text = summary.textContent?.replace(/\s+/g, " ").trim();
            if (typeof text === "string" && text.length > 0) {
                return text;
            }
        }

        return "";
    };

    const readJsonFailureMessage = responseText => {
        if (typeof responseText !== "string" || responseText.length === 0) {
            return "";
        }

        try {
            const payload = JSON.parse(responseText);
            if (!payload || typeof payload !== "object") {
                return "";
            }

            if (typeof payload.detail === "string" && payload.detail.trim().length > 0) {
                return payload.detail.trim();
            }

            if (typeof payload.title === "string" && payload.title.trim().length > 0) {
                return payload.title.trim();
            }

            const errorValues = payload.errors && typeof payload.errors === "object"
                ? Object.values(payload.errors)
                : [];
            for (const errorValue of errorValues) {
                if (Array.isArray(errorValue)) {
                    const firstMessage = errorValue.find(message => typeof message === "string" && message.trim().length > 0);
                    if (typeof firstMessage === "string") {
                        return firstMessage.trim();
                    }
                }
            }
        } catch {
            return "";
        }

        return "";
    };

    const readExportFailureMessage = (responseText, contentType) => {
        const normalizedContentType = typeof contentType === "string"
            ? contentType.toLowerCase()
            : "";
        if (normalizedContentType.includes("json")) {
            const jsonMessage = readJsonFailureMessage(responseText);
            if (jsonMessage.length > 0) {
                return jsonMessage;
            }
        }

        const responseDocument = parseHtmlDocument(responseText);
        const htmlMessage = readAjaxFailureMessage(responseDocument);
        if (htmlMessage.length > 0) {
            return htmlMessage;
        }

        const plainMessage = typeof responseText === "string"
            ? responseText.replace(/\s+/g, " ").trim()
            : "";
        return plainMessage.length > 0
            ? plainMessage
            : "Could not complete that export. Try again.";
    };

    const wireExportDownloadForms = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-export-download-form]"))
            : Array.from(document.querySelectorAll("[data-export-download-form]"));

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement) || form.dataset.exportDownloadWired === "true") {
                return;
            }

            form.dataset.exportDownloadWired = "true";
            form.addEventListener("submit", async event => {
                if (!(event.currentTarget instanceof HTMLFormElement) || typeof fetch !== "function") {
                    return;
                }

                event.preventDefault();
                const exportForm = event.currentTarget;
                if (exportForm.dataset.exportDownloadSubmitting === "true") {
                    return;
                }

                const submitButton = getSubmitButton(event);
                if (!(submitButton instanceof HTMLButtonElement)) {
                    return;
                }

                exportForm.dataset.exportDownloadSubmitting = "true";
                clearSubmitLoadingDelay(exportForm);
                setSubmitButtonLoadingState(submitButton);

                const actionUrl = stripHashFromFormAction(exportForm);
                if (!actionUrl) {
                    delete exportForm.dataset.exportDownloadSubmitting;
                    HTMLFormElement.prototype.submit.call(exportForm);
                    return;
                }

                try {
                    const response = await fetch(actionUrl, {
                        method: (exportForm.method || "POST").toUpperCase(),
                        body: new FormData(exportForm),
                        credentials: "same-origin",
                        headers: {
                            "Accept": "application/pdf,text/plain,application/json,text/html,*/*",
                            "X-Requested-With": "XMLHttpRequest"
                        }
                    });
                    const contentType = response.headers.get("content-type") ?? "";
                    const normalizedContentType = contentType.toLowerCase();

                    if (normalizedContentType.includes("text/html") || normalizedContentType.includes("application/xhtml+xml")) {
                        const responseText = await response.text();
                        if (response.ok) {
                            replaceDocumentWithHtml(responseText);
                            return;
                        }

                        showToast(readExportFailureMessage(responseText, contentType), "warning");
                        resetFormSubmittingState(exportForm);
                        return;
                    }

                    if (!response.ok) {
                        const responseText = await response.text();
                        showToast(readExportFailureMessage(responseText, contentType), "warning");
                        resetFormSubmittingState(exportForm);
                        return;
                    }

                    const blob = await response.blob();
                    triggerFileDownload(blob, readExportDownloadFileName(response, exportForm));
                    resetFormSubmittingState(exportForm);
                } catch {
                    delete exportForm.dataset.exportDownloadSubmitting;
                    HTMLFormElement.prototype.submit.call(exportForm);
                    return;
                } finally {
                    delete exportForm.dataset.exportDownloadSubmitting;
                }
            });
        });
    };

    const animateSwappedMeal = dayIndex => {
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
        }, 520);
    };

    const handleAjaxSwapFormSubmit = async (swapForm, submitButton = null) => {
        if (!(swapForm instanceof HTMLFormElement)) {
            return;
        }

        if (swapForm.dataset.ajaxSwapSubmitting === "true") {
            return;
        }

        if (typeof fetch !== "function") {
            HTMLFormElement.prototype.submit.call(swapForm);
            return;
        }

        swapForm.dataset.ajaxSwapSubmitting = "true";
        const submitActionLabel = submitButton instanceof HTMLButtonElement
            ? (submitButton.getAttribute("aria-label") ?? submitButton.textContent ?? "").trim()
            : "";
        const isFavoriteForm = swapForm.classList.contains("aislepilot-favorite-form");
        const wasSavedMealFavorite =
            isFavoriteForm &&
            submitButton instanceof HTMLButtonElement &&
            submitButton.classList.contains("is-saved-meal");
        const isIgnoreForm = swapForm.classList.contains("aislepilot-ignore-form");
        const isLeftoverRebalanceForm = swapForm.hasAttribute("data-leftover-rebalance-form");
        const isDessertSwapForm = swapForm.action.toLowerCase().includes("/swap-dessert");
        const isDayReorderForm = swapForm.hasAttribute("data-day-reorder-form");
        const isDirectMealSwapForm =
            !isFavoriteForm &&
            !isIgnoreForm &&
            !isLeftoverRebalanceForm &&
            !isDessertSwapForm &&
            !isDayReorderForm;
        const scrollSnapshot = buildSwapScrollSnapshot(swapForm);
        const swapDayIndex = Number.isInteger(scrollSnapshot.anchorDayIndex)
            ? scrollSnapshot.anchorDayIndex
            : null;
        const parentActionsMenu = resolveCardMoreActionsMenuForForm(swapForm);
        const currentCard = resolveSwapTargetCard(swapForm);
        const actionSheetPanel = swapForm.closest("[data-card-more-actions-panel]");
        const isMobileSheetSwapForm =
            isDirectMealSwapForm &&
            actionSheetPanel instanceof HTMLElement &&
            actionSheetPanel.classList.contains("is-mobile-sheet");
        writeSwapDebug("submit-start", {
            formAction: swapForm.getAttribute("action") ?? "",
            submitActionLabel,
            isFavoriteForm,
            isIgnoreForm,
            isLeftoverRebalanceForm,
            isDessertSwapForm,
            isDayReorderForm,
            isDirectMealSwapForm,
            isMobileSheetSwapForm,
            swapDayIndex,
            parentMenuOpen: parentActionsMenu instanceof HTMLDetailsElement ? parentActionsMenu.open : false,
            currentCardFound: currentCard instanceof HTMLElement
        });
        try {
            if (isMobileSheetSwapForm) {
                if (parentActionsMenu instanceof HTMLDetailsElement && parentActionsMenu.open) {
                    closeCardMoreActionsMenuImmediately(parentActionsMenu);
                }

                const nativeActionUrl = stripHashFromFormAction(swapForm);
                persistSwapScrollPosition(swapForm);
                writeSwapDebug("native-submit-branch", {
                    actionUrl: nativeActionUrl ?? "",
                    swapDayIndex
                });
                HTMLFormElement.prototype.submit.call(swapForm);
                return;
            }
            if (parentActionsMenu instanceof HTMLDetailsElement && parentActionsMenu.open) {
                closeCardMoreActionsMenuImmediately(parentActionsMenu);
            }
            if (!isFavoriteForm && currentCard instanceof HTMLElement) {
                currentCard.classList.add("is-swap-fading-out");
                currentCard.setAttribute("aria-busy", "true");
                currentCard.dataset.swapStatus = isLeftoverRebalanceForm
                    ? "Updating meal plan..."
                    : isDayReorderForm
                        ? "Moving meal card..."
                        : isIgnoreForm
                            ? "Updating meal..."
                            : isDessertSwapForm
                                ? "Loading new dessert..."
                                : "Loading new meal...";
            }
            if (!isFavoriteForm && submitButton instanceof HTMLButtonElement && !swapForm.hasAttribute("data-skip-submit-loading")) {
                clearSubmitLoadingDelay(swapForm);
                setSubmitButtonLoadingState(submitButton);
            }
            const actionUrl = stripHashFromFormAction(swapForm);
            if (!actionUrl) {
                writeSwapDebug("missing-action-url", {
                    swapDayIndex
                });
                persistSwapScrollPosition(swapForm);
                HTMLFormElement.prototype.submit.call(swapForm);
                return;
            }

            writeSwapDebug("fetch-start", {
                actionUrl,
                swapDayIndex,
                isDirectMealSwapForm
            });
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
                writeSwapDebug("fetch-response", {
                    status: response.status,
                    ok: response.ok,
                    contentType,
                    isHtmlResponse,
                    responseLength: responseText.length,
                    swapDayIndex
                });

                if (isHtmlResponse) {
                    const parsedResponseDocument = parseHtmlDocument(responseText);
                    if (isFavoriteForm) {
                        if (!applyAjaxFavoriteResponse(responseText)) {
                            if (parsedResponseDocument && !hasAislePilotWindowRoot(parsedResponseDocument)) {
                                const message = readAjaxFailureMessage(parsedResponseDocument);
                                showToast(
                                    message.length > 0 ? message : "Could not update saved meals. Try again.",
                                    "warning");
                                clearPersistedSwapScroll();
                                writeSwapDebug("favorite-ajax-failed", {
                                    swapDayIndex,
                                    message
                                });
                                return;
                            }

                            persistSwapScrollPosition(swapForm);
                            writeSwapDebug("favorite-replace-document", {
                                swapDayIndex
                            });
                            replaceDocumentWithHtml(responseText);
                            return;
                        }

                        if (parentActionsMenu instanceof HTMLDetailsElement) {
                            closeCardMoreActionsMenuImmediately(parentActionsMenu);
                        }
                        showToast(
                            wasSavedMealFavorite
                                ? "Meal removed from saved meals."
                                : "Meal saved.",
                            "success");
                        restoreInlineSwapScroll(scrollSnapshot);
                        clearPersistedSwapScroll();
                        writeSwapDebug("favorite-ajax-success", {
                            swapDayIndex
                        });
                        return;
                    }

                    const didApplySwapResponse = applyAjaxSwapResponse(responseText, swapDayIndex);
                    writeSwapDebug("apply-ajax-swap-response", {
                        swapDayIndex,
                        didApplySwapResponse
                    });
                    if (!didApplySwapResponse) {
                        if (parsedResponseDocument && !hasAislePilotWindowRoot(parsedResponseDocument)) {
                            const message = readAjaxFailureMessage(parsedResponseDocument);
                            showToast(
                                message.length > 0 ? message : "No alternative meal is available right now. Try another swap or regenerate.",
                                "warning");
                                clearPersistedSwapScroll();
                            writeSwapDebug("ajax-swap-failed-with-message", {
                                swapDayIndex,
                                message
                            });
                            return;
                        }

                        persistSwapScrollPosition(swapForm);
                        writeSwapDebug("ajax-swap-replace-document", {
                            swapDayIndex
                        });
                        replaceDocumentWithHtml(responseText);
                        return;
                    }

                    if (parentActionsMenu instanceof HTMLDetailsElement) {
                        closeCardMoreActionsMenuImmediately(parentActionsMenu);
                    }

                    if (isLeftoverRebalanceForm) {
                        // Leftover rebalancing is an inline layout tweak; avoid generic swap toast noise.
                    } else if (isDayReorderForm) {
                        showToast("Meal day updated.", "success");
                    } else if (isIgnoreForm) {
                        const normalizedAction = submitActionLabel.toLowerCase();
                        const ignoreMessage = normalizedAction.includes("ignore")
                            ? "Meal ignored."
                            : normalizedAction.includes("include")
                                ? "Meal included."
                                : "Meal updated.";
                        showToast(ignoreMessage, "success");
                    } else {
                        showToast(isDessertSwapForm ? "Dessert swapped." : "Meal swapped.", "success");
                    }

                    if (isLeftoverRebalanceForm) {
                        clearPersistedSwapScroll();
                        return;
                    }

                    restoreInlineSwapScroll(scrollSnapshot);
                    clearPersistedSwapScroll();
                    animateSwappedMeal(swapDayIndex);
                    writeSwapDebug("ajax-swap-success", {
                        swapDayIndex
                    });
                    return;
                }

                if (response.ok) {
                    persistSwapScrollPosition(swapForm);
                    writeSwapDebug("non-html-response-reload", {
                        swapDayIndex,
                        status: response.status
                    });
                    window.location.reload();
                    return;
                }

                showToast("Could not complete that action. Try again.", "warning");
                clearPersistedSwapScroll();
                resetSubmittingState();
                writeSwapDebug("response-not-ok", {
                    swapDayIndex,
                    status: response.status
                });
                return;
        } catch (error) {
            persistSwapScrollPosition(swapForm);
            writeSwapDebug("submit-handler-exception-native-submit", {
                swapDayIndex,
                error: error instanceof Error ? error.message : `${error ?? ""}`
            });
            HTMLFormElement.prototype.submit.call(swapForm);
            return;
        } finally {
            if (!isFavoriteForm && currentCard instanceof HTMLElement && currentCard.isConnected) {
                currentCard.classList.remove("is-swap-fading-out");
                currentCard.removeAttribute("aria-busy");
                delete currentCard.dataset.swapStatus;
            }
            if (swapForm.isConnected) {
                resetFormSubmittingState(swapForm);
            } else {
                clearSubmitLoadingDelay(swapForm);
            }
            delete swapForm.dataset.ajaxSwapSubmitting;
            writeSwapDebug("submit-finally", {
                swapDayIndex,
                formStillConnected: swapForm.isConnected
            });
        }
    };

    const wireAjaxSwapForm = form => {
        if (!(form instanceof HTMLFormElement) || form.dataset.ajaxSwapWired === "true") {
            return;
        }

        form.dataset.ajaxSwapWired = "true";
        form.addEventListener("submit", event => {
            if (!(event.currentTarget instanceof HTMLFormElement)) {
                return;
            }

            event.preventDefault();
            void handleAjaxSwapFormSubmit(event.currentTarget, getSubmitButton(event));
        });
    };

    const wireAjaxSwapHandlers = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-ajax-swap-form]"))
            : Array.from(document.querySelectorAll("[data-ajax-swap-form]"));
        forms.forEach(wireAjaxSwapForm);
    };

    const wireCardInteractionModules = scope => {
        wireSetupToggleHandlers(scope);
        wireOverviewToggleHandlers(scope);
        wireSavedWeeksToggleHandlers(scope);
        window.AislePilotActionMenus?.wireHeadMenus(scope);
        window.AislePilotActionMenus?.wireOverviewActionsMenus(scope);
        wirePreserveScrollHandlers(scope);
        wireLeftoverPlanner(scope);
        wireCardMoreActions(scope);
        wireInlineDetailsPanels(scope);
        wireDayCardCarousel(scope);
        wireDayCardReorder(scope);
        wireDayMealCards(scope);
        window.AislePilotShopping?.wireShoppingChecklist(scope);
        window.AislePilotShopping?.wireCustomShoppingList(scope);
        wireAjaxSwapHandlers(scope);
    };

    const wireModulesAfterAjaxSwap = scope => {
        wireSubmitLoadingHandlers(scope);
        wireExportThemeForms(scope);
        wireExportDownloadForms(scope);
        wirePlanBasicsSliders(scope);
        wireMealTypeSelectors(scope);
        wireCustomAisleFieldVisibility(scope);
        wireCardInteractionModules(scope);
        wireNotesExportButtons(scope);
    };

    const wireModulesAfterAjaxFavorite = scope => {
        wireSubmitLoadingHandlers(scope);
        wirePreserveScrollHandlers(scope);
        wireAjaxSwapHandlers(scope);
    };

    wireExportDownloadForms(document);
    wireCardInteractionModules(document);
    startMealImagePolling();

    viewport.addEventListener("touchstart", event => {
        viewportSwipeStartedInsideProtectedRegion =
            isEventWithinDayMealCard(event) ||
            isEventWithinDayCarouselPagination(event);
        const touch = event.changedTouches[0];
        if (!touch) {
            return;
        }

        touchStartX = touch.clientX;
        touchStartY = touch.clientY;
    }, { passive: true });

    viewport.addEventListener("touchend", event => {
        if (viewportSwipeStartedInsideProtectedRegion) {
            viewportSwipeStartedInsideProtectedRegion = false;
            return;
        }

        viewportSwipeStartedInsideProtectedRegion = false;
        const touch = event.changedTouches[0];
        if (!touch) {
            return;
        }

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

    viewport.addEventListener("touchcancel", () => {
        viewportSwipeStartedInsideProtectedRegion = false;
    }, { passive: true });

    window.addEventListener("resize", () => {
        syncMobileContextOffset();
        updateViewportHeight(false);
        schedulePlanBasicsSliderRefresh(document);
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
    syncMobileContextOffset();
    syncUi(initialIndex >= 0 ? initialIndex : 0, false);
    updateViewportHeight(false);
    syncSetupToggleState();
    syncOverviewToggleState();
    syncSavedWeeksToggleState();
    const restored = restoreSwapScrollPosition();
    if (!restored) {
        clearRestorePending();
    }
})();
