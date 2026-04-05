(() => {
    const createController = options => {
        const config = options && typeof options === "object" ? options : {};
        const documentRef = config.documentRef instanceof Document ? config.documentRef : document;
        const pollIntervalMs = Number.isInteger(config.intervalMs) ? config.intervalMs : 5000;
        const pollMaxAttempts = Number.isInteger(config.maxAttempts) ? config.maxAttempts : 48;
        const mealImageCacheStorageKey = "aislepilot:meal-image-cache";
        const mealImageCacheTtlMs = Number.isInteger(config.cacheTtlMs) ? config.cacheTtlMs : 1000 * 60 * 60 * 12;

        let pollTimerId = null;
        let pollLoopActive = false;
        let pollInFlight = false;
        let pollAttempts = 0;
        const preloadCache = new Map();
        const mealImageCache = new Map();
        let mealImageCacheLoaded = false;
        let cacheWriteTimerId = null;

        const normalizeImagePath = value => {
            if (typeof value !== "string") {
                return "";
            }

            const trimmed = value.trim();
            if (trimmed.length === 0) {
                return "";
            }

            try {
                return new URL(trimmed, window.location.origin).pathname.toLowerCase();
            } catch {
                const fallbackPath = trimmed.split("?")[0].split("#")[0];
                return fallbackPath.toLowerCase();
            }
        };

        const normalizeMealName = value => {
            if (typeof value !== "string") {
                return "";
            }

            return value.trim().toLowerCase();
        };

        const loadMealImageCache = () => {
            if (mealImageCacheLoaded) {
                return;
            }

            mealImageCacheLoaded = true;
            try {
                const raw = sessionStorage.getItem(mealImageCacheStorageKey);
                if (!raw) {
                    return;
                }

                const parsed = JSON.parse(raw);
                if (!parsed || typeof parsed !== "object") {
                    return;
                }

                const now = Date.now();
                Object.entries(parsed).forEach(([mealKey, payload]) => {
                    if (!mealKey || !payload || typeof payload !== "object") {
                        return;
                    }

                    const imageUrl = typeof payload.url === "string" ? payload.url.trim() : "";
                    const storedAt = Number.parseInt(`${payload.at ?? ""}`, 10);
                    if (!imageUrl || !Number.isFinite(storedAt)) {
                        return;
                    }

                    if (now - storedAt > mealImageCacheTtlMs) {
                        return;
                    }

                    mealImageCache.set(mealKey, {
                        url: imageUrl,
                        at: storedAt
                    });
                });
            } catch {
                // Ignore storage issues in private browsing modes.
            }
        };

        const flushMealImageCache = () => {
            cacheWriteTimerId = null;
            try {
                const payload = {};
                mealImageCache.forEach((value, mealKey) => {
                    if (!value || typeof value !== "object") {
                        return;
                    }

                    payload[mealKey] = {
                        url: value.url,
                        at: value.at
                    };
                });
                sessionStorage.setItem(mealImageCacheStorageKey, JSON.stringify(payload));
            } catch {
                // Ignore storage issues in private browsing modes.
            }
        };

        const scheduleMealImageCacheFlush = () => {
            if (typeof cacheWriteTimerId === "number") {
                return;
            }

            cacheWriteTimerId = window.setTimeout(() => {
                flushMealImageCache();
            }, 120);
        };

        const getCachedMealImageUrl = mealName => {
            loadMealImageCache();
            const mealKey = normalizeMealName(mealName);
            if (!mealKey) {
                return "";
            }

            const cachedEntry = mealImageCache.get(mealKey);
            if (!cachedEntry || typeof cachedEntry !== "object") {
                return "";
            }

            if (Date.now() - cachedEntry.at > mealImageCacheTtlMs) {
                mealImageCache.delete(mealKey);
                scheduleMealImageCacheFlush();
                return "";
            }

            return cachedEntry.url;
        };

        const setCachedMealImageUrl = (mealName, imageUrl) => {
            const mealKey = normalizeMealName(mealName);
            const normalizedUrl = typeof imageUrl === "string" ? imageUrl.trim() : "";
            if (!mealKey || !normalizedUrl) {
                return;
            }

            loadMealImageCache();
            mealImageCache.set(mealKey, {
                url: normalizedUrl,
                at: Date.now()
            });
            scheduleMealImageCacheFlush();
        };

        const preloadImage = url => {
            if (typeof url !== "string" || url.trim().length === 0) {
                return Promise.resolve(false);
            }

            const normalizedUrl = url.trim();
            const cachedResult = preloadCache.get(normalizedUrl);
            if (typeof cachedResult === "boolean") {
                return Promise.resolve(cachedResult);
            }

            return new Promise(resolve => {
                const probe = new Image();
                probe.decoding = "async";
                probe.loading = "eager";
                let settled = false;
                const finish = didLoad => {
                    if (settled) {
                        return;
                    }

                    settled = true;
                    preloadCache.set(normalizedUrl, didLoad);
                    resolve(didLoad);
                };

                probe.onload = async () => {
                    if (typeof probe.decode === "function") {
                        try {
                            await probe.decode();
                        } catch {
                            // Best effort only.
                        }
                    }

                    finish(true);
                };
                probe.onerror = () => finish(false);
                probe.src = normalizedUrl;
                if (probe.complete && probe.naturalWidth > 0) {
                    finish(true);
                    return;
                }

                window.setTimeout(() => finish(false), 8000);
            });
        };

        const resolveImageShell = imageElement => {
            if (!(imageElement instanceof HTMLImageElement)) {
                return null;
            }

            const shell = imageElement.closest(".aislepilot-meal-image-shell");
            return shell instanceof HTMLElement ? shell : null;
        };

        const setMealImageLoadingState = (imageElement, isLoading) => {
            if (!(imageElement instanceof HTMLImageElement)) {
                return;
            }

            const shell = resolveImageShell(imageElement);
            if (!(shell instanceof HTMLElement)) {
                return;
            }

            if (isLoading) {
                shell.dataset.mealImageLoading = "true";
                imageElement.setAttribute("aria-busy", "true");
                return;
            }

            delete shell.dataset.mealImageLoading;
            imageElement.removeAttribute("aria-busy");
        };

        const clearDocumentMealImageLoadingStates = () => {
            const imageElements = Array.from(documentRef.querySelectorAll("img[data-meal-image]"))
                .filter(node => node instanceof HTMLImageElement);
            imageElements.forEach(imageElement => {
                setMealImageLoadingState(imageElement, false);
            });
        };

        const syncMealImageLoadingStates = (pollContext, pendingByMealName) => {
            if (!pollContext) {
                clearDocumentMealImageLoadingStates();
                return;
            }

            const pendingElements = new Set();
            if (pendingByMealName instanceof Map) {
                pendingByMealName.forEach(imageElements => {
                    if (!Array.isArray(imageElements)) {
                        return;
                    }

                    imageElements.forEach(imageElement => {
                        if (imageElement instanceof HTMLImageElement) {
                            pendingElements.add(imageElement);
                        }
                    });
                });
            }

            pollContext.imageElements.forEach(imageElement => {
                setMealImageLoadingState(imageElement, pendingElements.has(imageElement));
            });
        };

        const wireMealImageFallbackHandlers = pollContext => {
            if (!pollContext) {
                return;
            }

            pollContext.imageElements.forEach(imageElement => {
                if (!(imageElement instanceof HTMLImageElement) || imageElement.dataset.mealImageFallbackWired === "true") {
                    return;
                }

                imageElement.dataset.mealImageFallbackWired = "true";
                imageElement.addEventListener("load", () => {
                    const currentPath = normalizeImagePath(imageElement.getAttribute("src") || imageElement.currentSrc || "");
                    setMealImageLoadingState(imageElement, currentPath === pollContext.fallbackPath);
                });
                imageElement.addEventListener("error", () => {
                    const currentPath = normalizeImagePath(imageElement.getAttribute("src") || imageElement.currentSrc || "");
                    if (currentPath === pollContext.fallbackPath) {
                        return;
                    }

                    imageElement.src = pollContext.fallbackUrl;
                    setMealImageLoadingState(imageElement, true);
                });

                const initialPath = normalizeImagePath(imageElement.getAttribute("src") || imageElement.currentSrc || "");
                if (imageElement.complete) {
                    setMealImageLoadingState(imageElement, initialPath === pollContext.fallbackPath);
                }
            });
        };

        const getPollContext = () => {
            const pollRoot = documentRef.querySelector("[data-meal-image-poll-root]");
            if (!(pollRoot instanceof HTMLElement)) {
                return null;
            }

            const pollEnabledValue = pollRoot.dataset.mealImagePollEnabled?.trim().toLowerCase();
            if (pollEnabledValue === "false") {
                return null;
            }

            const imageElements = Array.from(pollRoot.querySelectorAll("img[data-meal-image][data-meal-name]"))
                .filter(node => node instanceof HTMLImageElement);
            if (imageElements.length === 0) {
                return null;
            }

            const endpoint = pollRoot.dataset.mealImagePollUrl?.trim() || "/projects/aisle-pilot/meal-images";
            const fallbackUrl = pollRoot.dataset.fallbackMealImageUrl?.trim() ||
                "/projects/aisle-pilot/images/aislepilot-icon.svg";
            const fallbackPath = normalizeImagePath(fallbackUrl);

            const context = {
                endpoint,
                fallbackUrl,
                fallbackPath,
                imageElements
            };

            wireMealImageFallbackHandlers(context);
            return context;
        };

        const getPendingMealImageNames = pollContext => {
            const pendingByMealName = new Map();
            if (!pollContext) {
                return pendingByMealName;
            }

            pollContext.imageElements.forEach(imageElement => {
                if (!(imageElement instanceof HTMLImageElement)) {
                    return;
                }

                const mealName = imageElement.dataset.mealName?.trim();
                if (!mealName) {
                    return;
                }

                const currentSrc = imageElement.getAttribute("src") || imageElement.currentSrc || "";
                const currentPath = normalizeImagePath(currentSrc);
                if (currentPath === pollContext.fallbackPath) {
                    const cachedImageUrl = getCachedMealImageUrl(mealName);
                    if (cachedImageUrl && normalizeImagePath(cachedImageUrl) !== pollContext.fallbackPath) {
                        if (currentSrc.trim() !== cachedImageUrl) {
                            imageElement.src = cachedImageUrl;
                        }

                        setMealImageLoadingState(imageElement, true);
                        return;
                    }
                }

                if (currentPath !== pollContext.fallbackPath) {
                    return;
                }

                const existing = pendingByMealName.get(mealName);
                if (Array.isArray(existing)) {
                    existing.push(imageElement);
                    return;
                }

                pendingByMealName.set(mealName, [imageElement]);
            });

            return pendingByMealName;
        };

        const clearPollTimer = () => {
            if (typeof pollTimerId === "number") {
                window.clearTimeout(pollTimerId);
            }

            pollTimerId = null;
        };

        const stop = () => {
            pollLoopActive = false;
            clearPollTimer();
            pollInFlight = false;
            pollAttempts = 0;
            clearDocumentMealImageLoadingStates();
        };

        const pollOnce = async () => {
            if (pollInFlight || documentRef.visibilityState === "hidden") {
                return;
            }

            const pollContext = getPollContext();
            if (!pollContext) {
                stop();
                return;
            }

            const pendingByMealName = getPendingMealImageNames(pollContext);
            syncMealImageLoadingStates(pollContext, pendingByMealName);
            if (pendingByMealName.size === 0) {
                stop();
                return;
            }

            if (pollAttempts >= pollMaxAttempts) {
                stop();
                return;
            }

            pollAttempts += 1;
            pollInFlight = true;

            try {
                const searchParams = new URLSearchParams();
                pendingByMealName.forEach((_, mealName) => {
                    searchParams.append("mealNames", mealName);
                });

                const requestUrl = `${pollContext.endpoint}?${searchParams.toString()}`;
                const response = await fetch(requestUrl, {
                    method: "GET",
                    credentials: "same-origin",
                    headers: {
                        "Accept": "application/json"
                    }
                });
                if (!response.ok) {
                    return;
                }

                const payload = await response.json();
                if (payload?.canGenerateImages === false) {
                    stop();
                    return;
                }

                const images = Array.isArray(payload?.images) ? payload.images : [];
                if (images.length === 0) {
                    return;
                }

                const imageUrlByMealName = new Map();
                images.forEach(item => {
                    if (!item || typeof item !== "object") {
                        return;
                    }

                    const mealName = typeof item.mealName === "string" ? item.mealName.trim() : "";
                    const imageUrl = typeof item.imageUrl === "string" ? item.imageUrl.trim() : "";
                    if (!mealName || !imageUrl) {
                        return;
                    }

                    imageUrlByMealName.set(mealName, imageUrl);
                });

                const cacheVersionToken = Date.now();
                const applyUpdateTasks = Array.from(pendingByMealName.entries()).map(async ([mealName, imageElements]) => {
                    const nextImageUrl = imageUrlByMealName.get(mealName);
                    if (!nextImageUrl) {
                        return;
                    }

                    if (normalizeImagePath(nextImageUrl) === pollContext.fallbackPath) {
                        return;
                    }

                    const cacheBustedUrl = `${nextImageUrl}${nextImageUrl.includes("?") ? "&" : "?"}v=${cacheVersionToken}`;
                    const didLoad = await preloadImage(cacheBustedUrl);
                    if (!didLoad) {
                        return;
                    }

                    setCachedMealImageUrl(mealName, nextImageUrl);
                    imageElements.forEach(imageElement => {
                        if (imageElement instanceof HTMLImageElement) {
                            imageElement.src = cacheBustedUrl;
                            setMealImageLoadingState(imageElement, false);
                        }
                    });
                });
                await Promise.all(applyUpdateTasks);

                const refreshedPollContext = getPollContext();
                if (!refreshedPollContext || getPendingMealImageNames(refreshedPollContext).size === 0) {
                    stop();
                }
            } catch {
                // Ignore transient polling failures and try again on next interval.
            } finally {
                pollInFlight = false;
            }
        };

        const getNextPollDelayMs = () => {
            if (pollAttempts <= 2) {
                return 1200;
            }

            if (pollAttempts <= 7) {
                return 2500;
            }

            return pollIntervalMs;
        };

        const runPollLoop = async () => {
            if (!pollLoopActive) {
                return;
            }

            await pollOnce();
            if (!pollLoopActive) {
                return;
            }

            clearPollTimer();
            pollTimerId = window.setTimeout(() => {
                void runPollLoop();
            }, getNextPollDelayMs());
        };

        const start = () => {
            const pollContext = getPollContext();
            if (!pollContext) {
                stop();
                return;
            }

            const pendingByMealName = getPendingMealImageNames(pollContext);
            syncMealImageLoadingStates(pollContext, pendingByMealName);
            if (pendingByMealName.size === 0) {
                stop();
                return;
            }

            pollAttempts = 0;
            pollLoopActive = true;
            clearPollTimer();
            pollTimerId = window.setTimeout(() => {
                void runPollLoop();
            }, 0);
        };

        return Object.freeze({
            start,
            stop,
            pollOnce
        });
    };

    window.AislePilotMealImagePolling = Object.freeze({
        createController
    });
})();
