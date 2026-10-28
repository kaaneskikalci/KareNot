// Checks the IINA plugin's note format against the Windows app's rules (Notes.cs). Run: node iina/test/format_test.js
"use strict";

const assert = require("assert");
const F = require("../KareNot.iinaplugin/lib/format.js");

let count = 0;
function test(name, fn) {
  fn();
  count++;
  console.log("ok - " + name);
}

test("fmt floors tenths", () => {
  assert.strictEqual(F.fmt(0), "00:00:00.0");
  assert.strictEqual(F.fmt(65.49), "00:01:05.4");
  assert.strictEqual(F.fmt(65.4), "00:01:05.4");
  assert.strictEqual(F.fmt(59.99), "00:00:59.9");
  assert.strictEqual(F.fmt(3723.25), "01:02:03.2");
  assert.strictEqual(F.fmt(-3), "00:00:00.0");
  assert.strictEqual(F.fmt(360000), "100:00:00.0");
});

test("num2 is invariant two decimals", () => {
  assert.strictEqual(F.num2(65.4), "65.40");
  assert.strictEqual(F.num2(0), "0.00");
  assert.strictEqual(F.num2(1234.567), "1234.57");
  // .NET rounds to 15 significant digits, then half away from zero (toFixed would give 1.00, 2.67, 0.01).
  assert.strictEqual(F.num2(1.005), "1.01");
  assert.strictEqual(F.num2(2.675), "2.68");
  assert.strictEqual(F.num2(0.015), "0.02");
  assert.strictEqual(F.num2(0.004), "0.00");
  assert.strictEqual(F.num2(0.995), "1.00");
  assert.strictEqual(F.num2(13969.345), "13969.35");
});

test("trailing empty columns are trimmed down to six", () => {
  const n = { t: 65.4, kind: "BUG", text: " a\tb\nc ", video: "v.mp4", shot: null, status: null, severity: null, end: null, clip: null };
  assert.strictEqual(F.noteLine(n), "65.40\t00:01:05.4\tBUG\ta b c\tv.mp4\t");
  n.severity = "KRITIK";
  assert.strictEqual(F.noteLine(n), "65.40\t00:01:05.4\tBUG\ta b c\tv.mp4\t\t\tKRITIK");
  const b = { t: 10, kind: "BIP", text: "", video: "v.mp4", shot: null, status: null, severity: null, end: 11.5, clip: null };
  assert.strictEqual(F.noteLine(b), "10.00\t00:00:10.0\tBIP\t\tv.mp4\t\t\t\t11.50");
});

test("parse accepts 5+ columns, skips comments and junk", () => {
  const text = "﻿" + F.HEADER + "\r\n" +
    "12.50\t00:00:12.5\tNOT\teski not\tv.mp4\r\n" +
    "\n# yorum\n" +
    "x\t00\tNOT\tbozuk\tv.mp4\n" +
    "1\t2\t3\t4\n" +
    "20.00\t00:00:20.0\tBUG\tyeni\tv.mp4\tbug_x.png\tDUZELTILDI\tORTA\t\tklip.mp4\n";
  const notes = F.parseNotes(text);
  assert.strictEqual(notes.length, 2);
  assert.deepStrictEqual(notes[0], { t: 12.5, kind: "NOT", text: "eski not", video: "v.mp4", shot: null, status: null, severity: null, end: null, clip: null });
  assert.strictEqual(notes[1].shot, "bug_x.png");
  assert.strictEqual(notes[1].status, "DUZELTILDI");
  assert.strictEqual(notes[1].severity, "ORTA");
  assert.strictEqual(notes[1].end, null);
  assert.strictEqual(notes[1].clip, "klip.mp4");
});

test("round trip is byte-identical", () => {
  const text = F.HEADER + "\n" +
    "5.00\t00:00:05.0\tBIP\t\tb.mkv\t\t\t\t6.25\n" +
    "65.40\t00:01:05.4\tBUG\tDuvar\tb.mkv\tbug_20261028_101500_65s_01.png\n" +
    "70.00\t00:01:10.0\tDEGISIKLIK\tUI\tb.mkv\t\tTEKRAR_TEST\n";
  assert.strictEqual(F.serialize(F.parseNotes(text)), text);
});

test("sort by video ordinal then seconds", () => {
  const list = [{ video: "b", t: 1 }, { video: "a", t: 5 }, { video: "B", t: 0 }, { video: "a", t: 2 }];
  F.sortNotes(list);
  assert.deepStrictEqual(list.map((n) => n.video + n.t), ["B0", "a2", "a5", "b1"]);
});

test("safe file names match Windows invalid chars", () => {
  assert.strictEqual(F.safeFileName('a:b*c?d"e<f>g|h/i\\j\u0001.mp4'), "a_b_c_d_e_f_g_h_i_j_.mp4");
  assert.strictEqual(F.safeFileName("Oyun Testi ğüşİ.mkv"), "Oyun Testi ğüşİ.mkv");
});

test("video folder name: base name without extension, Windows-safe", () => {
  assert.strictEqual(F.videoFolderName("Oyun Testi 1.mp4"), "Oyun Testi 1");
  assert.strictEqual(F.videoFolderName("Oyun Testi 1.mkv"), "Oyun Testi 1");   // same folder as the .mp4
  assert.strictEqual(F.videoFolderName("kayıt.2026-10-28 ğüşİ.mkv"), "kayıt.2026-10-28 ğüşİ");
  assert.strictEqual(F.videoFolderName("uzantısız"), "uzantısız");
  assert.strictEqual(F.videoFolderName('a:b*c?d"e<f>g|h\\j\u0001.mp4'), "a_b_c_d_e_f_g_h_j_");
  assert.strictEqual(F.videoFolderName("a/b.c.mp4"), "a_b.c");
  assert.strictEqual(F.videoFolderName("son nokta."), "son nokta");
  assert.strictEqual(F.videoFolderName("a. .mp4"), "a");                       // Windows drops trailing dots/spaces
  assert.strictEqual(F.videoFolderName(".mp4"), "video");
  assert.strictEqual(F.videoFolderName(""), "video");
  assert.ok(F.sameFolderName("Test 1", "TEST 1"));
  assert.ok(F.sameFolderName("Kayıtİ", "kayıtİ"));
  assert.ok(F.sameFolderName("ç", "ç"));                            // NFD vs NFC
  assert.ok(!F.sameFolderName("Test 1", "Test 2"));
});

test("mergeNotes adds only missing notes", () => {
  const parse = (lines) => F.parseNotes(F.HEADER + "\n" + lines.join("\n") + "\n");
  const existing = parse(["65.40\t00:01:05.4\tBUG\tDuvar\tv.mp4\tbug_a.png", "5.00\t00:00:05.0\tNOT\tx\tv.mkv"]);
  const incoming = parse([
    "65.4\t00:01:05.4\tBUG\tDuvar\tv.mp4",            // same note (old 5-column line, other number spelling)
    "70.00\t00:01:10.0\tNOT\tyeni\tv.mp4",
    "70.00\t00:01:10.0\tNOT\tyeni\tv.mp4",            // duplicate inside the old data
    "65.40\t00:01:05.4\tBUG\tDuvar\tv.mkv",           // other video, same base name
  ]);
  const m = F.mergeNotes(existing, incoming);
  assert.strictEqual(m.added, 2);
  assert.strictEqual(existing.length, 2);           // input not changed
  assert.strictEqual(F.serialize(m.list), F.HEADER + "\n" +
    "5.00\t00:00:05.0\tNOT\tx\tv.mkv\t\n" +
    "65.40\t00:01:05.4\tBUG\tDuvar\tv.mkv\t\n" +
    "65.40\t00:01:05.4\tBUG\tDuvar\tv.mp4\tbug_a.png\n" +
    "70.00\t00:01:10.0\tNOT\tyeni\tv.mp4\t\n");
  assert.strictEqual(F.mergeNotes(m.list, incoming).added, 0);   // repeatable
});

test("mergeReview: current wins, old fills gaps", () => {
  const current = { "a.mp4": { pos: 50 }, "b.mp4": { reviewed: 3, pos: 4 } };
  const old = { "a.mp4": { pos: 10, reviewed: 8 }, "b.mp4": { pos: 1 }, "c.mp4": { done: true } };
  const m = F.mergeReview(current, old);
  assert.strictEqual(m.changed, true);
  assert.deepStrictEqual(m.root, { "a.mp4": { pos: 50, reviewed: 8 }, "b.mp4": { reviewed: 3, pos: 4 }, "c.mp4": { done: true } });
  assert.deepStrictEqual(current["a.mp4"], { pos: 50 });   // input not changed
  assert.strictEqual(F.mergeReview(m.root, old).changed, false);
  assert.deepStrictEqual(F.mergeReview({}, {}), { root: {}, changed: false });
});

test("findShot: exact name, else first prefix match", () => {
  const names = ["bug_20261028_1.png", "bug_20261028_0.png", "not_x.png"];
  assert.strictEqual(F.findShot("not_x.png", names), "not_x.png");
  assert.strictEqual(F.findShot("bug_20261028", names), "bug_20261028_0.png");
  assert.strictEqual(F.findShot("yok", names), null);
  assert.strictEqual(F.findShot(null, names), null);
});

test("cuts pair start with next end", () => {
  const n = (t, kind) => ({ t: t, kind: kind });
  const r = F.cuts([n(30, "KES_BITIR"), n(10, "KES_BASLA"), n(20, "KES_BASLA"), n(25, "KES_BITIR"), n(40, "KES_BASLA")]);
  assert.deepStrictEqual(r.map((c) => [c.start && c.start.t, c.end && c.end.t]),
    [[10, null], [20, 25], [null, 30], [40, null]]);
});

test("shot name", () => {
  const d = new Date(2026, 9, 28, 9, 5, 7);
  assert.strictEqual(F.shotName("DEGISIKLIK", d, 65.9, 3), "degisiklik_20261028_090507_65s_03.png");
  assert.strictEqual(F.shotName("BUG", d, 1, 123), "bug_20261028_090507_1s_123.png");
});

test("escapeDataString like .NET", () => {
  assert.strictEqual(F.escapeDataString("a b(1)!'*~-_.ç.png"), "a%20b%281%29%21%27%2A~-_.%C3%A7.png");
});

test("report", () => {
  const notes = F.parseNotes(F.HEADER + "\n" +
    "65.40\t00:01:05.4\tBUG\tDuvar\tv.mp4\tbug_a.png\tDUZELTILDI\tKRITIK\n" +
    "70.00\t00:01:10.0\tNOT\t\tv.mp4\tnot_b\n" +
    "5.00\t00:00:05.0\tBIP\t\tv.mp4\t\t\t\t6.25\n");
  const r = F.buildReport(notes, (s) => (s === "bug_a.png" ? s : null), new Date(2026, 9, 28, 14, 3));
  assert.strictEqual(r.missing, 1);
  assert.strictEqual(r.text,
    "# Video notları — bug fixing raporu\n\nOluşturuldu: 2026-10-28 14:03\n\n## v.mp4\n\n1 bug, 0 değişiklik, 1 not\n\n" +
    "### Buglar\n\n- **00:01:05.4** — Duvar [Kritik] [Düzeltildi]\n\n  ![00:01:05.4](ss/bug_a.png)\n\n\n" +
    "### Notlar\n\n- **00:01:10.0** — (açıklama yok)\n  - (SS bulunamadı: not_b)\n\n" +
    "### Bip (küfür)\n\n- **00:00:05.0** — (açıklama yok) [bitiş 00:00:06.2]\n\n");
});

test("review json keeps other entries", () => {
  assert.deepStrictEqual(F.parseReview("[1,2]"), {});
  assert.deepStrictEqual(F.parseReview("bozuk"), {});
  const root = F.parseReview('﻿{"a.mp4":{"pos":12.5,"x":1},"b.mp4":{"done":true}}');
  assert.strictEqual(root["a.mp4"].x, 1);
  assert.strictEqual(root["b.mp4"].done, true);
});

console.log(count + " tests passed");
