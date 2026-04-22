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
            "actionSheetPanel.classList.contains(\"is-mobile-sheet\")",
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
            "writeSwapDebug(\"menu-submit-direct-handler\"",
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
    }
}
