(() => {
    if (window.__aislePilotScriptWired === true) {
        return;
    }
    window.__aislePilotScriptWired = true;

    const quickReplaceInputs = Array.from(
        document.querySelectorAll(
            ".aislepilot-inline-fields input:not([type='hidden']):not([type='checkbox']):not([type='radio'])"
        )
    );
    const getAislePilotForms = () => Array.from(document.querySelectorAll(".aislepilot-app form"));
    const submitLoadingDelayTimers = new WeakMap();
    const appRoot = document.querySelector(".aislepilot-app");
    const shellHeader = document.querySelector(".app-shell-header");
    const planLoadingShell = appRoot instanceof HTMLElement
        ? appRoot.querySelector("[data-plan-loading-shell]")
        : null;

    const showPlanLoadingShell = () => {
        if (!(appRoot instanceof HTMLElement) || !(planLoadingShell instanceof HTMLElement)) {
            return;
        }

        appRoot.classList.add("is-plan-loading");
        planLoadingShell.removeAttribute("hidden");
        planLoadingShell.setAttribute("aria-hidden", "false");
    };

    const hidePlanLoadingShell = () => {
        if (!(appRoot instanceof HTMLElement) || !(planLoadingShell instanceof HTMLElement)) {
            return;
        }

        appRoot.classList.remove("is-plan-loading");
        planLoadingShell.setAttribute("hidden", "hidden");
        planLoadingShell.setAttribute("aria-hidden", "true");
    };

    const syncMobileContextOffset = () => {
        if (!(appRoot instanceof HTMLElement)) {
            return;
        }

        if (!(shellHeader instanceof HTMLElement)) {
            appRoot.style.setProperty("--ap-shell-header-offset", "0px");
            return;
        }

        const headerRect = shellHeader.getBoundingClientRect();
        const computedTop = Number.parseFloat(window.getComputedStyle(shellHeader).top ?? "0");
        const safeTop = Number.isFinite(computedTop) ? Math.max(0, computedTop) : 0;
        const safeHeight = Math.max(0, Math.min(220, Math.ceil(headerRect.height + safeTop)));
        appRoot.style.setProperty("--ap-shell-header-offset", `${safeHeight}px`);
    };

    if (shellHeader instanceof HTMLElement && typeof ResizeObserver !== "undefined") {
        const shellHeaderResizeObserver = new ResizeObserver(() => {
            syncMobileContextOffset();
        });
        shellHeaderResizeObserver.observe(shellHeader);
    }

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

    const lockSubmitButtonWidth = submitButton => {
        if (!(submitButton instanceof HTMLButtonElement) || submitButton.dataset.loadingWidthLocked === "true") {
            return;
        }

        const width = Math.ceil(submitButton.getBoundingClientRect().width);
        if (width <= 0) {
            return;
        }

        submitButton.style.minWidth = `${width}px`;
        submitButton.dataset.loadingWidthLocked = "true";
    };

    const unlockSubmitButtonWidth = submitButton => {
        if (!(submitButton instanceof HTMLButtonElement) || submitButton.dataset.loadingWidthLocked !== "true") {
            return;
        }

        submitButton.style.removeProperty("min-width");
        delete submitButton.dataset.loadingWidthLocked;
    };

    const setSubmitButtonLoadingState = submitButton => {
        lockSubmitButtonWidth(submitButton);
        const loadingLabel = submitButton.dataset.loadingLabel?.trim();
        const fallbackLoadingLabel = "Loading...";

        if (submitButton.classList.contains("is-icon-only")) {
            const nextAriaLabel = loadingLabel && loadingLabel.length > 0
                ? loadingLabel
                : fallbackLoadingLabel;
            submitButton.setAttribute("aria-label", nextAriaLabel);
            submitButton.classList.add("is-loading");
            return;
        }

        if (loadingLabel && loadingLabel.length > 0) {
            submitButton.textContent = loadingLabel;
        } else {
            submitButton.textContent = fallbackLoadingLabel;
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
            const originalAriaLabel = submitButton.dataset.originalAriaLabel ?? submitButton.getAttribute("aria-label") ?? "";
            submitButton.dataset.originalAriaLabel = originalAriaLabel.trim();
            lockSubmitButtonWidth(submitButton);
            targetForm.setAttribute("data-is-submitting", "true");
            submitButton.disabled = true;
            submitButton.setAttribute("aria-busy", "true");
            if (submitButton.hasAttribute("data-show-plan-skeleton") || targetForm.hasAttribute("data-show-plan-skeleton")) {
                showPlanLoadingShell();
            }
            if (targetForm.hasAttribute("data-skip-submit-loading")) {
                return;
            }

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

    const getActiveTheme = () => {
        const documentTheme = (document.documentElement.dataset.theme ?? "").trim().toLowerCase();
        if (documentTheme === "dark" || documentTheme === "light") {
            return documentTheme;
        }

        if (typeof window.matchMedia === "function" && window.matchMedia("(prefers-color-scheme: dark)").matches) {
            return "dark";
        }

        return "light";
    };

    const wireExportThemeForms = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-export-theme-form]"))
            : Array.from(document.querySelectorAll("[data-export-theme-form]"));

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const themeInput = form.querySelector("[data-export-theme-input]");
            if (!(themeInput instanceof HTMLInputElement)) {
                return;
            }

            const syncThemeInput = () => {
                themeInput.value = getActiveTheme();
            };

            syncThemeInput();
            if (form.dataset.exportThemeWired === "true") {
                return;
            }

            form.dataset.exportThemeWired = "true";
            form.addEventListener("submit", syncThemeInput);
        });
    };

    let toastHost = null;
    let toastLiveRegion = null;

    const ensureToastHost = () => {
        if (toastHost instanceof HTMLElement && toastLiveRegion instanceof HTMLElement) {
            return;
        }

        const host = document.createElement("div");
        host.className = "aislepilot-toast-stack";
        host.setAttribute("aria-hidden", "true");

        const liveRegion = document.createElement("p");
        liveRegion.className = "aislepilot-toast-live";
        liveRegion.setAttribute("aria-live", "polite");
        liveRegion.setAttribute("aria-atomic", "true");
        liveRegion.textContent = "";

        const appRoot = document.querySelector(".aislepilot-app");
        if (appRoot instanceof HTMLElement) {
            appRoot.append(host, liveRegion);
        } else {
            document.body.append(host, liveRegion);
        }

        toastHost = host;
        toastLiveRegion = liveRegion;
    };

    const showToast = (message, type = "info") => {
        const normalizedMessage = typeof message === "string" ? message.trim() : "";
        if (normalizedMessage.length === 0) {
            return;
        }

        ensureToastHost();
        if (!(toastHost instanceof HTMLElement) || !(toastLiveRegion instanceof HTMLElement)) {
            return;
        }

        const toast = document.createElement("div");
        toast.className = `aislepilot-toast is-${type}`;
        toast.textContent = normalizedMessage;
        toastHost.append(toast);
        toastLiveRegion.textContent = normalizedMessage;

        window.setTimeout(() => {
            toast.classList.add("is-fading-out");
            window.setTimeout(() => {
                toast.remove();
            }, 220);
        }, 2200);
    };

    wireSubmitLoadingHandlers(document);
    wireExportThemeForms(document);

    const wirePlanBasicsSliders = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        const readStepDecimals = stepRaw => {
            if (typeof stepRaw !== "string") {
                return 0;
            }

            const normalized = stepRaw.trim();
            if (normalized.length === 0 || normalized === "any") {
                return 0;
            }

            const decimalPointIndex = normalized.indexOf(".");
            if (decimalPointIndex < 0) {
                return 0;
            }

            const decimalCount = normalized.length - decimalPointIndex - 1;
            return Math.max(0, Math.min(6, decimalCount));
        };

        const setRangeValueFromClientX = (rangeInput, clientX) => {
            if (!(rangeInput instanceof HTMLInputElement)) {
                return false;
            }

            const inputRect = rangeInput.getBoundingClientRect();
            if (inputRect.width <= 0) {
                return false;
            }

            const parsedMin = Number.parseFloat(rangeInput.min ?? "0");
            const parsedMax = Number.parseFloat(rangeInput.max ?? "100");
            if (!Number.isFinite(parsedMin) || !Number.isFinite(parsedMax) || parsedMax <= parsedMin) {
                return false;
            }

            const relative = (clientX - inputRect.left) / inputRect.width;
            const ratio = Math.max(0, Math.min(1, Number.isFinite(relative) ? relative : 0));
            let nextValue = parsedMin + ((parsedMax - parsedMin) * ratio);

            const stepRaw = (rangeInput.step ?? "").trim();
            if (stepRaw.length > 0 && stepRaw !== "any") {
                const parsedStep = Number.parseFloat(stepRaw);
                if (Number.isFinite(parsedStep) && parsedStep > 0) {
                    const totalSteps = Math.round((nextValue - parsedMin) / parsedStep);
                    nextValue = parsedMin + (totalSteps * parsedStep);
                }
            }

            nextValue = Math.max(parsedMin, Math.min(parsedMax, nextValue));
            const stepDecimals = readStepDecimals(rangeInput.step ?? "0");
            const nextValueText = nextValue.toFixed(stepDecimals);
            if (rangeInput.value === nextValueText) {
                return false;
            }

            rangeInput.value = nextValueText;
            return true;
        };

        const wireValueBubbleDrag = (sliderField, rangeInput) => {
            if (!(sliderField instanceof HTMLElement) || !(rangeInput instanceof HTMLInputElement)) {
                return;
            }

            const valueBubble = sliderField.querySelector(".aislepilot-slider-value");
            if (!(valueBubble instanceof HTMLElement) || valueBubble.dataset.sliderBubbleDragWired === "true") {
                return;
            }

            valueBubble.dataset.sliderBubbleDragWired = "true";
            let activePointerId = null;
            let hasMoved = false;

            const emitRangeInput = () => {
                rangeInput.dispatchEvent(new Event("input", { bubbles: true }));
            };

            const emitRangeChange = () => {
                rangeInput.dispatchEvent(new Event("change", { bubbles: true }));
            };

            const stopDragging = shouldCommit => {
                if (activePointerId === null) {
                    return;
                }

                if (shouldCommit && hasMoved) {
                    emitRangeChange();
                }

                activePointerId = null;
                hasMoved = false;
                valueBubble.classList.remove("is-dragging");
            };

            valueBubble.addEventListener("pointerdown", event => {
                if (event.pointerType === "mouse" && event.button !== 0) {
                    return;
                }

                activePointerId = event.pointerId;
                hasMoved = false;
                valueBubble.classList.add("is-dragging");
                valueBubble.setPointerCapture(event.pointerId);
                if (setRangeValueFromClientX(rangeInput, event.clientX)) {
                    hasMoved = true;
                    emitRangeInput();
                }

                event.preventDefault();
            });

            valueBubble.addEventListener("pointermove", event => {
                if (activePointerId === null || event.pointerId !== activePointerId) {
                    return;
                }

                if (setRangeValueFromClientX(rangeInput, event.clientX)) {
                    hasMoved = true;
                    emitRangeInput();
                }
            });

            valueBubble.addEventListener("pointerup", event => {
                if (activePointerId === null || event.pointerId !== activePointerId) {
                    return;
                }

                valueBubble.releasePointerCapture(event.pointerId);
                stopDragging(true);
            });

            valueBubble.addEventListener("pointercancel", event => {
                if (activePointerId === null || event.pointerId !== activePointerId) {
                    return;
                }

                valueBubble.releasePointerCapture(event.pointerId);
                stopDragging(false);
            });
        };

        const syncSliderProgress = rangeInput => {
            if (!(rangeInput instanceof HTMLInputElement)) {
                return;
            }

            const sliderField = rangeInput.closest(".aislepilot-slider-field");
            const syncSliderFieldMetrics = progressPercent => {
                if (!(sliderField instanceof HTMLElement)) {
                    return;
                }

                const boundedProgress = Number.isFinite(progressPercent)
                    ? Math.max(0, Math.min(100, progressPercent))
                    : 0;
                const inputRect = rangeInput.getBoundingClientRect();
                const fieldRect = sliderField.getBoundingClientRect();
                const trackWidth = Math.max(1, inputRect.width);
                const trackOffset = Math.max(0, inputRect.left - fieldRect.left);
                const valueBubble = sliderField.querySelector(".aislepilot-slider-value");
                const valueBubbleWidth = valueBubble instanceof HTMLElement
                    ? Math.max(1, valueBubble.getBoundingClientRect().width)
                    : 36;

                sliderField.style.setProperty("--slider-progress", `${boundedProgress}%`);
                sliderField.style.setProperty("--slider-progress-ratio", `${boundedProgress / 100}`);
                sliderField.style.setProperty("--slider-track-width-px", `${trackWidth}px`);
                sliderField.style.setProperty("--slider-track-offset-px", `${trackOffset}px`);
                sliderField.style.setProperty("--slider-value-width-px", `${valueBubbleWidth}px`);
            };

            const min = Number.parseFloat(rangeInput.min ?? "0");
            const max = Number.parseFloat(rangeInput.max ?? "100");
            const value = Number.parseFloat(rangeInput.value ?? "0");
            const span = max - min;
            if (!Number.isFinite(min) || !Number.isFinite(max) || !Number.isFinite(value) || span <= 0) {
                rangeInput.style.setProperty("--slider-progress", "0%");
                syncSliderFieldMetrics(0);
                return;
            }

            const clamped = Math.min(max, Math.max(min, value));
            const progressPercent = ((clamped - min) / span) * 100;
            rangeInput.style.setProperty("--slider-progress", `${progressPercent}%`);
            syncSliderFieldMetrics(progressPercent);
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
                wireValueBubbleDrag(sliderField, rangeInput);

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
                if (sliderField instanceof HTMLElement) {
                    wireValueBubbleDrag(sliderField, rangeInput);
                }
                applyNumberValue();
            });

            const planDaysSlider = form.querySelector("[data-plan-days-slider]");
            const cookDaysSlider = form.querySelector("[data-cook-days-slider]");
            const cookDaysHiddenInput = form.querySelector("input[type='hidden'][name='Request.CookDays']");
            const planDaysSliderField = planDaysSlider instanceof HTMLInputElement
                ? planDaysSlider.closest(".aislepilot-slider-field")
                : null;
            const cookDaysSliderField = cookDaysSlider instanceof HTMLInputElement
                ? cookDaysSlider.closest(".aislepilot-slider-field")
                : null;
            const planDaysValueOutput = planDaysSliderField?.querySelector("[data-number-slider-value]");
            const cookDaysValueOutput = cookDaysSliderField?.querySelector("[data-number-slider-value]");
            let cookDaysLimitFlashTimer = 0;

            const flashCookDaysLimit = () => {
                if (!(cookDaysSliderField instanceof HTMLElement)) {
                    return;
                }

                if (cookDaysLimitFlashTimer > 0) {
                    window.clearTimeout(cookDaysLimitFlashTimer);
                    cookDaysLimitFlashTimer = 0;
                }

                cookDaysSliderField.classList.remove("is-limit-flash");
                cookDaysSliderField.getBoundingClientRect();
                cookDaysSliderField.classList.add("is-limit-flash");
                cookDaysLimitFlashTimer = window.setTimeout(() => {
                    cookDaysSliderField.classList.remove("is-limit-flash");
                    cookDaysLimitFlashTimer = 0;
                }, 460);
            };

            const syncPlanDaysConstraint = source => {
                if (!(planDaysSlider instanceof HTMLInputElement)) {
                    return;
                }

                const rawPlanDays = Number.parseInt(planDaysSlider.value ?? "7", 10);
                const safePlanDays = Number.isInteger(rawPlanDays)
                    ? Math.max(1, Math.min(7, rawPlanDays))
                    : 7;
                if (!(cookDaysSlider instanceof HTMLInputElement)) {
                    planDaysSlider.value = `${safePlanDays}`;
                    planDaysSlider.setAttribute("aria-valuetext", `${safePlanDays}`);
                    if (planDaysValueOutput instanceof HTMLElement) {
                        planDaysValueOutput.textContent = `${safePlanDays}`;
                    }
                    if (cookDaysHiddenInput instanceof HTMLInputElement) {
                        cookDaysHiddenInput.value = `${safePlanDays}`;
                    }
                    syncSliderProgress(planDaysSlider);
                    return;
                }

                const rawCookDays = Number.parseInt(cookDaysSlider.value ?? "1", 10);
                const cookMinRaw = Number.parseInt(cookDaysSlider.min ?? "1", 10);
                const cookMin = Number.isInteger(cookMinRaw) ? Math.max(1, cookMinRaw) : 1;
                const clampedCookDays = Number.isInteger(rawCookDays)
                    ? Math.max(cookMin, Math.min(safePlanDays, rawCookDays))
                    : Math.min(safePlanDays, Math.max(cookMin, 1));
                const safeCookDays = source === "plan-change"
                    ? safePlanDays
                    : clampedCookDays;
                const isOverCookDaysLimit = source === "cook-change" &&
                    Number.isInteger(rawCookDays) &&
                    rawCookDays > safePlanDays;

                planDaysSlider.value = `${safePlanDays}`;
                cookDaysSlider.value = `${safeCookDays}`;

                if (isOverCookDaysLimit) {
                    flashCookDaysLimit();
                }
                if (planDaysValueOutput instanceof HTMLElement) {
                    planDaysValueOutput.textContent = `${safePlanDays}`;
                }
                if (cookDaysValueOutput instanceof HTMLElement) {
                    cookDaysValueOutput.textContent = `${safeCookDays}`;
                }

                planDaysSlider.setAttribute("aria-valuetext", `${safePlanDays}`);
                cookDaysSlider.setAttribute("aria-valuetext", `${safeCookDays}`);
                syncSliderProgress(planDaysSlider);
                syncSliderProgress(cookDaysSlider);
            };

            if (planDaysSlider instanceof HTMLInputElement) {
                if (form.dataset.planDaysConstraintWired !== "true") {
                    form.dataset.planDaysConstraintWired = "true";
                    planDaysSlider.addEventListener("input", () => {
                        syncPlanDaysConstraint("plan-change");
                    });
                    planDaysSlider.addEventListener("change", () => {
                        syncPlanDaysConstraint("plan-change");
                    });
                    if (cookDaysSlider instanceof HTMLInputElement) {
                        cookDaysSlider.addEventListener("input", () => {
                            syncPlanDaysConstraint("cook-change");
                        });
                        cookDaysSlider.addEventListener("change", () => {
                            syncPlanDaysConstraint("cook-change");
                        });
                    }
                }

                syncPlanDaysConstraint("init");
            }

            const budgetSlider = form.querySelector("[data-budget-slider]");
            const budgetPrecisionInput = form.querySelector("[data-budget-precision-input]");
            const budgetPrecisionShell = form.querySelector("[data-budget-precision-shell]");
            const budgetPrecisionTrigger = form.querySelector("[data-budget-precision-trigger]");
            const budgetMinLabel = form.querySelector("[data-budget-min-label]");
            const budgetMaxLabel = form.querySelector("[data-budget-max-label]");
            const householdInput = form.querySelector("input[name='Request.HouseholdSize']");
            const portionInput = form.querySelector("input[name='Request.PortionSize']");
            const mealsPerDayInput = form.querySelector("input[type='hidden'][name='Request.MealsPerDay']");
            const selectedMealTypes = Array.from(
                form.querySelectorAll("input[type='checkbox'][name='Request.SelectedMealTypes']")
            ).filter(input => input instanceof HTMLInputElement);

            if (budgetSlider instanceof HTMLInputElement) {
                const clampNumber = (value, min, max) => Math.max(min, Math.min(max, value));
                const budgetIncrement = 5;
                const roundToIncrement = value => Math.round(value / budgetIncrement) * budgetIncrement;
                const snapBudgetValue = (value, min, max) => {
                    const clamped = clampNumber(value, min, max);
                    const snapped = roundToIncrement(clamped);
                    return clampNumber(snapped, min, max);
                };
                const getMealCount = () => {
                    if (selectedMealTypes.length > 0) {
                        const selectedCount = selectedMealTypes
                            .filter(input => input instanceof HTMLInputElement && input.checked)
                            .length;
                        if (selectedCount > 0) {
                            return Math.max(1, Math.min(3, selectedCount));
                        }
                    }

                    const parsedMealsPerDay = Number.parseInt(mealsPerDayInput?.value ?? "1", 10);
                    return Number.isInteger(parsedMealsPerDay)
                        ? Math.max(1, Math.min(3, parsedMealsPerDay))
                        : 1;
                };

                const getPortionMultiplier = () => {
                    const portionValue = (portionInput?.value ?? "").trim().toLowerCase();
                    if (portionValue.startsWith("small")) {
                        return 0.88;
                    }

                    if (portionValue.startsWith("large")) {
                        return 1.24;
                    }

                    return 1;
                };

                const formatPounds = value => `\u00a3${Math.round(value)}`;
                let isSyncingBudgetValue = false;
                let budgetMaxHitCount = 0;
                const budgetMaxHitThreshold = 3;

                const showBudgetPrecisionInput = () => {
                    if (budgetPrecisionShell instanceof HTMLElement) {
                        budgetPrecisionShell.removeAttribute("hidden");
                        budgetPrecisionShell.setAttribute("aria-hidden", "false");
                    }

                    if (budgetPrecisionTrigger instanceof HTMLElement) {
                        budgetPrecisionTrigger.setAttribute("hidden", "hidden");
                        budgetPrecisionTrigger.setAttribute("aria-hidden", "true");
                    }
                };

                const maybeRevealBudgetPrecisionTrigger = () => {
                    if (
                        budgetPrecisionShell instanceof HTMLElement &&
                        !budgetPrecisionShell.hasAttribute("hidden")
                    ) {
                        return;
                    }

                    if (budgetPrecisionTrigger instanceof HTMLElement) {
                        budgetPrecisionTrigger.removeAttribute("hidden");
                        budgetPrecisionTrigger.setAttribute("aria-hidden", "false");
                    }
                };

                const trackBudgetMaxHit = () => {
                    const currentValue = Number.parseFloat(budgetSlider.value ?? "0");
                    const maxValue = Number.parseFloat(budgetSlider.max ?? "600");
                    if (!Number.isFinite(currentValue) || !Number.isFinite(maxValue)) {
                        return;
                    }

                    const atMax = Math.abs(currentValue - maxValue) <= 0.001;
                    if (!atMax) {
                        budgetMaxHitCount = 0;
                        return;
                    }

                    budgetMaxHitCount += 1;
                    if (budgetMaxHitCount >= budgetMaxHitThreshold) {
                        maybeRevealBudgetPrecisionTrigger();
                    }
                };

                const syncBudgetSliderValue = (nextValue, emitChangeEvent) => {
                    if (isSyncingBudgetValue) {
                        return;
                    }

                    const min = Number.parseFloat(budgetSlider.min ?? "15");
                    const max = Number.parseFloat(budgetSlider.max ?? "600");
                    const safeMin = Number.isFinite(min) ? min : 15;
                    const safeMax = Number.isFinite(max) ? max : 600;
                    const normalizedValue = snapBudgetValue(
                        Number.isFinite(nextValue) ? nextValue : safeMin,
                        safeMin,
                        safeMax
                    );

                    isSyncingBudgetValue = true;
                    budgetSlider.value = `${normalizedValue}`;
                    if (budgetPrecisionInput instanceof HTMLInputElement) {
                        budgetPrecisionInput.value = `${normalizedValue}`;
                    }
                    budgetSlider.dispatchEvent(new Event("input", { bubbles: true }));
                    if (emitChangeEvent) {
                        budgetSlider.dispatchEvent(new Event("change", { bubbles: true }));
                    }
                    isSyncingBudgetValue = false;
                };

                const syncAdaptiveBudgetRange = () => {
                    const peopleValue = Number.parseInt(householdInput?.value ?? "2", 10);
                    const safePeople = Number.isInteger(peopleValue) ? Math.max(1, Math.min(8, peopleValue)) : 2;
                    const safeMealsPerDay = getMealCount();
                    const planDaysValue = Number.parseInt(planDaysSlider?.value ?? "7", 10);
                    const safePlanDays = Number.isInteger(planDaysValue) ? Math.max(1, Math.min(7, planDaysValue)) : 7;
                    const portionMultiplier = getPortionMultiplier();

                    const estimatedBaseline = safePeople * safeMealsPerDay * safePlanDays * 3.4 * portionMultiplier;
                    let adaptiveMin = snapBudgetValue(estimatedBaseline * 0.72, 15, 600);
                    let adaptiveMax = snapBudgetValue(estimatedBaseline * 1.75, 15, 600);

                    if (adaptiveMax - adaptiveMin < 45) {
                        adaptiveMax = snapBudgetValue(adaptiveMin + 45, 15, 600);
                    }
                    if (adaptiveMin > adaptiveMax - 25) {
                        adaptiveMin = snapBudgetValue(adaptiveMax - 45, 15, 600);
                    }
                    if (adaptiveMin >= adaptiveMax) {
                        adaptiveMin = snapBudgetValue(adaptiveMax - (budgetIncrement * 8), 15, 600);
                        if (adaptiveMin >= adaptiveMax) {
                            adaptiveMax = snapBudgetValue(adaptiveMin + budgetIncrement, 15, 600);
                        }
                    }

                    budgetSlider.min = `${adaptiveMin}`;
                    budgetSlider.max = `${adaptiveMax}`;
                    budgetSlider.step = `${budgetIncrement}`;

                    if (budgetPrecisionInput instanceof HTMLInputElement) {
                        budgetPrecisionInput.min = `${adaptiveMin}`;
                        budgetPrecisionInput.max = `${adaptiveMax}`;
                        budgetPrecisionInput.step = `${budgetIncrement}`;
                    }

                    if (budgetMinLabel instanceof HTMLElement) {
                        budgetMinLabel.textContent = formatPounds(adaptiveMin);
                    }
                    if (budgetMaxLabel instanceof HTMLElement) {
                        budgetMaxLabel.textContent = formatPounds(adaptiveMax);
                    }

                    const currentValue = Number.parseFloat(budgetSlider.value ?? `${adaptiveMin}`);
                    syncBudgetSliderValue(currentValue, false);
                };

                if (form.dataset.adaptiveBudgetRangeWired !== "true") {
                    form.dataset.adaptiveBudgetRangeWired = "true";

                    budgetSlider.addEventListener("input", () => {
                        if (isSyncingBudgetValue) {
                            return;
                        }

                        const currentValue = Number.parseFloat(budgetSlider.value ?? "0");
                        if (budgetPrecisionInput instanceof HTMLInputElement && Number.isFinite(currentValue)) {
                            const safeMin = Number.parseFloat(budgetSlider.min ?? "15");
                            const safeMax = Number.parseFloat(budgetSlider.max ?? "600");
                            budgetPrecisionInput.value = `${snapBudgetValue(
                                currentValue,
                                Number.isFinite(safeMin) ? safeMin : 15,
                                Number.isFinite(safeMax) ? safeMax : 600
                            )}`;
                        }
                    });
                    budgetSlider.addEventListener("change", () => {
                        if (isSyncingBudgetValue) {
                            return;
                        }

                        trackBudgetMaxHit();
                    });
                    budgetSlider.addEventListener("pointerup", () => {
                        if (isSyncingBudgetValue) {
                            return;
                        }

                        trackBudgetMaxHit();
                    });

                    if (budgetPrecisionInput instanceof HTMLInputElement) {
                        const onPrecisionInput = emitChangeEvent => {
                            const rawValue = Number.parseFloat(budgetPrecisionInput.value ?? "");
                            if (!Number.isFinite(rawValue)) {
                                return;
                            }

                            syncBudgetSliderValue(rawValue, emitChangeEvent);
                        };

                        budgetPrecisionInput.addEventListener("input", () => {
                            onPrecisionInput(false);
                        });
                        budgetPrecisionInput.addEventListener("change", () => {
                            onPrecisionInput(true);
                        });
                    }

                    if (budgetPrecisionTrigger instanceof HTMLButtonElement) {
                        budgetPrecisionTrigger.addEventListener("click", () => {
                            showBudgetPrecisionInput();
                            if (budgetPrecisionInput instanceof HTMLInputElement) {
                                budgetPrecisionInput.focus({ preventScroll: true });
                                budgetPrecisionInput.select();
                            }
                        });
                    }

                    if (householdInput instanceof HTMLInputElement) {
                        householdInput.addEventListener("input", syncAdaptiveBudgetRange);
                        householdInput.addEventListener("change", syncAdaptiveBudgetRange);
                    }
                    if (planDaysSlider instanceof HTMLInputElement) {
                        planDaysSlider.addEventListener("input", syncAdaptiveBudgetRange);
                        planDaysSlider.addEventListener("change", syncAdaptiveBudgetRange);
                    }
                    if (portionInput instanceof HTMLInputElement) {
                        portionInput.addEventListener("change", syncAdaptiveBudgetRange);
                    }
                    selectedMealTypes.forEach(input => {
                        if (input instanceof HTMLInputElement) {
                            input.addEventListener("change", syncAdaptiveBudgetRange);
                        }
                    });
                }

                syncAdaptiveBudgetRange();
            }
        });
    };

    wirePlanBasicsSliders(document);

    const refreshPlanBasicsSliders = scope => {
        const sliderScope = scope instanceof Element ? scope : document;
        const sliderInputs = Array.from(
            sliderScope.querySelectorAll("[data-number-slider-range], [data-option-slider-range]")
        );
        sliderInputs.forEach(input => {
            if (input instanceof HTMLInputElement) {
                input.dispatchEvent(new Event("input", { bubbles: true }));
            }
        });
    };

    const schedulePlanBasicsSliderRefresh = scope => {
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                refreshPlanBasicsSliders(scope);
            });
        });
    };

    const wirePlanBasicsAccordion = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();
        const currencyFormatter = new Intl.NumberFormat("en-GB", {
            style: "currency",
            currency: "GBP",
            maximumFractionDigits: 0
        });

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const planBasicItems = Array.from(form.querySelectorAll("[data-plan-basic-item]"))
                .filter(item => item instanceof HTMLDetailsElement);
            if (planBasicItems.length === 0) {
                return;
            }

            const getSelectedMealTypes = () => {
                const selected = Array.from(
                    form.querySelectorAll("input[type='checkbox'][name='Request.SelectedMealTypes']")
                )
                    .filter(input => input instanceof HTMLInputElement && input.checked)
                    .map(input => input.value.trim())
                    .filter(value => value.length > 0);

                const order = ["Breakfast", "Lunch", "Dinner"];
                return order.filter(type => selected.some(value => value.toLowerCase() === type.toLowerCase()));
            };

            const getSummaryText = item => {
                if (!(item instanceof HTMLDetailsElement)) {
                    return "";
                }

                const formatSupermarketLabel = value => {
                    const normalized = (value ?? "").trim();
                    if (normalized.length === 0) {
                        return "";
                    }

                    return normalized
                        .toLowerCase()
                        .replace(/\b([a-z])/g, letter => letter.toUpperCase());
                };

                const itemType = (item.dataset.planBasicItem ?? "").trim().toLowerCase();
                switch (itemType) {
                    case "supermarket": {
                        const selectedSupermarket = getSelectedSupermarket(form).trim();
                        return selectedSupermarket.length > 0
                            ? formatSupermarketLabel(selectedSupermarket)
                            : "Not set";
                    }
                    case "budget": {
                        const budgetInput = form.querySelector("input[name='Request.WeeklyBudget']");
                        const parsed = Number.parseFloat(budgetInput?.value ?? "0");
                        const safeValue = Number.isFinite(parsed) ? parsed : 0;
                        return currencyFormatter.format(safeValue);
                    }
                    case "plan-days": {
                        const planDaysInput = form.querySelector("input[name='Request.PlanDays']");
                        const rawValue = Number.parseInt(planDaysInput?.value ?? "1", 10);
                        const safeValue = Number.isInteger(rawValue) ? Math.max(1, Math.min(7, rawValue)) : 1;
                        return `${safeValue} day${safeValue === 1 ? "" : "s"}`;
                    }
                    case "cook-days": {
                        const cookDaysInput = form.querySelector("input[name='Request.CookDays']");
                        const rawValue = Number.parseInt(cookDaysInput?.value ?? "1", 10);
                        const safeValue = Number.isInteger(rawValue) ? Math.max(1, rawValue) : 1;
                        return `${safeValue} day${safeValue === 1 ? "" : "s"}`;
                    }
                    case "meal-types": {
                        const selectedMealTypes = getSelectedMealTypes();
                        return selectedMealTypes.length > 0
                            ? selectedMealTypes.join(", ")
                            : "None selected";
                    }
                    default:
                        return "";
                }
            };

            const isComplete = item => {
                if (!(item instanceof HTMLDetailsElement)) {
                    return false;
                }

                const itemType = (item.dataset.planBasicItem ?? "").trim().toLowerCase();
                switch (itemType) {
                    case "supermarket":
                        return getSelectedSupermarket(form).trim().length > 0;
                    case "budget":
                        return Number.isFinite(Number.parseFloat(
                            (form.querySelector("input[name='Request.WeeklyBudget']")?.value ?? "0")
                        ));
                    case "plan-days":
                        return Number.isFinite(Number.parseInt(
                            (form.querySelector("input[name='Request.PlanDays']")?.value ?? "0"),
                            10
                        ));
                    case "cook-days":
                        return Number.isFinite(Number.parseInt(
                            (form.querySelector("input[name='Request.CookDays']")?.value ?? "0"),
                            10
                        ));
                    case "meal-types":
                        return getSelectedMealTypes().length > 0;
                    case "special-options":
                        return true;
                    default:
                        return false;
                }
            };

            const syncSummary = item => {
                if (!(item instanceof HTMLDetailsElement)) {
                    return;
                }

                const summaryOutput = item.querySelector("[data-plan-basic-summary]");
                if (!(summaryOutput instanceof HTMLElement)) {
                    return;
                }

                summaryOutput.textContent = getSummaryText(item);
            };

            const openOnlyItem = nextItem => {
                if (!(nextItem instanceof HTMLDetailsElement)) {
                    return;
                }

                planBasicItems.forEach(item => {
                    if (!(item instanceof HTMLDetailsElement)) {
                        return;
                    }
                    item.open = item === nextItem;
                });

                schedulePlanBasicsSliderRefresh(nextItem);
            };

            const cookDaysItem = planBasicItems.find(item =>
                item instanceof HTMLDetailsElement &&
                (item.dataset.planBasicItem ?? "").trim().toLowerCase() === "cook-days"
            );

            const hasValidationError = item => {
                if (!(item instanceof HTMLElement)) {
                    return false;
                }

                return Array.from(item.querySelectorAll(".text-danger"))
                    .some(errorNode => (errorNode.textContent ?? "").trim().length > 0);
            };

            planBasicItems.forEach(item => {
                if (!(item instanceof HTMLDetailsElement)) {
                    return;
                }

                syncSummary(item);
                if (item.dataset.planBasicAccordionWired === "true") {
                    return;
                }

                item.dataset.planBasicAccordionWired = "true";
                item.addEventListener("toggle", () => {
                    syncSummary(item);
                    if (item.open) {
                        openOnlyItem(item);
                    }
                });

                item.addEventListener("focusin", event => {
                    if (item.open) {
                        return;
                    }

                    const summary = item.querySelector("summary");
                    if (
                        summary instanceof HTMLElement &&
                        event.target instanceof Node &&
                        summary.contains(event.target)
                    ) {
                        return;
                    }

                    openOnlyItem(item);
                });

                const itemType = (item.dataset.planBasicItem ?? "").trim().toLowerCase();
                if (itemType === "supermarket") {
                    const supermarketInputs = Array.from(item.querySelectorAll("[data-supermarket-option]"))
                        .filter(input => input instanceof HTMLInputElement);
                    supermarketInputs.forEach(input => {
                        input.addEventListener("change", () => {
                            syncSummary(item);
                        });
                    });
                }

                if (itemType === "budget" || itemType === "plan-days" || itemType === "cook-days") {
                    const rangeInput = item.querySelector("[data-number-slider-range]");
                    if (rangeInput instanceof HTMLInputElement) {
                        rangeInput.addEventListener("input", () => {
                            syncSummary(item);
                            if (itemType === "plan-days" && cookDaysItem instanceof HTMLDetailsElement) {
                                syncSummary(cookDaysItem);
                            }
                        });
                        rangeInput.addEventListener("change", () => {
                            syncSummary(item);
                            if (itemType === "plan-days" && cookDaysItem instanceof HTMLDetailsElement) {
                                syncSummary(cookDaysItem);
                            }
                        });
                    }
                }

                if (itemType === "meal-types") {
                    const mealTypeCheckboxes = Array.from(
                        item.querySelectorAll("input[type='checkbox'][name='Request.SelectedMealTypes']")
                    ).filter(input => input instanceof HTMLInputElement);
                    mealTypeCheckboxes.forEach(input => {
                        input.addEventListener("change", () => {
                            syncSummary(item);
                        });
                    });
                }
            });

            const itemWithError = planBasicItems.find(item => hasValidationError(item));
            if (itemWithError instanceof HTMLDetailsElement) {
                openOnlyItem(itemWithError);
            }

            if (form.dataset.planBasicScrollWired !== "true") {
                form.dataset.planBasicScrollWired = "true";
                let scrollFrameId = 0;
                const collapseScrolledPastItems = () => {
                    scrollFrameId = 0;
                    const activeElement = document.activeElement;
                    planBasicItems.forEach(item => {
                        if (!(item instanceof HTMLDetailsElement) || !item.open || !isComplete(item)) {
                            return;
                        }

                        if (activeElement instanceof HTMLElement && item.contains(activeElement)) {
                            return;
                        }

                        const bounds = item.getBoundingClientRect();
                        const hasScrolledPast = bounds.bottom < 64;
                        if (hasScrolledPast) {
                            item.open = false;
                        }
                    });
                };

                window.addEventListener("scroll", () => {
                    if (scrollFrameId !== 0) {
                        return;
                    }

                    scrollFrameId = requestAnimationFrame(collapseScrolledPastItems);
                }, { passive: true });
            }
        });
    };

    const wireMealTypeSelectors = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const selectedMealTypeInputs = Array.from(
                form.querySelectorAll("input[type='checkbox'][name='Request.SelectedMealTypes']")
            ).filter(input => input instanceof HTMLInputElement);
            const mealsPerDayInput = form.querySelector("input[type='hidden'][name='Request.MealsPerDay']");
            if (selectedMealTypeInputs.length === 0 || !(mealsPerDayInput instanceof HTMLInputElement)) {
                return;
            }

            const syncMealsPerDayValue = () => {
                const selectedCount = selectedMealTypeInputs
                    .filter(input => input instanceof HTMLInputElement && input.checked && input.value.trim().length > 0)
                    .length;
                mealsPerDayInput.value = `${Math.max(0, Math.min(3, selectedCount))}`;
            };

            if (form.dataset.mealTypeSelectorWired !== "true") {
                selectedMealTypeInputs.forEach(input => {
                    if (input instanceof HTMLInputElement) {
                        input.addEventListener("change", syncMealsPerDayValue);
                    }
                });

                form.dataset.mealTypeSelectorWired = "true";
            }

            syncMealsPerDayValue();
        });
    };

    wireMealTypeSelectors(document);

    const wireSharedPreferenceSummaries = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        const normalizeSummary = value => (value ?? "").trim();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const servingSummary = form.querySelector("[data-serving-summary]");
            const dietarySummary = form.querySelector("[data-dietary-summary]");
            const cookingSummary = form.querySelector("[data-cooking-summary]");
            const exclusionSummary = form.querySelector("[data-exclusion-summary]");
            const pantrySummary = form.querySelector("[data-pantry-summary]");
            const generatorCoreSummary = form.querySelector("[data-generator-core-summary]");
            const specialOptionsSummary = form.querySelector("[data-special-options-summary]");
            if (!(servingSummary instanceof HTMLElement) &&
                !(dietarySummary instanceof HTMLElement) &&
                !(cookingSummary instanceof HTMLElement) &&
                !(exclusionSummary instanceof HTMLElement) &&
                !(pantrySummary instanceof HTMLElement) &&
                !(generatorCoreSummary instanceof HTMLElement) &&
                !(specialOptionsSummary instanceof HTMLElement)) {
                return;
            }

            const householdInput = form.querySelector("input[name='Request.HouseholdSize']");
            const portionInput = form.querySelector("input[name='Request.PortionSize']");
            const dietaryInputs = Array.from(form.querySelectorAll("input[name='Request.DietaryModes']"));
            const quickMealsInput = form.querySelector("input[type='checkbox'][name='Request.PreferQuickMeals']");
            const savedMealRepeatsInput = form.querySelector("input[type='checkbox'][name='Request.EnableSavedMealRepeats']");
            const savedMealRepeatRateInput = form.querySelector("input[name='Request.SavedMealRepeatRatePercent']");
            const savedMealRepeatRateField = form.querySelector("[data-saved-repeat-rate-field]");
            const exclusionsInput = form.querySelector("input[name='Request.DislikesOrAllergens']");
            const pantryInput = form.querySelector("textarea[name='Request.PantryItems']");
            const strictCoreInput = form.querySelector("input[type='checkbox'][name='Request.RequireCorePantryIngredients']");
            const specialTreatInput = form.querySelector("input[type='checkbox'][name='Request.IncludeSpecialTreatMeal']");
            const specialTreatCookDayField = form.querySelector("[data-special-treat-day-field]");
            const specialTreatCookDaySelect = form.querySelector("[data-special-treat-day-select]");
            const dessertAddOnInput = form.querySelector("input[type='checkbox'][name='Request.IncludeDessertAddOn']");
            const cookDaysInput = form.querySelector("input[name='Request.CookDays']");
            const planDaysInput = form.querySelector("input[name='Request.PlanDays']");

            const weekDayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
            const buildDefaultCookDayMultipliers = (cookDays, planDays) => {
                const safePlanDays = Number.isInteger(planDays) ? Math.max(1, Math.min(7, planDays)) : 7;
                const safeCookDays = Number.isInteger(cookDays) ? Math.max(1, Math.min(safePlanDays, cookDays)) : safePlanDays;
                const leftoverDays = Math.max(0, safePlanDays - safeCookDays);
                const multipliers = Array.from({ length: safeCookDays }, () => 1);
                for (let index = 0; index < leftoverDays; index += 1) {
                    multipliers[index % safeCookDays] += 1;
                }

                return multipliers;
            };

            const buildSpecialTreatCookDayLabels = () => {
                const cookDaysValue = Number.parseInt(cookDaysInput?.value ?? "7", 10);
                const planDaysValue = Number.parseInt(planDaysInput?.value ?? "7", 10);
                const safePlanDays = Number.isInteger(planDaysValue) ? Math.max(1, Math.min(7, planDaysValue)) : 7;
                const safeCookDays = Number.isInteger(cookDaysValue) ? Math.max(1, Math.min(safePlanDays, cookDaysValue)) : safePlanDays;
                const multipliers = buildDefaultCookDayMultipliers(safeCookDays, safePlanDays);
                const labels = [];
                let dayCursor = 0;
                for (let index = 0; index < safeCookDays; index += 1) {
                    const safeDayIndex = Math.max(0, Math.min(safePlanDays - 1, dayCursor));
                    const dayName = weekDayNames[safeDayIndex];
                    labels.push(dayName);
                    dayCursor += Math.max(1, multipliers[index]);
                }

                return labels;
            };

            const syncSpecialTreatCookDayOptions = () => {
                if (!(specialTreatCookDaySelect instanceof HTMLSelectElement)) {
                    return;
                }

                const labels = buildSpecialTreatCookDayLabels();
                const previousValue = Number.parseInt(specialTreatCookDaySelect.value, 10);
                specialTreatCookDaySelect.innerHTML = "";
                labels.forEach((label, index) => {
                    const option = document.createElement("option");
                    option.value = `${index}`;
                    option.textContent = label;
                    specialTreatCookDaySelect.appendChild(option);
                });

                const fallbackIndex = labels.length > 0 ? 0 : -1;
                const nextIndex =
                    Number.isInteger(previousValue) &&
                    previousValue >= 0 &&
                    previousValue < labels.length
                        ? previousValue
                        : fallbackIndex;
                if (nextIndex >= 0) {
                    specialTreatCookDaySelect.value = `${nextIndex}`;
                }
            };

            const syncSpecialTreatCookDayVisibility = () => {
                if (!(specialTreatInput instanceof HTMLInputElement)) {
                    return;
                }

                if (specialTreatCookDayField instanceof HTMLElement) {
                    specialTreatCookDayField.hidden = !specialTreatInput.checked;
                }

                if (specialTreatCookDaySelect instanceof HTMLSelectElement) {
                    specialTreatCookDaySelect.disabled = !specialTreatInput.checked;
                }
            };

            const updateServingSummary = () => {
                if (!(servingSummary instanceof HTMLElement)) {
                    return;
                }

                const peopleValue = Number.parseInt(householdInput?.value ?? "2", 10);
                const safePeople = Number.isInteger(peopleValue) ? Math.max(1, Math.min(8, peopleValue)) : 2;
                const portionValue = (portionInput?.value ?? "Medium").trim() || "Medium";
                const peopleLabel = safePeople === 1 ? "person" : "people";
                servingSummary.textContent = `${safePeople} ${peopleLabel} - ${portionValue} portions`;
            };

            const updateDietarySummary = () => {
                if (!(dietarySummary instanceof HTMLElement)) {
                    return;
                }

                const selectedModes = dietaryInputs
                    .filter(input => input instanceof HTMLInputElement && input.checked)
                    .map(input => input.value.trim())
                    .filter(value => value.length > 0);
                dietarySummary.textContent = selectedModes.length > 0
                    ? selectedModes.join(", ")
                    : "No dietary filters";
            };

            const updateCookingSummary = () => {
                if (!(cookingSummary instanceof HTMLElement) || !(quickMealsInput instanceof HTMLInputElement)) {
                    return;
                }

                const quickMealsSummary = quickMealsInput.checked ? "Quick meals on" : "Quick meals off";
                const savedRepeatsSummary = (() => {
                    if (!(savedMealRepeatsInput instanceof HTMLInputElement) || !savedMealRepeatsInput.checked) {
                        return "Saved repeats off";
                    }

                    const parsedRepeatRate = Number.parseInt(savedMealRepeatRateInput?.value ?? "35", 10);
                    const safeRepeatRate = Number.isInteger(parsedRepeatRate)
                        ? Math.max(10, Math.min(100, parsedRepeatRate))
                        : 35;
                    return `Saved repeats ${safeRepeatRate}%`;
                })();

                cookingSummary.textContent = `${quickMealsSummary}, ${savedRepeatsSummary}`;
            };

            const syncSavedMealRepeatControls = () => {
                const enabled = savedMealRepeatsInput instanceof HTMLInputElement && savedMealRepeatsInput.checked;
                if (savedMealRepeatRateField instanceof HTMLElement) {
                    savedMealRepeatRateField.hidden = !enabled;
                }

                if (savedMealRepeatRateInput instanceof HTMLInputElement) {
                    savedMealRepeatRateInput.disabled = !enabled;
                }
            };

            const updateExclusionSummary = () => {
                if (!(exclusionSummary instanceof HTMLElement)) {
                    return;
                }

                const exclusionsValue = normalizeSummary(exclusionsInput?.value ?? "");
                if (exclusionsValue.length > 0) {
                    exclusionSummary.textContent = exclusionsValue;
                    exclusionSummary.setAttribute("title", exclusionsValue);
                    return;
                }

                exclusionSummary.textContent = "No exclusions set";
                exclusionSummary.removeAttribute("title");
            };

            const updatePantrySummary = () => {
                if (!(pantrySummary instanceof HTMLElement)) {
                    return;
                }

                const pantryValue = normalizeSummary(pantryInput?.value ?? "");
                if (pantryValue.length > 0) {
                    pantrySummary.textContent = pantryValue;
                    pantrySummary.setAttribute("title", pantryValue);
                    return;
                }

                pantrySummary.textContent = "No foods listed";
                pantrySummary.removeAttribute("title");
            };

            const updateGeneratorCoreSummary = () => {
                if (!(generatorCoreSummary instanceof HTMLElement) || !(strictCoreInput instanceof HTMLInputElement)) {
                    return;
                }

                generatorCoreSummary.textContent = strictCoreInput.checked
                    ? "Strict core on"
                    : "Strict core off";
            };

            const updateSpecialOptionsSummary = () => {
                if (!(specialOptionsSummary instanceof HTMLElement)) {
                    return;
                }

                const options = [];
                if (specialTreatInput instanceof HTMLInputElement && specialTreatInput.checked) {
                    const selectedTreatDayLabel = specialTreatCookDaySelect instanceof HTMLSelectElement
                        ? (specialTreatCookDaySelect.options[specialTreatCookDaySelect.selectedIndex]?.text ?? "").trim()
                        : "";
                    options.push(selectedTreatDayLabel.length > 0
                        ? `Treat meal on ${selectedTreatDayLabel}`
                        : "Treat meal on");
                }
                if (dessertAddOnInput instanceof HTMLInputElement && dessertAddOnInput.checked) {
                    options.push("Dessert add-on on");
                }

                specialOptionsSummary.textContent = options.length > 0
                    ? options.join(", ")
                    : "No extras";
            };

            if (form.dataset.sharedSummaryWired !== "true") {
                if (householdInput instanceof HTMLInputElement) {
                    householdInput.addEventListener("input", updateServingSummary);
                    householdInput.addEventListener("change", updateServingSummary);
                }
                if (portionInput instanceof HTMLInputElement) {
                    portionInput.addEventListener("input", updateServingSummary);
                    portionInput.addEventListener("change", updateServingSummary);
                }

                dietaryInputs.forEach(input => {
                    if (input instanceof HTMLInputElement) {
                        input.addEventListener("change", updateDietarySummary);
                    }
                });

                if (quickMealsInput instanceof HTMLInputElement) {
                    quickMealsInput.addEventListener("change", updateCookingSummary);
                }
                if (savedMealRepeatsInput instanceof HTMLInputElement) {
                    savedMealRepeatsInput.addEventListener("change", () => {
                        syncSavedMealRepeatControls();
                        updateCookingSummary();
                    });
                }
                if (savedMealRepeatRateInput instanceof HTMLInputElement) {
                    savedMealRepeatRateInput.addEventListener("input", updateCookingSummary);
                    savedMealRepeatRateInput.addEventListener("change", updateCookingSummary);
                }
                if (exclusionsInput instanceof HTMLInputElement) {
                    exclusionsInput.addEventListener("input", updateExclusionSummary);
                    exclusionsInput.addEventListener("change", updateExclusionSummary);
                }
                if (pantryInput instanceof HTMLTextAreaElement) {
                    pantryInput.addEventListener("input", updatePantrySummary);
                    pantryInput.addEventListener("change", updatePantrySummary);
                }
                if (strictCoreInput instanceof HTMLInputElement) {
                    strictCoreInput.addEventListener("change", updateGeneratorCoreSummary);
                }
                if (specialTreatInput instanceof HTMLInputElement) {
                    specialTreatInput.addEventListener("change", () => {
                        syncSpecialTreatCookDayVisibility();
                        updateSpecialOptionsSummary();
                    });
                }
                if (specialTreatCookDaySelect instanceof HTMLSelectElement) {
                    specialTreatCookDaySelect.addEventListener("change", updateSpecialOptionsSummary);
                }
                if (dessertAddOnInput instanceof HTMLInputElement) {
                    dessertAddOnInput.addEventListener("change", updateSpecialOptionsSummary);
                }
                if (cookDaysInput instanceof HTMLInputElement) {
                    cookDaysInput.addEventListener("input", () => {
                        syncSpecialTreatCookDayOptions();
                        updateSpecialOptionsSummary();
                    });
                    cookDaysInput.addEventListener("change", () => {
                        syncSpecialTreatCookDayOptions();
                        updateSpecialOptionsSummary();
                    });
                }
                if (planDaysInput instanceof HTMLInputElement) {
                    planDaysInput.addEventListener("input", () => {
                        syncSpecialTreatCookDayOptions();
                        updateSpecialOptionsSummary();
                    });
                    planDaysInput.addEventListener("change", () => {
                        syncSpecialTreatCookDayOptions();
                        updateSpecialOptionsSummary();
                    });
                }

                form.dataset.sharedSummaryWired = "true";
            }

            syncSpecialTreatCookDayOptions();
            syncSpecialTreatCookDayVisibility();
            syncSavedMealRepeatControls();
            updateServingSummary();
            updateDietarySummary();
            updateCookingSummary();
            updateExclusionSummary();
            updatePantrySummary();
            updateGeneratorCoreSummary();
            updateSpecialOptionsSummary();
        });
    };

    const wireSharedSetupAccordion = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const sharedItems = Array.from(
                form.querySelectorAll(".aislepilot-shared-setup > .aislepilot-collapsible")
            ).filter(item => item instanceof HTMLDetailsElement);
            if (sharedItems.length === 0) {
                return;
            }

            const openOnlyItem = nextItem => {
                if (!(nextItem instanceof HTMLDetailsElement)) {
                    return;
                }

                sharedItems.forEach(item => {
                    if (!(item instanceof HTMLDetailsElement)) {
                        return;
                    }

                    item.open = item === nextItem;
                });
            };

            sharedItems.forEach(item => {
                if (!(item instanceof HTMLDetailsElement) || item.dataset.sharedSetupAccordionWired === "true") {
                    return;
                }

                item.dataset.sharedSetupAccordionWired = "true";
                item.addEventListener("toggle", () => {
                    if (item.open) {
                        openOnlyItem(item);
                    }
                });

                item.addEventListener("focusin", event => {
                    if (item.open) {
                        return;
                    }

                    const summary = item.querySelector("summary");
                    if (
                        summary instanceof HTMLElement &&
                        event.target instanceof Node &&
                        summary.contains(event.target)
                    ) {
                        return;
                    }

                    openOnlyItem(item);
                });
            });
        });
    };

    const wireGeneratorSettingsAccordion = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const generatorItems = Array.from(
                form.querySelectorAll("[data-generator-only-preference] .aislepilot-planner-settings-list > .aislepilot-collapsible")
            ).filter(item => item instanceof HTMLDetailsElement);
            if (generatorItems.length === 0) {
                return;
            }

            const openOnlyItem = nextItem => {
                if (!(nextItem instanceof HTMLDetailsElement)) {
                    return;
                }

                generatorItems.forEach(item => {
                    if (!(item instanceof HTMLDetailsElement)) {
                        return;
                    }

                    item.open = item === nextItem;
                });
            };

            generatorItems.forEach(item => {
                if (!(item instanceof HTMLDetailsElement) || item.dataset.generatorAccordionWired === "true") {
                    return;
                }

                item.dataset.generatorAccordionWired = "true";
                item.addEventListener("toggle", () => {
                    if (item.open) {
                        openOnlyItem(item);
                    }
                });

                item.addEventListener("focusin", event => {
                    if (item.open) {
                        return;
                    }

                    const summary = item.querySelector("summary");
                    if (
                        summary instanceof HTMLElement &&
                        event.target instanceof Node &&
                        summary.contains(event.target)
                    ) {
                        return;
                    }

                    openOnlyItem(item);
                });
            });
        });
    };

    wireSharedPreferenceSummaries(document);

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
    wirePlanBasicsAccordion(document);
    wireSharedSetupAccordion(document);
    wireGeneratorSettingsAccordion(document);

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
                    const failedLabel = trigger.dataset.notesFailedLabel?.trim() || "Could not prepare shopping list. Try again.";
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

    const wireSetupModeSwitches = scope => {
        const setupModeModule = window.AislePilotSetupMode;
        if (!setupModeModule || typeof setupModeModule.wireSetupModeSwitches !== "function") {
            return;
        }

        setupModeModule.wireSetupModeSwitches(scope);
    };

    wireSetupModeSwitches(document);

    const wireModeScopedPreferenceVisibility = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const generatorOnlyPreferences = Array.from(form.querySelectorAll("[data-generator-only-preference]"))
                .filter(preference => preference instanceof HTMLElement);
            const plannerOnlyPreferences = Array.from(form.querySelectorAll("[data-planner-only-preference]"))
                .filter(preference => preference instanceof HTMLElement);
            if (generatorOnlyPreferences.length === 0 && plannerOnlyPreferences.length === 0) {
                return;
            }

            const setupModeValueInput = form.querySelector("[data-setup-mode-value]");
            const resolveActiveMode = () => {
                if (setupModeValueInput instanceof HTMLInputElement) {
                    const explicitMode = setupModeValueInput.value.trim().toLowerCase();
                    if (explicitMode.length > 0) {
                        return explicitMode;
                    }
                }

                const activePanel = form.querySelector("[data-setup-mode-panel]:not([hidden])");
                if (activePanel instanceof HTMLElement) {
                    return (activePanel.dataset.setupModePanel ?? "").trim().toLowerCase();
                }

                return "";
            };

            const syncModeScopedPreferenceVisibility = () => {
                const activeMode = resolveActiveMode();
                const shouldShowGeneratorOnly = activeMode === "generator";
                const shouldShowPlannerOnly = activeMode === "planner";

                generatorOnlyPreferences.forEach(preference => {
                    if (!(preference instanceof HTMLElement)) {
                        return;
                    }

                    if (shouldShowGeneratorOnly) {
                        preference.removeAttribute("hidden");
                        preference.setAttribute("aria-hidden", "false");
                    } else {
                        preference.setAttribute("hidden", "hidden");
                        preference.setAttribute("aria-hidden", "true");
                    }

                    const checkbox = preference.querySelector("input[type='checkbox']");
                    if (checkbox instanceof HTMLInputElement) {
                        checkbox.disabled = !shouldShowGeneratorOnly;
                    }
                });

                plannerOnlyPreferences.forEach(preference => {
                    if (!(preference instanceof HTMLElement)) {
                        return;
                    }

                    if (shouldShowPlannerOnly) {
                        preference.removeAttribute("hidden");
                        preference.setAttribute("aria-hidden", "false");
                    } else {
                        preference.setAttribute("hidden", "hidden");
                        preference.setAttribute("aria-hidden", "true");
                    }
                });
            };

            if (form.dataset.modeScopedPreferenceVisibilityWired !== "true") {
                const setupModeSwitch = form.querySelector("[data-setup-mode-switch]");
                if (setupModeSwitch instanceof HTMLElement) {
                    setupModeSwitch.addEventListener(
                        "aislepilot:setup-mode-change",
                        () => {
                            syncModeScopedPreferenceVisibility();
                            schedulePlanBasicsSliderRefresh(form);
                        }
                    );
                }

                form.dataset.modeScopedPreferenceVisibilityWired = "true";
            }

            syncModeScopedPreferenceVisibility();
        });
    };

    wireModeScopedPreferenceVisibility(document);

    const resetSubmittingState = () => {
        hidePlanLoadingShell();
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

                const originalAriaLabel = button.dataset.originalAriaLabel;
                if (typeof originalAriaLabel === "string") {
                    if (originalAriaLabel.length > 0) {
                        button.setAttribute("aria-label", originalAriaLabel);
                    } else {
                        button.removeAttribute("aria-label");
                    }
                }

                const originalLabel = button.dataset.originalLabel?.trim();
                if (!button.classList.contains("is-icon-only") && originalLabel && originalLabel.length > 0) {
                    button.textContent = originalLabel;
                }

                unlockSubmitButtonWidth(button);
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
    const swapScrollRestoreDurationMs = 1100;
    const clearPersistedSwapScroll = () => {
        sessionStorage.removeItem(swapScrollKey);
    };
    const clearRestorePending = () => {
        document.documentElement.classList.remove("aislepilot-restore-pending");
    };
    const mealImagePollingController = window.AislePilotMealImagePolling?.createController({
        documentRef: document,
        intervalMs: 5000,
        maxAttempts: 48
    });
    const pollMealImagesOnce = async () => {
        if (!mealImagePollingController || typeof mealImagePollingController.pollOnce !== "function") {
            return;
        }

        await mealImagePollingController.pollOnce();
    };
    const startMealImagePolling = () => {
        if (!mealImagePollingController || typeof mealImagePollingController.start !== "function") {
            return;
        }

        mealImagePollingController.start();
    };

    // Image polling must run for both planner and generator-only pages.
    startMealImagePolling();
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") {
            void pollMealImagesOnce();
        }
    });

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
    const savedWeeksPanel = document.querySelector("[data-saved-weeks-panel]");
    const getOverviewContent = () => document.querySelector("[data-overview-content]");

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
    let viewportSwipeStartedInsideDayMealCard = false;
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

    const isEventWithinDayMealCard = event => {
        if (!(event instanceof Event)) {
            return false;
        }

        const target = event.target;
        if (target instanceof Element && target.closest("[data-day-meal-card]")) {
            return true;
        }

        if (typeof event.composedPath !== "function") {
            return false;
        }

        const eventPath = event.composedPath();
        return eventPath.some(node => node instanceof Element && node.hasAttribute("data-day-meal-card"));
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

    const resolveSwapTargetCard = form => {
        if (!(form instanceof HTMLFormElement)) {
            return null;
        }

        const inlineCard = form.closest(".aislepilot-card");
        if (inlineCard instanceof HTMLElement) {
            return inlineCard;
        }

        const dayInput = form.querySelector("input[name='dayIndex']");
        const parsedDayIndex = Number.parseInt(dayInput?.value ?? "", 10);
        if (!Number.isInteger(parsedDayIndex) || parsedDayIndex < 0) {
            return null;
        }

        const selector = `[data-day-card-header-actions][data-slot-index='${parsedDayIndex}']`;
        const matchingActionRow = document.querySelector(selector);
        if (!(matchingActionRow instanceof HTMLElement)) {
            return null;
        }

        const matchingCard = matchingActionRow.closest(".aislepilot-card");
        return matchingCard instanceof HTMLElement ? matchingCard : null;
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

    let overviewActionsGlobalWired = false;

    const syncOverviewActionsLayerState = () => {
        const overviewSection = document.querySelector("#aislepilot-overview");
        if (!(overviewSection instanceof HTMLElement)) {
            return;
        }

        const hasOpenMenu = document.querySelector("[data-overview-actions-menu][open]") instanceof HTMLDetailsElement;
        overviewSection.classList.toggle("is-actions-menu-open", hasOpenMenu);
    };

    const closeOpenOverviewActionsMenus = except => {
        const openMenus = Array.from(document.querySelectorAll("[data-overview-actions-menu][open]"));
        openMenus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement) || menu === except) {
                return;
            }

            menu.open = false;
        });

        window.requestAnimationFrame(syncOverviewActionsLayerState);
    };

    const positionOverviewActionsMenu = menu => {
        if (!(menu instanceof HTMLDetailsElement) || !menu.open) {
            return;
        }

        const actionsMenu = menu.querySelector(".aislepilot-overview-actions-menu");
        if (!(actionsMenu instanceof HTMLElement)) {
            return;
        }

        const viewportPadding = 8;
        menu.classList.remove("is-align-left");
        let actionsRect = actionsMenu.getBoundingClientRect();
        if (actionsRect.left < viewportPadding) {
            menu.classList.add("is-align-left");
            actionsRect = actionsMenu.getBoundingClientRect();
        }

        if (actionsRect.right > window.innerWidth - viewportPadding && menu.classList.contains("is-align-left")) {
            menu.classList.remove("is-align-left");
        }
    };

    const wireOverviewActionsMenus = scope => {
        const menus = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-overview-actions-menu]"))
            : Array.from(document.querySelectorAll("[data-overview-actions-menu]"));

        menus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement) || menu.dataset.overviewActionsWired === "true") {
                return;
            }

            menu.dataset.overviewActionsWired = "true";
            menu.addEventListener("toggle", () => {
                const trigger = menu.querySelector("summary");
                if (trigger instanceof HTMLElement) {
                    trigger.setAttribute("aria-expanded", menu.open ? "true" : "false");
                }

                if (!menu.open) {
                    menu.classList.remove("is-align-left");
                    syncOverviewActionsLayerState();
                    return;
                }

                closeOpenOverviewActionsMenus(menu);
                syncOverviewActionsLayerState();
                window.requestAnimationFrame(() => {
                    positionOverviewActionsMenu(menu);
                });
            });

            const actionButtons = Array.from(menu.querySelectorAll("button"));
            actionButtons.forEach(button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                button.addEventListener("click", () => {
                    menu.open = false;
                });
            });
        });

        syncOverviewActionsLayerState();

        if (overviewActionsGlobalWired) {
            return;
        }

        overviewActionsGlobalWired = true;
        document.addEventListener("click", event => {
            if (!(event.target instanceof Element)) {
                return;
            }

            if (event.target.closest("[data-overview-actions-menu]")) {
                return;
            }

            closeOpenOverviewActionsMenus(null);
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeOpenOverviewActionsMenus(null);
            }
        });

        window.addEventListener("resize", () => {
            const openMenus = Array.from(document.querySelectorAll("[data-overview-actions-menu][open]"));
            openMenus.forEach(openMenu => {
                positionOverviewActionsMenu(openMenu);
            });
        });
    };

    let cardMoreActionsGlobalWired = false;

    let headMenuGlobalWired = false;

    const closeOpenHeadMenus = except => {
        const openMenus = Array.from(document.querySelectorAll("[data-head-menu][open]"));
        openMenus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement) || menu === except) {
                return;
            }

            menu.open = false;
        });
    };

    const wireHeadMenus = scope => {
        const menus = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-head-menu]"))
            : Array.from(document.querySelectorAll("[data-head-menu]"));

        menus.forEach(menu => {
            if (!(menu instanceof HTMLDetailsElement) || menu.dataset.headMenuWired === "true") {
                return;
            }

            menu.dataset.headMenuWired = "true";
            menu.addEventListener("toggle", () => {
                const trigger = menu.querySelector("summary");
                if (trigger instanceof HTMLElement) {
                    trigger.setAttribute("aria-expanded", menu.open ? "true" : "false");
                }

                if (!menu.open) {
                    return;
                }

                closeOpenHeadMenus(menu);
            });
        });

        if (headMenuGlobalWired) {
            return;
        }

        headMenuGlobalWired = true;
        document.addEventListener("click", event => {
            if (!(event.target instanceof Element)) {
                return;
            }

            if (event.target.closest("[data-head-menu]")) {
                return;
            }

            closeOpenHeadMenus(null);
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeOpenHeadMenus(null);
            }
        });
    };

    let cardMoreActionsBackdrop = null;
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
                nextSibling: actionsMenu.nextSibling
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

            const actionButtons = Array.from(menu.querySelectorAll("button[type='submit']"));
            actionButtons.forEach(button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                button.addEventListener("click", () => {
                    closeCardMoreActionsMenu(menu);
                });
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
            let touchStartX = Number.NaN;
            let touchStartY = Number.NaN;

            const clearTouchTracking = () => {
                touchStartX = Number.NaN;
                touchStartY = Number.NaN;
            };

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

            card.addEventListener("touchstart", event => {
                const touch = event.changedTouches[0];
                if (!touch) {
                    clearTouchTracking();
                    return;
                }

                touchStartX = touch.clientX;
                touchStartY = touch.clientY;
            }, { passive: true });

            card.addEventListener("touchend", event => {
                if (!Number.isFinite(touchStartX) || !Number.isFinite(touchStartY)) {
                    return;
                }

                const touch = event.changedTouches[0];
                const startX = touchStartX;
                const startY = touchStartY;
                clearTouchTracking();
                if (!touch) {
                    return;
                }

                const deltaX = touch.clientX - startX;
                const deltaY = touch.clientY - startY;
                if (Math.abs(deltaX) < 32 || Math.abs(deltaX) <= Math.abs(deltaY)) {
                    return;
                }

                if (deltaX < 0) {
                    syncSlot(currentSlotIndex + 1);
                } else {
                    syncSlot(currentSlotIndex - 1);
                }
            }, { passive: true });

            card.addEventListener("touchcancel", () => {
                clearTouchTracking();
            }, { passive: true });

            syncSlot(readRememberedDayMealSlot(card));
        });
    };

    const wireDayCardExpanders = scope => {
        const expanders = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-day-card-expander]"))
            : Array.from(document.querySelectorAll("[data-day-card-expander]"));

        const syncSummaryExpandedState = expander => {
            if (!(expander instanceof HTMLDetailsElement)) {
                return;
            }

            const summary = expander.querySelector(":scope > summary");
            if (!(summary instanceof HTMLElement)) {
                return;
            }

            summary.setAttribute("aria-expanded", expander.open ? "true" : "false");
        };

        expanders.forEach(expander => {
            if (!(expander instanceof HTMLDetailsElement) || expander.dataset.dayCardExpanderWired === "true") {
                return;
            }

            expander.dataset.dayCardExpanderWired = "true";
            syncSummaryExpandedState(expander);
            expander.addEventListener("toggle", () => {
                syncSummaryExpandedState(expander);
                const isMobileViewport = typeof window.matchMedia === "function" && window.matchMedia("(max-width: 760px)").matches;
                if (isMobileViewport && expander.open) {
                    expanders.forEach(otherExpander => {
                        if (!(otherExpander instanceof HTMLDetailsElement) || otherExpander === expander || !otherExpander.open) {
                            return;
                        }

                        otherExpander.open = false;
                        syncSummaryExpandedState(otherExpander);
                    });
                }

                updateViewportHeight(true);
            });
        });
    };

    const findMealCardBySlotIndex = (scope, slotIndex) => {
        if (!(scope instanceof Document || scope instanceof Element) || !Number.isInteger(slotIndex) || slotIndex < 0) {
            return null;
        }

        const selector = `.aislepilot-swap-form[action*='/swap-meal'] input[name='dayIndex'][value='${slotIndex}']`;
        const dayInput = scope.querySelector(selector);
        if (!(dayInput instanceof HTMLInputElement)) {
            return null;
        }

        const card = dayInput.closest("[data-day-meal-card]");
        return card instanceof HTMLElement ? card : null;
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
        const preservedMealImageSources = captureRenderedMealImageSources(document);
        const wasOverviewExpanded = (() => {
            const currentOverviewContent = getOverviewContent();
            return currentOverviewContent instanceof HTMLElement && !currentOverviewContent.hasAttribute("hidden");
        })();
        const responseDocument = new DOMParser().parseFromString(responseText, "text/html");
        const didReplaceMeals =
            replaceSwappedMealCard(responseDocument, slotIndex) ||
            replaceSectionContent(responseDocument, "#aislepilot-meals");
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
        if (!didSyncForms && !didSyncSavedState) {
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
            const submitButton = getSubmitButton(event);
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
            const scrollSnapshot = buildSwapScrollSnapshot(swapForm);
            const swapDayIndex = Number.isInteger(scrollSnapshot.anchorDayIndex)
                ? scrollSnapshot.anchorDayIndex
                : null;
            const currentCard = resolveSwapTargetCard(swapForm);
            if (!isFavoriteForm && currentCard instanceof HTMLElement) {
                currentCard.classList.add("is-swap-fading-out");
                currentCard.setAttribute("aria-busy", "true");
                currentCard.dataset.swapStatus = isLeftoverRebalanceForm
                    ? "Updating meal plan..."
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
                    const parsedResponseDocument = parseHtmlDocument(responseText);
                    if (isFavoriteForm) {
                        if (!applyAjaxFavoriteResponse(responseText)) {
                            if (parsedResponseDocument && !hasAislePilotWindowRoot(parsedResponseDocument)) {
                                const message = readAjaxFailureMessage(parsedResponseDocument);
                                showToast(
                                    message.length > 0 ? message : "Could not update saved meals. Try again.",
                                    "warning");
                                clearPersistedSwapScroll();
                                return;
                            }

                            persistSwapScrollPosition(swapForm);
                            replaceDocumentWithHtml(responseText);
                            return;
                        }

                        showToast(
                            wasSavedMealFavorite
                                ? "Meal removed from saved meals."
                                : "Meal saved.",
                            "success");
                        restoreInlineSwapScroll(scrollSnapshot);
                        clearPersistedSwapScroll();
                        return;
                    }

                    if (!applyAjaxSwapResponse(responseText, swapDayIndex)) {
                        if (parsedResponseDocument && !hasAislePilotWindowRoot(parsedResponseDocument)) {
                            const message = readAjaxFailureMessage(parsedResponseDocument);
                            showToast(
                                message.length > 0 ? message : "No alternative meal is available right now. Try another swap or regenerate.",
                                "warning");
                            clearPersistedSwapScroll();
                            return;
                        }

                        persistSwapScrollPosition(swapForm);
                        replaceDocumentWithHtml(responseText);
                        return;
                    }

                    if (isLeftoverRebalanceForm) {
                        // Leftover rebalancing is an inline layout tweak; avoid generic swap toast noise.
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
                    return;
                }

                if (response.ok) {
                    persistSwapScrollPosition(swapForm);
                    window.location.reload();
                    return;
                }

                showToast("Could not complete that action. Try again.", "warning");
                clearPersistedSwapScroll();
                resetSubmittingState();
                return;
            } catch {
                persistSwapScrollPosition(swapForm);
                HTMLFormElement.prototype.submit.call(swapForm);
                return;
            } finally {
                if (!isFavoriteForm && currentCard instanceof HTMLElement && currentCard.isConnected) {
                    currentCard.classList.remove("is-swap-fading-out");
                    currentCard.removeAttribute("aria-busy");
                    delete currentCard.dataset.swapStatus;
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

    const wireCardInteractionModules = scope => {
        wireSetupToggleHandlers(scope);
        wireOverviewToggleHandlers(scope);
        wireSavedWeeksToggleHandlers(scope);
        wireHeadMenus(scope);
        wireOverviewActionsMenus(scope);
        wirePreserveScrollHandlers(scope);
        wireLeftoverPlanner(scope);
        wireCardMoreActions(scope);
        wireInlineDetailsPanels(scope);
        wireDayCardExpanders(scope);
        wireDayMealCards(scope);
        wireAjaxSwapHandlers(scope);
    };

    const wireModulesAfterAjaxSwap = scope => {
        wireSubmitLoadingHandlers(scope);
        wireExportThemeForms(scope);
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

    wireCardInteractionModules(document);
    startMealImagePolling();

    viewport.addEventListener("touchstart", event => {
        viewportSwipeStartedInsideDayMealCard = isEventWithinDayMealCard(event);
        const touch = event.changedTouches[0];
        if (!touch) {
            return;
        }

        touchStartX = touch.clientX;
        touchStartY = touch.clientY;
    }, { passive: true });

    viewport.addEventListener("touchend", event => {
        if (viewportSwipeStartedInsideDayMealCard) {
            viewportSwipeStartedInsideDayMealCard = false;
            return;
        }

        viewportSwipeStartedInsideDayMealCard = false;
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
        viewportSwipeStartedInsideDayMealCard = false;
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
