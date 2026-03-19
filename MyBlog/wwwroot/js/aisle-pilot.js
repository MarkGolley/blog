(() => {
    const quickReplaceInputs = Array.from(
        document.querySelectorAll(
            ".aislepilot-inline-fields input:not([type='hidden']):not([type='checkbox']):not([type='radio'])"
        )
    );

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

    const swapScrollKey = "aislepilot:swap-scroll";
    const root = document.querySelector("[data-aislepilot-window]");
    if (!root) {
        return;
    }

    const setupPanel = document.querySelector("[data-setup-panel]");
    const setupToggleButtons = Array.from(document.querySelectorAll("[data-setup-toggle]"));

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

    const persistSwapScrollPosition = () => {
        const activePanelId = panels[currentIndex]?.id ?? null;
        const payload = {
            x: window.scrollX,
            y: window.scrollY,
            activePanelId,
            at: Date.now()
        };

        sessionStorage.setItem(swapScrollKey, JSON.stringify(payload));
    };

    const restoreSwapScrollPosition = () => {
        const raw = sessionStorage.getItem(swapScrollKey);
        if (!raw) {
            return;
        }

        sessionStorage.removeItem(swapScrollKey);

        try {
            const parsed = JSON.parse(raw);
            if (!parsed || typeof parsed.y !== "number") {
                return;
            }

            // Ignore stale restore requests.
            if (typeof parsed.at === "number" && Date.now() - parsed.at > 60_000) {
                return;
            }

            if (typeof parsed.activePanelId === "string" && parsed.activePanelId.length > 0) {
                const panelIndex = panels.findIndex(panel => panel.id === parsed.activePanelId);
                if (panelIndex >= 0) {
                    syncUi(panelIndex, false);
                }
            }

            const targetX = typeof parsed.x === "number" ? parsed.x : 0;
            const targetY = parsed.y;

            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    window.scrollTo(targetX, targetY);
                });
            });
        } catch {
            // Ignore malformed session payloads.
        }
    };

    const syncSetupToggleState = () => {
        if (!setupPanel || setupToggleButtons.length === 0) {
            return;
        }

        const isHidden = setupPanel.hasAttribute("hidden");
        setupToggleButtons.forEach(button => {
            button.textContent = isHidden ? "Edit setup" : "Hide setup";
            button.setAttribute("aria-expanded", isHidden ? "false" : "true");
        });
    };

    setupToggleButtons.forEach(button => {
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

    const preserveScrollForms = Array.from(document.querySelectorAll("[data-preserve-scroll-form]"));
    preserveScrollForms.forEach(form => {
        form.addEventListener("submit", () => {
            persistSwapScrollPosition();
        });
    });

    const leftoverToggleButton = document.querySelector("[data-leftover-toggle]");
    const leftoverPlannerShell = document.querySelector("[data-leftover-planner]");
    if (leftoverToggleButton && leftoverPlannerShell) {
        leftoverToggleButton.addEventListener("click", () => {
            const isHidden = leftoverPlannerShell.hasAttribute("hidden");
            if (isHidden) {
                leftoverPlannerShell.removeAttribute("hidden");
                leftoverToggleButton.setAttribute("aria-expanded", "true");
                leftoverToggleButton.textContent = "Hide leftover rebalance";
            } else {
                leftoverPlannerShell.setAttribute("hidden", "hidden");
                leftoverToggleButton.setAttribute("aria-expanded", "false");
                leftoverToggleButton.textContent = "Rebalance leftovers";
            }

            updateViewportHeight(true);
        });
    }

    const leftoverRebalanceForm = document.querySelector("[data-leftover-rebalance-form]");
    const leftoverCsvInput = leftoverRebalanceForm?.querySelector("[data-leftover-csv]");
    const leftoverZones = Array.from(document.querySelectorAll("[data-leftover-day-zone]"));
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
        persistSwapScrollPosition();
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
    restoreSwapScrollPosition();
})();
