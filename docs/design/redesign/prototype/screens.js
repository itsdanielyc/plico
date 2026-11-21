"use strict";
/* Plico redesign prototype: overlays, secondary screens, presets, notes and input. */

/* ───────── Search field and suggestions ───────── */
const TOKEN_KEYS = ["tag", "status", "from", "type", "dated", "text", "folder"];
function wireSearch() {
  const q = $("#q"); if (!q) return;
  q.addEventListener("focus", () => { if (!S.searchOpen) { S.searchOpen = true; S.sIdx = -1; $("#search-f").classList.add("on"); renderSearchDrop(); } });
  q.addEventListener("input", () => { S.q.text = q.value; S.sIdx = -1; tokenize(q); renderSearchDrop(); });
  q.addEventListener("keydown", (e) => {
    const opts = searchOpts();
    if (e.key === "ArrowDown") { e.preventDefault(); S.sIdx = Math.min(opts.length - 1, S.sIdx + 1); renderSearchDrop(); }
    else if (e.key === "ArrowUp") { e.preventDefault(); S.sIdx = Math.max(-1, S.sIdx - 1); renderSearchDrop(); }
    else if (e.key === "Enter") { e.preventDefault(); const o = S.sIdx >= 0 ? opts[S.sIdx] : null; if (o) pickSearchOpt(o); else if (S.q.text.trim() || S.q.tokens.length) runSearch(); }
    else if (e.key === "Escape") { e.preventDefault(); q.blur(); closeSearch(); }
    else if (e.key === "Backspace" && !q.value && S.q.tokens.length) { S.q.tokens.pop(); refreshTokens(); renderSearchDrop(); }
  });
  q.addEventListener("blur", () => later(() => { if (document.activeElement !== $("#q") && S.searchOpen) closeSearch(); }, 160));
  const host = $("#sdrop-host"); if (host) host.addEventListener("mousedown", (e) => e.preventDefault());
}
function tokenize(q) {
  const m = /(?:^|\s)(\w+):("[^"]+"|\S+)\s$/.exec(q.value);
  if (m && TOKEN_KEYS.includes(m[1].toLowerCase())) {
    S.q.tokens.push({ k: m[1].toLowerCase(), v: m[2].replace(/"/g, "") });
    q.value = q.value.slice(0, m.index).trim(); S.q.text = q.value; refreshTokens();
  }
}
function refreshTokens() { const t = $("#toks"); if (t) t.innerHTML = toksHTML(); const q = $("#q"); if (q) q.placeholder = S.q.tokens.length ? "" : "Search documents, text and tags"; }
function searchOpts() {
  const text = S.q.text.trim(), opts = [];
  if (!text && !S.q.tokens.length) {
    opts.push({ kind: "recent", label: "renewal", text: "renewal", tokens: [] });
    opts.push({ kind: "recent", label: "tag:Tax dated:2026", text: "", tokens: [{ k: "tag", v: "Tax" }, { k: "dated", v: "2026" }] });
    opts.push({ kind: "tok", k: "status", v: "inbox", count: DOCS.filter((d) => d.status === "inbox").length });
    opts.push({ kind: "tok", k: "text", v: "missing", count: DOCS.filter((d) => d.noText).length });
    return opts;
  }
  const last = text.split(/\s+/).pop().toLowerCase();
  const keyed = /^(\w+):(.*)$/.exec(last);
  if (keyed && TOKEN_KEYS.includes(keyed[1])) {
    const k = keyed[1], v = keyed[2];
    const pool = k === "tag" ? TAGS.map((t) => t.name) : k === "from" ? CORRESPONDENTS : k === "type" ? DOC_TYPES : k === "status" ? ["inbox", "reviewed"] : k === "text" ? ["missing", "present"] : k === "dated" ? ["2026", "2025", "2026-11"] : [];
    pool.filter((x) => x.toLowerCase().includes(v)).slice(0, 6).forEach((x) => opts.push({ kind: "tok", k, v: x, replaceLast: true }));
    return opts;
  }
  if (last.length >= 2) {
    TAGS.filter((t) => t.name.toLowerCase().startsWith(last)).slice(0, 2).forEach((t) => opts.push({ kind: "tok", k: "tag", v: t.name, replaceLast: true, count: t.count }));
    CORRESPONDENTS.filter((c) => c.toLowerCase().includes(last)).slice(0, 2).forEach((c) => opts.push({ kind: "tok", k: "from", v: c, replaceLast: true }));
    DOC_TYPES.filter((c) => c.toLowerCase().startsWith(last)).slice(0, 1).forEach((c) => opts.push({ kind: "tok", k: "type", v: c, replaceLast: true }));
  }
  searchDocs().slice(0, 4).forEach((d) => opts.push({ kind: "doc", id: d.id }));
  opts.push({ kind: "run" });
  return opts;
}
function renderSearchDrop() {
  const host = $("#sdrop-host"); if (!host) return;
  if (!S.searchOpen) { host.innerHTML = ""; return; }
  const opts = searchOpts(); let html = "", group = "";
  const head = (g, extra = "") => { if (g !== group) { group = g; html += `<div class="grp">${g}${extra}</div>`; } };
  opts.forEach((o, i) => {
    const on = i === S.sIdx ? "on" : "";
    if (o.kind === "recent") { head("Recent searches"); html += `<div class="opt ${on}" data-act="sopt" data-i="${i}">${I.history}<span>${o.tokens.map((t) => `<span class="tok"><i>${t.k}:</i>${esc(t.v)}</span>`).join(" ")} ${esc(o.text)}</span></div>`; }
    else if (o.kind === "tok") { head("Filters"); html += `<div class="opt ${on}" data-act="sopt" data-i="${i}"><span class="tok"><i>${o.k}:</i>${esc(o.v)}</span>${o.count != null ? `<span class="o2" style="margin-left:auto">${o.count} documents</span>` : ""}</div>`; }
    else if (o.kind === "doc") { const d = doc(o.id); head("Documents"); html += `<div class="opt ${on}" data-act="sopt" data-i="${i}">${thumb(d, 22, 31)}<div class="ob"><div>${esc(d.title)}</div><div class="o2">${snippet(d)}</div></div></div>`; }
    else if (o.kind === "run") { html += `<div class="opt ${on}" data-act="sopt" data-i="${i}" style="margin-top:4px">${I.search}<span>Search all documents for “${esc(S.q.text.trim())}”${S.q.tokens.length ? " with these filters" : ""}</span><span style="margin-left:auto">${K("enter")}</span></div>`; }
  });
  html += `<div class="help"><span><code>"lease renewal"</code> exact phrase</span><span><code>tag:tax</code></span><span><code>dated:2024</code></span><span><code>NOT status:reviewed</code></span></div>`;
  host.innerHTML = `<div class="pop sdrop" role="listbox" aria-label="Search suggestions">${html}</div>`;
}
function pickSearchOpt(o) {
  const q = $("#q");
  if (o.kind === "tok") {
    if (o.replaceLast) { const parts = S.q.text.trim().split(/\s+/); parts.pop(); S.q.text = parts.join(" "); }
    S.q.tokens.push({ k: o.k, v: o.v }); if (q) q.value = S.q.text; refreshTokens(); S.sIdx = -1; renderSearchDrop(); if (q) q.focus(); return;
  }
  if (o.kind === "recent") { S.q = { text: o.text, tokens: o.tokens.map((t) => ({ ...t })) }; runSearch(); return; }
  if (o.kind === "doc") { runSearch(o.id); return; }
  runSearch();
}
function runSearch(focusId) {
  S.view = "search"; S.savedName = null; S.sel.clear(); S.matchIdx = 0; S.editing = null;
  const res = searchDocs(); S.cur = focusId || (res[0] && res[0].id) || null;
  S.searchOpen = false;
  renderSidebar(); renderToolbar(); renderContent();
}
function closeSearch() { S.searchOpen = false; renderToolbar(); }

/* ───────── Overlays ───────── */
function renderOverlay() {
  const ov = $("#ov");
  clearInterval(S.actTick);
  switch (S.overlay) {
    case "palette": ov.innerHTML = `<div class="scrim" data-act="close"></div><div class="pop palette" role="dialog" aria-label="Commands">${paletteInner()}</div>`; wirePalette(); break;
    case "activity": ov.innerHTML = `<div style="position:absolute;inset:0;z-index:55" data-act="close"></div><div class="pop activity" role="dialog" aria-label="Activity" id="activity">${activityHTML()}</div>`; break;
    case "scan": ov.innerHTML = `<div class="scrim" data-act="noop"></div><div class="dialog scan sheetlike" role="dialog" aria-label="Scan">${scanHTML()}</div>`; break;
    case "settings": ov.innerHTML = `<div class="scrim" data-act="close"></div><div class="dialog settings" role="dialog" aria-label="Settings">${settingsHTML()}</div>`; break;
    case "welcome": ov.innerHTML = welcomeHTML(); break;
    case "drop": ov.innerHTML = dropHTML(); break;
    case "shortcuts": ov.innerHTML = `<div class="scrim" data-act="close"></div><div class="dialog" style="width:720px;height:540px;margin-left:-360px;margin-top:-270px" role="dialog" aria-label="Keyboard shortcuts">${shortcutsHTML()}</div>`; break;
    case "addmenu": ov.innerHTML = `<div style="position:absolute;inset:0;z-index:55" data-act="close"></div>${menuHTML(addMenu(), "right:" + (S.platform === "win" ? 150 : 14) + "px;top:44px")}`; break;
    case "libmenu": ov.innerHTML = `<div style="position:absolute;inset:0;z-index:55" data-act="close"></div>${menuHTML(libMenu(), "left:10px;top:44px", "transform-origin:top left")}`; break;
    case "docmenu": ov.innerHTML = `<div style="position:absolute;inset:0;z-index:55" data-act="close"></div>${menuHTML(docMenu(), "right:12px;bottom:58px", "transform-origin:bottom right")}`; break;
    case "fmenu": ov.innerHTML = `<div style="position:absolute;inset:0;z-index:55" data-act="close"></div>${menuHTML(S.fmenu.items, `left:${S.fmenu.x}px;top:${S.fmenu.y}px`, "transform-origin:top left")}`; break;
    case "ql": ov.innerHTML = qlHTML(); qlIn(); break;
    default: ov.innerHTML = "";
  }
  if (S.overlay === "activity") S.actTick = setInterval(() => { const a = $("#activity"); if (a && DOCS.some((d) => d.processing)) a.innerHTML = activityHTML(); }, 400);
}
function openOverlay(name) { S.overlay = name; S.searchOpen = false; renderOverlay(); }
function closeOverlay() {
  if (S.overlay === "ql") { qlOut(); return; }
  if (S.overlay === "welcome") return;
  S.overlay = null; renderOverlay();
}

/* Menus */
function menuHTML(items, pos, extra = "") {
  return `<div class="pop menu" role="menu" style="${pos};${extra}">${items.map((it) => it === "-" ? "<hr>" : it.h ? `<div class="mh">${esc(it.h)}</div>` : `<div class="mi" role="menuitem" tabindex="-1" data-act="${it.act || "todo"}" data-msg="${esc(it.label)}" ${it.data || ""}>${it.icon || ""}<span>${esc(it.label)}</span>${it.keys ? K(it.keys) : ""}</div>`).join("")}</div>`;
}
const addMenu = () => [
  { label: "Add files…", icon: I.upload, keys: "mod+o", act: "addfiles" },
  { label: "Scan…", icon: I.scan, keys: "mod+shift+s", act: "scan" },
  "-", { h: "Bring in a library" },
  { label: "From Paperless-ngx…", icon: I.share }, { label: "From Evernote…", icon: I.share }, { label: "From Mariner Paperless…", icon: I.share },
  "-", { label: "Password-protected PDF…", icon: I.lock },
];
const libMenu = () => [
  { h: "Home paperwork" },
  { label: "Show library folder", icon: I.folder16 },
  { label: "Back up library…", icon: I.history },
  "-", { label: "Open another library…", icon: I.stack }, { label: "New library…", icon: I.plus },
  "-", { label: "Settings", icon: I.sliders, keys: "mod+,", act: "settings" },
];
const docMenu = () => [
  { label: "Open in PDF viewer", icon: I.open, keys: "o" }, { label: "Show in folder", icon: I.folder16 }, { label: "Move to folder…", icon: I.move },
  "-", { label: "Edit pages…", icon: I.pages, act: "goto", data: 'data-s="pages"' }, { label: "Redact…", icon: I.redact }, { label: "Add stamp…", icon: I.stamp }, { label: "Compress…", icon: I.compress },
  "-", { label: "Export a copy…", icon: I.share }, { label: "Make searchable copy", icon: I.ocr }, { label: "Version history…", icon: I.history },
  "-", { label: "Related documents", icon: I.link }, { label: "Ask about this document…", icon: I.chat },
];

/* Command palette */
function CMDS() {
  const T = (label, g, icon, extra = {}) => ({ label, g, icon, ...extra });
  return [
    T("Mark reviewed", "Document", I.check, { keys: "r", alias: "done finish file archive complete", run: () => review() }),
    T("Accept all suggestions", "Document", I.check, { keys: "a", alias: "apply suggested", run: acceptAll }),
    T("Add tag", "Document", I.tag, { keys: "t", alias: "label", run: () => startEdit("tags"), hint: true }),
    T("Set document date", "Document", I.cal, { keys: "d", alias: "when dated", run: () => startEdit("date"), hint: true }),
    T("Set sender", "Document", I.from, { keys: "f", alias: "correspondent from who", run: () => startEdit("from"), hint: true }),
    T("Add reminder", "Document", I.bell, { alias: "deadline due renewal expiry keep until alarm", run: () => startEdit("deadline") }),
    T("Rename", "Document", I.text, { keys: "e", alias: "title edit name", run: () => { const t = $("#title-in"); if (t) { t.focus(); t.select(); } } }),
    T("Move to Trash", "Document", I.trash, { keys: "x", alias: "delete remove bin", run: trashDoc }),
    T("Open in PDF viewer", "Document", I.open, { keys: "o", alias: "external acrobat preview" }),
    T("Show in folder", "Document", I.folder16, { alias: "reveal explorer finder files path" }),
    T("Move to folder…", "Document", I.move, { alias: "file relocate" }),
    T("Edit pages…", "Document", I.pages, { alias: "rotate reorder delete extract split pages", run: () => go("pages") }),
    T("Rotate pages…", "Document", I.rotR, { alias: "turn upright sideways" }),
    T("Extract pages…", "Document", I.split, { alias: "split copy pages" }),
    T("Redact…", "Document", I.redact, { alias: "black out hide censor" }),
    T("Add stamp…", "Document", I.stamp, { alias: "paid received" }),
    T("Export a copy…", "Document", I.share, { alias: "pdf/a password metadata save as" }),
    T("Compress…", "Document", I.compress, { alias: "smaller size" }),
    T("Make searchable copy", "Document", I.ocr, { alias: "ocr recognise recognize text" }),
    T("Version history…", "Document", I.history, { alias: "versions previous restore" }),
    T("Related documents", "Document", I.link, { alias: "similar see also" }),
    T("Ask about this document…", "Document", I.chat, { ctx: "Local model", alias: "chat question llm" }),
    T("Set archive number…", "Document", I.tag, { alias: "asn serial label" }),
    T("Inbox", "Go to", I.inbox, { alias: "review queue", run: () => setView("inbox") }),
    T("All documents", "Go to", I.stack, { alias: "library everything", run: () => setView("all") }),
    T("Upcoming", "Go to", I.clock, { alias: "deadlines reminders due", run: () => setView("upcoming") }),
    T("Trash", "Go to", I.trash, { alias: "deleted bin", run: () => setView("trash") }),
    T("Add files…", "Add", I.upload, { keys: "mod+o", alias: "import open" }),
    T("Scan…", "Add", I.scan, { keys: "mod+shift+s", alias: "scanner capture feeder", run: () => openScan() }),
    T("Import from Paperless-ngx…", "Add", I.share, { alias: "migrate move" }),
    T("Import from Evernote…", "Add", I.share, { alias: "migrate enex" }),
    T("Import from Mariner Paperless…", "Add", I.share, { alias: "migrate" }),
    T("Import password-protected PDF…", "Add", I.lock, { alias: "locked encrypted" }),
    T("Search…", "Search", I.search, { keys: "/", run: () => $("#q").focus() }),
    T("Save this search…", "Search", I.bookmark, { alias: "saved view shortcut" }),
    T("Find by archive number…", "Search", I.search, { alias: "asn" }),
    T("Documents without text", "Search", I.ocr, { alias: "ocr missing scans", run: () => { S.q = { text: "", tokens: [{ k: "text", v: "missing" }] }; runSearch(); } }),
    T("Semantic search…", "Search", I.search, { ctx: "Local model", alias: "meaning similar ai" }),
    T("Back up library…", "Library", I.history, { alias: "backup copy" }),
    T("Show library folder", "Library", I.folder16, { alias: "pdfs explorer finder" }),
    T("Refresh index", "Library", I.refresh, { keys: "f5", alias: "rescan reload" }),
    T("Check library integrity…", "Library", I.check, { alias: "checksum verify" }),
    T("Index report", "Library", I.warn, { alias: "missing unavailable restore" }),
    T("Export catalog as JSON…", "Library", I.share, { alias: "metadata" }),
    T("Export results as CSV…", "Library", I.share, { alias: "spreadsheet" }),
    T("Export calendar…", "Library", I.cal, { alias: "ical ics reminders" }),
    T("Print archive number labels…", "Library", I.tag, { alias: "asn sheet" }),
    T("Print separator sheet…", "Library", I.split, { alias: "patch-t barcode" }),
    T("Settings", "App", I.sliders, { keys: "mod+,", alias: "preferences options", run: () => openSettings() }),
    T("Keyboard shortcuts", "App", I.keyboard, { keys: "?", alias: "keys help", run: () => openOverlay("shortcuts") }),
    T("Tag rules…", "App", I.tag, { alias: "filing rules automatic", run: () => openSettings("filing") }),
    T("Senders, types and fields…", "App", I.from, { alias: "correspondents metadata custom fields tag colours", run: () => openSettings("filing") }),
    T("Local models…", "App", I.chat, { alias: "ollama llm ai embeddings", run: () => openSettings("models") }),
    T("Switch to dark appearance", "App", I.sliders, { alias: "theme light dark mode", run: () => setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark") }),
  ];
}
function fuzzy(q, s) {
  q = q.toLowerCase(); s = s.toLowerCase();
  if (s.includes(q)) return 100 - s.indexOf(q);
  let i = 0, score = 0;
  for (const ch of s) { if (ch === q[i]) { i++; score += 2; } if (i === q.length) return score; }
  return 0;
}
function palItems() {
  const all = CMDS(), q = S.pal.q.trim();
  if (!q) {
    const d = doc(S.cur), sug = d && d.status === "inbox" ? ["Mark reviewed", "Accept all suggestions", "Add tag", "Set document date", "Edit pages…", "Scan…"] : ["Scan…", "Add files…", "Search…", "Inbox", "Settings"];
    return [...sug.map((l) => ({ ...all.find((c) => c.label === l), g: "Suggested" })), ...all.filter((c) => c.g === "Go to"), ...all.filter((c) => c.g === "Library").slice(0, 5)];
  }
  return all.map((c) => ({ c, s: Math.max(fuzzy(q, c.label) * 1.2, fuzzy(q, c.alias || "") * 0.9) }))
    .filter((x) => x.s > 0).sort((a, b) => b.s - a.s).slice(0, 12).map((x) => x.c);
}
function paletteInner() {
  return `<div class="pin">${I.search}<input id="pal-q" placeholder="Type a command or search" autocomplete="off" spellcheck="false" aria-label="Command" value="${esc(S.pal.q)}">${doc(S.cur) ? `<span class="scope">${esc(doc(S.cur).title)}</span>` : ""}</div><div class="plist" id="plist">${palListHTML()}</div>
    <div class="pfoot"><span>${K("up")}${K("down")} Move</span><span>${K("enter")} Run</span><span>${K("esc")} Close</span><span style="margin-left:auto">${K("mod+k")} opens this anywhere</span></div>`;
}
function palListHTML() {
  const items = palItems(); if (!items.length) return `<div class="empty">No command matches “${esc(S.pal.q)}”.</div>`;
  let g = "", out = "";
  items.forEach((c, i) => {
    if (!S.pal.q.trim() && c.g !== g) { g = c.g; out += `<div class="pg-h">${esc(g)}</div>`; }
    out += `<div class="pi ${i === S.pal.idx ? "on" : ""}" data-act="pal-run" data-i="${i}" role="option" aria-selected="${i === S.pal.idx}">${c.icon || ""}<span>${esc(c.label)}</span>${S.pal.q.trim() ? `<span class="ctx">${esc(c.ctx || c.g)}</span>` : c.ctx ? `<span class="ctx">${esc(c.ctx)}</span>` : ""}<span class="keys">${c.keys ? c.keys.split(" ").map(K).join("") : ""}</span></div>`;
  });
  return out;
}
function wirePalette() {
  const inp = $("#pal-q"); if (!inp) return; inp.focus();
  inp.addEventListener("input", () => { S.pal.q = inp.value; S.pal.idx = 0; $("#plist").innerHTML = palListHTML(); });
  inp.addEventListener("keydown", (e) => {
    const n = palItems().length;
    if (e.key === "ArrowDown") { e.preventDefault(); S.pal.idx = Math.min(n - 1, S.pal.idx + 1); $("#plist").innerHTML = palListHTML(); $(".pi.on")?.scrollIntoView({ block: "nearest" }); }
    else if (e.key === "ArrowUp") { e.preventDefault(); S.pal.idx = Math.max(0, S.pal.idx - 1); $("#plist").innerHTML = palListHTML(); $(".pi.on")?.scrollIntoView({ block: "nearest" }); }
    else if (e.key === "Enter") { e.preventDefault(); runPal(S.pal.idx); }
    else if (e.key === "Escape") { e.preventDefault(); closeOverlay(); }
  });
}
function runPal(i) {
  const c = palItems()[i]; if (!c) return;
  S.overlay = null; renderOverlay();
  if (c.run) { c.run(); if (c.hint && c.keys) later(() => toast({ text: `Next time, press ${keyLabel(c.keys)}` }), 50); }
  else notMocked(c.label);
}
function notMocked(label) { toast({ text: `${String(label).replace(/…$/, "")} isn't mocked up in this round` }); }

/* Activity */
function activityHTML() {
  const proc = DOCS.filter((d) => d.processing), path = S.platform === "win" ? "C:\\Users\\Dana\\Scans" : "~/Scans";
  const phone = S.platform === "win" ? "C:\\Users\\Dana\\iCloudDrive\\Plico" : S.platform === "mac" ? "~/Library/Mobile Documents/Plico" : "~/Nextcloud/Plico";
  const watching = S.watch !== false;
  return `
    <div class="ah">Reading ${proc.length ? `<span>${proc.length} file${proc.length > 1 ? "s" : ""}</span>` : ""}</div>
    ${proc.length ? proc.map((d) => `<div class="aitem">${thumb(d, 22, 31, "reading")}<div style="min-width:0"><div class="a1">${esc(d.title)}</div><div class="a2">${esc(d.stage || "Reading text…")}</div><div class="meter"><i style="width:${d.progress || 0}%"></i></div></div><span class="a2">${d.progress || 0}%</span></div>`).join("") : `<div class="aitem"><span class="ico">${I.check}</span><div class="a2" style="grid-column:2/4">Nothing is being read right now.</div></div>`}
    <div class="adiv"></div>
    <div class="ah">Watching</div>
    <div class="aitem"><span class="ico">${I.watch}</span><div style="min-width:0"><div class="a1">Scans folder</div><div class="a2" title="${esc(path)}">${esc(path)}</div></div><div class="acts"><span class="st" style="display:inline-flex;gap:5px;align-items:center;font-size: 12px;color:var(--ink-3)"><span class="live ${watching ? "" : "off"}"></span>${watching ? "Watching" : "Paused"}</span><button class="btn sm" type="button" data-act="watch-toggle">${watching ? "Pause" : "Resume"}</button></div></div>
    <div class="aitem"><span class="ico">${I.phone}</span><div style="min-width:0"><div class="a1">Phone inbox</div><div class="a2" title="${esc(phone)}">${esc(phone)}</div></div><div class="acts"><button class="btn sm" type="button" data-act="todo" data-msg="Resume and catch up">Resume and catch up</button></div></div>
    <div class="aitem" style="padding-top:2px"><span></span><span class="a2">Catch up on folders when this library opens</span><button class="toggle" type="button" role="switch" aria-checked="true" data-act="flip" aria-label="Catch up on open"></button></div>
    <div class="adiv"></div>
    <div class="ah">Needs you <span>${(S.dupes || []).length + 1}</span></div>
    <div class="aitem"><span class="ico err">${I.warn}</span><div style="min-width:0"><div class="a1">budget-2027.xlsx</div><div class="a2">Install LibreOffice to bring in Office files.</div></div><div class="acts"><button class="btn sm" type="button" data-act="todo" data-msg="Retry">Retry</button><button class="ibtn" type="button" data-act="todo" data-msg="Dismiss" aria-label="Dismiss">${I.x}</button></div></div>
    ${(S.dupes || []).map((x) => `<div class="aitem"><span class="ico">${I.stack}</span><div style="min-width:0"><div class="a1">${esc(x.file)}</div><div class="a2">Already in your library as “${esc(doc(x.of).title)}”</div></div><div class="acts"><button class="btn sm" type="button" data-act="open" data-id="${x.of}">Show it</button></div></div>`).join("")}
    <div class="adiv"></div>
    <div class="ah">Added today</div>
    ${DOCS.filter((d) => /^Today/.test(d.added) && !d.processing).slice(0, 3).map((d) => `<div class="aitem" data-act="open" data-id="${d.id}">${thumb(d, 22, 31)}<div style="min-width:0"><div class="a1">${esc(d.title)}</div><div class="a2">${esc(d.source)} · ${esc(String(d.added).replace("Today, ", ""))}</div></div><span></span></div>`).join("")}`;
}

/* Arrivals: files read in the row itself */
const ARRIVALS = [
  { id: "arr-cobalt", file: "cobalt-summary.pdf", pages: [P.phoneContract], title: "Plan renewal notice", pending: { from: "Cobalt Mobile", type: "Letter", date: "19 Nov 2026", tags: ["Bills"], deadline: "Term ends 14 Jan 2027", why: "Matches 3 reviewed Cobalt Mobile documents" } },
  { id: "arr-bank", file: "IMG_4481.HEIC", pages: [P.bankLetter], title: "New debit card", pending: { from: "Millbrook Building Society", type: "Letter", date: "18 Nov 2026", tags: ["Bank"], why: "From the letterhead" } },
  { id: "arr-receipt", file: "receipt-scan.jpg", pages: [P.receipt], duplicateOf: "receipt" },
];
function arrive(names, source = "Dropped on the window") {
  const specs = ARRIVALS.map((a, i) => ({ ...a, file: names && names[i] ? names[i] : a.file }));
  const before = DOCS.filter((d) => d.status === "inbox").length;
  specs.slice().reverse().forEach((a, i) => {
    DOCS.unshift({ id: a.id + "-" + Date.now() % 100000 + i, spec: a, title: a.file, from: "", type: "", date: "", added: "Today, 09:4" + (4 + i), source, tags: [], status: "inbox", pages: a.pages, size: "—", path: "PDFs/Inbox/" + a.file.replace(/\.\w+$/, ".pdf"), excerpt: "", processing: true, progress: 4, stage: "Reading text…", pending: null, deadline: null, note: "", _arriving: true });
  });
  S.view = "inbox"; S.savedName = null; S.layout = "list";
  if (!S.cur || !doc(S.cur) || doc(S.cur).status !== "inbox") S.cur = DOCS.find((d) => d.status === "inbox" && !d.processing)?.id || DOCS[0].id;
  renderSidebar(); renderContent(); bumpInbox(before, DOCS.filter((d) => d.status === "inbox").length); renderActBtn();
  const tick = () => {
    const proc = DOCS.filter((d) => d.processing);
    if (!proc.length) return;
    proc.forEach((d, i) => {
      d.progress = Math.min(100, d.progress + 7 + ((i * 5 + d.progress) % 7));
      if (d.progress > 52) d.stage = "Finding dates and senders…";
      if (d.spec.duplicateOf && d.progress > 40) {
        DOCS.splice(DOCS.indexOf(d), 1); (S.dupes = S.dupes || []).push({ file: d.spec.file, of: d.spec.duplicateOf });
        if (S.cur === d.id) S.cur = DOCS.find((x) => x.status === "inbox")?.id;
        toast({ text: `${d.spec.file} is already in your library`, dim: doc(d.spec.duplicateOf).title });
        bumpInbox(DOCS.filter((x) => x.status === "inbox").length + 1, DOCS.filter((x) => x.status === "inbox").length);
      } else if (d.progress >= 100) {
        d.processing = false; d.title = d.spec.title; d.pending = { ...d.spec.pending, tags: [...d.spec.pending.tags] }; d.excerpt = d.spec.pending.why; d._arriving = false;
      }
    });
    if (S.view === "inbox" && layoutName() === "review") {
      updateProcRows();
      const cd = doc(S.cur); if (cd && cd.spec && !S.editing) renderInsp();
    }
    renderSidebar(); renderActBtn();
    if (DOCS.some((d) => d.processing)) later(tick, 360);
    else later(() => { renderSidebar(); renderActBtn(); }, 50);
  };
  later(tick, 700);
}
/* Update reading rows in place so the scan-line sweep keeps running; rebuild only when the set changes. */
function updateProcRows() {
  const rows = $$("#rows .row"), ids = viewDocs().map((d) => d.id);
  if (rows.length !== ids.length || rows.some((r, i) => r.dataset.id !== ids[i] || (r.querySelector(".reading") && !doc(ids[i]).processing))) { renderRows(); return; }
  DOCS.filter((d) => d.processing).forEach((d) => {
    const r = $(`#rows .row[data-id="${d.id}"]`); if (!r) return;
    const m = r.querySelector(".meter i"); if (m) m.style.width = d.progress + "%";
    const rm = r.querySelector(".rm"); if (rm) rm.textContent = d.stage;
  });
}
function renderActBtn() {
  const b = $("#act-btn"); if (!b) return;
  const n = DOCS.filter((d) => d.processing).length, r = 6.5, c = 2 * Math.PI * r;
  const avg = n ? DOCS.filter((d) => d.processing).reduce((a, d) => a + d.progress, 0) / n / 100 : 0;
  b.innerHTML = n ? `<svg class="ring" viewBox="0 0 16 16" aria-hidden="true"><circle class="bg" cx="8" cy="8" r="${r}"/><circle class="fg" cx="8" cy="8" r="${r}" stroke-dasharray="${c}" stroke-dashoffset="${c * (1 - avg)}"/></svg><span class="act-n">${n}</span>` : I.sparkline;
}

/* Drop overlay */
function dropHTML() {
  const n = S.dropCount || 3;
  const files = S.dropCount ? [] : ARRIVALS.map((a) => a.file);
  return `<div class="drop" id="drop" data-act="drop-now"><div class="drop-scrim"></div><div class="drop-zone"></div>
    <div class="drop-in">${markSVG("drop-mark")}
      <h2>Drop to add ${n} file${n === 1 ? "" : "s"} to the Inbox</h2>
      <p>Plico keeps a copy, reads the text and suggests where it belongs.</p>
      <div class="drop-files">${files.map((f) => `<span>${esc(f)}</span>`).join("")}</div>
      <span class="drop-dest">${I.folder}PDFs/Inbox</span>
    </div></div>`;
}
function doDrop() {
  const el = $("#drop"); if (!el || el.classList.contains("dropped")) return;
  el.classList.add("dropped");
  const names = S.dropNames; S.dropNames = null; S.dropCount = 0;
  later(() => { S.overlay = null; renderOverlay(); arrive(names); }, motionOK() ? 520 : 0);
}

/* Scan */
const SEP_DOC = { id: "sep", pages: [() => pageFrame(A4.w, A4.h, "pg-sans pg-sep", `<div class="sepbars"><i></i><i class="t"></i><i></i><i class="t"></i><i class="t"></i><i></i></div><div class="sepbig">PATCH T</div><p>Separator sheet · Plico starts a new document after this page</p>`)] };
const SCAN_FEED = [
  { d: { id: "sc-1", pages: [P.boilerCert] } }, { d: SEP_DOC, sep: true }, { d: { id: "sc-2", pages: [P.councilTax] } }, { d: SEP_DOC, sep: true }, { d: { id: "sc-3", pages: [P.phoneContract] } },
];
function newScan() { return { state: "idle", n: 0, src: "Feeder", res: "300", color: "Grey", split: "sep", jam: false, profile: "Letters and bills" }; }
function openScan() { S.scan = S.scan && S.scan.state !== "done" ? S.scan : newScan(); openOverlay("scan"); }
function seg(group, opts, cur) { return `<div class="segc" role="group">${opts.map(([v, l]) => `<button type="button" data-act="seg" data-g="${group}" data-v="${v}" aria-pressed="${cur === v}">${l}</button>`).join("")}</div>`; }
function scanHTML() {
  const sc = S.scan, shown = SCAN_FEED.slice(0, sc.n);
  const docsN = shown.filter((x) => !x.sep).length, sepN = shown.filter((x) => x.sep).length;
  let pages = "", num = 0;
  shown.forEach((x, i) => {
    if (x.sep) { pages += `<div class="sp ${i === sc.n - 1 && sc.state === "scanning" ? "feed" : ""}" style="opacity:.55">${sheetAt(x.d, 0, fitScale(x.d, 0, 120, 170))}<span>Separator · skipped</span></div>${sc.split === "sep" ? `<div class="split-mark" title="A new document starts here"><span>${I.split}</span></div>` : ""}`; return; }
    num++; pages += `<div class="sp ${i === sc.n - 1 && sc.state === "scanning" ? "feed" : ""}">${sheetAt(x.d, 0, fitScale(x.d, 0, 120, 170))}<span>Page ${num}</span></div>`;
  });
  const banner = sc.state === "jam" ? `<div class="scan-banner" role="alert"><span class="errdot"></span><span class="grow"><b>The scanner stopped: paper jam.</b> ${docsN} page${docsN === 1 ? "" : "s"} arrived. Clear the feeder, then keep them or start again.</span><button class="btn sm" type="button" data-act="scan-discard">Discard</button><button class="btn sm primary" type="button" data-act="scan-add">Keep ${docsN} page${docsN === 1 ? "" : "s"}</button></div>` : "";
  const empty = sc.n === 0 ? `<div class="scan-empty"><div><b>Load the feeder, then press Scan</b>Pages appear here as they arrive.<br>Office feeder holds up to 20 sheets.</div></div>` : "";
  const foot = sc.state === "idle" ? `<span class="note">Pages go to PDFs/Inbox and are read in English.</span><span class="grow"></span><button class="btn" type="button" data-act="close">Cancel</button><button class="btn primary lg" type="button" data-act="scan-start">${I.scan} Scan</button>`
    : sc.state === "scanning" ? `<span class="note" aria-live="polite">Scanning page ${shown.filter((x) => !x.sep).length + 1}…</span><span class="grow"></span><button class="btn" type="button" data-act="scan-stop">Stop</button>`
      : sc.state === "jam" ? `<span class="note">Waiting for you</span><span class="grow"></span><button class="btn" type="button" data-act="close">Close</button>`
        : `<span class="note">${docsN} document${docsN === 1 ? "" : "s"} · ${docsN} page${docsN === 1 ? "" : "s"}${sepN ? ` · ${sepN} separator sheet${sepN > 1 ? "s" : ""} skipped` : ""}</span><span class="grow"></span><button class="btn" type="button" data-act="scan-more">Scan more</button><button class="btn primary lg" type="button" data-act="scan-add">Add ${docsN} to Inbox</button>`;
  return `<div class="d-head"><h2>Scan</h2><span class="grow"></span><button class="ibtn" type="button" data-act="close" aria-label="Close">${I.winX}</button></div>
    <div class="scan-body">
      <div class="scan-pages" aria-label="Scanned pages">${banner}${empty}${pages}</div>
      <div class="scan-set">
        <div class="sf"><span class="l">Scanner <span class="status-ok"><span class="live"></span>Ready</span></span><div class="sel">${I.scan}<span class="grow">Office feeder <span class="sub">· network</span></span>${I.chevD}</div></div>
        <div class="sf"><span class="l">Profile</span><div class="sel"><span class="grow">${esc(sc.profile)}</span>${I.chevD}</div><button class="addtag" type="button" data-act="todo" data-msg="Save as profile" style="align-self:flex-start">${I.plus12}Save these settings as a profile</button></div>
        <div class="sf"><span class="l">Paper source</span>${seg("src", [["Auto", "Auto"], ["Flatbed", "Flatbed"], ["Feeder", "Feeder"], ["Duplex", "Both sides"]], sc.src)}</div>
        <div class="sf"><span class="l">Resolution</span>${seg("res", [["150", "150 dpi"], ["300", "300 dpi"], ["600", "600 dpi"]], sc.res)}</div>
        <div class="sf"><span class="l">Colour</span>${seg("color", [["Colour", "Colour"], ["Grey", "Grey"], ["BW", "Black & white"]], sc.color)}</div>
        <div class="sf"><span class="l">Paper size</span><div class="sel"><span class="grow">A4</span>${I.chevD}</div></div>
        <div class="disc"><div class="dh">${I.chevD} Clean up <span>2 on</span></div>
          <div class="chk"><span class="bx on">${I.check}</span>Skip blank pages</div><div class="chk"><span class="bx on">${I.check}</span>Straighten pages</div><div class="chk"><span class="bx"></span>Crop to page edges</div></div>
        <div class="disc"><div class="dh">${I.chevD} Split into documents</div>
          ${seg("split", [["one", "Keep as one"], ["sep", "At separators"], ["asn", "At ASN labels"]], sc.split)}</div>
      </div>
    </div>
    <div class="d-foot">${foot}</div>`;
}
function scanStep() {
  const sc = S.scan; if (!sc || sc.state !== "scanning") return;
  sc.n++;
  if (sc.jam && sc.n === 3) { sc.state = "jam"; refreshScan(); return; }
  if (sc.n >= SCAN_FEED.length) { sc.state = "done"; refreshScan(); return; }
  refreshScan(); later(scanStep, motionOK() ? 720 : 300);
}
function refreshScan() { const dlg = $(".dialog.scan"); if (dlg) dlg.innerHTML = scanHTML(); const sp = $(".scan-pages"); if (sp) sp.scrollTop = sp.scrollHeight; }
function scanAdd() {
  const sc = S.scan, docs = SCAN_FEED.slice(0, sc.n).filter((x) => !x.sep);
  const names = ["Gas safety record", "Council tax bill 2026/27", "Plan renewal notice"];
  const before = DOCS.filter((d) => d.status === "inbox").length;
  docs.forEach((x, i) => DOCS.unshift({ id: "scan-" + i + "-" + (Date.now() % 9999), title: `Scan 00${46 + i}`, from: "", type: "", date: "", added: "Today, 09:5" + i, source: "Scanner · Office feeder", tags: [], status: "inbox", pages: x.d.pages, size: "—", path: `PDFs/Inbox/2026-11-20 Scan 00${46 + i}.pdf`, excerpt: "", processing: true, progress: 6, stage: "Reading text…", pending: null, deadline: null, note: "", _arriving: true, spec: { title: names[i] || "Scan", pending: { from: "", type: "Letter", date: "", tags: [], why: "From the document text" } } }));
  S.overlay = null; S.scan = null; renderOverlay();
  S.view = "inbox"; renderSidebar(); renderContent(); bumpInbox(before, before + docs.length); renderActBtn();
  toast({ text: `Added ${docs.length} scanned document${docs.length > 1 ? "s" : ""} to the Inbox` });
  const tick = () => {
    const proc = DOCS.filter((d) => d.processing); if (!proc.length) return;
    proc.forEach((d, i) => { d.progress = Math.min(100, d.progress + 9 + i * 2); if (d.progress > 55) d.stage = "Finding dates and senders…"; if (d.progress >= 100) { d.processing = false; d.title = d.spec.title; } });
    if (layoutName() === "review") updateProcRows(); renderSidebar(); renderActBtn(); if (proc.length) later(tick, 360);
  };
  later(tick, 600);
}

/* Settings */
const SECTIONS = [["general", "General", I.sliders], ["library", "Library", I.stack], ["intake", "Intake", I.inbox], ["scanning", "Scanning", I.scan], ["recognition", "Text recognition", I.ocr], ["filing", "Filing", I.tag], ["reminders", "Reminders", I.bell], ["models", "Local models", I.chat], ["advanced", "Advanced", I.warn]];
function srow(t, s, ctl) { return `<div class="srow"><div><div class="s1">${t}</div>${s ? `<div class="s2">${s}</div>` : ""}</div><div class="ctl">${ctl}</div></div>`; }
const tg = (on, label) => `<button class="toggle" type="button" role="switch" aria-checked="${on}" data-act="flip" aria-label="${esc(label)}"></button>`;
const selc = (v, w = 180) => `<div class="sel" style="width:${w}px"><span class="grow">${v}</span>${I.chevD}</div>`;
function settingsHTML() {
  const sec = S.settingsSection, p = S.platform, lib = p === "win" ? "C:\\Users\\Dana\\Documents\\Plico" : "~/Documents/Plico";
  const B = {
    general: `<h2>General</h2><p>How Plico looks and behaves on this computer.</p>
      <div class="group">${srow("Appearance", "Follows your system unless you choose otherwise.", seg("appearance", [["system", "System"], ["light", "Light"], ["dark", "Dark"]], "system"))}${srow("Language", "Takes effect the next time Plico opens.", selc("English", 140))}${srow("After marking a document reviewed", "", selc("Open the next one", 180))}${srow("Show keyboard hints on buttons", "", tg(true, "Keyboard hints"))}</div>`,
    library: `<h2>Library</h2><p>Your library is an ordinary folder. You can open, copy and back it up with any tool.</p>
      <div class="group">${srow("Location", `<code>${esc(lib)}</code>`, `<button class="btn sm" type="button" data-act="todo" data-msg="Change location">Change…</button><button class="btn sm" type="button" data-act="todo" data-msg="Show in folder">Show</button>`)}${srow("Back up", "A dated copy with PDFs, Trash, version history and saved searches. Last backup 3 days ago.", `<button class="btn sm" type="button" data-act="todo" data-msg="Back up now">Back up now…</button>`)}${srow("Trash", "Keeps deleted PDFs until you empty it.", `<button class="btn sm" type="button" data-act="nav" data-view="trash">Open Trash</button>`)}</div>`,
    intake: `<h2>Intake</h2><p>Where new paperwork comes from and where it lands.</p>
      <div class="group"><div class="gh">New documents</div>${srow("File new PDFs in", "Added files, drops and direct scans land here and appear in the Inbox.", selc("PDFs/Inbox", 170))}${srow("Also file watched scans there", "", tg(true, "Include watched scans"))}</div>
      <div class="group"><div class="gh">Watched folders</div>${srow(`<span style="display:inline-flex;gap:8px;align-items:center">${I.watch}Scans folder</span>`, `<code>${p === "win" ? "C:\\Users\\Dana\\Scans" : "~/Scans"}</code> · watching`, `<button class="btn sm" type="button" data-act="todo" data-msg="Import existing files">Import existing files</button><button class="btn sm" type="button" data-act="todo" data-msg="Pause">Pause</button>`)}${srow(`<span style="display:inline-flex;gap:8px;align-items:center">${I.phone}Phone inbox</span>`, `A folder your phone's scanner app saves to · paused · <a href="#" data-act="todo" data-msg="Phone inbox guide" style="color:inherit">How to set it up</a>`, `<button class="btn sm" type="button" data-act="todo" data-msg="Resume">Resume</button>`)}${srow("Catch up when this library opens", "Checks watched folders for scans saved while Plico was closed.", tg(true, "Catch up on open"))}${srow("", "", `<button class="btn sm" type="button" data-act="todo" data-msg="Watch another folder">${I.plus12} Watch another folder…</button>`)}</div>
      <div class="group"><div class="gh">Other files</div>${srow("Office documents", "Word, Excel, PowerPoint and OpenDocument files need LibreOffice on this computer.", `<span class="s2" style="color:var(--warn)">Not installed</span>`)}</div>`,
    scanning: `<h2>Scanning</h2><p>Scanners, profiles and the sheets Plico can read while scanning.</p>
      <div class="group">${srow("Office feeder", "Network · eSCL · remembered", `<button class="btn sm" type="button" data-act="todo" data-msg="Forget">Forget</button>`)}${srow("Flatbed", "USB · " + (p === "win" ? "WIA" : p === "mac" ? "Image Capture" : "SANE"), "")}${srow("", "", `<button class="btn sm" type="button" data-act="todo" data-msg="Add by address">Add by address…</button>`)}</div>
      <div class="group"><div class="gh">Profiles</div>${srow("Letters and bills", "Feeder · 300 dpi · grey · skip blanks · split at separators", `<button class="btn sm" type="button" data-act="todo" data-msg="Edit profile">Edit</button>`)}${srow("Receipts", "Two-sided · 300 dpi · grey · crop", `<button class="btn sm" type="button" data-act="todo" data-msg="Edit profile">Edit</button>`)}</div>
      <div class="group"><div class="gh">Sheets</div>${srow("Read archive number labels", "A label starts a new document and stays on its first page.", tg(false, "Read ASN labels"))}${srow("Print", "", `<button class="btn sm" type="button" data-act="todo" data-msg="Separator sheet">Separator sheet…</button><button class="btn sm" type="button" data-act="todo" data-msg="ASN labels">ASN labels…</button>`)}</div>`,
    recognition: `<h2>Text recognition</h2><p>Plico reads text on this computer. Nothing is uploaded.</p>
      <div class="group">${srow("Language", "Used for new scans, imports and searchable copies.", selc("English", 150))}${srow("Engine", "RapidOCR handles Latin-script text on Windows, Linux and Apple silicon.", seg("engine", [["t", "Tesseract"], ["r", "RapidOCR"]], "t"))}${srow("Turn sideways pages upright", "When orientation detection is confident.", tg(true, "Orient pages"))}</div>`,
    filing: `<h2>Filing</h2><p>The lists and rules behind suggestions.</p>
      <div class="group">${srow("Tag rules", "8 rules suggest tags from a phrase or words in the text.", `<button class="btn sm" type="button" data-act="todo" data-msg="Edit rules">Edit rules…</button>`)}${srow("Learn from reviewed documents", "Suggestions improve as you file.", tg(true, "Learn"))}</div>
      <div class="group">${srow("Senders", "12 senders, with aliases", `<button class="btn sm" type="button" data-act="todo" data-msg="Senders">Manage…</button>`)}${srow("Document types", "9 types", `<button class="btn sm" type="button" data-act="todo" data-msg="Types">Manage…</button>`)}${srow("Tags", "12 tags, colours and parents", `<button class="btn sm" type="button" data-act="todo" data-msg="Tags">Manage…</button>`)}${srow("Custom fields", "Deposit, policy number", `<button class="btn sm" type="button" data-act="todo" data-msg="Fields">Manage…</button>`)}</div>`,
    reminders: `<h2>Reminders</h2><p>Due, renewal, expiry and keep-until dates on your documents.</p>
      <div class="group">${srow("Remind me", "Default for new reminders", selc("7 days before", 150))}${srow("Calendar file", "An iCalendar file with every active date, for your calendar app.", `<button class="btn sm" type="button" data-act="todo" data-msg="Export calendar">Export…</button>`)}</div>`,
    models: `<h2>Local models</h2><p>Optional. Semantic search, filing suggestions and questions use a model server on this computer. Plico sends excerpts only when you ask.</p>
      <div class="group">${srow("Use a local model server", "", tg(false, "Local models"))}${srow("Server address", "<code>http://127.0.0.1:11434</code>", `<button class="btn sm" type="button" data-act="todo" data-msg="Test">Test</button>`)}${srow("Embedding model", "", selc("Not set", 160))}${srow("Chat model", "", selc("Not set", 160))}</div>`,
    advanced: `<h2>Advanced</h2><p>Maintenance for when files change outside Plico.</p>
      <div class="group">${srow("Refresh index", "Picks up PDFs added, edited, moved or removed in the library folder.", `<button class="btn sm" type="button" data-act="todo" data-msg="Refresh">Refresh</button>`)}${srow("Index report", "1 PDF is unavailable. Restore it from a backup or a folder.", `<button class="btn sm" type="button" data-act="todo" data-msg="Index report">Open report…</button>`)}${srow("Check integrity", "Compares every file with its recorded checksum.", `<button class="btn sm" type="button" data-act="todo" data-msg="Check">Check…</button>`)}${srow("Export catalog", "Titles, tags, dates and fields as JSON.", `<button class="btn sm" type="button" data-act="todo" data-msg="Export">Export…</button>`)}</div>`,
  };
  return `<nav class="set-nav" aria-label="Settings sections"><div class="ttl">${I.sliders} Settings</div>${SECTIONS.map(([id, l, ic]) => `<button class="nav-i" type="button" data-act="set-sec" data-sec="${id}" aria-current="${sec === id}">${ic}<span class="lbl">${l}</span></button>`).join("")}</nav>
    <div class="set-body">${B[sec]}</div><button class="ibtn set-x" type="button" data-act="close" aria-label="Close settings">${I.winX}</button>`;
}
function openSettings(sec) { if (sec) S.settingsSection = sec; openOverlay("settings"); }

/* Welcome */
function welcomeHTML() {
  const lib = S.platform === "win" ? "C:\\Users\\Dana\\Documents\\Plico" : "~/Documents/Plico";
  return `<div class="welcome" role="dialog" aria-label="Welcome">
    <div class="wtop"><div class="lights" style="${S.platform === "mac" ? "display:flex" : ""}"><i></i><i></i><i></i></div><span></span><div class="caps" style="${S.platform === "win" ? "display:flex" : ""}"><button type="button" aria-label="Minimise">${I.winMin}</button><button type="button" aria-label="Maximise">${I.winMax}</button><button type="button" class="x" aria-label="Close">${I.winX}</button></div></div>
    <div class="wbody"><div class="wcol">
      <img class="icon" alt="" src="${ICON_DARK}">
      <h1>Welcome to Plico</h1>
      <p class="lede">Plico turns receipts, letters and forms into searchable PDFs in a folder on this computer. You review each one once, and find it again by anything it says.</p>
      <div class="wstep"><h3><span class="n">1</span>Where your library lives</h3>
        <div class="loc">${I.folder16}<div style="flex:1;min-width:0"><code>${esc(lib)}</code><div class="s2">An ordinary folder of PDFs. Back it up or move it like any other folder.</div></div><button class="btn sm" type="button" data-act="todo" data-msg="Choose another folder">Change…</button></div></div>
      <div class="wstep"><h3><span class="n">2</span>Bring in your first paperwork</h3>
        <div class="ways">
          <button class="way" type="button" data-act="welcome-go" data-then="addfiles">${I.upload}<b>Add files</b><span>PDFs, photos, scans, emails and Office documents.</span></button>
          <button class="way" type="button" data-act="welcome-go" data-then="scan">${I.scan}<b>Scan paper</b><span>From a USB or network scanner, with a feeder or flatbed.</span></button>
          <button class="way" type="button" data-act="welcome-go" data-then="activity">${I.watch}<b>Watch a folder</b><span>Where your scanner software or phone app already saves.</span></button>
          <button class="way" type="button" data-act="welcome-go" data-then="addmenu">${I.share}<b>Move from another app</b><span>Paperless-ngx, Evernote or Mariner Paperless, with tags and dates.</span></button>
        </div></div>
      <div class="privacy">${I.lock}<span>Text recognition, search and suggestions run on this computer, and Plico works offline. It reaches your local network only to find scanners.</span></div>
      <div><button class="btn primary lg" type="button" data-act="welcome-go">Open Plico</button></div>
    </div></div></div>`;
}

/* Keyboard shortcuts sheet */
function shortcutsHTML() {
  const g = (title, rows) => `<div><h3 style="font-size: 13px;color:var(--ink-3);margin:0 0 8px;font-weight:600">${title}</h3>${rows.map(([k, l]) => `<div style="display:flex;justify-content:space-between;align-items:center;height:30px;border-top:1px solid var(--line);font-size: 14px"><span>${l}</span><span style="display:flex;gap:3px">${k.split(" ").map(K).join("")}</span></div>`).join("")}</div>`;
  return `<div class="d-head"><h2>Keyboard shortcuts</h2><span class="grow"></span><button class="ibtn" type="button" data-act="close" aria-label="Close">${I.winX}</button></div>
    <div style="flex:1;overflow:auto;padding:18px 22px;display:grid;grid-template-columns:1fr 1fr;gap:24px">
      ${g("Reviewing", [["j k", "Next and previous"], ["r", "Mark reviewed and open the next"], ["shift+r", "Mark reviewed and stay"], ["a", "Accept all suggestions"], ["t", "Add a tag"], ["d", "Set the document date"], ["f", "Set the sender"], ["n", "Add a note"], ["e", "Rename"], ["x", "Move to Trash"], ["o", "Open in your PDF viewer"], ["space", "Zoom the page"]])}
      ${g("Everywhere", [["mod+k", "Commands"], ["/", "Search"], ["mod+z", "Undo"], ["mod+a", "Select all in this view"], ["shift+j", "Extend the selection"], ["esc", "Clear selection or close"], ["mod+o", "Add files"], ["mod+shift+s", "Scan"], ["mod+i", "Show details"], ["mod+,", "Settings"], ["g", "Grid or list"], ["?", "This sheet"]])}
    </div>`;
}

/* Quick look grows from the card */
function qlHTML() {
  const d = doc(S.cur); if (!d) return "";
  const s = fitScale(d, 0, 900, 690);
  return `<div class="ql" role="dialog" aria-label="Quick look"><div class="ql-scrim" data-act="close"></div>${sheetAt(d, 0, s, "sheet", ";position:relative;z-index:1")}
    <div class="ql-cap"><div><b>${esc(d.title)}</b><span>${esc(d.from || "")}${d.date ? " · " + esc(d.date) : ""}</span><button class="btn sm" type="button" data-act="open" data-id="${d.id}">Open ${K("enter")}</button><button class="btn sm ghost" type="button" data-act="close">Close ${K("space")}</button></div></div></div>`;
}
function qlFrom() { const card = $(`.card[data-id="${S.cur}"] .thumb`); return card ? stageRect(card) : null; }
function qlIn() {
  const sh = $(".ql > .sheet"), a = qlFrom(); if (!sh || !a || !motionOK()) return;
  const b = stageRect(sh);
  sh.animate([{ transform: `translate(${a.x - b.x}px, ${a.y - b.y}px) scale(${a.w / b.w})`, transformOrigin: "0 0" }, { transform: "none", transformOrigin: "0 0" }], { duration: 260, easing: "cubic-bezier(0.2, 0, 0, 1)" });
}
function qlOut() {
  const sh = $(".ql > .sheet"), a = qlFrom();
  const done = () => { S.overlay = null; renderOverlay(); };
  if (!sh || !a || !motionOK()) { done(); return; }
  const b = stageRect(sh);
  $(".ql-scrim").animate([{ opacity: 1 }, { opacity: 0 }], { duration: 200, fill: "forwards" });
  $(".ql-cap").style.display = "none";
  sh.animate([{ transform: "none", transformOrigin: "0 0" }, { transform: `translate(${a.x - b.x}px, ${a.y - b.y}px) scale(${a.w / b.w})`, transformOrigin: "0 0" }], { duration: 200, easing: "cubic-bezier(0.4, 0, 1, 1)", fill: "forwards" }).onfinish = done;
}

/* Page editor */
const PE_DOC = { id: "scan45", title: "Scan 0045", pages: [P.leaseLetter, P.leaseForm, P.insurance, P.dental] };
function peHTML() {
  const pe = S.pe;
  const pages = PE_DOC.pages.map((_, i) => i).filter((i) => !pe.deleted.has(i));
  return `<section class="pe" aria-label="Edit pages">
    <div class="pe-bar"><span class="t">Edit pages</span><span style="color:var(--ink-3);font-size: 13px">${esc(PE_DOC.title)} · ${pages.length} pages · ${pe.sel.size} selected</span><span class="grow"></span>
      <button class="ibtn" type="button" data-act="pe-rot" data-d="-90" title="Rotate left ([)" aria-label="Rotate left">${I.rotL}</button><button class="ibtn" type="button" data-act="pe-rot" data-d="90" title="Rotate right (])" aria-label="Rotate right">${I.rotR}</button><button class="ibtn" type="button" data-act="pe-del" title="Delete (Del)" aria-label="Delete pages">${I.trash}</button>
      <span class="vsep"></span><button class="btn sm" type="button" data-act="todo" data-msg="Extract as a new document">${I.split} Extract</button><button class="btn sm" type="button" data-act="todo" data-msg="Split after this page">Split here</button>
      <span class="vsep"></span><button class="btn sm ghost" type="button" data-act="nav" data-view="inbox">Cancel</button><button class="btn sm primary" type="button" data-act="pe-save">Save as new version</button></div>
    <div class="pe-grid">${pages.map((i) => `<button class="pe-p" type="button" data-act="pe-select" data-i="${i}" aria-selected="${pe.sel.has(i)}">${sheetAt(PE_DOC, i, fitScale(PE_DOC, i, 168, 238), "sheet", `;transform:rotate(${pe.rot[i]}deg)`)}<span>${pages.indexOf(i) + 1}${i === 1 && pe.rot[1] % 360 === 180 ? ` · <span style="color:var(--warn)">Looks upside down</span>` : ""}</span></button>`).join("")}</div>
  </section>`;
}
function peRotate(delta) {
  const pe = S.pe; if (!pe.sel.size) return;
  pe.sel.forEach((i) => { pe.rot[i] += delta; const el = $(`.pe-p[data-i="${i}"] .sheet`); if (el) el.style.transform = `rotate(${pe.rot[i]}deg)`; });
  later(() => { const c = $(".pe"); if (c) c.outerHTML = peHTML(); }, motionOK() ? 280 : 0);
}

/* ───────── Click handling ───────── */
const ACT = {
  nav: (el) => setView(el.dataset.view),
  saved: (el) => { const s = SAVED.find((x) => x.name === el.dataset.name); S.q = { text: s.text, tokens: s.tokens.map((t) => ({ ...t })) }; runSearch(); S.savedName = s.name; renderSidebar(); renderContent(); },
  row: (el, e) => {
    const id = el.dataset.id, list = viewDocs();
    if (e.shiftKey && S.cur) { const a = list.findIndex((d) => d.id === (S.anchor || S.cur)), b = list.findIndex((d) => d.id === id); S.sel = new Set(list.slice(Math.min(a, b), Math.max(a, b) + 1).map((d) => d.id)); S.cur = id; }
    else if (e.ctrlKey || e.metaKey) { if (!S.sel.size && S.cur) S.sel.add(S.cur); S.sel.has(id) ? S.sel.delete(id) : S.sel.add(id); S.cur = id; S.anchor = id; }
    else { S.sel.clear(); S.cur = id; S.anchor = id; }
    if (S.sel.size === 1) S.sel.clear();
    S.editing = null; S.matchIdx = 0;
    if (S.screen === "pages") { S.screen = "inbox"; renderContent(); return; }
    renderRows(); renderViewer(); renderInsp();
  },
  card: (el) => { S.cur = el.dataset.id; syncCards(); },
  open: (el) => openDoc(el.dataset.id),
  layout: (el) => { S.layout = el.dataset.l; renderContent(); },
  info: () => { S.info = !S.info; renderContent(); },
  openext: () => toast({ text: `Opens in ${S.platform === "win" ? "your default PDF app" : S.platform === "mac" ? "Preview or your default PDF app" : "your default PDF viewer"}` }),
  reveal: () => toast({ text: `Shows the file in ${S.platform === "win" ? "File Explorer" : S.platform === "mac" ? "Finder" : "Files"}` }),
  page: (el) => { const vs = $("#vscroll"); const sheets = $$(".sheet", vs); if (!sheets.length) return; const n = +el.dataset.d; const cur = sheets.findIndex((s) => s.offsetTop + s.offsetHeight / 2 > vs.scrollTop + 60); const t = sheets[Math.max(0, Math.min(sheets.length - 1, cur + n))]; vs.scrollTop = t.offsetTop - 40; },
  zoom: (el) => { S.zoom = el.getAttribute("aria-label") === "Zoom in" ? (S.zoom === "fit" ? "width" : "150") : (S.zoom === "150" ? "width" : "fit"); renderViewer(); },
  zoomcycle: () => { S.zoom = S.zoom === "fit" ? "width" : S.zoom === "width" ? "150" : "fit"; renderViewer(); },
  readtext: () => readText(),
  match: (el) => { const marks = $$("#vscroll mark.ph"); if (!marks.length) return; S.matchIdx = (S.matchIdx + +el.dataset.d + marks.length) % marks.length; marks[S.matchIdx].scrollIntoView({ block: "center" }); const m = $(".pagebar .match"); if (m) m.textContent = `${S.matchIdx + 1} of ${marks.length}`; },
  edit: (el) => { if (S.editing !== el.dataset.k) startEdit(el.dataset.k); },
  accept: (el) => acceptOne(el.dataset.k),
  "accept-tag": (el) => acceptOne(null, el.dataset.t),
  "accept-all": () => acceptAll(),
  untag: (el) => { const d = doc(S.cur); const t = el.dataset.t; d.tags = d.tags.filter((x) => x !== t); renderInsp(); pushUndo(() => { d.tags.push(t); renderInsp(); }); toast({ text: `Removed ${t}`, undo: true }); },
  ac: (el) => commitField(el.dataset.v),
  review: () => review(),
  trash: () => trashDoc(),
  restore: () => { const d = doc(S.cur); d.status = "reviewed"; renderAll(); toast({ text: "Restored", dim: d.title }); },
  unreview: () => { const d = doc(S.cur); d.status = "inbox"; renderSidebar(); renderInsp(); toast({ text: "Returned to Inbox", dim: d.title }); },
  docmenu: () => openOverlay("docmenu"),
  addmenu: () => openOverlay("addmenu"),
  libmenu: () => openOverlay("libmenu"),
  activity: () => openOverlay(S.overlay === "activity" ? null : "activity"),
  shortcuts: () => openOverlay("shortcuts"),
  settings: (el) => openSettings(el.dataset.sec),
  "set-sec": (el) => { S.settingsSection = el.dataset.sec; $(".dialog.settings").innerHTML = settingsHTML(); },
  scan: () => openScan(),
  addfiles: () => { S.overlay = null; renderOverlay(); toast({ text: `Your system's file picker opens here. Try dragging real files onto the window instead.` }); },
  close: () => closeOverlay(),
  noop: () => {},
  undo: () => undo(),
  todo: (el) => { if (S.overlay && S.overlay !== "settings" && S.overlay !== "activity" && S.overlay !== "scan") { S.overlay = null; renderOverlay(); } notMocked(el.dataset.msg || "This"); },
  goto: (el) => go(el.dataset.s),
  "focus-search": () => $("#q")?.focus(),
  untok: (el, e) => { e.stopPropagation(); S.q.tokens.splice(+el.dataset.i, 1); refreshTokens(); if (S.view === "search" && !S.searchOpen) { renderContent(); } },
  sopt: (el) => pickSearchOpt(searchOpts()[+el.dataset.i]),
  fchip: (el) => {
    const k = el.dataset.k, r = stageRect(el);
    const vals = k === "status" ? ["inbox", "reviewed"] : k === "tag" ? TAGS.map((t) => t.name) : k === "from" ? CORRESPONDENTS : k === "dated" ? ["2026", "2025", "2026-11"] : k === "type" ? DOC_TYPES : ["missing", "present"];
    S.fmenu = { x: r.x, y: r.y + r.h + 4, items: [{ h: el.textContent.trim() }, ...vals.slice(0, 9).map((v) => ({ label: v, act: "fpick", data: `data-k="${k}" data-v="${esc(v)}"`, icon: k === "tag" ? `<span style="width:8px;height:8px;border-radius:50%;background:${tagColor(v)};margin:0 4px"></span>` : "" }))] };
    openOverlay("fmenu");
  },
  fpick: (el) => { const k = el.dataset.k, v = el.dataset.v; S.q.tokens = S.q.tokens.filter((t) => t.k !== k || k === "tag"); S.q.tokens.push({ k, v }); S.overlay = null; renderOverlay(); runSearch(); },
  "save-search": () => { const name = S.q.text ? S.q.text[0].toUpperCase() + S.q.text.slice(1) : S.q.tokens.map((t) => t.v).join(" "); if (!name) { toast({ text: "Search for something first, then save it." }); return; } SAVED.push({ name, text: S.q.text, tokens: S.q.tokens.map((t) => ({ ...t })) }); S.savedName = name; renderSidebar(); renderContent(); toast({ text: `Saved “${name}” to the sidebar` }); },
  sort: () => notMocked("Sort options"),
  "pal-run": (el) => runPal(+el.dataset.i),
  "watch-toggle": () => { S.watch = S.watch === false; $("#activity").innerHTML = activityHTML(); toast({ text: S.watch === false ? "Paused the scans folder" : "Watching the scans folder" }); },
  flip: (el) => el.setAttribute("aria-checked", String(el.getAttribute("aria-checked") !== "true")),
  seg: (el) => { const g = el.dataset.g; if (S.scan && g in { src: 1, res: 1, color: 1, split: 1 }) { S.scan[g] = el.dataset.v; refreshScan(); } else { $$(`[data-g="${g}"]`).forEach((b) => b.setAttribute("aria-pressed", String(b === el))); } },
  "scan-start": () => { S.scan.state = "scanning"; S.scan.n = 0; refreshScan(); later(scanStep, 450); },
  "scan-stop": () => { S.scan.state = "done"; refreshScan(); },
  "scan-more": () => { S.scan.state = "scanning"; refreshScan(); later(scanStep, 450); },
  "scan-add": () => scanAdd(),
  "scan-discard": () => { S.scan = newScan(); refreshScan(); toast({ text: "Discarded the captured pages" }); },
  "drop-now": () => doDrop(),
  "welcome-go": (el) => { const then = el.dataset.then; go("inbox"); if (then) later(() => (ACT[then] || (() => openOverlay(then)))(), 60); },
  "pe-select": (el, e) => { const i = +el.dataset.i; if (e.shiftKey || e.ctrlKey || e.metaKey) S.pe.sel.has(i) ? S.pe.sel.delete(i) : S.pe.sel.add(i); else S.pe.sel = new Set([i]); $$(".pe-p").forEach((p) => p.setAttribute("aria-selected", String(S.pe.sel.has(+p.dataset.i)))); },
  "pe-rot": (el) => peRotate(+el.dataset.d),
  "pe-del": () => { const n = S.pe.sel.size; S.pe.sel.forEach((i) => S.pe.deleted.add(i)); S.pe.sel.clear(); $(".pe").outerHTML = peHTML(); toast({ text: `Deleted ${n} page${n > 1 ? "s" : ""}`, undo: true }); pushUndo(() => { S.pe.deleted.clear(); $(".pe").outerHTML = peHTML(); }); },
  "pe-save": () => { toast({ text: "Saved as version 2. The original stays in version history." }); },
};
function setView(v) {
  S.view = v; S.savedName = null; S.sel.clear(); S.editing = null; S.matchIdx = 0; S.q = { text: "", tokens: [] };
  if (S.screen === "pages") S.screen = "inbox";
  const list = viewDocs(); if (!list.find((d) => d.id === S.cur)) S.cur = list[0] ? list[0].id : null;
  S.overlay = null; renderOverlay(); renderSidebar(); renderToolbar(); renderContent();
}
function openDoc(id) {
  const d = doc(id); if (!d) return;
  S.overlay = null; renderOverlay();
  if (d.status === "inbox") { S.view = "inbox"; } else if (S.view === "inbox" || S.view === "upcoming" || !viewDocs().includes(d)) { S.view = "all"; }
  S.layout = "list"; S.cur = id; S.sel.clear(); S.savedName = null;
  renderSidebar(); renderContent();
}
function readText() {
  const d = doc(S.cur), vs = $("#vscroll"); if (!d || !vs) return;
  if (vs.dataset.text) { renderViewer(); return; }
  vs.dataset.text = "1";
  vs.innerHTML = `<article style="background:var(--surface);color:var(--ink-1);max-width:520px;width:100%;padding:28px 30px;border-radius:8px;box-shadow:var(--paper-shadow);font-size: 15px;line-height:1.65;user-select:text">${d.noText ? `<p style="color:var(--ink-3)">This page has no searchable text yet.</p>` : `<p style="font-size: 13px;color:var(--ink-3);margin:0 0 10px">Page 1 · selectable text</p><p style="margin:0">${esc(docText(d))}</p>`}</article>`;
}
function ocrOne() {
  const d = doc(S.cur); if (!d) return;
  const n = $("#notext"); if (n) n.innerHTML = `${I.ocr}<span>Reading text…</span><div class="meter" style="width:120px"><i style="width:10%" id="ocr-m"></i></div>`;
  let p = 10;
  const step = () => {
    p += 18; const m = $("#ocr-m"); if (m) m.style.width = Math.min(p, 100) + "%";
    if (p < 100) { later(step, 260); return; }
    d.noText = false; d.title = "Boiler service note"; d._text = null;
    d.pending = { from: "Harbourside Heating", type: "Note", date: "13 Nov 2026", tags: ["Housing"], deadline: "Service due Oct 2027", why: "From the handwriting and your gas safety record" };
    renderRows(); renderViewer(); renderInsp(["from", "type", "date", "tags", "deadline"]);
    toast({ text: "Made searchable. The original scan is kept." });
  };
  later(step, 260);
}
ACT["ocr-one"] = ocrOne;

/* ───────── Keyboard ───────── */
document.addEventListener("keydown", (e) => {
  if (S.screen === "system") return;
  const t = e.target, k = e.key, lk = k.length === 1 ? k.toLowerCase() : k, mod = e.metaKey || e.ctrlKey;
  if (t.closest && t.closest(".studio-bar, .screens, .notes") && (k === " " || k === "Enter")) return;
  if (mod && lk === "k") { e.preventDefault(); openOverlay(S.overlay === "palette" ? null : "palette"); if (S.overlay === "palette") S.pal = { q: "", idx: 0 }; return; }
  if (S.overlay === "palette") return;
  if (S.overlay === "ql") { e.preventDefault(); if (k === " " || k === "Escape") closeOverlay(); else if (k === "Enter") openDoc(S.cur); else if (k === "ArrowRight" || lk === "j") { move(1); S.overlay = "ql"; renderOverlay(); } else if (k === "ArrowLeft" || lk === "k") { move(-1); S.overlay = "ql"; renderOverlay(); } return; }
  if (S.overlay === "drop") { if (k === "Escape") { S.overlay = null; renderOverlay(); } else if (k === "Enter") doDrop(); return; }
  if (S.overlay === "welcome") return;
  if (S.overlay) { if (k === "Escape") { e.preventDefault(); closeOverlay(); } return; }
  const typing = t.matches && t.matches("input, textarea");
  if (typing) return;
  if (t.matches && t.matches("button") && t.closest("#win") && (k === " " || k === "Enter")) return;
  if (mod && lk === "z") { e.preventDefault(); undo(); return; }
  if (mod && lk === "a") { e.preventDefault(); selectAll(); return; }
  if ((mod && lk === "f") || k === "/") { e.preventDefault(); $("#q")?.focus(); return; }
  if (mod && e.shiftKey && lk === "s") { e.preventDefault(); openScan(); return; }
  if (mod && lk === "o") { e.preventDefault(); ACT.addfiles(); return; }
  if (mod && lk === ",") { e.preventDefault(); openSettings(); return; }
  if (mod && lk === "i") { e.preventDefault(); if (layoutName() === "list") ACT.info(); return; }
  if (mod || e.altKey) return;
  const lay = layoutName();
  if (S.screen === "pages") {
    if (k === "[" || k === "]") { e.preventDefault(); peRotate(k === "]" ? 90 : -90); }
    else if (k === "Delete" || k === "Backspace") { e.preventDefault(); ACT["pe-del"](); }
    else if (k === "Escape") setView("inbox");
    return;
  }
  const handled = (() => {
    if (lay === "grid") {
      const cols = (() => { const c = $(".cards"); return c ? getComputedStyle(c).gridTemplateColumns.split(" ").length : 4; })();
      if (k === "ArrowRight" || lk === "j") return move(1), true;
      if (k === "ArrowLeft" || lk === "k") return move(-1), true;
      if (k === "ArrowDown") return move(cols), true;
      if (k === "ArrowUp") return move(-cols), true;
      if (k === " ") return S.cur && openOverlay("ql"), true;
      if (k === "Enter") return S.cur && openDoc(S.cur), true;
      if (lk === "g") return ACT.layout({ dataset: { l: "list" } }), true;
    }
    switch (lk) {
      case "j": case "ArrowDown": move(1, e.shiftKey); return true;
      case "k": case "ArrowUp": move(-1, e.shiftKey); return true;
      case "r": review(e.shiftKey); return true;
      case "x": case "Delete": trashDoc(); return true;
      case "a": acceptAll(); return true;
      case "t": startEdit("tags"); return true;
      case "d": startEdit("date"); return true;
      case "f": startEdit("from"); return true;
      case "n": startEdit("note"); return true;
      case "e": { const ti = $("#title-in"); if (ti) { ti.focus(); ti.select(); } return true; }
      case "o": ACT.openext(); return true;
      case " ": ACT.zoomcycle(); return true;
      case "?": openOverlay("shortcuts"); return true;
      case "g": if (S.view !== "inbox" && S.view !== "upcoming") { S.layout = S.layout === "grid" ? "list" : "grid"; renderContent(); } return true;
      case "Enter": if (lay === "list" && !S.info) { ACT.info(); } return true;
      case "Escape": if (S.sel.size) { S.sel.clear(); renderContent(); } else if (S.editing) { S.editing = null; renderInsp(); } return true;
      case "[": case "]": ACT.page({ dataset: { d: k === "]" ? 1 : -1 } }); return true;
    }
    return false;
  })();
  if (handled) e.preventDefault();
});

/* Click delegation */
const winEl = () => $("#win");
document.addEventListener("click", (e) => {
  const el = e.target.closest("[data-act]"); if (!el || !winEl().contains(el)) return;
  const fn = ACT[el.dataset.act]; if (fn) fn(el, e);
});
document.addEventListener("dblclick", (e) => { const c = e.target.closest(".card, .row"); if (c && winEl().contains(c)) openDoc(c.dataset.id); });

/* Real files dragged onto the window: only their names are used */
let dragDepth = 0;
document.addEventListener("dragenter", (e) => {
  if (!winEl().contains(e.target) || !e.dataTransfer || ![...e.dataTransfer.types].includes("Files")) return;
  e.preventDefault(); dragDepth++;
  if (S.overlay !== "drop") { S.dropCount = e.dataTransfer.items ? e.dataTransfer.items.length : 1; S.dropNames = null; openOverlay("drop"); }
});
document.addEventListener("dragover", (e) => { if (S.overlay === "drop") e.preventDefault(); });
document.addEventListener("dragleave", () => { if (S.overlay === "drop" && --dragDepth <= 0) { dragDepth = 0; S.overlay = null; renderOverlay(); } });
document.addEventListener("drop", (e) => {
  if (S.overlay !== "drop") return; e.preventDefault(); dragDepth = 0;
  const files = [...(e.dataTransfer.files || [])].map((f) => f.name).slice(0, 3);
  if (files.length) S.dropNames = files;
  doDrop();
});

/* ───────── Screens and notes ───────── */
const SCREENS = [
  ["inbox", "Review"], ["batch", "Batch"], ["zero", "Inbox clear"], ["search", "Search"], ["library", "Browse"], ["drop", "Drop to add"],
  ["scan", "Scan"], ["activity", "Activity"], ["palette", "Commands"], ["pages", "Edit pages"], ["settings", "Settings"], ["welcome", "First run"], ["system", "Design language"],
];
const NOTES = {
  inbox: { t: "Review the Inbox", lede: "One document at a time: the page in the middle and its filing card on the right. Plico proposes the sender, type, date, tags and a reminder from the text. You accept, correct and move on.",
    changed: ["The page gets the most room. Today it shares the window with about twenty buttons of equal weight.", "Suggestions sit in the fields they fill. A dashed value is a suggestion and a solid one is filed. One key accepts them all.", "Mark reviewed files the document and opens the next one at once. Nothing asks for confirmation, and the message at the bottom offers Undo.", "Rarer tools such as redact, stamp, versions and export move to the ⋯ menu and the command palette."],
    tryit: ["Press <kbd>A</kbd> to accept the suggestions, then <kbd>R</kbd>. The page settles into the tray and the Inbox count ticks down.", "Press <kbd>R</kbd> several times quickly. The animation steps aside when you work fast.", "<kbd>J</kbd> and <kbd>K</kbd> move through the queue. <kbd>T</kbd> adds a tag, <kbd>D</kbd> sets the date, <kbd>X</kbd> moves to Trash, and Ctrl+Z undoes.", "Open Scan 0043 and choose Make searchable."] },
  batch: { t: "Work on several at once", lede: "Shift-click, or press Shift+J, to select a run of documents. The filing card becomes a batch card with the same fields, and it shows counts where values differ.",
    changed: ["Batch tools stop squeezing under the list. They replace the filing card, where a single document's tools already live.", "Tags show how many of the selected documents carry each one.", "Every batch change can be undone. Only permanent deletion in Trash asks first."],
    tryit: ["Press <kbd>R</kbd> to mark all three reviewed, then Ctrl+Z.", "Press <kbd>Esc</kbd> to clear the selection.", "Ctrl+A selects everything in the current view."] },
  zero: { t: "Inbox clear", lede: "When the queue is empty, the window says so plainly and shows what comes next: dates read from the documents you just filed.",
    changed: ["Reaching zero has a visible end state instead of an empty list.", "Coming up draws on reminders and suggested deadlines. The same dates live under Upcoming in the sidebar.", "No streaks, confetti or scores. It's paperwork."],
    tryit: ["Open a document from Coming up.", "Press Scan or Add files to bring in more."] },
  search: { t: "Search the library", lede: "Search reads the text inside every PDF. Results show where the words appear, and the page marks each hit in amber, the one colour the interface keeps for search.",
    changed: ["Filters become tokens in the search field and chips under the title, so the query stays visible and editable.", "Typing suggests filters (tags, senders, types) and the best documents before you press Enter.", "The page bar steps through matches across pages.", "Save turns a search into a sidebar shortcut."],
    tryit: ["Press <kbd>/</kbd> and type <code>tag:</code> or <code>north</code>.", "Use the arrows beside the match count in the page bar.", "Open the Renewals saved search in the sidebar."] },
  library: { t: "Browse by sight", lede: "All documents as paper, grouped by the month printed on them. People recognise a bill by its shape faster than by its title.",
    changed: ["A grid for browsing. The list is one click away, or press <kbd>G</kbd>.", "Space opens a quick look that grows out of the thumbnail and shrinks back into it.", "Enter opens the document with its details."],
    tryit: ["Select a card, press <kbd>Space</kbd>, then use the arrow keys to page through.", "Press <kbd>G</kbd> to switch to the list."] },
  drop: { t: "Drop to add", lede: "Dragging files over the window shows where they will go. On drop, the sheet settles into the tray and the files appear at the top of the Inbox while Plico reads them.",
    changed: ["The destination folder is visible before you let go.", "Processing happens in the row with plain stage names: reading text, then finding dates and senders.", "A duplicate is explained in words and links to the copy you already have, instead of being skipped silently."],
    tryit: ["Click anywhere in the window to drop three sample files.", "Drag real files from your desktop onto the window. Only their names are used.", "Open Activity in the toolbar while they are read."] },
  scan: { t: "Scan", lede: "Scanning happens in one sheet: settings on the right, pages arriving on the left as the feeder pulls them in. Separator sheets split the stack into documents before anything reaches the Inbox.",
    changed: ["Source, resolution and colour are segmented controls you can read at a glance instead of dropdowns.", "The profile sits at the top because it sets everything else.", "A paper jam shows inside the same sheet, with the pages that arrived and a choice to keep or discard them, instead of a second dialog."],
    tryit: ["Press Scan.", `<button class="btn sm" type="button" data-note="jam">Simulate a paper jam</button> then press Scan.`, "Switch the platform to macOS: the dialog drops from the toolbar as a sheet."] },
  activity: { t: "Intake and activity", lede: "Everything Plico does in the background lives in one place: files being read, folders being watched, and anything that needs you.",
    changed: ["Watched folders, phone inbox, catch-up and import errors leave the sidebar's stack of buttons and move here and into Settings.", "The sidebar keeps one line per source with a live dot.", "Each problem says what happened and what to do next: install LibreOffice, retry, or open the copy you already have."],
    tryit: ["Watch the progress move while files are read.", "Pause the scans folder.", "Press <kbd>Esc</kbd> to close."] },
  palette: { t: "Every command, one key", lede: "Ctrl+K (⌘K on a Mac) reaches every action in Plico, including the forty or so that no longer need a permanent button. It understands other words for the same thing: “done” finds Mark reviewed and “ocr” finds Make searchable copy.",
    changed: ["Commands show their shortcut, so the palette teaches the keyboard as you use it.", "Context comes first. With a document open, its actions lead.", "Library maintenance, migrations and label sheets live here and in Settings."],
    tryit: ["Type <code>rot</code>, <code>asn</code>, <code>backup</code> or <code>done</code>.", "Use the arrows and Enter. Ctrl+K again closes it."] },
  pages: { t: "Edit pages", lede: "Rotate, reorder, delete, extract and split pages on one surface instead of five dialogs. Orientation detection flags the page that came out upside down.",
    changed: ["Pages are objects you work on directly: select, rotate, drag into order, delete with a key.", "Changes save as a new version, and the original stays in version history.", "Extract and Split create new Inbox documents and keep the source."],
    tryit: ["Page 2 is selected. Press <kbd>]</kbd> twice to turn it upright.", "Select a page and press Delete, then Ctrl+Z.", "Shift-click to select several pages."] },
  settings: { t: "Settings", lede: "Preferences leave the sidebar and the Library tools menu for a standard Settings window (Ctrl+,), grouped by what they affect.",
    changed: ["Intake folder, watched folders and phone inbox live together under Intake.", "Tag rules, senders, types, custom fields and tag colours live under Filing.", "Maintenance (refresh index, index report, integrity check) lives under Advanced, with plain explanations."],
    tryit: ["Switch sections in the left column.", "Press <kbd>Esc</kbd> to close."] },
  welcome: { t: "First run", lede: "The first window explains the one idea that matters, where the files live, and offers the ways to bring paperwork in. No tour.",
    changed: ["You choose the library location up front, with the plain promise that it is an ordinary folder.", "Moving from Paperless-ngx, Evernote or Mariner Paperless is a first-class way to start.", "Privacy is stated once, in one sentence, where it matters."],
    tryit: ["Choose any option, or Open Plico to see the Inbox."] },
  system: { t: "Design language", lede: "The rules behind these mockups, written so they can become Avalonia resources: colour tokens, type, geometry, motion and icons." , changed: [], tryit: [] },
};
function renderNotes() {
  const n = NOTES[S.screen];
  $("#notes").innerHTML = `<div><h2>${n.t}</h2><p>${n.lede}</p></div>
    ${n.changed.length ? `<div><h3>What changes</h3><ul>${n.changed.map((x) => `<li>${x}</li>`).join("")}</ul></div>` : "<div></div>"}
    ${n.tryit.length ? `<div class="try"><h3>Try it</h3><ul>${n.tryit.map((x) => `<li>${x}</li>`).join("")}</ul></div>` : "<div></div>"}`;
}
$("#notes").addEventListener("click", (e) => {
  const b = e.target.closest("[data-note]"); if (!b) return;
  if (b.dataset.note === "jam") { go("scan"); S.scan.jam = true; toast({ text: "The feeder will jam on the third sheet" }); }
});

function go(screen) {
  S.timers.forEach(clearTimeout); S.timers = []; clearInterval(S.actTick);
  freshDocs(); PAGECACHE.clear();
  Object.assign(S, { undo: [], sel: new Set(), editing: null, info: false, zoom: "fit", q: { text: "", tokens: [] }, searchOpen: false, savedName: null, overlay: null, reviewedToday: 0, matchIdx: 0, layout: "list", scan: null, dupes: [], watch: true, dropNames: null, view: "inbox", cur: "lease", anchor: null, screen });
  $("#toast-host").innerHTML = "";
  const sys = screen === "system";
  $("#stage").hidden = sys; $("#ds-host").hidden = !sys;
  if (!sys) fitStage();
  if (sys) { $("#ds-host").innerHTML = designSystemHTML(); renderNotes(); navState(); return; }
  switch (screen) {
    case "batch": S.cur = "energy-oct"; S.sel = new Set(["energy-oct", "insurance", "dental"]); break;
    case "zero": DOCS.filter((d) => d.status === "inbox").forEach((d) => { if (d.pending) { ["from", "type", "date", "deadline"].forEach((k) => { if (d.pending[k]) d[k] = d.pending[k]; }); d.tags.push(...(d.pending.tags || [])); d.pending = null; } d.status = "reviewed"; }); S.reviewedToday = 7; S.cur = null; break;
    case "search": S.view = "search"; S.q = { text: "renewal", tokens: [] }; S.cur = "lease"; break;
    case "library": S.view = "all"; S.layout = "grid"; S.cur = "energy-oct"; break;
    case "drop": S.overlay = "drop"; break;
    case "scan": S.scan = newScan(); S.overlay = "scan"; break;
    case "activity": S.dupes = []; break;
    case "palette": S.overlay = "palette"; S.pal = { q: "", idx: 0 }; break;
    case "pages": S.pe = { rot: [0, 180, 0, 0], sel: new Set([1]), deleted: new Set() }; break;
    case "settings": S.overlay = "settings"; S.settingsSection = "intake"; break;
    case "welcome": S.overlay = "welcome"; break;
  }
  renderAll(); renderNotes(); navState();
  if (screen === "activity") { arrive(null, "Scans folder"); later(() => openOverlay("activity"), 300); }
  try { localStorage.setItem("plico-mock-screen", screen); } catch (_) { /* storage is optional */ }
}
function navState() {
  $("#screens").innerHTML = SCREENS.map(([id, label], i) => `<button type="button" data-screen="${id}" aria-current="${S.screen === id}"><span class="n">${i + 1}</span>${label}</button>`).join("");
  if (location.hash.slice(1) !== S.screen) history.replaceState(null, "", "#" + S.screen);
}
$("#screens").addEventListener("click", (e) => { const b = e.target.closest("[data-screen]"); if (b) go(b.dataset.screen); });
window.addEventListener("hashchange", () => { const h = location.hash.slice(1); if (NOTES[h] && h !== S.screen) go(h); });

/* ───────── Studio controls ───────── */
function setTheme(t) {
  document.documentElement.dataset.theme = t;
  $$("[data-theme-set]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.themeSet === t)));
  try { localStorage.setItem("plico-mock-theme", t); } catch (_) { /* optional */ }
}
function effectiveTheme() { return document.documentElement.dataset.theme || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"); }
$("#ctl-theme").addEventListener("click", (e) => { const b = e.target.closest("[data-theme-set]"); if (b) setTheme(b.dataset.themeSet); });
$("#ctl-platform").addEventListener("click", (e) => {
  const b = e.target.closest("[data-platform-set]"); if (!b) return;
  S.platform = b.dataset.platformSet; $$("[data-platform-set]").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
  renderAll(); if (S.screen === "system") $("#ds-host").innerHTML = designSystemHTML();
});
$("#ctl-motion").addEventListener("click", (e) => {
  const b = e.target.closest("[data-motion-set]"); if (!b) return;
  S.motion = b.dataset.motionSet; document.documentElement.dataset.motion = S.motion;
  $$("[data-motion-set]").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
});

function fitStage() {
  const st = $("#stage"); if (st.hidden) return;
  const winW = Math.max(1120, Math.min(1440, st.clientWidth));
  $("#win").style.width = winW + "px";
  stageScale = Math.min(1, st.clientWidth / winW);
  $("#stage-inner").style.transform = `scale(${stageScale})`;
  st.style.height = Math.round(800 * stageScale) + "px";
}
window.addEventListener("resize", fitStage);

/* ───────── Design language page ───────── */
function designSystemHTML() {
  const sw = (name, token, note) => `<div><i style="background:var(${token})"></i><span>${name}</span><code>${token}${note ? " · " + note : ""}</code></div>`;
  return `<div class="ds">
    <section><h2>Concept</h2><p class="lead">Paper on a quiet desk. The interface is graphite and grey, borrowed from the miille tile, so the paperwork is the only bright object in the window and the only thing that casts a shadow. Colour appears only when it means something: amber for search hits, a tag's own colour, a live dot for a watched folder, and the system's focus ring.</p></section>
    <section><h2>Colour</h2><p class="lead">Every value is a token with a light and a dark definition. Dark mode is designed, not inverted: surfaces step up from near-black, and paper is dimmed ten percent so a white page doesn't glare.</p>
      <div class="sw">${sw("Window", "--surface")}${sw("Sidebar", "--sidebar")}${sw("Desk (behind pages)", "--canvas")}${sw("Ink", "--ink-1")}${sw("Secondary ink", "--ink-2")}${sw("Tertiary ink", "--ink-3", "4.5:1 minimum")}${sw("Line", "--line")}${sw("Selected row", "--sel")}${sw("Primary action", "--primary")}${sw("Search highlight", "--hl-ui", "UI")}${sw("Live / success", "--ok")}${sw("Due soon", "--warn")}${sw("Destructive", "--danger")}${sw("Focus ring", "--focus", "system accent")}</div></section>
    <div class="ds-grid2">
      <section><h2>Type</h2><p class="lead">Inter, already bundled with Avalonia, in a scale that never goes below 12 px. Hierarchy comes from weight and ink more than size. Numbers use tabular figures; paths and query syntax use the system monospace.</p>
        <div class="typescale">
          <div><code>Display 28/650 −2.5%</code><span style="font-size:28px;font-weight:650;letter-spacing:-0.025em">Inbox clear</span></div>
          <div><code>View title 22/650 −2%</code><span style="font-size:22px;font-weight:650;letter-spacing:-0.02em">All documents</span></div>
          <div><code>Card title 18/650</code><span style="font-size:18px;font-weight:650;letter-spacing:-0.015em">Tenancy renewal offer</span></div>
          <div><code>Body 14/400</code><span style="font-size:14px">Northstar Properties · 14 Nov 2026</span></div>
          <div><code>Label 13/400 ink-3</code><span style="font-size:13px;color:var(--ink-3)">Date on the paper</span></div>
          <div><code>Meta 12/600 ink-3</code><span style="font-size:12px;font-weight:600;color:var(--ink-3)">Saved searches</span></div>
          <div><code>Mono 12</code><span style="font:12px var(--font-mono);color:var(--ink-3)">PDFs/Inbox/2026-11-20 Scan 0041.pdf</span></div>
        </div></section>
      <section><h2>Geometry</h2><p class="lead">A 4 px grid. Controls are 30 px tall (26 small, 36 for the one primary action in a panel). Radii: 7 for controls, 10 for panels and popovers, 12 for dialogs, 1.5 for paper, because paper has crisp corners. Borders are 1 px. Shadows belong to paper and to floating surfaces only.</p>
        <div class="ds-row" style="margin-bottom:14px"><button class="btn primary" type="button">Mark reviewed <span class="kbd">R</span></button><button class="btn" type="button">Scan</button><button class="btn ghost" type="button">Cancel</button><button class="btn sm" type="button">Accept all <span class="kbd">A</span></button><button class="btn danger" type="button">Delete permanently</button></div>
        <div class="ds-row" style="margin-bottom:14px"><span class="gv" style="display:inline-flex">Northstar Properties</span><span class="tagc"><i style="background:#3f6fb0"></i>Housing</span><span class="gv tagg" style="display:inline-flex"><i style="background:#c2652a"></i>Bills</span><span class="tok"><i>tag:</i>Tax</span><span class="fchip on">Status: inbox</span></div>
        <p class="lead" style="margin:0">Dashed means suggested; solid means filed. That single rule carries the whole suggestion system.</p></section>
    </div>
    <section><h2>Motion</h2><p class="lead">Motion explains where something went. It never makes you wait: every animation runs on a copy or after the state has already changed, and repeated keyboard actions skip it. Reduced motion keeps every state change and drops the travel.</p>
      <table class="motion-t"><thead><tr><th>Moment</th><th>What moves</th><th>Duration</th><th>Easing</th></tr></thead><tbody>
        <tr><td>Mark reviewed</td><td>A copy of the page shrinks into the tray; the row collapses; the count rolls</td><td>380 ms, skipped within 1.2 s of the last review</td><td><code>standard (0.4, 0, 0.2, 1)</code></td></tr>
        <tr><td>Next document</td><td>Pages rise 10 px and fade in</td><td>240 ms</td><td><code>decelerate (0.2, 0, 0, 1)</code></td></tr>
        <tr><td>Popover, menu, palette</td><td>Fade and 4 px drop from its anchor</td><td>180 ms in, 120 ms out</td><td><code>decelerate / accelerate</code></td></tr>
        <tr><td>Dialog or sheet</td><td>Fade and 12 px rise (macOS: drops from the toolbar)</td><td>280 ms</td><td><code>decelerate</code></td></tr>
        <tr><td>Quick look</td><td>The page grows from its thumbnail and returns to it</td><td>260 ms in, 200 ms out</td><td><code>decelerate / accelerate</code></td></tr>
        <tr><td>Arrival</td><td>New rows slide 10 px down into place; a scan line sweeps the thumbnail while text is read</td><td>320 ms; sweep 1.6 s loop while reading</td><td><code>decelerate</code></td></tr>
        <tr><td>Suggestion accepted</td><td>The field flashes the highlight colour once</td><td>900 ms fade</td><td><code>standard</code></td></tr>
      </tbody></table></section>
    <section><h2>Icons</h2><p class="lead">One family drawn on a 20-unit grid with a 1.5 stroke and round ends, matching the mark's round terminals. The Inbox icon is the mark itself.</p>
      <div class="icons">${[["Inbox", I.inbox], ["All documents", I.stack], ["Upcoming", I.clock], ["Trash", I.trash], ["Search", I.search], ["Scan", I.scan], ["Add files", I.upload], ["Folder", I.folder16], ["Tag", I.tag], ["Sender", I.from], ["Date", I.cal], ["Reminder", I.bell], ["Note", I.note], ["Pages", I.pages], ["Rotate", I.rotR], ["Split", I.split], ["Combine", I.combine], ["Export", I.share], ["Binder", I.binder], ["Make searchable", I.ocr], ["Redact", I.redact], ["Versions", I.history], ["Watch", I.watch], ["Phone", I.phone], ["Settings", I.sliders], ["Lock", I.lock], ["Related", I.link], ["Ask", I.chat]].map(([n, s]) => `<div>${s}<span>${n}</span></div>`).join("")}</div></section>
  </div>`;
}

/* ───────── Boot ───────── */
const ICON_DARK = "data:image/svg+xml," + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="100 100 824 824"><defs><linearGradient id="b" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#262628"/><stop offset=".55" stop-color="#0F0F10"/><stop offset="1" stop-color="#040404"/></linearGradient><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff"/><stop offset="1" stop-color="#CBCBCB"/></linearGradient><linearGradient id="r" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".4"/><stop offset=".35" stop-color="#fff" stop-opacity=".08"/><stop offset="1" stop-color="#fff" stop-opacity=".18"/></linearGradient></defs><path d="M627.36 100c103.83 0 155.75 0 195.41 20.21a185.4 185.4 0 0 1 81.02 81.02c20.21 39.66 20.21 91.58 20.21 195.41L924 627.36c0 103.83 0 155.75-20.21 195.41a185.4 185.4 0 0 1-81.02 81.02c-39.66 20.21-91.58 20.21-195.41 20.21L396.64 924c-103.83 0-155.75 0-195.41-20.21a185.4 185.4 0 0 1-81.02-81.02c-20.21-39.66-20.21-91.58-20.21-195.41L100 396.64c0-103.83 0-155.75 20.21-195.41a185.4 185.4 0 0 1 81.02-81.02c39.66-20.21 91.58-20.21 195.41-20.21Z" fill="url(#b)" stroke="url(#r)" stroke-width="3"/><path fill="url(#g)" d="M690.666 728.789A87.626 87.626 0 0 0 778.292 641.163L778.292 551.261A33 33 0 0 0 712.288 551.261L712.288 641.163A21.622 21.622 0 0 1 690.666 662.785L333.334 662.785A21.622 21.622 0 0 1 311.712 641.163L311.712 551.261A33 33 0 0 0 245.708 551.261L245.708 641.163A87.626 87.626 0 0 0 333.334 728.789L690.666 728.789ZM365.198 551.261A78.522 78.522 0 0 0 443.72 629.783L580.28 629.783A78.522 78.522 0 0 0 658.802 551.261L658.802 416.486A78.522 78.522 0 0 0 635.803 360.963L585.084 310.244A78.522 78.522 0 0 0 529.561 287.245L443.72 287.245A78.522 78.522 0 0 0 365.198 365.767L365.198 551.261ZM431.202 365.767A12.518 12.518 0 0 1 443.72 353.249L529.561 353.249A12.518 12.518 0 0 1 538.412 356.916L589.131 407.634A12.518 12.518 0 0 1 592.797 416.486L592.798 551.261A12.518 12.518 0 0 1 580.28 563.779L443.72 563.779A12.518 12.518 0 0 1 431.202 551.261L431.202 365.767Z"/></svg>`);

(function boot() {
  let theme = null; try { theme = localStorage.getItem("plico-mock-theme"); } catch (_) { /* optional */ }
  const qt = new URLSearchParams(location.search).get("t"); if (qt === "light" || qt === "dark") theme = qt;
  const qp = new URLSearchParams(location.search).get("p"); if (qp) S.platformForced = qp;
  if (theme) setTheme(theme); else $$("[data-theme-set]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.themeSet === effectiveTheme())));
  if (/Mac/.test(navigator.platform)) { S.platform = "mac"; $$("[data-platform-set]").forEach((x) => x.setAttribute("aria-pressed", String(x.dataset.platformSet === "mac"))); }
  else if (/Linux/.test(navigator.platform)) { S.platform = "linux"; $$("[data-platform-set]").forEach((x) => x.setAttribute("aria-pressed", String(x.dataset.platformSet === "linux"))); }
  if (S.platformForced) { S.platform = S.platformForced; $$("[data-platform-set]").forEach((x) => x.setAttribute("aria-pressed", String(x.dataset.platformSet === S.platform))); }
  const start = location.hash.slice(1);
  fitStage();
  go(NOTES[start] ? start : "inbox");
  fitStage();
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => { if (S.screen !== "system" && !S.overlay) renderContent(); });
})();
