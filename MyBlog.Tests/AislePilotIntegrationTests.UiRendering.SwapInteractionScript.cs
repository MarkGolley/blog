namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task AislePilotScript_MealActions_RestorePortaledMenusBeforeSubmit()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains(
            "const resolveCardMoreActionsMenuForForm = form => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "ownerMenu: menu",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "closeCardMoreActionsMenuImmediately(menu);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const actionsPanel = form.closest(\"[data-card-more-actions-panel]\");",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const cards = Array.from(scope.querySelectorAll(\"[data-day-meal-card][data-day-index]\"));",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "getCardMoreActionsPanel(menu).contains(form)",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "const parentActionsMenu = swapForm.closest(\"[data-card-more-actions]\");",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const restoreAllCardMoreActionsPanelsToMenus = () => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "restoreAllCardMoreActionsPanelsToMenus();",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const isPortaled = actionsMenu instanceof HTMLElement && actionsMenu.parentElement === document.body;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const isMobileSheetSwapForm =",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const isCardMoreActionsSwapForm =",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const shouldUseNativeSubmitForCardMoreActionsSwap = isCardMoreActionsSwapForm;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "actionSheetPanel.classList.contains(\"is-mobile-sheet\")",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "swapForm.classList.contains(\"aislepilot-card-more-action-form\")",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "isCardMoreActionsSwapForm,",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "shouldUseNativeSubmitForCardMoreActionsSwap,",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "HTMLFormElement.prototype.submit.call(swapForm);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const swapDebugEndpoint = \"/projects/aisle-pilot/debug-client-log\";",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "navigator.sendBeacon(swapDebugEndpoint, blob);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "sendSwapDebugToServer(payload);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const handleAjaxSwapFormSubmit = async (swapForm, submitButton = null) => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const didReplaceMealsSection = replaceSectionContent(responseDocument, \"#aislepilot-meals\");",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const didReplaceMealCard = !didReplaceMealsSection && replaceSwappedMealCard(responseDocument, slotIndex);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (!didReplaceMealsSection) {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "let handoffToNativeSubmit = false;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const submitDetachedFormClone = sourceForm => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const submitCardMoreActionsSwapAction = async (form, submitButton, dayIndexValue = \"\") => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const resolveSwapSnapshotTargetY = snapshot => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const rememberActiveDayCardSlide = scope => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const restoreActiveDayCardSlide = scope => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const applyRememberedDayMealSlotToCard = card => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const applyRememberedDayMealSlotsToScope = scope => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const applyRememberedActiveDayCardSlideToScope = scope => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "setSubmitButtonLoadingState,",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "swapScrollRestoreDurationMs,",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "wireNotesExportButtons,",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.AislePilotCore = {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-card-start\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "sessionStorage.setItem(swapScrollKey, JSON.stringify(payload));",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "raw = sessionStorage.getItem(swapScrollKey);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "sessionStorage.removeItem(swapScrollKey);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Scroll persistence should never block a swap/navigation action.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Ignore storage failures; stale scroll state is non-critical.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const handleCardMoreActionsSubmitButtonClick = (button, event) => {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const isCardMoreActionsSwapAction =",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-card-fetch-start\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"X-Requested-With\": \"XMLHttpRequest\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-card-redirect-navigate\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const didApplySwapResponse = applyAjaxSwapResponse(responseText, slotIndex);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "rememberActiveDayCardSlide(document);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "applyRememberedDayMealSlotsToScope(responseDocument);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "applyRememberedActiveDayCardSlideToScope(responseDocument);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "restoreActiveDayCardSlide(document);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-card-apply-response\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const targetY = resolveSwapSnapshotTargetY(snapshot);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const restoreDeadline = Date.now() + swapScrollRestoreDurationMs;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "restoreInlineSwapScroll(scrollSnapshot);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.location.assign(response.url);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-card-fetch-response\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-card-exception-native-submit\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "wireNotesExportButtons(scope);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "handoffToNativeSubmit = true;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (!handoffToNativeSubmit && !isFavoriteForm && currentCard instanceof HTMLElement && currentCard.isConnected) {",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "handoffToNativeSubmit",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-direct-handler\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "const submitButton = event.target.closest(\"[data-card-more-actions-panel] button[type='submit']\");",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "void submitCardMoreActionsSwapAction(",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "void handleAjaxSwapFormSubmit(form, button);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "void handleAjaxSwapFormSubmit(event.currentTarget, getSubmitButton(event));",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"submit-handler-exception-native-submit\"",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "writeSwapDebug(\"fetch-exception-native-submit\"",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "writeSwapDebug(\"menu-submit-click\", {\r\n                        ariaLabel: button.getAttribute(\"aria-label\") ?? \"\",\r\n                        formAction: form instanceof HTMLFormElement ? form.getAttribute(\"action\") ?? \"\" : \"\",\r\n                        dayIndex: dayInput instanceof HTMLInputElement ? dayInput.value : \"\",\r\n                        portaled: shouldUseMobileCardActionsSheet()\r\n                    });\r\n                    closeCardMoreActionsMenuImmediately(menu);",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"menu-submit-click\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "writeSwapDebug(\"submit-start\"",
            script,
            StringComparison.Ordinal);

        var mobileBranchIndex = script.IndexOf("if (shouldUseNativeSubmitForCardMoreActionsSwap) {", StringComparison.Ordinal);
        var pendingStateIndex = script.IndexOf("if (!isFavoriteForm && currentCard instanceof HTMLElement) {", StringComparison.Ordinal);
        Assert.True(mobileBranchIndex >= 0, "Expected mobile sheet swap branch in submit handler.");
        Assert.True(pendingStateIndex >= 0, "Expected generic pending-state branch in submit handler.");
        Assert.True(
            mobileBranchIndex < pendingStateIndex,
            "Expected mobile sheet submit branch to run before generic pending/loading state work.");
    }
}
