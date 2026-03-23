(() => {
    const createController = options => {
        const config = options && typeof options === "object" ? options : {};
        const documentRef = config.documentRef instanceof Document ? config.documentRef : document;
        const pollIntervalMs = Number.isInteger(config.intervalMs) ? config.intervalMs : 5000;
        const pollMaxAttempts = Number.isInteger(config.maxAttempts) ? config.maxAttempts : 48;

        let pollIntervalId = null;
        let pollInFlight = false;
        let pollAttempts = 0;

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
            const fallbackPath = normalizeImagePath(
                pollRoot.dataset.fallbackMealImageUrl?.trim() || "/images/aislepilot-icon.svg"
            );

            return {
                endpoint,
                fallbackPath,
                imageElements
            };
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

        const stop = () => {
            if (typeof pollIntervalId === "number") {
                window.clearInterval(pollIntervalId);
            }

            pollIntervalId = null;
            pollInFlight = false;
            pollAttempts = 0;
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

                pendingByMealName.forEach((imageElements, mealName) => {
                    const nextImageUrl = imageUrlByMealName.get(mealName);
                    if (!nextImageUrl) {
                        return;
                    }

                    if (normalizeImagePath(nextImageUrl) === pollContext.fallbackPath) {
                        return;
                    }

                    const cacheBustedUrl = `${nextImageUrl}${nextImageUrl.includes("?") ? "&" : "?"}v=${Date.now()}`;
                    imageElements.forEach(imageElement => {
                        if (imageElement instanceof HTMLImageElement) {
                            imageElement.src = cacheBustedUrl;
                        }
                    });
                });

                if (getPendingMealImageNames(getPollContext()).size === 0) {
                    stop();
                }
            } catch {
                // Ignore transient polling failures and try again on next interval.
            } finally {
                pollInFlight = false;
            }
        };

        const start = () => {
            const pollContext = getPollContext();
            if (!pollContext) {
                stop();
                return;
            }

            if (getPendingMealImageNames(pollContext).size === 0) {
                stop();
                return;
            }

            if (typeof pollIntervalId === "number") {
                window.clearInterval(pollIntervalId);
            }

            pollAttempts = 0;
            pollIntervalId = window.setInterval(() => {
                void pollOnce();
            }, pollIntervalMs);
            void pollOnce();
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
