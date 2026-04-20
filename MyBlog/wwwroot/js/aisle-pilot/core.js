(() => {
    if (window.__aislePilotCoreWired === true) {
        return;
    }
    window.__aislePilotCoreWired = true;

    const quickReplaceInputs = Array.from(
        document.querySelectorAll(
            ".aislepilot-inline-fields input:not([type='hidden']):not([type='checkbox']):not([type='radio'])"
        )
    );
    const getAislePilotForms = () => Array.from(document.querySelectorAll(".aislepilot-app form"));
    const supermarketSelectionStorageKey = "aislepilot:setup-supermarket";
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

    const getExportDownloadFallbackName = form => {
        if (!(form instanceof HTMLFormElement)) {
            return "aislepilot-export";
        }

        const fallbackName = form.dataset.exportDownloadFallbackName?.trim();
        return fallbackName && fallbackName.length > 0
            ? fallbackName
            : "aislepilot-export";
    };

    const readExportDownloadFileName = (response, form) => {
        if (!(response instanceof Response)) {
            return getExportDownloadFallbackName(form);
        }

        const contentDisposition = response.headers.get("content-disposition") ?? "";
        const encodedNameMatch = contentDisposition.match(/filename\*\s*=\s*([^;]+)/i);
        if (encodedNameMatch) {
            const encodedName = encodedNameMatch[1].trim().replace(/^UTF-8''/i, "").replace(/^"|"$/g, "");
            if (encodedName.length > 0) {
                try {
                    return decodeURIComponent(encodedName);
                } catch {
                    return encodedName;
                }
            }
        }

        const plainNameMatch = contentDisposition.match(/filename\s*=\s*"?(.*?)"?($|;)/i);
        const plainName = plainNameMatch?.[1]?.trim();
        if (plainName && plainName.length > 0) {
            return plainName;
        }

        return getExportDownloadFallbackName(form);
    };

    const triggerFileDownload = (blob, fileName) => {
        if (!(blob instanceof Blob)) {
            return;
        }

        const anchor = document.createElement("a");
        const objectUrl = URL.createObjectURL(blob);
        anchor.href = objectUrl;
        anchor.download = typeof fileName === "string" && fileName.trim().length > 0
            ? fileName.trim()
            : "aislepilot-export";
        anchor.hidden = true;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        window.setTimeout(() => {
            URL.revokeObjectURL(objectUrl);
        }, 1000);
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
            const dietarySelector = form.querySelector("[data-dietary-selector]");
            const dietaryGuidance = form.querySelector("[data-dietary-guidance]");
            const defaultDietaryGuidance = dietaryGuidance instanceof HTMLElement
                ? (dietaryGuidance.dataset.defaultGuidance ?? dietaryGuidance.textContent ?? "").trim()
                : "";
            const dietaryMaxMessage = dietarySelector instanceof HTMLElement
                ? (dietarySelector.dataset.dietaryMaxMessage ?? defaultDietaryGuidance)
                : defaultDietaryGuidance;
            const parsedDietaryMaxSelections = Number.parseInt(
                dietarySelector instanceof HTMLElement ? (dietarySelector.dataset.dietaryMaxSelections ?? "2") : "2",
                10);
            const dietaryMaxSelections = Number.isInteger(parsedDietaryMaxSelections)
                ? Math.max(1, parsedDietaryMaxSelections)
                : 2;
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

            const getSelectedDietaryInputs = () => dietaryInputs.filter(input =>
                input instanceof HTMLInputElement && input.checked);

            const resetDietaryGuidance = () => {
                if (!(dietaryGuidance instanceof HTMLElement)) {
                    return;
                }

                dietaryGuidance.textContent = defaultDietaryGuidance;
                dietaryGuidance.dataset.state = "default";
            };

            const setDietaryGuidanceMessage = message => {
                if (!(dietaryGuidance instanceof HTMLElement)) {
                    return;
                }

                dietaryGuidance.textContent = message;
                dietaryGuidance.dataset.state = "warning";
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

            const handleDietaryModeChange = targetInput => {
                if (!(targetInput instanceof HTMLInputElement)) {
                    return;
                }

                if (targetInput.checked && targetInput.dataset.dietaryGroup === "core") {
                    dietaryInputs.forEach(input => {
                        if (!(input instanceof HTMLInputElement) ||
                            input === targetInput ||
                            !input.checked ||
                            input.dataset.dietaryGroup !== "core") {
                            return;
                        }

                        input.checked = false;
                    });
                }

                if (getSelectedDietaryInputs().length > dietaryMaxSelections) {
                    targetInput.checked = false;
                    setDietaryGuidanceMessage(dietaryMaxMessage);
                } else {
                    resetDietaryGuidance();
                }

                updateDietarySummary();
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
                        input.addEventListener("change", event => {
                            handleDietaryModeChange(event.currentTarget);
                        });
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
            resetDietaryGuidance();
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

    const getSupermarketRadioOptions = form => {
        if (!(form instanceof HTMLFormElement)) {
            return [];
        }

        return Array.from(form.querySelectorAll("[data-supermarket-option]"))
            .filter(input => input instanceof HTMLInputElement);
    };

    const normalizeSupermarketValue = value => typeof value === "string" ? value.trim() : "";

    const readPersistedSupermarketSelection = () => {
        try {
            return normalizeSupermarketValue(window.localStorage.getItem(supermarketSelectionStorageKey));
        } catch {
            return "";
        }
    };

    const persistSupermarketSelection = value => {
        const normalizedValue = normalizeSupermarketValue(value);
        if (normalizedValue.length === 0) {
            return;
        }

        try {
            window.localStorage.setItem(supermarketSelectionStorageKey, normalizedValue);
        } catch {
            // Ignore storage failures in private modes.
        }
    };

    const resolveSupermarketOptionValue = (options, candidateValue) => {
        const normalizedCandidate = normalizeSupermarketValue(candidateValue).toLowerCase();
        if (normalizedCandidate.length === 0) {
            return "";
        }

        const matchedOption = options.find(option =>
            normalizeSupermarketValue(option.value).toLowerCase() === normalizedCandidate);
        return matchedOption instanceof HTMLInputElement ? matchedOption.value : "";
    };

    const getSelectedSupermarket = form => {
        if (!(form instanceof HTMLFormElement)) {
            return "";
        }

        const supermarketRadioOptions = getSupermarketRadioOptions(form);
        if (supermarketRadioOptions.length > 0) {
            const enabledCheckedOption = [...supermarketRadioOptions]
                .reverse()
                .find(input => input.checked && !input.matches(":disabled"));
            if (enabledCheckedOption instanceof HTMLInputElement) {
                return enabledCheckedOption.value;
            }

            const selectedOption = [...supermarketRadioOptions]
                .reverse()
                .find(input => input.checked);
            return selectedOption instanceof HTMLInputElement ? selectedOption.value : "";
        }

        const supermarketSelect = form.querySelector("[data-supermarket-select]");
        if (supermarketSelect instanceof HTMLSelectElement || supermarketSelect instanceof HTMLInputElement) {
            return supermarketSelect.value;
        }

        return "";
    };

    const syncSupermarketSelection = (form, selectedValue) => {
        if (!(form instanceof HTMLFormElement)) {
            return "";
        }

        const supermarketRadioOptions = getSupermarketRadioOptions(form);
        if (supermarketRadioOptions.length === 0) {
            const supermarketSelect = form.querySelector("[data-supermarket-select]");
            if (supermarketSelect instanceof HTMLSelectElement || supermarketSelect instanceof HTMLInputElement) {
                const normalizedValue = normalizeSupermarketValue(selectedValue);
                if (normalizedValue.length > 0) {
                    supermarketSelect.value = normalizedValue;
                    persistSupermarketSelection(normalizedValue);
                }

                return supermarketSelect.value;
            }

            return "";
        }

        const resolvedSelectedValue =
            resolveSupermarketOptionValue(supermarketRadioOptions, selectedValue) ||
            resolveSupermarketOptionValue(supermarketRadioOptions, getSelectedSupermarket(form)) ||
            supermarketRadioOptions.find(option => !option.matches(":disabled"))?.value ||
            supermarketRadioOptions[0]?.value ||
            "";

        const selectedOption =
            supermarketRadioOptions.find(option =>
                !option.matches(":disabled") &&
                normalizeSupermarketValue(option.value).toLowerCase() ===
                normalizeSupermarketValue(resolvedSelectedValue).toLowerCase()) ||
            supermarketRadioOptions.find(option =>
                normalizeSupermarketValue(option.value).toLowerCase() ===
                normalizeSupermarketValue(resolvedSelectedValue).toLowerCase()) ||
            null;

        supermarketRadioOptions.forEach(option => {
            option.checked = option === selectedOption;
        });

        if (resolvedSelectedValue.length > 0) {
            persistSupermarketSelection(resolvedSelectedValue);
        }

        return resolvedSelectedValue;
    };

    const wireSupermarketSelectionPersistence = scope => {
        const forms = scope instanceof Element
            ? Array.from(scope.querySelectorAll("form"))
            : getAislePilotForms();

        forms.forEach(form => {
            if (!(form instanceof HTMLFormElement)) {
                return;
            }

            const supermarketRadioOptions = getSupermarketRadioOptions(form);
            if (supermarketRadioOptions.length === 0) {
                return;
            }

            supermarketRadioOptions.forEach(option => {
                if (option.dataset.supermarketPersistenceWired === "true") {
                    return;
                }

                option.dataset.supermarketPersistenceWired = "true";
                option.addEventListener("change", () => {
                    syncSupermarketSelection(form, option.value);
                });
            });

            const currentSelection = normalizeSupermarketValue(getSelectedSupermarket(form));
            const persistedSelection = resolveSupermarketOptionValue(
                supermarketRadioOptions,
                readPersistedSupermarketSelection());
            const preferredSelection = persistedSelection || currentSelection;
            const resolvedSelection = syncSupermarketSelection(form, preferredSelection);
            if (
                resolvedSelection.length > 0 &&
                resolvedSelection.localeCompare(currentSelection, undefined, { sensitivity: "accent" }) !== 0
            ) {
                const selectedOption = supermarketRadioOptions.find(option => option.checked);
                if (selectedOption instanceof HTMLInputElement) {
                    selectedOption.dispatchEvent(new Event("change", { bubbles: true }));
                }
            }
        });
    };

    wireSupermarketSelectionPersistence(document);

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

    const resetFormSubmittingState = form => {
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

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
    };

    const resetSubmittingState = () => {
        hidePlanLoadingShell();
        getAislePilotForms().forEach(resetFormSubmittingState);
    };

    window.addEventListener("pageshow", event => {
        if (event.persisted) {
            resetSubmittingState();
        }

        requestAnimationFrame(() => {
            wireSupermarketSelectionPersistence(document);
        });
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


    window.AislePilotCore = {
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
    };
})();
