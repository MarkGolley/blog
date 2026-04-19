(() => {
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
            window.localStorage.setItem(shoppingItemStateStorageKey, JSON.stringify(readShoppingItemState()));
        } catch {
            // Ignore storage failures in private modes.
        }
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

    const syncShoppingItemVisualState = (label, isChecked) => {
        if (label instanceof HTMLElement) {
            label.classList.toggle("is-checked", isChecked);
        }
    };

    const resetLocalSubmittingState = form => {
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        form.removeAttribute("data-is-submitting");
        Array.from(form.querySelectorAll("button[type='submit']")).forEach(button => {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            button.classList.remove("is-loading");
            button.disabled = false;
            button.removeAttribute("aria-busy");
            if (button.dataset.originalLabel && !button.classList.contains("is-icon-only")) {
                button.textContent = button.dataset.originalLabel;
            }

            if (typeof button.dataset.originalAriaLabel === "string") {
                if (button.dataset.originalAriaLabel.length > 0) {
                    button.setAttribute("aria-label", button.dataset.originalAriaLabel);
                } else {
                    button.removeAttribute("aria-label");
                }
            }

            button.style.removeProperty("min-width");
            delete button.dataset.loadingWidthLocked;
        });
    };

    const wireShoppingChecklist = scope => {
        const labels = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-shopping-item-label]"))
            : Array.from(document.querySelectorAll("[data-shopping-item-label]"));

        labels.forEach(label => {
            if (!(label instanceof HTMLElement) || label.dataset.shoppingItemWired === "true") {
                return;
            }

            const checkbox = label.querySelector("[data-shopping-item-input]");
            const shoppingItemKey = (label.dataset.shoppingItemKey ?? "").trim();
            if (!(checkbox instanceof HTMLInputElement) || shoppingItemKey.length === 0) {
                return;
            }

            label.dataset.shoppingItemWired = "true";
            const savedState = readShoppingItemState();
            const isChecked = savedState[shoppingItemKey] === true;
            checkbox.checked = isChecked;
            syncShoppingItemVisualState(label, isChecked);

            checkbox.addEventListener("change", () => {
                const currentState = readShoppingItemState();
                if (checkbox.checked) {
                    currentState[shoppingItemKey] = true;
                } else {
                    delete currentState[shoppingItemKey];
                }

                syncShoppingItemVisualState(label, checkbox.checked);
                writeShoppingItemState();
            });
        });
    };

    const syncShoppingNotesExportContent = () => {
        const fields = Array.from(document.querySelectorAll("[data-notes-export-content]"));
        const customItems = readCustomShoppingItems();

        fields.forEach(field => {
            if (!(field instanceof HTMLTextAreaElement)) {
                return;
            }

            const baseContent = field.dataset.notesBaseContent?.trim() ?? field.value.trim();
            field.dataset.notesBaseContent = baseContent;

            let nextContent = baseContent;
            if (customItems.length > 0) {
                const customItemLines = customItems
                    .map(item => (item.text ?? "").trim())
                    .filter(text => text.length > 0)
                    .map(text => `- ${text}`)
                    .join("\n");
                if (customItemLines.length > 0) {
                    nextContent = `${baseContent}\n\nYour extra items\n${customItemLines}`.trim();
                }
            }

            field.value = nextContent.trim();
        });
    };

    const normalizeCustomShoppingItemText = value => typeof value === "string"
        ? value.replace(/\s+/g, " ").trim()
        : "";

    const buildCustomShoppingItemKey = itemId => `custom|${itemId}`;
    const createCustomShoppingItemId = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

    const buildCustomShoppingListItem = item => {
        const listItem = document.createElement("li");
        listItem.className = "aislepilot-custom-shopping-item";
        listItem.dataset.customShoppingItemId = item.id;

        const row = document.createElement("div");
        row.className = "aislepilot-custom-shopping-row";

        const label = document.createElement("label");
        label.className = "aislepilot-checkbox-item";
        label.dataset.shoppingItemLabel = "";
        label.dataset.shoppingItemKey = buildCustomShoppingItemKey(item.id);

        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.dataset.shoppingItemInput = "";
        checkbox.setAttribute("aria-label", `Mark ${item.text} as already have this item`);

        const text = document.createElement("span");
        text.dataset.shoppingItemText = "";
        text.textContent = item.text;

        label.append(checkbox, text);

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "aislepilot-custom-shopping-remove";
        removeButton.dataset.customShoppingRemove = item.id;
        removeButton.setAttribute("aria-label", `Remove ${item.text}`);
        removeButton.textContent = "Remove";

        row.append(label, removeButton);
        listItem.appendChild(row);
        return listItem;
    };

    const renderCustomShoppingList = shell => {
        if (!(shell instanceof HTMLElement)) {
            return;
        }

        const list = shell.querySelector("[data-custom-shopping-list]");
        const emptyState = shell.querySelector("[data-custom-shopping-empty]");
        if (!(list instanceof HTMLElement) || !(emptyState instanceof HTMLElement)) {
            return;
        }

        list.replaceChildren();
        readCustomShoppingItems().forEach(item => {
            list.appendChild(buildCustomShoppingListItem(item));
        });

        if (readCustomShoppingItems().length > 0) {
            list.removeAttribute("hidden");
            emptyState.setAttribute("hidden", "hidden");
            wireShoppingChecklist(list);
        } else {
            list.setAttribute("hidden", "hidden");
            emptyState.removeAttribute("hidden");
        }

        syncShoppingNotesExportContent();
    };

    const wireCustomShoppingList = scope => {
        const shells = scope instanceof Element
            ? Array.from(scope.querySelectorAll("[data-custom-shopping-shell]"))
            : Array.from(document.querySelectorAll("[data-custom-shopping-shell]"));

        shells.forEach(shell => {
            if (!(shell instanceof HTMLElement)) {
                return;
            }

            const form = shell.querySelector("[data-custom-shopping-form]");
            const input = shell.querySelector("[data-custom-shopping-input]");
            if (!(form instanceof HTMLFormElement) || !(input instanceof HTMLInputElement)) {
                return;
            }

            form.setAttribute("data-skip-submit-loading", "true");

            if (shell.dataset.customShoppingWired !== "true") {
                shell.dataset.customShoppingWired = "true";

                form.addEventListener("submit", event => {
                    event.preventDefault();

                    try {
                        const normalizedText = normalizeCustomShoppingItemText(input.value);
                        if (normalizedText.length === 0) {
                            input.focus();
                            return;
                        }

                        const currentItems = readCustomShoppingItems();
                        const alreadyExists = currentItems.some(item =>
                            (item.text ?? "").trim().toLowerCase() === normalizedText.toLowerCase());
                        if (alreadyExists) {
                            input.value = "";
                            renderCustomShoppingList(shell);
                            input.focus();
                            return;
                        }

                        currentItems.push({
                            id: createCustomShoppingItemId(),
                            text: normalizedText
                        });

                        writeCustomShoppingItems(currentItems);
                        input.value = "";
                        renderCustomShoppingList(shell);
                        input.focus();
                    } finally {
                        resetLocalSubmittingState(form);
                    }
                });

                shell.addEventListener("click", event => {
                    if (!(event.target instanceof Element)) {
                        return;
                    }

                    const removeButton = event.target.closest("[data-custom-shopping-remove]");
                    if (!(removeButton instanceof HTMLButtonElement)) {
                        return;
                    }

                    const itemId = (removeButton.dataset.customShoppingRemove ?? "").trim();
                    if (itemId.length === 0) {
                        return;
                    }

                    const nextItems = readCustomShoppingItems().filter(item => item.id !== itemId);
                    writeCustomShoppingItems(nextItems);

                    const currentState = readShoppingItemState();
                    delete currentState[buildCustomShoppingItemKey(itemId)];
                    writeShoppingItemState();

                    renderCustomShoppingList(shell);
                });
            }

            renderCustomShoppingList(shell);
        });
    };

    window.AislePilotShopping = {
        wireCustomShoppingList,
        wireShoppingChecklist
    };
})();
