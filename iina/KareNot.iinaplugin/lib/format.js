// Note file format shared with the Windows app (Notes.cs). Pure functions only, no IINA API, so this file can be
// tested with plain node (see iina/test/format_test.js).
"use strict";

const HEADER = "# saniye\tzaman\ttur\tnot\tvideo\tss\tdurum\tonem\tbitis\tklip";

const GAME_KINDS = ["BUG", "DEGISIKLIK", "NOT"];
const EDIT_KINDS = ["BIP", "KES_BASLA", "KES_BITIR"];
const ALL_KINDS = GAME_KINDS.concat(EDIT_KINDS);

const LABELS = {
  BUG: "BUG",
  DEGISIKLIK: "DEĞİŞİKLİK",
  NOT: "NOT",
  BIP: "BİP",
  KES_BASLA: "KES ▶",
  KES_BITIR: "KES ■",
};

const COLORS = {
  BUG: "#DC2828",
  DEGISIKLIK: "#E68200",
  NOT: "#2878E6",
  BIP: "#A03CC8",
  KES_BASLA: "#1EA046",
  KES_BITIR: "#146E32",
};

const REPORT_TITLES = {
  BUG: "Buglar",
  DEGISIKLIK: "Değişiklikler",
  NOT: "Notlar",
  BIP: "Bip (küfür)",
  KES_BASLA: "Kesim başı",
  KES_BITIR: "Kesim sonu",
};

const STATUSES = ["", "DUZELTILDI", "TEKRAR_TEST"];
const STATUS_LABELS = { "": "Açık", DUZELTILDI: "Düzeltildi", TEKRAR_TEST: "Tekrar test" };
const SEVERITIES = ["", "DUSUK", "ORTA", "KRITIK"];
const SEVERITY_LABELS = { "": "", DUSUK: "Düşük", ORTA: "Orta", KRITIK: "Kritik" };

function isEdit(kind) {
  return EDIT_KINDS.indexOf(kind) >= 0;
}

function isGame(kind) {
  return !isEdit(kind);
}

function label(kind) {
  return Object.prototype.hasOwnProperty.call(LABELS, kind) ? LABELS[kind] : kind;
}

function color(kind) {
  return Object.prototype.hasOwnProperty.call(COLORS, kind) ? COLORS[kind] : "#808080";
}

function reportTitle(kind) {
  return Object.prototype.hasOwnProperty.call(REPORT_TITLES, kind) ? REPORT_TITLES[kind] : kind;
}

function pad(n, width) {
  let s = String(n);
  while (s.length < width) s = "0" + s;
  return s;
}

// hh:mm:ss.s with the tenths floored, like NoteStore.Fmt (e.g. 65.49 -> 00:01:05.4).
function fmt(t) {
  if (!(t > 0)) t = 0;
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = Math.floor((t % 60) * 10) / 10;
  return pad(h, 2) + ":" + pad(m, 2) + ":" + pad(s.toFixed(1), 4);
}

// hh:mm:ss (seconds floored), used for the cut ranges in the Kurgu list.
function fmtShort(t) {
  if (!(t > 0)) t = 0;
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = Math.floor(t % 60);
  return pad(h, 2) + ":" + pad(m, 2) + ":" + pad(s, 2);
}

// Invariant "0.00" exactly like .NET: a double is first rounded to 15 significant digits, then the decimal string is
// rounded half away from zero (toFixed differs, e.g. 1.005 -> "1.00" but .NET gives "1.01").
function num2(t) {
  let x = Number(t);
  if (!isFinite(x)) return "0.00";
  const neg = x < 0;
  x = Math.abs(x);
  if (x === 0) return "0.00";
  const parts = x.toExponential(14).split("e");   // d.dddddddddddddd, 15 significant digits
  const digits = parts[0].replace(".", "");
  const before = parseInt(parts[1], 10) + 1 + 2; // digits before the point of x * 100
  let n;
  if (before < 0) n = 0;
  else if (before === 0) n = digits[0] >= "5" ? 1 : 0;
  else {
    let intPart = digits.slice(0, before);
    while (intPart.length < before) intPart += "0";
    n = parseInt(intPart, 10) + ((digits[before] || "0") >= "5" ? 1 : 0);
  }
  let s = String(n);
  while (s.length < 3) s = "0" + s;
  const out = s.slice(0, s.length - 2) + "." + s.slice(s.length - 2);
  return neg && n !== 0 ? "-" + out : out;
}

function clean(text) {
  return String(text == null ? "" : text).replace(/[\t\r\n]/g, " ").trim();
}

// Accepts what double.TryParse(NumberStyles.Float, InvariantCulture) accepts for our files.
const NUM_RE = /^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?\s*$/;
function parseNum(s) {
  if (s == null || !NUM_RE.test(s)) return null;
  const v = parseFloat(s);
  return isFinite(v) ? v : null;
}

// Windows' Path.GetInvalidFileNameChars(), so both apps use the same per-video file name.
function safeFileName(video) {
  return String(video).replace(/["<>|\u0000-\u001f:*?\\/]/g, "_");
}

function splitLines(content) {
  return String(content).replace(/^﻿/, "").split(/\r\n|\r|\n/);
}

// Parses a note file (per-video or combined). Lines starting with '#' and empty lines are skipped; 5+ columns needed.
function parseNotes(content) {
  const result = [];
  if (content == null) return result;
  for (const line of splitLines(content)) {
    if (line.length === 0 || line[0] === "#") continue;
    const f = line.split("\t");
    if (f.length < 5) continue;
    const t = parseNum(f[0]);
    if (t == null) continue;
    const col = (i) => (f.length > i && f[i].length > 0 ? f[i] : null);
    result.push({
      t: t,
      kind: f[2],
      text: f[3],
      video: f[4],
      shot: col(5),
      status: col(6),
      severity: col(7),
      end: parseNum(col(8)),
      clip: col(9),
    });
  }
  return result;
}

function noteLine(n) {
  const cols = [
    num2(n.t), fmt(n.t), n.kind, clean(n.text), n.video, n.shot || "",
    n.status || "", n.severity || "", n.end != null ? num2(n.end) : "", n.clip || "",
  ];
  while (cols.length > 6 && cols[cols.length - 1].length === 0) cols.pop();
  return cols.join("\t");
}

function serialize(notes) {
  let s = HEADER + "\n";
  for (const n of notes) s += noteLine(n) + "\n";
  return s;
}

// Ordinal (UTF-16 code unit) compare, like string.CompareOrdinal.
function ordinal(a, b) {
  return a < b ? -1 : a > b ? 1 : 0;
}

function sortNotes(notes) {
  notes.sort((a, b) => ordinal(a.video, b.video) || (a.t < b.t ? -1 : a.t > b.t ? 1 : 0));
  return notes;
}

// Pairs cut marks in time order: a start closes at the next end; a start or end without a partner stays alone
// (Kinds.Cuts in Notes.cs). Returns [{ start, end }] with null for a missing partner.
function cuts(notes) {
  const marks = notes.filter((n) => n.kind === "KES_BASLA" || n.kind === "KES_BITIR")
    .slice().sort((a, b) => a.t - b.t);
  const result = [];
  let open = null;
  for (const n of marks) {
    if (n.kind === "KES_BASLA") {
      if (open) result.push({ start: open, end: null });
      open = n;
    } else {
      result.push({ start: open, end: n });
      open = null;
    }
  }
  if (open) result.push({ start: open, end: null });
  return result;
}

// Uri.EscapeDataString: everything except RFC 3986 unreserved characters is percent-encoded.
function escapeDataString(s) {
  return encodeURIComponent(s).replace(/[!'()*]/g, (c) => "%" + c.charCodeAt(0).toString(16).toUpperCase());
}

function tags(n) {
  const out = [];
  if (isGame(n.kind)) {
    if (n.severity) out.push("[" + (SEVERITY_LABELS[n.severity] || n.severity) + "]");
    if (n.status) out.push("[" + (STATUS_LABELS[n.status] || n.status) + "]");
  }
  if (n.kind === "BIP" && n.end != null && n.end > n.t) out.push("[bitiş " + fmt(n.end) + "]");
  return out;
}

// Markdown report grouped by video, then kind (bugs and changes first); screenshots linked relative to rapor.md.
// resolveShot(shot) returns the file name in ss/ or null. Returns { text, missing }.
function buildReport(all, resolveShot, now) {
  let missing = 0;
  const d = now || new Date();
  let sb = "# Video notları — bug fixing raporu\n\n";
  sb += "Oluşturuldu: " + d.getFullYear() + "-" + pad(d.getMonth() + 1, 2) + "-" + pad(d.getDate(), 2) + " " +
    pad(d.getHours(), 2) + ":" + pad(d.getMinutes(), 2) + "\n\n";
  const videos = [];
  for (const n of all) if (videos.indexOf(n.video) < 0) videos.push(n.video);
  videos.sort(ordinal);
  for (const video of videos) {
    const notes = all.filter((n) => n.video === video);
    const count = (k) => notes.filter((n) => n.kind === k).length;
    sb += "## " + video + "\n\n";
    sb += count("BUG") + " bug, " + count("DEGISIKLIK") + " değişiklik, " + count("NOT") + " not\n\n";
    const kinds = ALL_KINDS.slice();
    for (const n of notes) if (kinds.indexOf(n.kind) < 0) kinds.push(n.kind);
    for (const kind of kinds) {
      const ofKind = notes.filter((n) => n.kind === kind).sort((a, b) => a.t - b.t);
      if (ofKind.length === 0) continue;
      sb += "### " + reportTitle(kind) + "\n\n";
      for (const n of ofKind) {
        const tagText = tags(n);
        sb += "- **" + fmt(n.t) + "** — " + (n.text.length > 0 ? n.text : "(açıklama yok)") +
          (tagText.length ? " " + tagText.join(" ") : "") + "\n";
        if (!n.shot) continue;
        const f = resolveShot(n.shot);
        if (f) {
          sb += "\n  ![" + fmt(n.t) + "](ss/" + escapeDataString(f) + ")\n\n";
        } else {
          missing++;
          sb += "  - (SS bulunamadı: " + n.shot + ")\n";
        }
      }
      sb += "\n";
    }
  }
  return { text: sb, missing: missing };
}

// <kind lowercase>_<yyyyMMdd_HHmmss>_<int seconds>s_<2-digit counter>.png
function shotName(kind, date, t, counter) {
  const d = date;
  const stamp = d.getFullYear() + pad(d.getMonth() + 1, 2) + pad(d.getDate(), 2) + "_" +
    pad(d.getHours(), 2) + pad(d.getMinutes(), 2) + pad(d.getSeconds(), 2);
  return kind.toLowerCase() + "_" + stamp + "_" + Math.trunc(t) + "s_" + pad(counter, 2) + ".png";
}

// inceleme.json: object keyed by video file name. Anything that is not a plain object becomes {}.
function parseReview(text) {
  if (!text) return {};
  try {
    const v = JSON.parse(String(text).replace(/^﻿/, ""));
    return v && typeof v === "object" && !Array.isArray(v) ? v : {};
  } catch (e) {
    return {};
  }
}

module.exports = {
  HEADER, GAME_KINDS, EDIT_KINDS, ALL_KINDS, STATUSES, STATUS_LABELS, SEVERITIES, SEVERITY_LABELS,
  isEdit, isGame, label, color, reportTitle, pad, fmt, fmtShort, num2, clean, parseNum, safeFileName,
  parseNotes, noteLine, serialize, sortNotes, cuts, escapeDataString, buildReport, shotName, parseReview,
};
