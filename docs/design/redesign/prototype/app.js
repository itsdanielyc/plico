"use strict";
/* Plico redesign prototype: core state, layout and the review loop.
   Plain DOM rendering keeps the mockup dependency-free. Rendering is split
   by region (sidebar, toolbar, list, viewer, filing panel) so typing and
   animations survive updates elsewhere. */

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const strip = (h) => String(h ?? "").replace(/<[^>]+>/g, " ").replace(/&amp;/g, "&").replace(/\s+/g, " ").trim();

/* ───────── Icons: 20-unit grid, 1.5 stroke, round ends like the mark ───────── */
const ico = (body, size = 16) => `<svg viewBox="0 0 20 20" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;
const MARK_TRAY = "M391 388A77 77 0 0 0 468 311L468 232A29 29 0 0 0 410 232L410 311A19 19 0 0 1 391 330L77 330A19 19 0 0 1 58 311L58 232A29 29 0 0 0 0 232L0 311A77 77 0 0 0 77 388L391 388Z";
const MARK_SHEET = "M105 232A69 69 0 0 0 174 301L294 301A69 69 0 0 0 363 232L363 113.569A69 69 0 0 0 342.79 64.778L298.222 20.21A69 69 0 0 0 249.431 0L174 0A69 69 0 0 0 105 69L105 232ZM163 69A11 11 0 0 1 174 58L249.431 58A11 11 0 0 1 257.209 61.222L301.778 105.79A11 11 0 0 1 305 113.568L305 232A11 11 0 0 1 294 243L174 243A11 11 0 0 1 163 232L163 69Z";
const markSVG = (cls = "") => `<svg class="${cls}" viewBox="0 0 468 388" style="overflow:visible" aria-hidden="true"><path fill="currentColor" d="${MARK_TRAY}"/><path class="sheetp" fill="currentColor" fill-rule="evenodd" d="${MARK_SHEET}"/></svg>`;
const I = {
  inbox: `<svg viewBox="0 0 468 388" width="16" height="13" aria-hidden="true"><path fill="currentColor" d="${MARK_TRAY}"/><path fill="currentColor" fill-rule="evenodd" d="${MARK_SHEET}"/></svg>`,
  stack: ico('<rect x="3.5" y="5.5" width="10" height="11.5" rx="1.5"/><path d="M6.5 2.75h8a1.75 1.75 0 0 1 1.75 1.75v9.5"/>'),
  clock: ico('<circle cx="10" cy="10" r="7"/><path d="M10 6.2V10l2.6 1.6"/>'),
  trash: ico('<path d="M4 6h12M8 6V4.5h4V6M5.5 6l.7 9.6a1 1 0 0 0 1 .9h5.6a1 1 0 0 0 1-.9L14.5 6"/>'),
  search: ico('<circle cx="9" cy="9" r="5.5"/><path d="M13.2 13.2l3.3 3.3"/>'),
  plus: ico('<path d="M10 4.5v11M4.5 10h11"/>'),
  plus12: ico('<path d="M10 4.5v11M4.5 10h11"/>', 12),
  scan: ico('<rect x="3" y="9.5" width="14" height="6" rx="1.5"/><path d="M6 9.5V4h8v5.5M6 12.5h2"/>'),
  upload: ico('<path d="M10 13V4.5M6.5 8L10 4.5 13.5 8M4 12.5v3h12v-3"/>'),
  folder: ico('<path d="M3 6.5V15a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1h-6L8.5 5H4a1 1 0 0 0-1 1.5z"/>', 13),
  folder16: ico('<path d="M3 6.5V15a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1h-6L8.5 5H4a1 1 0 0 0-1 1.5z"/>'),
  tag: ico('<path d="M3.5 10.5V4a.5.5 0 0 1 .5-.5h6.5l6 6-7 7z"/><circle cx="7" cy="7" r="1"/>'),
  chevD: ico('<path d="M6 8l4 4 4-4"/>', 14),
  chevR: ico('<path d="M8 6l4 4-4 4"/>', 12),
  chevL16: ico('<path d="M12 5l-5 5 5 5"/>'),
  chevR16: ico('<path d="M8 5l5 5-5 5"/>'),
  more: ico('<circle cx="5" cy="10" r="1.1" fill="currentColor" stroke="none"/><circle cx="10" cy="10" r="1.1" fill="currentColor" stroke="none"/><circle cx="15" cy="10" r="1.1" fill="currentColor" stroke="none"/>'),
  check: ico('<path d="M4.5 10.5l3.5 3.5 7.5-8"/>'),
  x: ico('<path d="M6 6l8 8M14 6l-8 8"/>', 12),
  minus: ico('<path d="M5.5 10h9"/>'),
  text: ico('<path d="M4 5.5h12M4 9.5h12M4 13.5h7.5"/>'),
  info: ico('<circle cx="10" cy="10" r="7"/><path d="M10 9v4.5M10 6.6v.01"/>'),
  sliders: ico('<path d="M3.5 6h7M15.5 6h1M3.5 14h1M8.5 14h8"/><circle cx="13" cy="6" r="2"/><circle cx="6.5" cy="14" r="2"/>'),
  phone: ico('<rect x="6" y="2.5" width="8" height="15" rx="2"/><path d="M9 14.5h2"/>'),
  watch: ico('<path d="M2.5 10s3-5.5 7.5-5.5S17.5 10 17.5 10 14.5 15.5 10 15.5 2.5 10 2.5 10z"/><circle cx="10" cy="10" r="2.2"/>'),
  bookmark: ico('<path d="M6 3.5h8a.5.5 0 0 1 .5.5v12.5L10 13.5l-4.5 3V4a.5.5 0 0 1 .5-.5z"/>'),
  grid: ico('<rect x="3.5" y="3.5" width="5" height="5" rx="1"/><rect x="11.5" y="3.5" width="5" height="5" rx="1"/><rect x="3.5" y="11.5" width="5" height="5" rx="1"/><rect x="11.5" y="11.5" width="5" height="5" rx="1"/>'),
  list: ico('<path d="M7.5 5.5h9M7.5 10h9M7.5 14.5h9"/><path d="M3.5 5.5h.01M3.5 10h.01M3.5 14.5h.01" stroke-width="2"/>'),
  rotL: ico('<path d="M4.5 8.5A6 6 0 1 1 6 14"/><path d="M4.5 4v4.5H9"/>'),
  rotR: ico('<path d="M15.5 8.5A6 6 0 1 0 14 14"/><path d="M15.5 4v4.5H11"/>'),
  open: ico('<path d="M11 4h5v5M16 4l-7 7M14 11.5V15a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h3.5"/>'),
  cal: ico('<rect x="3.5" y="4.5" width="13" height="12" rx="1.5"/><path d="M3.5 8.5h13M7 3v3M13 3v3"/>'),
  bell: ico('<path d="M5.5 13.5V9a4.5 4.5 0 0 1 9 0v4.5l1 1.5h-11z"/><path d="M8.5 17h3"/>'),
  note: ico('<path d="M4 4h12v8l-4 4H4z"/><path d="M12 16v-4h4"/>'),
  split: ico('<circle cx="6" cy="6" r="2"/><circle cx="6" cy="14" r="2"/><path d="M8 7.2l8 6.3M8 12.8l8-6.3"/>'),
  combine: ico('<rect x="3" y="3.5" width="8" height="10" rx="1"/><rect x="9" y="6.5" width="8" height="10" rx="1"/>'),
  share: ico('<path d="M10 12.5V3.5M6.5 7L10 3.5 13.5 7M5 10.5v6h10v-6"/>'),
  binder: ico('<path d="M5.5 3.5h10v13h-10A1.5 1.5 0 0 1 4 15V5a1.5 1.5 0 0 1 1.5-1.5z"/><path d="M4 14.5A1.5 1.5 0 0 1 5.5 13h10"/>'),
  ocr: ico('<path d="M3.5 6V4.5a1 1 0 0 1 1-1H6M14 3.5h1.5a1 1 0 0 1 1 1V6M16.5 14v1.5a1 1 0 0 1-1 1H14M6 16.5H4.5a1 1 0 0 1-1-1V14M7 13l3-7 3 7M8.2 10.6h3.6"/>'),
  redact: ico('<rect x="3.5" y="4" width="13" height="12" rx="1.5"/><path d="M6 8h8" stroke-width="3"/><path d="M6 12.5h5"/>'),
  stamp: ico('<path d="M8 3.5h4v5l3 2v2.5H5V10.5l3-2zM4.5 16h11"/>'),
  compress: ico('<path d="M10 3v5M7.5 5.5L10 8l2.5-2.5M10 17v-5M7.5 14.5L10 12l2.5 2.5M4 10h12"/>'),
  history: ico('<path d="M3.5 10a6.5 6.5 0 1 0 2-4.7"/><path d="M3.5 4v3.5H7M10 6.5V10l2.5 1.5"/>'),
  pages: ico('<rect x="3" y="3" width="6" height="7.5" rx="1"/><rect x="11" y="3" width="6" height="7.5" rx="1"/><rect x="3" y="12.5" width="6" height="4.5" rx="1"/><rect x="11" y="12.5" width="6" height="4.5" rx="1"/>'),
  lock: ico('<rect x="4.5" y="9" width="11" height="8" rx="1.5"/><path d="M7 9V6.5a3 3 0 0 1 6 0V9"/>'),
  link: ico('<path d="M8.5 11.5l3-3M7 9.5l-1.5 1.5a2.5 2.5 0 0 0 3.5 3.5l1.5-1.5M13 10.5l1.5-1.5A2.5 2.5 0 0 0 11 5.5L9.5 7"/>'),
  chat: ico('<path d="M4 5h12v8H9l-3.5 3v-3H4z"/>'),
  keyboard: ico('<rect x="2.5" y="5" width="15" height="10" rx="1.5"/><path d="M5.5 8h.01M8.5 8h.01M11.5 8h.01M14.5 8h.01M6.5 12h7"/>'),
  refresh: ico('<path d="M15.5 10a5.5 5.5 0 1 1-1.6-3.9"/><path d="M15.5 3.5V7H12"/>'),
  pause: ico('<path d="M7.5 5.5v9M12.5 5.5v9"/>', 14),
  play: ico('<path d="M7 5.5l7.5 4.5L7 14.5z"/>', 14),
  warn: ico('<path d="M10 3.5l7 12.5H3z"/><path d="M10 8.5V12M10 14.2v.01"/>'),
  sort: ico('<path d="M6 4v12M3.5 13.5L6 16l2.5-2.5M14 16V4M11.5 6.5L14 4l2.5 2.5"/>'),
  fit: ico('<path d="M4 8V4h4M16 8V4h-4M4 12v4h4M16 12v4h-4"/>'),
  help: ico('<circle cx="10" cy="10" r="7"/><path d="M8 8a2 2 0 1 1 2.8 1.8c-.5.3-.8.7-.8 1.2v.5M10 14v.01"/>'),
  move: ico('<path d="M3 6.5V15a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1h-6L8.5 5H4a1 1 0 0 0-1 1.5z"/><path d="M8 11.5h5M11 9.5l2 2-2 2"/>'),
  from: ico('<rect x="3" y="5" width="14" height="10" rx="1.5"/><path d="M3.5 6l6.5 5 6.5-5"/>'),
  winMin: ico('<path d="M5 10h10"/>', 14),
  winMax: ico('<rect x="5.5" y="5.5" width="9" height="9" rx="1"/>', 14),
  winX: ico('<path d="M5.5 5.5l9 9M14.5 5.5l-9 9"/>', 14),
  sidebar: ico('<rect x="3" y="4" width="14" height="12" rx="1.5"/><path d="M8 4v12"/>'),
  filter: ico('<path d="M3.5 5.5h13M6 10h8M8.5 14.5h3"/>'),
  sparkline: ico('<path d="M3 13l3.5-4 3 2.5L13 6l4 4"/>'),
};

/* ───────── Keys: labels follow the platform ───────── */
function keyLabel(k) {
  const mac = S.platform === "mac";
  const map = { mod: mac ? "⌘" : "Ctrl", shift: mac ? "⇧" : "Shift", alt: mac ? "⌥" : "Alt", enter: mac ? "↩" : "Enter", esc: "Esc", up: "↑", down: "↓", left: "←", right: "→", space: "Space", del: mac ? "⌫" : "Del", tab: "Tab" };
  return k.split("+").map((p) => map[p] ?? p.toUpperCase()).join(mac ? "" : "+");
}
const K = (k) => `<span class="kbd">${esc(keyLabel(k))}</span>`;

/* ───────── State ───────── */
const S = {
  platform: "win", motion: "full", screen: "inbox",
  view: "inbox", layout: "list", cur: null, sel: new Set(), anchor: null,
  info: false, zoom: "fit", q: { text: "", tokens: [] }, searchOpen: false, sIdx: 0,
  overlay: null, pal: { q: "", idx: 0 }, editing: null, acIdx: 0, justTagged: null,
  reviewedToday: 0, lastReviewAt: 0, undo: [], timers: [], savedName: null, matchIdx: 0,
  settingsSection: "intake", scan: null, act: { progress: 38 },
};
let DOCS = [];
const PAGECACHE = new Map();
const TODAY = new Date(2026, 10, 20);

const SAVED = [
  { name: "Renewals", text: "renewal", tokens: [] },
  { name: "Energy bills", text: "", tokens: [{ k: "from", v: "Brightwell Energy" }] },
  { name: "Tax 2026", text: "", tokens: [{ k: "tag", v: "Tax" }, { k: "dated", v: "2026" }] },
];

function freshDocs() {
  DOCS = SAMPLE_DOCUMENTS.map((d) => {
    const x = { ...d, tags: [...d.tags], deadline: d.deadline || null, note: "", pending: null };
    if (d.status === "inbox" && d.suggest) {
      x.pending = { from: d.suggest.from, type: d.suggest.type, date: d.suggest.date, tags: (d.suggest.tags || []).filter((t) => !x.tags.includes(t)), deadline: d.suggest.deadline || null, why: d.suggest.why };
      x.from = ""; x.type = ""; x.date = "";
    }
    if (d.id === "phone") x.deadline = "Term ends 14 Jan 2027";
    if (d.id === "boiler") x.deadline = "Inspection due 13 Sep 2027";
    return x;
  });
}
const doc = (id) => DOCS.find((d) => d.id === id);
const tagColor = (t) => (TAGS.find((x) => x.name.toLowerCase() === String(t).toLowerCase()) || { color: "#8a8a92" }).color;
const motionOK = () => S.motion === "full" && !matchMedia("(prefers-reduced-motion: reduce)").matches;
const later = (fn, ms) => { const t = setTimeout(fn, ms); S.timers.push(t); return t; };
let stageScale = 1;

/* ───────── Pages and thumbnails ───────── */
function pageInfo(d, i = 0) {
  const key = d.id + ":" + i;
  let p = PAGECACHE.get(key);
  if (!p) {
    const html = d.pages[i]();
    const m = /width:(\d+)px;height:(\d+)px/.exec(html);
    p = { html, w: +m[1], h: +m[2] };
    PAGECACHE.set(key, p);
  }
  return p;
}
function sheetAt(d, i, s, cls = "sheet", extra = "") {
  const p = pageInfo(d, i);
  return `<div class="${cls}" style="width:${Math.round(p.w * s)}px;height:${Math.round(p.h * s)}px${extra}" data-doc="${d.id}" data-page="${i}"><div class="pgs" style="width:${p.w}px;height:${p.h}px;transform:scale(${s.toFixed(5)})">${p.html}</div></div>`;
}
function fitScale(d, i, bw, bh) { const p = pageInfo(d, i); return Math.min(bw / p.w, bh / p.h); }
const thumb = (d, w, h, cls = "") => `<span class="tbox" style="width:${w}px;height:${h}px">${sheetAt(d, 0, fitScale(d, 0, w, h), "thumb " + cls)}</span>`;
function docText(d) {
  if (!d._text) d._text = d.pages.map((_, i) => strip(pageInfo(d, i).html)).join(" ");
  return d._text;
}

/* ───────── Dates and deadlines ───────── */
const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
function parseDay(s) {
  const m = /(\d{1,2}) (\w{3})\w* (\d{4})/.exec(s || "");
  if (!m) return null;
  const mi = MONTHS.findIndex((x) => x.slice(0, 3) === m[2]);
  return mi < 0 ? null : new Date(+m[3], mi, +m[1]);
}
function daysFromToday(dt) { return Math.round((dt - TODAY) / 86400000); }
function upcoming() {
  return DOCS.filter((d) => d.status !== "trash")
    .map((d) => ({ d, label: d.deadline || (d.pending && d.pending.deadline) || (d.suggest && d.suggest.deadline) }))
    .filter((u) => u.label && parseDay(u.label))
    .map((u) => { const dt = parseDay(u.label); const n = daysFromToday(dt); return { ...u, dt, n, when: n <= 0 ? "Today" : n === 1 ? "Tomorrow" : n < 45 ? `in ${n} days` : dt.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" }) }; })
    .filter((u) => u.n >= 0).sort((a, b) => a.dt - b.dt);
}

/* ───────── Views ───────── */
function viewDocs() {
  const v = S.view;
  if (v === "inbox") return DOCS.filter((d) => d.status === "inbox");
  if (v === "all") return DOCS.filter((d) => d.status === "inbox" || d.status === "reviewed");
  if (v === "upcoming") return upcoming().map((u) => u.d);
  if (v === "trash") return DOCS.filter((d) => d.status === "trash");
  if (v.startsWith("tag:")) { const t = v.slice(4); return DOCS.filter((d) => d.status !== "trash" && d.tags.includes(t)); }
  if (v === "search") return searchDocs();
  return [];
}
function tokMatch(d, t) {
  const v = t.v.toLowerCase();
  if (t.k === "tag") return d.tags.some((x) => x.toLowerCase() === v);
  if (t.k === "status") return d.status === v;
  if (t.k === "from") return (d.from || "").toLowerCase().includes(v);
  if (t.k === "type") return (d.type || "").toLowerCase() === v;
  if (t.k === "dated") return (d.date || "").includes(t.v);
  if (t.k === "text") return v === "missing" ? !!d.noText : !d.noText;
  return true;
}
function searchDocs() {
  const words = S.q.text.toLowerCase().split(/\s+/).filter(Boolean);
  return DOCS.filter((d) => d.status !== "trash"
    && words.every((w) => (d.title + " " + d.from + " " + docText(d)).toLowerCase().includes(w))
    && S.q.tokens.every((t) => tokMatch(d, t)));
}
function snippet(d) {
  const words = S.q.text.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return esc(strip(d.excerpt));
  const text = docText(d); const at = text.toLowerCase().indexOf(words[0]);
  if (at < 0) return esc(strip(d.excerpt));
  const a = Math.max(0, at - 48), b = Math.min(text.length, at + 80);
  let s = esc((a > 0 ? "…" : "") + text.slice(a, b) + (b < text.length ? "…" : ""));
  words.forEach((w) => { s = s.replace(new RegExp(`(${w.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")})`, "gi"), "<mark>$1</mark>"); });
  return s;
}
function layoutName() {
  if (S.screen === "pages") return "pages";
  if (S.view === "inbox") return viewDocs().length ? "review" : "zero";
  return S.layout === "grid" ? "grid" : "list";
}

/* ───────── Sidebar ───────── */
function navItem(id, icon, label, count, opts = {}) {
  const cur = S.view === id && !S.savedName;
  const ct = count === "" || count == null ? "" : `<span class="ct ${opts.badge ? "badge" : ""}" ${opts.ctId ? `id="${opts.ctId}"` : ""}><span class="roll">${count}</span></span>`;
  return `<button class="nav-i" type="button" data-act="nav" data-view="${esc(id)}" aria-current="${cur}">${icon}<span class="lbl">${esc(label)}</span>${ct}</button>`;
}
function renderSidebar() {
  const inboxN = DOCS.filter((d) => d.status === "inbox").length;
  const up = upcoming().length;
  const trashN = DOCS.filter((d) => d.status === "trash").length;
  const proc = DOCS.filter((d) => d.processing);
  $("#sb").innerHTML = `
    <div class="sb-top">
      <div class="lights" aria-hidden="true"><i></i><i></i><i></i></div>
      <button class="libsw" type="button" data-act="libmenu" aria-haspopup="menu"><span class="mk">${I.inbox}</span><b>Home paperwork</b><span class="chev">${I.chevD}</span></button>
    </div>
    <div class="sb-scroll">
      <nav class="nav" aria-label="Library">
        ${navItem("inbox", I.inbox, "Inbox", inboxN || "", { badge: true, ctId: "inbox-ct" })}
        ${navItem("all", I.stack, "All documents", "")}
        ${navItem("upcoming", I.clock, "Upcoming", up || "")}
      </nav>
      <nav class="nav" aria-label="Saved searches">
        <div class="nav-h">Saved searches <button type="button" data-act="save-search" aria-label="Save the current search">${I.plus12}</button></div>
        ${SAVED.map((s) => `<button class="nav-i" type="button" data-act="saved" data-name="${esc(s.name)}" aria-current="${S.savedName === s.name}">${I.bookmark}<span class="lbl">${esc(s.name)}</span></button>`).join("")}
      </nav>
      <nav class="nav" aria-label="Tags">
        <div class="nav-h">Tags <button type="button" data-act="settings" data-sec="filing" aria-label="Manage tags">${I.sliders}</button></div>
        ${TAGS.slice(0, 9).map((t) => `<button class="nav-i" type="button" data-act="nav" data-view="tag:${esc(t.name)}" aria-current="${S.view === "tag:" + t.name}"><span class="dot" style="background:${t.color}"></span><span class="lbl">${esc(t.name)}</span><span class="ct">${t.count}</span></button>`).join("")}
      </nav>
      <nav class="nav" aria-label="Other">${navItem("trash", I.trash, "Trash", trashN || "")}</nav>
    </div>
    <div class="intake">
      <div class="nav-h">Intake</div>
      <button class="src" type="button" data-act="activity">${I.watch}<span class="lbl">Scans folder</span><span class="st"><span class="live"></span>Watching</span></button>
      <button class="src" type="button" data-act="activity">${I.phone}<span class="lbl">Phone inbox</span><span class="st"><span class="live off"></span>Paused</span></button>
      ${proc.length ? `<div class="proc"><div class="row"><span>Reading ${proc.length} file${proc.length > 1 ? "s" : ""}…</span><span>${Math.round(proc.reduce((a, d) => a + (d.progress || 0), 0) / proc.length)}%</span></div><div class="meter"><i style="width:${Math.round(proc.reduce((a, d) => a + (d.progress || 0), 0) / proc.length)}%"></i></div></div>` : ""}
    </div>`;
}
function bumpInbox(before, after) {
  const ct = $("#inbox-ct");
  if (!ct) { renderSidebar(); return; }
  if (!after) { ct.remove(); return; }
  if (!motionOK()) { ct.innerHTML = `<span class="roll">${after}</span>`; return; }
  ct.innerHTML = `<span class="roll out" aria-hidden="true">${before}</span><span class="roll in">${after}</span>`;
  later(() => { if (ct.isConnected) ct.innerHTML = `<span class="roll">${after}</span>`; }, 220);
}

/* ───────── Toolbar ───────── */
function renderToolbar() {
  const proc = DOCS.filter((d) => d.processing).length;
  const r = 6.5, c = 2 * Math.PI * r, pct = proc ? 0.62 : 0;
  $("#tb").innerHTML = `
    <div class="search" id="search">
      <div class="search-f ${S.searchOpen ? "on" : ""}" id="search-f" data-act="focus-search">
        ${I.search}<span id="toks" style="display:contents">${toksHTML()}</span>
        <input id="q" type="text" autocomplete="off" spellcheck="false" aria-label="Search" placeholder="${S.q.tokens.length ? "" : "Search documents, text and tags"}" value="${esc(S.q.text)}">
        ${S.searchOpen ? "" : K("/")}
      </div>
      <div id="sdrop-host"></div>
    </div>
    <span class="grow"></span>
    <button class="ibtn" type="button" id="act-btn" data-act="activity" aria-label="Activity" title="Activity">
      ${proc ? `<svg class="ring" viewBox="0 0 16 16" aria-hidden="true"><circle class="bg" cx="8" cy="8" r="${r}"/><circle class="fg" cx="8" cy="8" r="${r}" stroke-dasharray="${c}" stroke-dashoffset="${c * (1 - pct)}"/></svg><span class="act-n">${proc}</span>` : I.sparkline}
    </button>
    <button class="ibtn" type="button" data-act="shortcuts" aria-label="Keyboard shortcuts" title="Keyboard shortcuts (?)">${I.keyboard}</button>
    <button class="btn" type="button" data-act="scan" title="Scan (${keyLabel("mod+shift+s")})">${I.scan} Scan</button>
    <div class="split"><button class="btn primary" type="button" data-act="addfiles" title="Add files (${keyLabel("mod+o")})">${I.plus} Add files</button><button class="btn primary" type="button" data-act="addmenu" aria-label="More ways to add" aria-haspopup="menu">${I.chevD}</button></div>
    <div class="gnome"><button type="button" aria-label="Minimise">${I.winMin}</button><button type="button" aria-label="Maximise">${I.winMax}</button><button type="button" aria-label="Close">${I.winX}</button></div>
    <div class="caps"><button type="button" aria-label="Minimise">${I.winMin}</button><button type="button" aria-label="Maximise">${I.winMax}</button><button type="button" class="x" aria-label="Close">${I.winX}</button></div>`;
  wireSearch();
  if (S.searchOpen) renderSearchDrop();
}
const toksHTML = () => S.q.tokens.map((t, i) => `<span class="tok"><i>${esc(t.k)}:</i>${esc(t.v)}<button type="button" data-act="untok" data-i="${i}" aria-label="Remove filter">${I.x}</button></span>`).join("");

/* ───────── Content: list | viewer | filing panel ───────── */
function renderContent() {
  const c = $("#content"), lay = layoutName();
  c.className = "content";
  if (lay === "zero") { c.classList.add("zero-mode"); c.innerHTML = zeroHTML(); return; }
  if (lay === "grid") { c.classList.add("grid-mode"); c.innerHTML = `<section class="list" id="list" style="border:0">${listHeadHTML()}<div class="gridwrap" id="gridwrap">${gridHTML()}</div></section>`; return; }
  if (lay === "pages") { c.classList.add("pe-mode"); c.innerHTML = listShellHTML() + peHTML(); renderRows(); return; }
  const insp = lay === "review" || S.info;
  if (!insp) c.classList.add("no-insp"); else if (lay === "list") c.classList.add("list-insp");
  c.innerHTML = listShellHTML() + `<section class="viewer" id="viewer" aria-label="Preview"></section>` + (insp ? `<aside class="insp" id="insp" aria-label="Filing"></aside>` : "<div></div>");
  renderRows(); renderViewer(); if (insp) renderInsp();
}
function viewTitle() {
  if (S.savedName) return S.savedName;
  const v = S.view;
  if (v === "inbox") return "Inbox";
  if (v === "all") return "All documents";
  if (v === "upcoming") return "Upcoming";
  if (v === "trash") return "Trash";
  if (v === "search") return "Search";
  if (v.startsWith("tag:")) return v.slice(4);
  return "";
}
function listHeadHTML() {
  const n = viewDocs().length, v = S.view;
  const sub = v === "inbox" ? `${n} to review` : v === "upcoming" ? `${n} dates` : `${n} ${n === 1 ? "document" : "documents"}`;
  const toggle = v !== "inbox" && v !== "upcoming" ? `<div class="segc" style="width:64px" role="group" aria-label="Layout"><button type="button" data-act="layout" data-l="list" aria-pressed="${S.layout !== "grid"}" aria-label="List">${I.list}</button><button type="button" data-act="layout" data-l="grid" aria-pressed="${S.layout === "grid"}" aria-label="Grid">${I.grid}</button></div>` : "";
  const dot = v.startsWith("tag:") ? `<span class="dot" style="width:9px;height:9px;border-radius:50%;background:${tagColor(v.slice(4))};align-self:center"></span>` : "";
  let chips = "";
  if (v === "search" || v === "all" || v.startsWith("tag:")) {
    const has = (k) => S.q.tokens.find((t) => t.k === k);
    const chip = (k, label) => { const t = v === "search" && has(k); return `<button type="button" class="fchip ${t ? "on" : ""}" data-act="fchip" data-k="${k}">${t ? `${label}: ${esc(t.v)}` : label}${I.chevD}</button>`; };
    chips = `<div class="chips">${chip("status", "Status")}${chip("tag", "Tag")}${chip("from", "From")}${chip("dated", "Date")}${chip("type", "Type")}${chip("text", "Text")}${v === "search" && !S.savedName ? `<button type="button" class="fchip" data-act="save-search">${I.bookmark} Save</button>` : ""}</div>`;
  }
  return `<div class="list-h"><div class="t">${dot}<h2>${esc(viewTitle())}</h2><span class="sub">${sub}</span><span class="grow"></span>${toggle}<button class="ibtn" type="button" data-act="sort" aria-label="Sort" title="${v === "inbox" ? "Oldest first" : v === "search" ? "Best match" : "Newest first"}">${I.sort}</button></div>${chips}</div>`;
}
function listShellHTML() {
  const foot = S.view === "inbox"
    ? `${K("j")}${K("k")}<span>Move</span>${K("r")}<span>Reviewed</span>${K("?")}<span>Keys</span>`
    : `${K("space")}<span>Look closer</span>${K("enter")}<span>Details</span>${K("?")}<span>Keys</span>`;
  return `<section class="list" id="list" aria-label="${esc(viewTitle())}">${listHeadHTML()}<div class="rows" id="rows" role="listbox" tabindex="0" aria-label="Documents" aria-multiselectable="true"></div><div class="list-foot">${foot}</div></section>`;
}
function rowHTML(d) {
  const selected = S.sel.size > 1 ? S.sel.has(d.id) : d.id === S.cur;
  const meta = d.processing ? esc(d.stage || "Reading text…") : esc(d.from || d.source);
  const time = S.view === "inbox" ? esc(String(d.added).replace(/^Today, /, "")) : esc(shortDate(d.date || d.added));
  const tags = S.view !== "inbox" && d.tags.length ? `<div class="rtags">${d.tags.map((t) => `<span class="ltag"><i style="background:${tagColor(t)}"></i>${esc(t)}</span>`).join("")}</div>` : "";
  const ex = S.view === "search" ? `<div class="rx">${snippet(d)}</div>` : "";
  const proc = d.processing ? `<div class="proc-line"><div class="meter"><i style="width:${d.progress || 8}%"></i></div></div>` : "";
  const up = S.view === "upcoming" ? `<div class="rm" style="color:var(--warn)">${esc(d.deadline || (d.pending && d.pending.deadline) || (d.suggest && d.suggest.deadline) || "")}</div>` : "";
  return `<div class="row ${d.id === S.cur ? "cur" : ""} ${d._arriving ? "arriving" : ""}" role="option" aria-selected="${selected}" data-act="row" data-id="${d.id}">
    ${thumb(d, 38, 54, d.processing ? "reading" : "")}
    <div class="rb" style="min-width:0"><div class="rt">${esc(d.title)}</div><div class="rm">${meta}</div>${up}${ex}${tags}${proc}</div>
    <div class="rtime">${time}</div></div>`;
}
function shortDate(s) { const dt = parseDay(s); return dt ? dt.toLocaleDateString("en-GB", { day: "numeric", month: "short" }) : s; }
function renderRows() {
  const r = $("#rows"); if (!r) return;
  const st = r.scrollTop, ds = viewDocs();
  r.innerHTML = ds.length ? ds.map(rowHTML).join("") : `<div style="padding:28px 12px;text-align:center;color:var(--ink-3);font-size: 14px">${S.view === "trash" ? "Trash is empty." : S.view === "search" ? "No documents match. Check the spelling or remove a filter." : "Nothing here yet."}</div>`;
  r.scrollTop = st;
  ds.forEach((d) => { delete d._arriving; });
  const cur = r.querySelector(".row.cur");
  if (cur) { const top = cur.offsetTop - r.offsetTop, bot = top + cur.offsetHeight; if (top < r.scrollTop) r.scrollTop = top - 8; else if (bot > r.scrollTop + r.clientHeight) r.scrollTop = bot - r.clientHeight + 8; }
}

/* Viewer */
function renderViewer(enter = false) {
  const v = $("#viewer"); if (!v) return;
  if (S.sel.size > 1) { v.innerHTML = fanHTML(); fanOut(); return; }
  const d = doc(S.cur);
  if (!d) { v.innerHTML = `<div style="margin:auto;color:var(--ink-3);font-size: 14px">Select a document to preview it.</div>`; return; }
  const info = layoutName() === "list" ? `<button class="btn sm" type="button" data-act="info" aria-pressed="${S.info}" title="Filing details (${keyLabel("mod+i")})">${I.info} ${S.info ? "Hide details" : "Details"}</button>` : "";
  v.innerHTML = `<div class="v-head">${info}<button class="btn sm" type="button" data-act="openext" title="Open in your PDF viewer (O)">${I.open} Open</button></div>
    ${d.noText ? `<div class="notext" id="notext">${I.ocr}<span>This scan has no searchable text yet.</span><button class="btn sm primary" type="button" data-act="ocr-one">Make searchable</button></div>` : ""}
    <div class="v-scroll ${enter && motionOK() ? "v-enter" : ""} ${S.view === "search" ? "hl-on" : ""}" id="vscroll"></div>
    ${pagebarHTML(d)}`;
  const vs = $("#vscroll"), aw = vs.clientWidth - 56, ah = vs.clientHeight - 136;
  vs.innerHTML = d.pages.map((_, i) => {
    const p = pageInfo(d, i); let s = Math.min(aw / p.w, ah / p.h);
    if (S.zoom === "width") s = aw / p.w;
    if (S.zoom === "150") s = s * 1.5;
    return sheetAt(d, i, s);
  }).join("");
  if (S.view === "search" && S.q.text.trim()) {
    markPageHits(vs);
    const n = $$("mark.ph", vs).length, m = $(".pagebar .match");
    if (m) m.textContent = n ? `${Math.min(S.matchIdx + 1, n)} of ${n}` : "No hits on these pages";
  }
  vs.addEventListener("scroll", () => {
    const sheets = $$(".sheet", vs); let n = 1;
    sheets.forEach((sh, i) => { if (sh.offsetTop - vs.scrollTop < vs.clientHeight / 2) n = i + 1; });
    const el = $("#pgnum"); if (el) el.textContent = `${n} / ${sheets.length}`;
  }, { passive: true });
}
function pagebarHTML(d) {
  const hits = S.view === "search" && S.q.text.trim() ? countHits(d) : 0;
  const zl = S.zoom === "fit" ? "Fit" : S.zoom === "width" ? "Width" : "150%";
  return `<div class="pagebar" role="toolbar" aria-label="Page controls">
    <button class="ibtn" type="button" data-act="page" data-d="-1" aria-label="Previous page">${I.chevL16}</button><span class="num" id="pgnum">1 / ${d.pages.length}</span><button class="ibtn" type="button" data-act="page" data-d="1" aria-label="Next page">${I.chevR16}</button>
    <span class="sep"></span>
    <button class="ibtn" type="button" data-act="zoom" aria-label="Zoom out">${I.minus}</button><button class="btn ghost sm" type="button" data-act="zoomcycle" title="Space">${zl}</button><button class="ibtn" type="button" data-act="zoom" aria-label="Zoom in">${I.plus}</button>
    <span class="sep"></span>
    <button class="ibtn" type="button" data-act="readtext" aria-label="Read text" title="Read text">${I.text}</button>
    ${hits ? `<span class="sep"></span><span class="match">${Math.min(S.matchIdx + 1, hits)} of ${hits}</span><button class="ibtn" type="button" data-act="match" data-d="-1" aria-label="Previous match">${I.chevL16}</button><button class="ibtn" type="button" data-act="match" data-d="1" aria-label="Next match">${I.chevR16}</button>` : ""}
  </div>`;
}
function countHits(d) {
  const words = S.q.text.toLowerCase().split(/\s+/).filter(Boolean);
  const t = docText(d).toLowerCase();
  return words.reduce((n, w) => n + (t.split(w).length - 1), 0);
}
function markPageHits(root) {
  const words = S.q.text.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return;
  const re = new RegExp(`(${words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|")})`, "gi");
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes = []; while (walker.nextNode()) nodes.push(walker.currentNode);
  nodes.forEach((n) => {
    if (!re.test(n.nodeValue)) return; re.lastIndex = 0;
    const span = document.createElement("span");
    span.innerHTML = esc(n.nodeValue).replace(re, '<mark class="ph">$1</mark>');
    n.replaceWith(...span.childNodes);
  });
}

/* Filing panel */
const PH = { from: "Add sender", type: "Choose a type", date: "Date on the paper", deadline: "Add a reminder", note: "Add a note" };
const FKEY = { from: "f", date: "d", note: "n" };
function fieldHTML(d, key, label) {
  const val = d[key], pend = d.pending && d.pending[key], editing = S.editing === key;
  let inner;
  if (editing) inner = `<input id="fin" value="${esc(val || "")}" placeholder="${esc(PH[key])}" autocomplete="off" spellcheck="false" aria-label="${esc(label)}">`;
  else if (val) inner = key === "deadline" ? `<span class="dl">${I.bell}<span class="when">${esc(val)}</span><span style="color:var(--ink-3)">· remind 7 days before</span></span>` : `<span class="${key === "date" ? "num" : ""}">${esc(val)}</span>`;
  else if (pend) inner = `<button class="gv" type="button" data-act="accept" data-k="${key}" title="Suggested. Select to accept">${esc(pend)}</button>`;
  else inner = `<span class="ph">${esc(PH[key])}</span>`;
  const fk = FKEY[key] && !editing ? `<span class="fk">${K(FKEY[key])}</span>` : "";
  return `<div class="f" data-field="${key}"><span class="fl">${esc(label)}</span><div class="fv ${editing ? "editing" : ""} ${!val && !pend ? "empty" : ""}" data-act="edit" data-k="${key}">${inner}${fk}</div>${editing ? `<div id="ac-host"></div>` : ""}</div>`;
}
function tagsFieldHTML(d) {
  const editing = S.editing === "tags";
  const chips = d.tags.map((t) => `<span class="tagc ${S.justTagged === t ? "pop" : ""}"><i style="background:${tagColor(t)}"></i>${esc(t)}<button type="button" data-act="untag" data-t="${esc(t)}" aria-label="Remove ${esc(t)}">${I.x}</button></span>`).join("");
  const ghosts = ((d.pending && d.pending.tags) || []).map((t) => `<button class="gv tagg" type="button" data-act="accept-tag" data-t="${esc(t)}" title="Suggested. Select to accept"><i style="background:${tagColor(t)}"></i>${esc(t)}</button>`).join("");
  const input = editing ? `<input id="fin" placeholder="Tag name" autocomplete="off" spellcheck="false" aria-label="Add tag" style="flex:1">` : `<button class="addtag" type="button" data-act="edit" data-k="tags">${I.plus12}Add tag</button><span class="fk">${K("t")}</span>`;
  return `<div class="f" data-field="tags"><span class="fl">Tags</span><div class="fv ${editing ? "editing" : ""}" data-act="edit" data-k="tags">${chips}${ghosts}${input}</div>${editing ? `<div id="ac-host"></div>` : ""}</div>`;
}
function pendingCount(d) { const p = d.pending; if (!p) return 0; return ["from", "type", "date", "deadline"].filter((k) => p[k]).length + (p.tags || []).length; }
function renderInsp(flash = []) {
  const el = $("#insp"); if (!el) return;
  if (S.sel.size > 1) { el.innerHTML = batchHTML(); return; }
  const d = doc(S.cur);
  if (!d) { el.innerHTML = ""; return; }
  if (d.processing) {
    el.innerHTML = `<div class="insp-scroll"><div class="title-static">${esc(d.title)}</div><div class="proc-card"><div class="row"><b>${esc(d.stage || "Reading text…")}</b><span>${d.progress || 0}%</span></div><div class="meter"><i style="width:${d.progress || 0}%"></i></div><p>Plico is recognising the text on this computer. You can keep reviewing other documents.</p></div></div>`;
    return;
  }
  const n = pendingCount(d);
  const sug = n ? `<div class="sugline"><div><b>${n} suggestion${n > 1 ? "s" : ""}</b><span>${esc(d.pending.why || "From the document text")}</span></div><button class="btn sm" type="button" data-act="accept-all">Accept all ${K("a")}</button></div>` : "";
  const foot = d.status === "inbox"
    ? `<button class="btn primary" type="button" data-act="review">Mark reviewed ${K("r")}</button><button class="ibtn" type="button" data-act="trash" aria-label="Move to Trash" title="Move to Trash (X)">${I.trash}</button><button class="ibtn" type="button" data-act="docmenu" aria-label="More actions" aria-haspopup="menu">${I.more}</button>`
    : d.status === "trash"
      ? `<div class="reviewed-badge">${I.trash} In Trash</div><button class="btn sm" type="button" data-act="restore">Restore</button>`
      : `<div class="reviewed-badge">${I.check} Reviewed</div><button class="btn sm" type="button" data-act="unreview">Return to Inbox</button><button class="ibtn" type="button" data-act="docmenu" aria-label="More actions" aria-haspopup="menu">${I.more}</button>`;
  el.innerHTML = `<div class="insp-scroll">
      <textarea class="title-in" id="title-in" rows="1" aria-label="Title" spellcheck="false">${esc(d.title)}</textarea>
      <button class="path" type="button" data-act="reveal" title="Show in folder">${I.folder}<span>${esc(d.path)}</span></button>
      ${sug}
      <div class="fields">
        ${fieldHTML(d, "from", "From")}${fieldHTML(d, "type", "Type")}${fieldHTML(d, "date", "Date")}${tagsFieldHTML(d)}${fieldHTML(d, "deadline", "Reminder")}${fieldHTML(d, "note", "Note")}
      </div>
      <details class="details"><summary>${I.chevR} Details</summary><dl class="dgrid">
        <dt>Added</dt><dd>${esc(d.added)}</dd><dt>Source</dt><dd>${esc(d.source)}</dd><dt>Pages</dt><dd>${d.pages.length}</dd><dt>Size</dt><dd>${esc(d.size)}</dd>
        <dt>Text</dt><dd class="${d.noText ? "" : "okt"}">${d.noText ? "None yet" : "Searchable · English"}</dd><dt>Versions</dt><dd>Original kept</dd></dl></details>
    </div>
    <div class="insp-foot">${foot}</div>`;
  flash.forEach((f) => { const x = el.querySelector(`[data-field="${f}"]`); if (x && motionOK()) x.classList.add("flash"); });
  S.justTagged = null;
  wireInsp(d);
}
function wireInsp(d) {
  const ti = $("#title-in");
  if (ti) {
    ti.addEventListener("keydown", (e) => { if (e.key === "Enter") { e.preventDefault(); ti.blur(); } if (e.key === "Escape") { ti.value = d.title; ti.blur(); } });
    ti.addEventListener("change", () => { const v = ti.value.trim(); if (v && v !== d.title) { const old = d.title; d.title = v; renderRows(); toast({ text: "Title saved", undo: () => { d.title = old; renderRows(); renderInsp(); } }); } });
  }
  const fin = $("#fin");
  if (fin) {
    fin.focus(); renderAC();
    fin.addEventListener("input", () => { S.acIdx = 0; renderAC(); });
    fin.addEventListener("keydown", (e) => {
      const opts = acOptions();
      if (e.key === "ArrowDown") { e.preventDefault(); S.acIdx = Math.min(opts.length - 1, S.acIdx + 1); renderAC(); }
      else if (e.key === "ArrowUp") { e.preventDefault(); S.acIdx = Math.max(0, S.acIdx - 1); renderAC(); }
      else if (e.key === "Enter" || (e.key === "Tab" && fin.value)) { e.preventDefault(); commitField(opts[S.acIdx] ? opts[S.acIdx].value : fin.value.trim()); }
      else if (e.key === "Escape") { e.preventDefault(); S.editing = null; renderInsp(); }
      else if (e.key === "Backspace" && !fin.value && S.editing === "tags" && d.tags.length) { d.tags.pop(); renderInsp(); }
    });
    fin.addEventListener("blur", () => later(() => { if (S.editing && document.activeElement !== $("#fin")) { S.editing = null; renderInsp(); } }, 120));
  }
}
function acOptions() {
  const fin = $("#fin"); if (!fin) return [];
  const q = fin.value.trim().toLowerCase(), d = doc(S.cur), key = S.editing;
  let pool = [];
  if (key === "tags") pool = TAGS.map((t) => t.name).filter((t) => !d.tags.includes(t));
  else if (key === "from") pool = CORRESPONDENTS;
  else if (key === "type") pool = DOC_TYPES;
  else return [];
  let out = pool.filter((t) => !q || t.toLowerCase().includes(q)).slice(0, 6).map((t) => ({ value: t, label: t }));
  if (q && !pool.some((t) => t.toLowerCase() === q)) out.push({ value: fin.value.trim(), label: `Create “${fin.value.trim()}”`, create: true });
  return out;
}
function renderAC() {
  const host = $("#ac-host"); if (!host) return;
  const opts = acOptions();
  host.innerHTML = opts.length ? `<div class="ac" role="listbox">${opts.map((o, i) => `<div role="option" aria-selected="${i === S.acIdx}" class="${i === S.acIdx ? "on" : ""}" data-act="ac" data-v="${esc(o.value)}">${S.editing === "tags" && !o.create ? `<i style="background:${tagColor(o.value)}"></i>` : ""}${esc(o.label)}${S.editing === "tags" && !o.create ? `<span>${(TAGS.find((t) => t.name === o.value) || {}).count || ""}</span>` : ""}</div>`).join("")}</div>` : "";
}
function commitField(v) {
  const d = doc(S.cur), key = S.editing;
  if (!d || !key) return;
  if (key === "tags") {
    if (v && !d.tags.includes(v)) { d.tags.push(v); S.justTagged = v; if (d.pending && d.pending.tags) d.pending.tags = d.pending.tags.filter((t) => t !== v); }
    S.acIdx = 0; renderInsp(); return;
  }
  const old = d[key]; d[key] = v;
  if (d.pending && d.pending[key]) delete d.pending[key];
  S.editing = null; renderInsp([key]);
  if (key === "from") renderRows();
  if (old !== v) S.undo.push(() => { d[key] = old; renderInsp(); renderRows(); });
}
function startEdit(key) {
  if (layoutName() !== "review" && !S.info) return;
  const d = doc(S.cur); if (!d || d.processing || S.sel.size > 1) return;
  S.editing = key; S.acIdx = 0; renderInsp();
}

/* ───────── The review loop ───────── */
function stageRect(el) {
  const w = $("#win").getBoundingClientRect(), r = el.getBoundingClientRect();
  return { x: (r.left - w.left) / stageScale, y: (r.top - w.top) / stageScale, w: r.width / stageScale, h: r.height / stageScale };
}
function fileAway(sheetEl, viewerEl) {
  if (!sheetEl || !viewerEl || !motionOK()) return;
  const a = stageRect(sheetEl), v = stageRect(viewerEl);
  const g = document.createElement("div");
  g.className = "fghost";
  Object.assign(g.style, { left: a.x + "px", top: a.y + "px", width: a.w + "px", height: a.h + "px" });
  g.innerHTML = sheetEl.innerHTML;
  const tray = document.createElement("div");
  tray.className = "tray";
  const tx = v.x + v.w / 2, ty = v.y + v.h - 92;
  Object.assign(tray.style, { left: tx - 32 + "px", top: ty - 6 + "px" });
  tray.innerHTML = `<svg viewBox="0 0 468 388" width="64" height="53" style="position:absolute;top:-27px" aria-hidden="true"><path fill="currentColor" d="${MARK_TRAY}"/></svg>`;
  const win = $("#win"); win.append(g, tray);
  const sc = 0.085, dx = tx - a.x - (a.w * sc) / 2, dy = ty - a.y - (a.h * sc) / 2 - 6;
  requestAnimationFrame(() => tray.classList.add("show"));
  g.animate([
    { transform: "translate(0,0) scale(1)", opacity: 1, offset: 0 },
    { transform: `translate(${dx}px, ${dy}px) scale(${sc})`, opacity: 1, offset: 0.86 },
    { transform: `translate(${dx}px, ${dy + 10}px) scale(${sc})`, opacity: 0, offset: 1 },
  ], { duration: 380, easing: "cubic-bezier(0.4, 0, 0.2, 1)", fill: "forwards" });
  later(() => { g.remove(); tray.remove(); }, 640);
}
function leaveRow(id) {
  const row = $(`.row[data-id="${id}"]`);
  if (!row || !motionOK()) { renderRows(); return; }
  row.style.height = row.offsetHeight + "px";
  row.classList.add("leaving");
  later(renderRows, 230);
}
function review(stay = false) {
  if (S.sel.size > 1) return batchReview();
  const d = doc(S.cur);
  if (!d || d.status !== "inbox" || d.processing) return;
  if (S.view !== "inbox") {
    const before = DOCS.filter((x) => x.status === "inbox").length;
    d.status = "reviewed"; bumpInbox(before, before - 1); renderRows(); renderInsp();
    pushUndo(() => { d.status = "inbox"; renderAll(); });
    toast({ text: "Marked reviewed", dim: d.title, undo: true, icon: I.check });
    return;
  }
  const now = Date.now(), fast = now - S.lastReviewAt < 1200;
  S.lastReviewAt = now;
  if (!fast) fileAway($("#vscroll .sheet"), $("#viewer"));
  const list = viewDocs(), idx = list.indexOf(d), before = list.length;
  d.status = "reviewed"; S.reviewedToday++;
  const left = viewDocs(), next = left[idx] || left[idx - 1] || null;
  if (!stay || !next) S.cur = next ? next.id : null;
  S.editing = null;
  bumpInbox(before, left.length);
  pushUndo(() => { d.status = "inbox"; S.reviewedToday--; S.cur = d.id; renderAll(); toast({ text: "Back in Inbox", dim: d.title }); });
  toast({ text: "Marked reviewed", dim: d.title, undo: true, icon: I.check });
  if (!left.length) { later(() => renderContent(), fast ? 0 : 260); return; }
  leaveRow(d.id); renderViewer(true); renderInsp();
}
function trashDoc() {
  if (S.sel.size > 1) return batchTrash();
  const d = doc(S.cur); if (!d || d.status === "trash") return;
  const list = viewDocs(), idx = list.indexOf(d), wasInbox = d.status === "inbox", prev = d.status, before = DOCS.filter((x) => x.status === "inbox").length;
  d.status = "trash";
  const left = viewDocs(), next = left[idx] || left[idx - 1] || null;
  S.cur = next ? next.id : null; S.editing = null;
  if (wasInbox) bumpInbox(before, before - 1);
  pushUndo(() => { d.status = prev; S.cur = d.id; renderAll(); toast({ text: "Restored", dim: d.title }); });
  toast({ text: "Moved to Trash", dim: d.title, undo: true, icon: I.trash });
  if (!left.length) { renderContent(); return; }
  leaveRow(d.id); renderViewer(true); renderInsp();
  if (!wasInbox) renderSidebar();
}
function acceptAll() {
  const d = doc(S.cur); if (!d || !d.pending || S.sel.size > 1) return;
  const p = d.pending, flash = [], snapshot = JSON.stringify({ from: d.from, type: d.type, date: d.date, deadline: d.deadline, tags: d.tags, pending: p });
  ["from", "type", "date", "deadline"].forEach((k) => { if (p[k]) { d[k] = p[k]; flash.push(k); } });
  (p.tags || []).forEach((t) => { if (!d.tags.includes(t)) d.tags.push(t); });
  if ((p.tags || []).length) flash.push("tags");
  const n = pendingCount(d); d.pending = null;
  renderInsp(flash); renderRows(); renderSidebar();
  pushUndo(() => { Object.assign(d, JSON.parse(snapshot)); renderInsp(); renderRows(); });
  toast({ text: `Accepted ${n} suggestion${n > 1 ? "s" : ""}`, undo: true, icon: I.check });
}
function acceptOne(key, tag) {
  const d = doc(S.cur); if (!d || !d.pending) return;
  if (tag) { d.tags.push(tag); d.pending.tags = d.pending.tags.filter((t) => t !== tag); S.justTagged = tag; renderInsp(); }
  else { d[key] = d.pending[key]; delete d.pending[key]; renderInsp([key]); }
  if (!pendingCount(d)) { d.pending = null; renderInsp(); }
  renderRows();
}
function move(delta, extend = false) {
  const list = viewDocs(); if (!list.length) return;
  let i = list.findIndex((d) => d.id === S.cur);
  i = Math.max(0, Math.min(list.length - 1, i < 0 ? 0 : i + delta));
  if (extend) { if (!S.sel.size && S.cur) S.sel.add(S.cur); S.sel.add(list[i].id); }
  else S.sel.clear();
  S.cur = list[i].id; S.editing = null; S.matchIdx = 0;
  if (layoutName() === "grid") { syncCards(); return; }
  renderRows(); renderViewer(); renderInsp();
}

/* Batch selection */
function fanHTML() {
  const ds = [...S.sel].map(doc).filter(Boolean), shown = ds.slice(0, 4);
  return `<div class="stackview"><div><div class="fan">${shown.map((d, i) => sheetAt(d, 0, fitScale(d, 0, 190, 268), "sheet", `;transform:translate(-50%,-50%);z-index:${i}`)).join("")}</div>
    <div class="stack-cap"><b>${ds.length} documents selected</b><span>${K("esc")} clears the selection</span></div></div></div>`;
}
function fanOut() {
  const sheets = $$(".fan .sheet"), n = sheets.length;
  const rot = [-7, -2.5, 3, 8], dx = [-40, -13, 14, 40], dy = [10, -4, 2, 12];
  const off = (4 - n) >> 1;
  requestAnimationFrame(() => requestAnimationFrame(() => sheets.forEach((s, i) => { const j = i + off; s.style.transform = `translate(-50%,-50%) translate(${dx[j]}px,${dy[j]}px) rotate(${rot[j]}deg)`; })));
}
function batchHTML() {
  const ds = [...S.sel].map(doc).filter(Boolean), n = ds.length;
  const counts = {}; ds.forEach((d) => d.tags.forEach((t) => { counts[t] = (counts[t] || 0) + 1; }));
  const same = (k) => { const vals = new Set(ds.map((d) => d[k] || "")); return vals.size === 1 ? [...vals][0] : null; };
  const val = (k, label) => { const v = same(k); return `<div class="f"><span class="fl">${label}</span><div class="fv ${v ? "" : "empty"}">${v ? esc(v) : v === "" ? `<span class="ph">Not set</span>` : `<span class="mixed">Mixed</span>`}</div></div>`; };
  const allInbox = ds.every((d) => d.status === "inbox");
  return `<div class="insp-scroll">
    <div><div class="title-static">${n} documents</div><div class="sub" style="color:var(--ink-3);font-size: 13px;margin-top:3px">Changes apply to every selected document. ${K("mod+z")} undoes them.</div></div>
    <div class="bsec"><h4>Tags</h4>
      ${Object.entries(counts).map(([t, c]) => `<div class="tagcount"><i style="background:${tagColor(t)}"></i>${esc(t)}<span class="of">${c} of ${n}</span><span class="part"><b style="width:${(c / n) * 100}%"></b></span></div>`).join("") || `<div class="tagcount" style="color:var(--ink-3)">No tags yet</div>`}
      <button class="addtag" type="button" data-act="batch-tag" style="margin-top:2px">${I.plus12}Add a tag to all ${n}</button>
    </div>
    <div class="fields">${val("from", "From")}${val("date", "Date")}${val("type", "Type")}</div>
    <div class="bsec"><h4>Actions</h4><div class="actlist">
      <button type="button" data-act="todo" data-msg="Move to folder">${I.move}Move to folder…</button>
      <button type="button" data-act="todo" data-msg="Set document date">${I.cal}Set document date…</button>
      <button type="button" data-act="todo" data-msg="Combine into one PDF">${I.combine}Combine into one PDF…</button>
      <button type="button" data-act="todo" data-msg="Export copies">${I.share}Export copies…</button>
      <button type="button" data-act="todo" data-msg="Export as binder">${I.binder}Export as binder…</button>
      <button type="button" data-act="todo" data-msg="Make searchable copies">${I.ocr}Make searchable copies</button>
      <button type="button" class="danger" data-act="trash">${I.trash}Move ${n} to Trash${K("x")}</button>
    </div></div>
  </div>
  <div class="insp-foot">${allInbox ? `<button class="btn primary" type="button" data-act="review">Mark ${n} reviewed ${K("r")}</button>` : `<button class="btn" type="button" style="flex:1" data-act="todo" data-msg="Return to Inbox">Return ${n} to Inbox</button>`}</div>`;
}
function batchReview() {
  const ds = [...S.sel].map(doc).filter((d) => d && d.status === "inbox"); if (!ds.length) return;
  const before = DOCS.filter((d) => d.status === "inbox").length;
  ds.forEach((d) => { d.status = "reviewed"; }); S.reviewedToday += ds.length;
  S.sel.clear(); const left = viewDocs(); S.cur = left[0] ? left[0].id : null;
  bumpInbox(before, left.length);
  pushUndo(() => { ds.forEach((d) => { d.status = "inbox"; }); S.reviewedToday -= ds.length; ds.forEach((d) => S.sel.add(d.id)); S.cur = ds[0].id; renderAll(); });
  toast({ text: `Marked ${ds.length} reviewed`, undo: true, icon: I.check });
  renderContent();
}
function batchTrash() {
  const ds = [...S.sel].map(doc).filter(Boolean);
  const prev = ds.map((d) => d.status); ds.forEach((d) => { d.status = "trash"; });
  S.sel.clear(); const left = viewDocs(); S.cur = left[0] ? left[0].id : null;
  pushUndo(() => { ds.forEach((d, i) => { d.status = prev[i]; S.sel.add(d.id); }); S.cur = ds[0].id; renderAll(); });
  toast({ text: `Moved ${ds.length} to Trash`, undo: true, icon: I.trash });
  renderAll();
}
function selectAll() { const list = viewDocs(); if (!list.length) return; S.sel = new Set(list.map((d) => d.id)); if (!S.cur) S.cur = list[0].id; renderContent(); }

/* Inbox clear */
function zeroHTML() {
  const ups = upcoming().slice(0, 4), n = S.reviewedToday;
  return `<div class="zero"><div class="zero-in">
    ${markSVG("zero-mark")}
    <h2>Inbox clear</h2>
    <p>${n ? `You reviewed ${n} document${n > 1 ? "s" : ""} this session. ` : "Nothing is waiting for review. "}New paperwork appears here when you scan, add files or drop them on this window.</p>
    <div class="zero-actions"><button class="btn" type="button" data-act="scan">${I.scan} Scan</button><button class="btn primary" type="button" data-act="addfiles">${I.upload} Add files</button></div>
    ${ups.length ? `<div class="next"><h4>Coming up</h4>${ups.map((u) => `<button type="button" data-act="open" data-id="${u.d.id}">${thumb(u.d, 18, 25)}<span>${esc(u.label)} <span style="color:var(--ink-3)">· ${esc(u.d.title)}</span></span><span class="when">${esc(u.when)}</span></button>`).join("")}</div>` : ""}
  </div></div>`;
}

/* Library grid */
function gridHTML() {
  const ds = viewDocs(), groups = [];
  ds.forEach((d) => {
    const dt = parseDay(d.date || (d.pending && d.pending.date) || "") || TODAY;
    const name = `${MONTHS[dt.getMonth()]} ${dt.getFullYear()}`;
    let g = groups.find((x) => x.name === name); if (!g) groups.push(g = { name, dt, items: [] }); g.items.push(d);
  });
  groups.sort((a, b) => b.dt - a.dt);
  return groups.map((g) => `<div class="month">${g.name}<span>${g.items.length}</span></div><div class="cards">${g.items.map((d) => `
    <button class="card" type="button" data-act="card" data-id="${d.id}" aria-selected="${d.id === S.cur}">
      <span class="tw">${sheetAt(d, 0, fitScale(d, 0, 132, 176), "thumb")}</span>
      <span class="ct1">${esc(d.title)}</span>
      <span class="ct2">${d.tags.map((t) => `<i style="background:${tagColor(t)}"></i>`).join("")}${esc(d.from || (d.status === "inbox" ? "In Inbox" : "No sender"))}${d.date ? " · " + esc(shortDate(d.date)) : ""}</span>
    </button>`).join("")}</div>`).join("");
}
function syncCards() {
  $$(".card").forEach((c) => c.setAttribute("aria-selected", String(c.dataset.id === S.cur)));
  const cur = $(`.card[data-id="${S.cur}"]`); if (cur) cur.scrollIntoView({ block: "nearest" });
}

/* ───────── Toasts and undo ───────── */
let toastTimer = null;
function pushUndo(fn) { S.undo.push(fn); if (S.undo.length > 30) S.undo.shift(); }
function undo() { const fn = S.undo.pop(); if (fn) fn(); else toast({ text: "Nothing to undo" }); }
function toast({ text, dim = "", undo: canUndo = false, icon = "" }) {
  const host = $("#toast-host");
  host.innerHTML = `<div class="toast" role="status">${icon}<span>${esc(text)}</span>${dim ? `<span class="tdim">${esc(dim)}</span>` : ""}${canUndo ? `<button type="button" data-act="undo">Undo ${K("mod+z")}</button>` : ""}</div>`;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { const t = $(".toast", host); if (t) { t.classList.add("out"); setTimeout(() => t.remove(), 170); } }, canUndo ? 5200 : 2600);
}

/* ───────── Full render ───────── */
function renderAll() {
  const win = $("#win");
  win.dataset.platform = S.platform;
  win.toggleAttribute("data-welcome", S.overlay === "welcome");
  renderSidebar(); renderToolbar(); renderContent(); renderOverlay();
}
