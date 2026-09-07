(()=>{
const shoppingItemStateStorageKey="aislepilot:shopping-item-state";
const customShoppingItemsStorageKey="aislepilot:custom-shopping-items";
let shoppingItemStateCache=null;
let customShoppingItemsCache=null;
const syncShoppingChecklistStatus=()=>{
const shop=document.querySelector("#aislepilot-shop");
if (!(shop instanceof HTMLElement)) return;
const labels=Array.from(shop.querySelectorAll("[data-shopping-item-label]"));
const checked=labels.filter(label=>label.querySelector("[data-shopping-item-input]")?.checked);
const hideToggle=shop.querySelector("[data-shopping-hide-checked]");
const hideChecked=hideToggle instanceof HTMLInputElement && hideToggle.checked;
const searchInput=shop.querySelector("[data-shopping-search]");
const searchQuery=searchInput instanceof HTMLInputElement ? searchInput.value.trim().toLocaleLowerCase() : "";
const progress=shop.querySelector("[data-shopping-progress]");
const filterLabel=shop.querySelector("[data-shopping-filter-label]");
const emptyState=shop.querySelector("[data-shopping-filter-empty]");
if (progress instanceof HTMLElement) {
progress.textContent=labels.length > 0 && checked.length===labels.length
? "All items checked"
: `${checked.length} of ${labels.length} items checked`;
}
if (filterLabel instanceof HTMLElement) {
filterLabel.textContent=hideChecked ? "Show all items" : "Hide checked items";
}
labels.forEach(label=>{
const item=label.closest("li");
const itemText=(label.querySelector("[data-shopping-item-text]")?.textContent ?? "").toLocaleLowerCase();
const hiddenByCheck=hideChecked && checked.includes(label);
const hiddenBySearch=searchQuery.length > 0 && !itemText.includes(searchQuery);
if (item instanceof HTMLElement) item.hidden=hiddenByCheck || hiddenBySearch;
});
shop.querySelectorAll("[data-shopping-department]").forEach(department=>{
const departmentLabels=Array.from(department.querySelectorAll("[data-shopping-item-label]"));
const remaining=departmentLabels.filter(label=>!label.querySelector("[data-shopping-item-input]")?.checked).length;
const visible=departmentLabels.filter(label=>!label.closest("li")?.hidden).length;
const count=department.querySelector("[data-shopping-department-count]");
if (count instanceof HTMLElement) count.textContent=`${remaining} of ${departmentLabels.length} left`;
if (department instanceof HTMLElement) department.hidden=visible===0;
});
if (emptyState instanceof HTMLElement) {
const visibleDepartments=Array.from(shop.querySelectorAll("[data-shopping-department]"))
.some(department=>department instanceof HTMLElement && !department.hidden);
emptyState.hidden=visibleDepartments || (searchQuery.length===0 && !hideChecked);
emptyState.textContent=searchQuery.length > 0
? `No items match “${searchInput.value.trim()}”.`
: "Everything on the generated list is checked.";
}
};
const readShoppingItemState=()=>{
if (shoppingItemStateCache && typeof shoppingItemStateCache==="object") {
return shoppingItemStateCache;
}
try {
const raw=window.localStorage.getItem(shoppingItemStateStorageKey);
if (!raw) {
shoppingItemStateCache={};
return shoppingItemStateCache;
}
const parsed=JSON.parse(raw);
shoppingItemStateCache=parsed && typeof parsed==="object" ? parsed : {};
} catch {
shoppingItemStateCache={};
}
return shoppingItemStateCache;
};
const writeShoppingItemState=()=>{
try {
window.localStorage.setItem(shoppingItemStateStorageKey, JSON.stringify(readShoppingItemState()));
} catch {
}
};
const readCustomShoppingItems=()=>{
if (Array.isArray(customShoppingItemsCache)) {
return customShoppingItemsCache;
}
try {
const raw=window.localStorage.getItem(customShoppingItemsStorageKey);
if (!raw) {
customShoppingItemsCache=[];
return customShoppingItemsCache;
}
const parsed=JSON.parse(raw);
customShoppingItemsCache=Array.isArray(parsed)
? parsed.filter(item =>
item &&
typeof item==="object" &&
typeof item.id==="string" &&
typeof item.text==="string")
: [];
} catch {
customShoppingItemsCache=[];
}
return customShoppingItemsCache;
};
const writeCustomShoppingItems=items=>{
customShoppingItemsCache=Array.isArray(items) ? items : [];
try {
window.localStorage.setItem(customShoppingItemsStorageKey, JSON.stringify(customShoppingItemsCache));
} catch {
}
};
const syncShoppingItemVisualState=(label, isChecked)=>{
if (label instanceof HTMLElement) {
label.classList.toggle("is-checked", isChecked);
}
};
const resetLocalSubmittingState=form=>{
if (!(form instanceof HTMLFormElement)) {
return;
}
form.removeAttribute("data-is-submitting");
Array.from(form.querySelectorAll("button[type='submit']")).forEach(button=>{
if (!(button instanceof HTMLButtonElement)) {
return;
}
button.classList.remove("is-loading");
button.disabled=false;
button.removeAttribute("aria-busy");
if (button.dataset.originalLabel && !button.classList.contains("is-icon-only")) {
button.textContent=button.dataset.originalLabel;
}
if (typeof button.dataset.originalAriaLabel==="string") {
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
const wireShoppingChecklist=scope=>{
const labels=scope instanceof Element
? Array.from(scope.querySelectorAll("[data-shopping-item-label]"))
: Array.from(document.querySelectorAll("[data-shopping-item-label]"));
labels.forEach(label=>{
if (!(label instanceof HTMLElement) || label.dataset.shoppingItemWired==="true") {
return;
}
const checkbox=label.querySelector("[data-shopping-item-input]");
const shoppingItemKey=(label.dataset.shoppingItemKey ?? "").trim();
if (!(checkbox instanceof HTMLInputElement) || shoppingItemKey.length===0) {
return;
}
label.dataset.shoppingItemWired="true";
const savedState=readShoppingItemState();
const isChecked=savedState[shoppingItemKey]===true;
checkbox.checked=isChecked;
syncShoppingItemVisualState(label, isChecked);
checkbox.addEventListener("change", ()=>{
const currentState=readShoppingItemState();
if (checkbox.checked) {
currentState[shoppingItemKey]=true;
} else {
delete currentState[shoppingItemKey];
}
syncShoppingItemVisualState(label, checkbox.checked);
writeShoppingItemState();
const hideToggle=document.querySelector("[data-shopping-hide-checked]");
if (checkbox.checked && hideToggle instanceof HTMLInputElement && hideToggle.checked) hideToggle.focus();
syncShoppingChecklistStatus();
});
});
syncShoppingChecklistStatus();
};
const syncShoppingNotesExportContent=()=>{
const fields=Array.from(document.querySelectorAll("[data-notes-export-content]"));
const customItems=readCustomShoppingItems();
fields.forEach(field=>{
if (!(field instanceof HTMLTextAreaElement)) {
return;
}
const baseContent=field.dataset.notesBaseContent?.trim() ?? field.value.trim();
field.dataset.notesBaseContent=baseContent;
let nextContent=baseContent;
if (customItems.length > 0) {
const customItemLines=customItems
.map(item=>(item.text ?? "").trim())
.filter(text=>text.length > 0)
.map(text=>`- ${text}`)
.join("\n");
if (customItemLines.length > 0) {
nextContent=`${baseContent}\n\nYour extra items\n${customItemLines}`.trim();
}
}
field.value=nextContent.trim();
});
};
const normalizeCustomShoppingItemText=value=>typeof value==="string"
? value.replace(/\s+/g, " ").trim()
: "";
const buildCustomShoppingItemKey=itemId=>`custom|${itemId}`;
const createCustomShoppingItemId=()=>`${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
const buildCustomShoppingListItem=item=>{
const listItem=document.createElement("li");
listItem.className="aislepilot-custom-shopping-item";
listItem.dataset.customShoppingItemId=item.id;
const row=document.createElement("div");
row.className="aislepilot-custom-shopping-row";
const label=document.createElement("label");
label.className="aislepilot-checkbox-item";
label.dataset.shoppingItemLabel="";
label.dataset.shoppingItemKey=buildCustomShoppingItemKey(item.id);
const checkbox=document.createElement("input");
checkbox.type="checkbox";
checkbox.dataset.shoppingItemInput="";
checkbox.setAttribute("aria-label", `Mark ${item.text} as already have this item`);
const text=document.createElement("span");
text.className="aislepilot-shopping-item-name";
text.dataset.shoppingItemText="";
text.textContent=item.text;
label.append(checkbox, text);
const removeButton=document.createElement("button");
removeButton.type="button";
removeButton.className="aislepilot-custom-shopping-remove";
removeButton.dataset.customShoppingRemove=item.id;
removeButton.setAttribute("aria-label", `Remove ${item.text}`);
removeButton.textContent="Remove";
row.append(label, removeButton);
listItem.appendChild(row);
return listItem;
};
const renderCustomShoppingList=shell=>{
if (!(shell instanceof HTMLElement)) {
return;
}
const list=shell.querySelector("[data-custom-shopping-list]");
const emptyState=shell.querySelector("[data-custom-shopping-empty]");
if (!(list instanceof HTMLElement) || !(emptyState instanceof HTMLElement)) {
return;
}
list.replaceChildren();
readCustomShoppingItems().forEach(item=>{
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
syncShoppingChecklistStatus();
};
const wireCustomShoppingList=scope=>{
const shells=scope instanceof Element
? Array.from(scope.querySelectorAll("[data-custom-shopping-shell]"))
: Array.from(document.querySelectorAll("[data-custom-shopping-shell]"));
shells.forEach(shell=>{
if (!(shell instanceof HTMLElement)) {
return;
}
const form=shell.querySelector("[data-custom-shopping-form]");
const input=shell.querySelector("[data-custom-shopping-input]");
if (!(form instanceof HTMLFormElement) || !(input instanceof HTMLInputElement)) {
return;
}
form.setAttribute("data-skip-submit-loading", "true");
if (shell.dataset.customShoppingWired!=="true") {
shell.dataset.customShoppingWired="true";
form.addEventListener("submit", event=>{
event.preventDefault();
try {
const normalizedText=normalizeCustomShoppingItemText(input.value);
if (normalizedText.length===0) {
input.focus();
return;
}
const currentItems=readCustomShoppingItems();
const alreadyExists=currentItems.some(item =>
(item.text ?? "").trim().toLowerCase()===normalizedText.toLowerCase());
if (alreadyExists) {
input.value="";
renderCustomShoppingList(shell);
input.focus();
return;
}
currentItems.push({
id: createCustomShoppingItemId(),
text: normalizedText
});
writeCustomShoppingItems(currentItems);
input.value="";
renderCustomShoppingList(shell);
input.focus();
} finally {
resetLocalSubmittingState(form);
}
});
shell.addEventListener("click", event=>{
if (!(event.target instanceof Element)) {
return;
}
const removeButton=event.target.closest("[data-custom-shopping-remove]");
if (!(removeButton instanceof HTMLButtonElement)) {
return;
}
const itemId=(removeButton.dataset.customShoppingRemove ?? "").trim();
if (itemId.length===0) {
return;
}
const nextItems=readCustomShoppingItems().filter(item=>item.id!==itemId);
writeCustomShoppingItems(nextItems);
const currentState=readShoppingItemState();
delete currentState[buildCustomShoppingItemKey(itemId)];
writeShoppingItemState();
renderCustomShoppingList(shell);
});
}
renderCustomShoppingList(shell);
});
};
const wireShoppingFilter=()=>{
const toggle=document.querySelector("[data-shopping-hide-checked]");
const search=document.querySelector("[data-shopping-search]");
if (toggle instanceof HTMLInputElement && toggle.dataset.shoppingFilterWired!=="true") {
toggle.dataset.shoppingFilterWired="true";
toggle.addEventListener("change", syncShoppingChecklistStatus);
}
if (search instanceof HTMLInputElement && search.dataset.shoppingSearchWired!=="true") {
search.dataset.shoppingSearchWired="true";
search.addEventListener("input", syncShoppingChecklistStatus);
}
syncShoppingChecklistStatus();
};
const wireShoppingDepartments=scope=>{
const departments=scope instanceof Element
? Array.from(scope.querySelectorAll("[data-shopping-department]"))
: Array.from(document.querySelectorAll("[data-shopping-department]"));
const compactViewport=window.matchMedia("(max-width: 767px)").matches;
departments.forEach((department,index)=>{
if (!(department instanceof HTMLDetailsElement) || department.dataset.shoppingDepartmentWired==="true") return;
department.dataset.shoppingDepartmentWired="true";
if (compactViewport && index > 0) department.open=false;
});
};
const wireShoppingReset=()=>{
const reset=document.querySelector("[data-shopping-reset]");
const confirm=document.querySelector("[data-shopping-reset-confirm]");
const accept=document.querySelector("[data-shopping-reset-accept]");
const cancel=document.querySelector("[data-shopping-reset-cancel]");
if (!(reset instanceof HTMLButtonElement) || !(confirm instanceof HTMLElement) || !(accept instanceof HTMLButtonElement) || !(cancel instanceof HTMLButtonElement) || reset.dataset.shoppingResetWired==="true") return;
reset.dataset.shoppingResetWired="true";
const close=()=>{ confirm.hidden=true; reset.setAttribute("aria-expanded", "false"); };
reset.setAttribute("aria-expanded", "false");
reset.addEventListener("click", ()=>{ confirm.hidden=false; reset.setAttribute("aria-expanded", "true"); accept.focus(); });
cancel.addEventListener("click", ()=>{ close(); reset.focus(); });
accept.addEventListener("click", ()=>{
const state=readShoppingItemState();
document.querySelectorAll("#aislepilot-shop [data-shopping-item-label]").forEach(label=>{
const key=(label.dataset.shoppingItemKey || "").trim();
const input=label.querySelector("[data-shopping-item-input]");
if (key) delete state[key];
if (input instanceof HTMLInputElement) input.checked=false;
syncShoppingItemVisualState(label, false);
});
writeShoppingItemState();
const filter=document.querySelector("[data-shopping-hide-checked]");
if (filter instanceof HTMLInputElement) filter.checked=false;
close();
syncShoppingChecklistStatus();
reset.focus();
});
};
window.AislePilotShopping={
wireCustomShoppingList,
wireShoppingChecklist,
wireShoppingFilter,
wireShoppingDepartments,
wireShoppingReset
};
})();
