(() => {
    let overviewActionsGlobalWired = false;
    let headMenuGlobalWired = false;

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

            Array.from(menu.querySelectorAll("button")).forEach(button => {
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
            if (!(event.target instanceof Element) || event.target.closest("[data-overview-actions-menu]")) {
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
            Array.from(document.querySelectorAll("[data-overview-actions-menu][open]")).forEach(openMenu => {
                positionOverviewActionsMenu(openMenu);
            });
        });
    };

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

                if (menu.open) {
                    closeOpenHeadMenus(menu);
                }
            });
        });

        if (headMenuGlobalWired) {
            return;
        }

        headMenuGlobalWired = true;
        document.addEventListener("click", event => {
            if (!(event.target instanceof Element) || event.target.closest("[data-head-menu]")) {
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

    window.AislePilotActionMenus = {
        wireHeadMenus,
        wireOverviewActionsMenus
    };
})();
