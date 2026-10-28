// KareNot for IINA: timestamped playtest notes, sharing the data files of the Windows app (see iina/README.md).
// Main entry: runs once per player window.
"use strict";

const { core, mpv, event, sidebar, overlay, menu, file, utils, preferences } = iina;
const F = require("./lib/format.js");

function log(msg) {
  try { iina.console.log("[KareNot] " + msg); } catch (e) { /* ignore */ }
}

// ---------- Paths (same layout as the Windows app, rooted at ~/Desktop) ----------

const DESKTOP = utils.resolvePath("~/Desktop").replace(/\/+$/, "");
const COMBINED = DESKTOP + "/video_notlari.txt";
const WORK = DESKTOP + "/Video Notlari";
const NOTES_DIR = WORK + "/notlar";
const SS_DIR = WORK + "/ss";
const REPORT = WORK + "/rapor.md";
const REVIEW = WORK + "/inceleme.json";

function videoFile(video) {
  return NOTES_DIR + "/" + F.safeFileName(video) + ".txt";
}

// ---------- File writing: temp file + rename, serialized through one queue ----------

// Content queued for writing but not on disk yet (null = queued delete); reads look here first.
const pending = {};
let queue = Promise.resolve();
let dirsReady = false;

function enqueue(job) {
  queue = queue.then(job).catch((e) => log("write error: " + e));
  return queue;
}

async function ensureDirs() {
  if (dirsReady) return true;
  try {
    const r = await utils.exec("/bin/mkdir", ["-p", NOTES_DIR, SS_DIR]);
    dirsReady = r.status === 0;
    if (!dirsReady) log("mkdir failed: " + r.stderr);
  } catch (e) {
    log("mkdir error: " + e);
  }
  return dirsReady;
}

function writeAtomic(path, content) {
  pending[path] = content;
  return enqueue(async () => {
    await ensureDirs();
    const tmp = path + ".tmp";
    let ok = false;
    try {
      file.write(tmp, content);
      const r = await utils.exec("/bin/mv", ["-f", tmp, path]);
      ok = r.status === 0;
      if (!ok) log("mv failed: " + r.stderr);
    } catch (e) {
      log("atomic write failed for " + path + ": " + e);
    }
    if (!ok) {
      try {
        file.write(path, content);
        ok = true;
      } catch (e) {
        log("write failed for " + path + ": " + e);
        osd("KareNot: dosya yazılamadı (" + path.split("/").pop() + ")");
      }
      try { if (file.exists(tmp)) file.delete(tmp); } catch (e) { /* ignore */ }
    }
    if (pending[path] === content) delete pending[path];
  });
}

function deleteFile(path) {
  pending[path] = null;
  return enqueue(async () => {
    try { if (file.exists(path)) file.delete(path); } catch (e) { log("delete failed: " + e); }
    if (pending[path] === null) delete pending[path];
  });
}

function readText(path) {
  if (Object.prototype.hasOwnProperty.call(pending, path)) return pending[path];
  try {
    if (!file.exists(path)) return null;
    const s = file.read(path);
    return s == null ? null : s;
  } catch (e) {
    log("read failed for " + path + ": " + e);
    return null;
  }
}

function listDir(path) {
  try {
    if (!file.exists(path)) return [];
    return file.list(path, { includeSubDir: false }) || [];
  } catch (e) {
    return [];
  }
}

// ---------- Notes ----------

let notes = [];   // all notes of all videos, each with an in-memory id
let nextId = 1;

function withIds(list) {
  for (const n of list) n.id = nextId++;
  return list;
}

// All per-video files, plus combined-file notes whose video has no per-video file (NoteStore.Load).
function readAllFromDisk() {
  const list = [];
  const videos = {};
  const paths = listDir(NOTES_DIR)
    .filter((e) => !e.isDir && /\.txt$/i.test(e.filename))
    .map((e) => NOTES_DIR + "/" + e.filename);
  // Files queued for writing but not created yet.
  for (const p of Object.keys(pending)) {
    if (p.indexOf(NOTES_DIR + "/") === 0 && /\.txt$/.test(p) && paths.indexOf(p) < 0) paths.push(p);
  }
  for (const p of paths) {
    for (const n of F.parseNotes(readText(p))) {
      list.push(n);
      videos[n.video] = true;
    }
  }
  const migrated = [];
  for (const n of F.parseNotes(readText(COMBINED))) {
    if (videos[n.video]) continue;
    list.push(n);
    if (migrated.indexOf(n.video) < 0) migrated.push(n.video);
  }
  F.sortNotes(list);
  return { list: list, migrated: migrated };
}

function reloadAll() {
  const keepSelected = selectedNote();
  const r = readAllFromDisk();
  notes = withIds(r.list);
  for (const v of r.migrated) saveVideoFile(v);
  // Keep the selection on the same note if it is still there.
  selectedId = null;
  if (keepSelected) {
    const same = notes.find((n) => n.video === keepSelected.video && n.t === keepSelected.t && n.kind === keepSelected.kind);
    if (same) selectedId = same.id;
  }
}

function saveVideoFile(video) {
  const mine = notes.filter((n) => n.video === video);
  if (mine.length === 0) {
    if (file.exists(videoFile(video)) || pending[videoFile(video)] != null) deleteFile(videoFile(video));
  } else {
    writeAtomic(videoFile(video), F.serialize(mine));
  }
}

// Writes this video's file and the combined file. Other videos are taken from disk first, so a second IINA window
// or the Windows app (on a synced Desktop) does not get its notes overwritten by stale memory.
function persist(video) {
  const fromDisk = readAllFromDisk().list.filter((n) => n.video !== video);
  notes = withIds(fromDisk).concat(notes.filter((n) => n.video === video));
  F.sortNotes(notes);
  saveVideoFile(video);
  writeAtomic(COMBINED, F.serialize(notes));
}

function noteById(id) {
  return notes.find((n) => n.id === id) || null;
}

// ---------- Review state (inceleme.json) ----------

function reviewRoot() {
  return F.parseReview(readText(REVIEW));
}

function reviewEntry(video) {
  const e = reviewRoot()[video];
  return e && typeof e === "object" && !Array.isArray(e) ? e : {};
}

function reviewSet(video, patch) {
  if (!video) return;
  const root = reviewRoot();
  const e = root[video] && typeof root[video] === "object" && !Array.isArray(root[video]) ? root[video] : {};
  Object.keys(patch).forEach((k) => { e[k] = patch[k]; });
  root[video] = e;
  writeAtomic(REVIEW, JSON.stringify(root, null, 2));
}

function round2(x) {
  return Math.round(x * 100) / 100;
}

// ---------- Player helpers ----------

let windowLoaded = false;

function osd(msg) {
  if (!windowLoaded) return;
  try { core.osd(msg); } catch (e) { log("osd: " + e); }
}

function position() {
  const p = core.status.position;
  return typeof p === "number" && isFinite(p) ? p : null;
}

function duration() {
  let d = core.status.duration;
  if (typeof d !== "number" || !(d > 0)) {
    try { d = mpv.getNumber("duration"); } catch (e) { d = null; }
  }
  return typeof d === "number" && d > 0 ? d : null;
}

function currentVideoName() {
  let name = "";
  try { name = mpv.getString("filename") || ""; } catch (e) { name = ""; }
  if (!name) {
    const url = core.status.url || "";
    try { name = decodeURIComponent(url.split("?")[0].split("/").pop() || ""); } catch (e) { name = ""; }
  }
  return name;
}

function seekExact(t) {
  mpv.command("seek", [String(Math.max(0, t)), "absolute+exact"]);
}

function num(prop) {
  try {
    const v = mpv.getNumber(prop);
    return typeof v === "number" && isFinite(v) ? v : null;
  } catch (e) {
    return null;
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// ---------- UI state ----------

let video = null;          // open video file name
let tab = "game";          // game | edit | keys
let listTab = "game";      // last list tab, used by [ ] and L while the shortcuts tab is open
let selectedId = null;
let draft = "";            // text in the sidebar input
let inputFocused = false;  // a text field in the sidebar has keyboard focus
let ssEnabled = preferences.get("ssEnabled") !== false;
let shotCounter = 0;

function selectedNote() {
  return selectedId != null ? noteById(selectedId) : null;
}

function videoNotes() {
  return video ? notes.filter((n) => n.video === video) : [];
}

// Rows of a tab's list. Game: one row per note. Kurgu: BIP rows (with range) and KES start/end pairs as one row.
function rows(which) {
  const mine = videoNotes();
  if (which === "game") {
    return mine.filter((n) => F.isGame(n.kind)).sort((a, b) => a.t - b.t).map((n) => ({
      id: n.id, ids: [n.id], t: n.t, time: F.fmt(n.t), kind: n.kind, label: F.label(n.kind), color: F.color(n.kind),
      text: n.text, status: n.status || "", severity: n.severity || "", shot: !!n.shot, warn: "",
    }));
  }
  const result = [];
  for (const n of mine.filter((x) => x.kind === "BIP")) {
    const ranged = n.end != null && n.end > n.t;
    result.push({
      id: n.id, ids: [n.id], t: n.t, time: ranged ? F.fmt(n.t) + "–" + F.fmt(n.end) : F.fmt(n.t),
      kind: "BIP", label: F.label("BIP"), color: F.color("BIP"), text: n.text, warn: "", canEnd: true,
    });
  }
  for (const c of F.cuts(mine)) {
    if (c.start && c.end) {
      const len = Math.round(c.end.t - c.start.t);
      result.push({
        id: c.start.id, ids: [c.start.id, c.end.id], t: c.start.t,
        time: F.fmtShort(c.start.t) + "–" + F.fmtShort(c.end.t) + " · " + len + " sn",
        kind: "KES", label: "KES", color: F.color("KES_BASLA"), text: c.start.text || c.end.text, warn: "",
      });
    } else {
      const n = c.start || c.end;
      result.push({
        id: n.id, ids: [n.id], t: n.t, time: F.fmt(n.t), kind: n.kind, label: F.label(n.kind), color: F.color(n.kind),
        text: n.text, warn: c.start ? "Bitişi yok" : "Başlangıcı yok",
      });
    }
  }
  return result.sort((a, b) => a.t - b.t);
}

function pushState() {
  const rev = video ? reviewEntry(video) : {};
  sidebar.postMessage("state", {
    video: video,
    tab: tab,
    selectedId: selectedId,
    ssEnabled: ssEnabled,
    area: areaRect != null,
    areaMode: areaMode,
    reviewed: typeof rev.reviewed === "number" && rev.reviewed > 0 ? F.fmt(rev.reviewed) : "",
    loop: loop != null,
    game: rows("game"),
    edit: rows("edit"),
  });
}

function setTab(which) {
  tab = which;
  if (which === "game" || which === "edit") listTab = which;
}

// ---------- Adding notes ----------

let busy = false;

async function addNote(kind, textArg) {
  if (!video) { osd("KareNot: önce bir video aç"); return; }
  if (busy) return;
  const t = position();
  if (t == null) return;
  const game = F.isGame(kind);
  const text = F.clean(textArg != null ? textArg : game ? draft : "");
  if (kind === "NOT" && !text) {
    setTab("game");
    sidebar.show();
    pushState();
    sidebar.postMessage("focus-input", {});
    osd("NOT için önce kutuya yaz");
    return;
  }
  busy = true;
  try {
    let shot = null;
    // A marked area always gets a screenshot, like the Windows app.
    if (game && (ssEnabled || areaRect)) shot = await takeShot(kind, t);
    stopLoop();
    const n = { id: nextId++, t: t, kind: kind, text: text, video: video, shot: shot,
      status: null, severity: null, end: null, clip: null };
    notes.push(n);
    persist(video);
    selectedId = n.id;
    setTab(game ? "game" : "edit");
    if (game || textArg != null) {
      draft = "";
      sidebar.postMessage("clear-input", {});
    }
    pushState();
    osd(F.label(kind) + " eklendi — " + F.fmt(t) + (shot ? " (SS)" : ""));
  } finally {
    busy = false;
  }
}

async function takeShot(kind, t) {
  if (!(await ensureDirs())) { osd("SS klasörü oluşturulamadı"); return null; }
  const name = F.shotName(kind, new Date(), t, ++shotCounter);
  const path = SS_DIR + "/" + name;
  try {
    mpv.command("screenshot-to-file", [path, "video"]);
  } catch (e) {
    log("screenshot failed: " + e);
  }
  let ok = false;
  for (let i = 0; i < 20 && !ok; i++) {
    ok = file.exists(path);
    if (!ok) await sleep(100);
  }
  if (!ok) { osd("SS alınamadı"); return null; }
  if (areaRect) {
    const frac = areaRect;
    endAreaMode();
    const burned = await burnArea(path, frac);
    if (!burned) osd("Alan çerçevesi çizilemedi; SS tam kare olarak kaydedildi");
  }
  return name;
}

// ---------- List actions ----------

function gotoNote(n) {
  stopLoop();
  selectedId = n.id;
  seekExact(n.t - 2);
  pushState();
}

function removeIds(ids) {
  const set = ids.filter((id) => noteById(id));
  if (set.length === 0 || !video) return;
  notes = notes.filter((n) => set.indexOf(n.id) < 0);
  if (set.indexOf(selectedId) >= 0) selectedId = null;
  persist(video);
  pushState();
}

function updateNote(id, patch) {
  const n = noteById(id);
  if (!n) return;
  Object.keys(patch).forEach((k) => { n[k] = patch[k]; });
  persist(n.video);
  pushState();
}

function navigate(dir) {
  const list = rows(listTab);
  if (list.length === 0) { osd("Bu sekmede not yok"); return; }
  let idx = list.findIndex((r) => r.ids.indexOf(selectedId) >= 0);
  if (idx < 0) {
    const p = position() || 0;
    if (dir > 0) {
      idx = list.findIndex((r) => r.t > p + 0.5);
      if (idx < 0) idx = list.length - 1;
    } else {
      idx = -1;
      for (let i = 0; i < list.length; i++) if (list[i].t < p - 0.5) idx = i;
      if (idx < 0) idx = 0;
    }
  } else {
    idx = Math.max(0, Math.min(list.length - 1, idx + dir));
  }
  const n = noteById(list[idx].id);
  if (n) {
    gotoNote(n);
    osd((idx + 1) + "/" + list.length + " · " + list[idx].label + " " + F.fmt(n.t));
  }
}

// ---------- Loop the selected note (t-3 .. t+3) with mpv's A-B loop ----------

let loop = null;

function startLoop() {
  if (loop) { stopLoop(); osd("Döngü kapandı"); pushState(); return; }
  const n = selectedNote();
  if (!n || n.video !== video) { osd("Önce listeden bir not seç"); return; }
  const d = duration();
  const a = Math.max(0, n.t - 3);
  const b = d ? Math.min(d, n.t + 3) : n.t + 3;
  try {
    mpv.set("ab-loop-a", a);
    mpv.set("ab-loop-b", b);
  } catch (e) {
    log("ab-loop: " + e);
  }
  seekExact(a);
  core.resume();
  loop = { a: a, b: b, grace: Date.now() + 1500 };
  osd("Döngü: " + F.fmt(a) + " – " + F.fmt(b) + " (duraklat ya da atla: biter)");
  pushState();
}

function stopLoop() {
  if (!loop) return;
  loop = null;
  try {
    mpv.set("ab-loop-a", "no");
    mpv.set("ab-loop-b", "no");
  } catch (e) {
    log("ab-loop off: " + e);
  }
}

// ---------- Area marking (overlay) ----------

let overlayReady = false;
let areaMode = false;   // overlay is catching the mouse
let areaRect = null;    // { fx, fy, fw, fh } fractions of the video picture, burned into the next game screenshot

function toggleAreaMode() {
  if (areaMode || areaRect) {
    endAreaMode();
    osd("Alan işaretleme kapandı");
    return;
  }
  if (!video) { osd("KareNot: önce bir video aç"); return; }
  if (!overlayReady) { osd("Alan işaretleme hazır değil"); return; }
  areaMode = true;
  core.pause();
  overlay.show();
  overlay.setClickable(true);
  overlay.postMessage("mode", { on: true });
  osd("Videoda alanı sürükle");
  pushState();
}

function endAreaMode() {
  areaMode = false;
  areaRect = null;
  if (overlayReady) {
    overlay.postMessage("mode", { on: false });
    overlay.setClickable(false);
    overlay.hide();
  }
  pushState();
}

// Maps a rectangle in overlay CSS pixels to fractions of the video picture. The picture's place inside the view comes
// from mpv's osd-dimensions margins (handles letterboxing); without them it is computed from the display aspect.
function toVideoFractions(r) {
  if (!(r.vw > 0 && r.vh > 0)) return null;
  let left, top, right, bottom;
  const w = num("osd-dimensions/w"), h = num("osd-dimensions/h");
  const ml = num("osd-dimensions/ml"), mr = num("osd-dimensions/mr");
  const mt = num("osd-dimensions/mt"), mb = num("osd-dimensions/mb");
  if (w > 0 && h > 0 && ml != null && mr != null && mt != null && mb != null && ml + mr < w && mt + mb < h) {
    left = ml / w; right = 1 - mr / w; top = mt / h; bottom = 1 - mb / h;
  } else {
    const dw = num("dwidth") || core.status.videoWidth, dh = num("dheight") || core.status.videoHeight;
    if (!(dw > 0 && dh > 0)) return null;
    const scale = Math.min(r.vw / dw, r.vh / dh);
    const bw = (dw * scale) / r.vw, bh = (dh * scale) / r.vh;
    left = (1 - bw) / 2; right = 1 - left; top = (1 - bh) / 2; bottom = 1 - top;
  }
  const bx = left * r.vw, by = top * r.vh, bw = (right - left) * r.vw, bh = (bottom - top) * r.vh;
  const clamp = (v) => Math.max(0, Math.min(1, v));
  const x0 = clamp((r.x - bx) / bw), y0 = clamp((r.y - by) / bh);
  const x1 = clamp((r.x + r.w - bx) / bw), y1 = clamp((r.y + r.h - by) / bh);
  if (x1 - x0 < 0.005 || y1 - y0 < 0.005) return null;
  return { fx: x0, fy: y0, fw: x1 - x0, fh: y1 - y0 };
}

// JXA (osascript -l JavaScript) fallback for burning the frame in; AppKit is always there on macOS.
// Kept as a real function so its syntax is checked with the rest of the file; it never runs inside IINA.
/* global ObjC, $ */
function jxaBurn(argv) {
  ObjC.import("AppKit");
  var path = argv[0];
  var fx = parseFloat(argv[1]), fy = parseFloat(argv[2]), fw = parseFloat(argv[3]), fh = parseFloat(argv[4]);
  var data = $.NSData.dataWithContentsOfFile(path);
  if (data.isNil()) return "error: read";
  var src = $.NSBitmapImageRep.imageRepWithData(data);
  if (src.isNil()) return "error: decode";
  var pw = src.pixelsWide, ph = src.pixelsHigh;
  src.setSize($.NSMakeSize(pw, ph));
  var rep = $.NSBitmapImageRep.alloc.initWithBitmapDataPlanesPixelsWidePixelsHighBitsPerSampleSamplesPerPixelHasAlphaIsPlanarColorSpaceNameBytesPerRowBitsPerPixel(
    null, pw, ph, 8, 4, true, false, $.NSDeviceRGBColorSpace, 0, 0);
  if (rep.isNil()) return "error: bitmap";
  rep.setSize($.NSMakeSize(pw, ph));
  var ctx = $.NSGraphicsContext.graphicsContextWithBitmapImageRep(rep);
  $.NSGraphicsContext.saveGraphicsState;
  $.NSGraphicsContext.setCurrentContext(ctx);
  src.drawInRect($.NSMakeRect(0, 0, pw, ph));
  var lw = Math.max(4, pw / 300);
  var x = fx * pw, w = fw * pw, h = fh * ph;
  var y = ph - (fy * ph) - h;   // AppKit's origin is bottom-left
  $.NSColor.colorWithSRGBRedGreenBlueAlpha(1, 0, 0, 1).setStroke;
  var box = $.NSBezierPath.bezierPathWithRect($.NSMakeRect(x + lw / 2, y + lw / 2, Math.max(1, w - lw), Math.max(1, h - lw)));
  box.setLineWidth(lw);
  box.stroke;
  $.NSGraphicsContext.restoreGraphicsState;
  var png = rep.representationUsingTypeProperties(4 /* NSBitmapImageFileTypePNG */, $.NSDictionary.dictionary);
  if (png.isNil()) return "error: encode";
  return png.writeToFileAtomically(path, true) ? "ok" : "error: write";
}

function findFfmpeg() {
  for (const p of ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg"]) {
    try { if (utils.fileInPath(p)) return p; } catch (e) { /* ignore */ }
  }
  return null;
}

// Draws the red frame onto the saved screenshot: ffmpeg (if installed), otherwise AppKit through osascript.
async function burnArea(path, f) {
  const args = [f.fx, f.fy, f.fw, f.fh].map((v) => v.toFixed(5));
  const ffmpeg = findFfmpeg();
  if (ffmpeg) {
    const width = num("width") || core.status.videoWidth || 1920;
    const thick = Math.max(4, Math.round(width / 300));
    const tmp = path.replace(/\.png$/, ".tmp.png");
    try {
      const r = await utils.exec(ffmpeg, ["-y", "-loglevel", "error", "-i", path, "-vf",
        "drawbox=x=iw*" + args[0] + ":y=ih*" + args[1] + ":w=iw*" + args[2] + ":h=ih*" + args[3] +
        ":color=0xFF0000@1:t=" + thick, "-frames:v", "1", "-update", "1", tmp]);
      if (r.status === 0) {
        const mv = await utils.exec("/bin/mv", ["-f", tmp, path]);
        if (mv.status === 0) return true;
      }
      log("ffmpeg burn failed: " + r.stderr);
    } catch (e) {
      log("ffmpeg burn error: " + e);
    }
    try { if (file.exists(tmp)) file.delete(tmp); } catch (e) { /* ignore */ }
  }
  try {
    const script = jxaBurn.toString() + "\nfunction run(argv) { return jxaBurn(argv); }\n";
    const r = await utils.exec("/usr/bin/osascript", ["-l", "JavaScript", "-e", script, path].concat(args));
    if (r.status === 0 && /ok/.test(r.stdout)) return true;
    log("osascript burn failed: " + r.status + " " + r.stdout + " " + r.stderr);
  } catch (e) {
    log("osascript burn error: " + e);
  }
  return false;
}

// ---------- Report ----------

function resolveShot(shot) {
  if (!shot) return null;
  if (file.exists(SS_DIR + "/" + shot)) return shot;
  const names = listDir(SS_DIR).filter((e) => !e.isDir).map((e) => e.filename)
    .filter((n) => n.indexOf(shot) === 0).sort();
  return names.length ? names[0] : null;
}

function writeReport() {
  reloadAll();
  const r = F.buildReport(notes, resolveShot, new Date());
  writeAtomic(REPORT, r.text).then(() => {
    osd("Rapor kaydedildi: Masaüstü/Video Notlari/rapor.md" + (r.missing ? " (" + r.missing + " SS bulunamadı)" : ""));
  });
  pushState();
}

// ---------- Review progress and resume ----------

let lastPos = null;     // last known position of the open video
let savedPos = null;    // last position written to inceleme.json

function savePosition(v, p) {
  if (!v || p == null) return;
  if (savedPos != null && Math.abs(savedPos - p) < 1) return;
  const e = reviewEntry(v);
  if (savedPos == null && typeof e.pos !== "number" && p < 5) return;   // nothing worth remembering yet
  reviewSet(v, { pos: round2(p) });
  savedPos = p;
}

function markReviewed() {
  const p = position();
  if (!video || p == null) return;
  reviewSet(video, { reviewed: round2(p) });
  osd("Buraya kadar incelendi: " + F.fmt(p));
  pushState();
}

function tryResume(attempt) {
  if (!video) return;
  const e = reviewEntry(video);
  const pos = typeof e.pos === "number" ? e.pos : 0;
  const d = duration();
  if (!d) {
    if (attempt < 10) setTimeout(() => tryResume(attempt + 1), 300);
    return;
  }
  savedPos = pos > 0 ? pos : null;
  if (!(pos >= 5 && pos <= d - 5)) return;
  const p = position();
  if (p != null && p > 5) return;   // IINA's own "resume last position" already did it
  seekExact(pos);
  osd("Kaldığın yerden devam: " + F.fmt(pos));
}

// ---------- Events ----------

event.on("iina.window-loaded", () => {
  windowLoaded = true;
  sidebar.loadFile("ui/sidebar.html");
  overlay.loadFile("ui/overlay.html");
});

function onOverlayLoaded() {
  if (overlayReady) return;
  overlayReady = true;
  overlay.hide();
  overlay.setClickable(false);
}

event.on("iina.plugin-overlay-loaded", onOverlayLoaded);

event.on("iina.file-loaded", () => {
  if (video && lastPos != null) savePosition(video, lastPos);
  stopLoop();
  if (areaMode || areaRect) endAreaMode();
  video = currentVideoName() || null;
  lastPos = null;
  savedPos = null;
  selectedId = null;
  reloadAll();
  pushState();
  tryResume(0);
});

event.on("iina.window-will-close", () => {
  if (video && lastPos != null) savePosition(video, lastPos);
});

event.on("mpv.end-file", () => {
  if (video && lastPos != null) savePosition(video, lastPos);
});

let tick = 0;
setInterval(() => {
  if (!video) return;
  const p = position();
  if (p != null) lastPos = p;
  if (loop) {
    const settled = Date.now() > loop.grace;
    const out = p != null && (p < loop.a - 0.75 || p > loop.b + 0.75);
    if (settled && (core.status.paused || out)) {
      stopLoop();
      pushState();
    }
  }
  if (++tick % 20 === 0) savePosition(video, lastPos);   // every ~5 s
}, 250);

// ---------- Sidebar messages ----------

sidebar.onMessage("ready", () => pushState());
sidebar.onMessage("draft", (d) => { draft = d && typeof d.text === "string" ? d.text : ""; });
sidebar.onMessage("focus", (d) => { inputFocused = !!(d && d.focused); });
sidebar.onMessage("ss", (d) => {
  ssEnabled = !!(d && d.enabled);
  try { preferences.set("ssEnabled", ssEnabled); preferences.sync(); } catch (e) { /* ignore */ }
});
sidebar.onMessage("tab", (d) => { if (d && /^(game|edit|keys)$/.test(d.tab)) setTab(d.tab); });
sidebar.onMessage("add", (d) => {
  if (!d || F.ALL_KINDS.indexOf(d.kind) < 0) return;
  if (typeof d.text === "string" && F.isGame(d.kind)) draft = d.text;
  addNote(d.kind, d.kind === "BIP" && d.text ? d.text : null);
});
sidebar.onMessage("select", (d) => { if (d && noteById(d.id)) { selectedId = d.id; pushState(); } });
sidebar.onMessage("goto", (d) => { const n = d && noteById(d.id); if (n) gotoNote(n); });
sidebar.onMessage("delete", (d) => { if (d && Array.isArray(d.ids)) removeIds(d.ids); });
sidebar.onMessage("edit", (d) => { if (d && typeof d.text === "string") updateNote(d.id, { text: F.clean(d.text) }); });
sidebar.onMessage("status", (d) => {
  const n = d && noteById(d.id);
  if (n && F.isGame(n.kind) && F.STATUSES.indexOf(d.value) >= 0) updateNote(d.id, { status: d.value || null });
});
sidebar.onMessage("severity", (d) => {
  const n = d && noteById(d.id);
  if (n && F.isGame(n.kind) && F.SEVERITIES.indexOf(d.value) >= 0) updateNote(d.id, { severity: d.value || null });
});
sidebar.onMessage("bip-end", (d) => {
  const n = d && noteById(d.id);
  const p = position();
  if (!n || n.kind !== "BIP" || p == null) return;
  if (p <= n.t) { osd("Bitiş, BİP zamanından sonra olmalı"); return; }
  updateNote(n.id, { end: p });
});
sidebar.onMessage("area", () => toggleAreaMode());
sidebar.onMessage("report", () => writeReport());
sidebar.onMessage("reviewed", () => markReviewed());
sidebar.onMessage("loop", () => startLoop());
sidebar.onMessage("open-folder", () => { try { file.showInFinder(WORK); } catch (e) { log("finder: " + e); } });

// ---------- Overlay messages ----------

overlay.onMessage("ready", () => onOverlayLoaded());
overlay.onMessage("rect", (r) => {
  if (!areaMode || !r) return;
  const f = toVideoFractions(r);
  if (!f) { osd("Alan videonun dışında kaldı, tekrar sürükle"); return; }
  areaRect = f;
  osd("Alan seçildi: notu yaz, BUG / DEĞİŞİKLİK / NOT seç");
  pushState();
});
overlay.onMessage("cancel", () => { endAreaMode(); osd("Alan işaretleme iptal"); });

// ---------- Menu (Plugin menu) with key bindings ----------

// Menu key equivalents are handled before the sidebar text field gets the key. While a sidebar field has focus the
// character is typed into it instead of running the shortcut.
function key(ch, action) {
  return () => {
    if (inputFocused) { sidebar.postMessage("insert", { text: ch }); return; }
    action();
  };
}

function addMenu(title, ch, action) {
  const opts = ch ? { keyBinding: ch } : {};
  menu.addItem(menu.item(title, ch ? key(ch, action) : action, opts));
}

addMenu("BUG ekle", "b", () => addNote("BUG"));
addMenu("DEĞİŞİKLİK ekle", "d", () => addNote("DEGISIKLIK"));
addMenu("NOT ekle (yazı gerekir)", "n", () => addNote("NOT"));
menu.addItem(menu.separator());
addMenu("BİP", "k", () => addNote("BIP"));
addMenu("Kesim BAŞLA", "1", () => addNote("KES_BASLA"));
addMenu("Kesim BİTİR", "2", () => addNote("KES_BITIR"));
menu.addItem(menu.separator());
addMenu("Önceki not", "[", () => navigate(-1));
addMenu("Sonraki not", "]", () => navigate(1));
addMenu("Seçili notu döngüye al / bırak", "l", () => startLoop());
addMenu("Buraya kadar inceledim", "i", () => markReviewed());
menu.addItem(menu.separator());
addMenu("Alan işaretle / iptal", null, () => toggleAreaMode());
addMenu("Raporu kaydet (rapor.md)", null, () => writeReport());
addMenu("Not klasörünü Finder'da göster", null, () => { try { file.showInFinder(WORK); } catch (e) { log(e); } });
addMenu("KareNot panelini göster", null, () => sidebar.show());
