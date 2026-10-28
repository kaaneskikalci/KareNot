// KareNot sidebar webview. It has no IINA API; it only talks to main.js through iina.postMessage / iina.onMessage.
"use strict";

(function () {
  // Outside IINA (opening the file in a browser to look at the layout) use a stub with sample data.
  const bridge = window.iina || {
    _h: {},
    postMessage: function (name, data) { console.log("post", name, data); },
    onMessage: function (name, cb) { this._h[name] = cb; },
  };

  const $ = (id) => document.getElementById(id);
  const draft = $("draft");
  const ss = $("ss");
  let state = { video: null, tab: "game", game: [], edit: [], selectedId: null };
  let editingId = null;
  let editValue = "";
  let confirmDeleteId = null;
  let confirmTimer = null;

  function post(name, data) {
    bridge.postMessage(name, data || {});
  }

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    e.setAttribute("data-clickable", "");
    return e;
  }

  // ---------- Tabs ----------

  function showTab(tab) {
    document.querySelectorAll("#tabs button").forEach((b) => b.classList.toggle("active", b.dataset.tab === tab));
    ["game", "edit", "keys"].forEach((t) => $("panel-" + t).classList.toggle("active", t === tab));
  }

  document.querySelectorAll("#tabs button").forEach((b) => {
    b.addEventListener("click", () => {
      state.tab = b.dataset.tab;
      showTab(state.tab);
      post("tab", { tab: state.tab });
    });
  });

  // ---------- Adding ----------

  function add(kind, withText) {
    const msg = { kind: kind };
    if (withText) msg.text = draft.value;
    post("add", msg);
  }

  document.querySelectorAll(".kinds button").forEach((b) => {
    b.addEventListener("click", () => {
      const kind = b.dataset.kind;
      add(kind, kind === "BUG" || kind === "DEGISIKLIK" || kind === "NOT");
    });
  });

  draft.addEventListener("input", () => post("draft", { text: draft.value }));
  draft.addEventListener("keydown", (e) => {
    const k = e.key.toLowerCase();
    if (e.key === "Enter" && !e.isComposing) {
      e.preventDefault();
      add("NOT", true);
    } else if (e.ctrlKey && k === "b") {
      e.preventDefault();
      add("BUG", true);
    } else if (e.ctrlKey && k === "d") {
      e.preventDefault();
      add("DEGISIKLIK", true);
    } else if (e.ctrlKey && k === "k") {
      e.preventDefault();
      add("BIP", true);
    } else if (e.key === "Escape") {
      e.preventDefault();
      draft.blur();
    }
  });

  ss.addEventListener("change", () => post("ss", { enabled: ss.checked }));
  $("btn-area").addEventListener("click", () => post("area"));
  $("btn-report").addEventListener("click", () => post("report"));
  $("btn-folder").addEventListener("click", () => post("open-folder"));
  $("btn-reviewed").addEventListener("click", () => post("reviewed"));
  $("btn-loop").addEventListener("click", () => post("loop"));

  // ---------- Keyboard focus (main.js types menu-shortcut characters into the field while it has focus) ----------

  function isTextField(e) {
    return e && e.tagName === "INPUT" && (e.type === "text" || e.type === "search");
  }

  function sendFocus() {
    post("focus", { focused: document.hasFocus() && isTextField(document.activeElement) });
  }

  document.addEventListener("focusin", sendFocus);
  document.addEventListener("focusout", () => setTimeout(sendFocus, 0));
  window.addEventListener("blur", () => post("focus", { focused: false }));
  window.addEventListener("focus", sendFocus);

  bridge.onMessage("insert", (d) => {
    const f = document.activeElement;
    if (!isTextField(f) || !d || !d.text) return;
    if (!document.execCommand("insertText", false, d.text)) {
      const s = f.selectionStart, e = f.selectionEnd;
      f.value = f.value.slice(0, s) + d.text + f.value.slice(e);
      f.selectionStart = f.selectionEnd = s + d.text.length;
      f.dispatchEvent(new Event("input"));
    }
  });

  bridge.onMessage("focus-input", () => {
    showTab("game");
    draft.focus();
  });

  bridge.onMessage("clear-input", () => {
    draft.value = "";
  });

  // ---------- Lists ----------

  function select(options, value, labels, onChange) {
    const s = el("select");
    options.forEach((v) => {
      const o = document.createElement("option");
      o.value = v;
      o.textContent = labels[v];
      if (v === value) o.selected = true;
      s.appendChild(o);
    });
    s.addEventListener("click", (e) => e.stopPropagation());
    s.addEventListener("change", () => onChange(s.value));
    return s;
  }

  const STATUS = { "": "Açık", DUZELTILDI: "Düzeltildi", TEKRAR_TEST: "Tekrar test" };
  const SEVERITY = { "": "Önem: —", DUSUK: "Düşük", ORTA: "Orta", KRITIK: "Kritik" };

  function button(text, onClick) {
    const b = el("button", null, text);
    b.addEventListener("click", (e) => {
      e.stopPropagation();
      onClick(b);
    });
    return b;
  }

  function renderRow(r, game) {
    const row = el("div", "row" + (r.ids.indexOf(state.selectedId) >= 0 ? " sel" : ""));
    const head = el("div", "head");
    head.appendChild(el("span", "time", r.time));
    const badge = el("span", "badge", r.label);
    badge.style.background = r.color;
    head.appendChild(badge);
    if (game && r.severity) head.appendChild(el("span", "tag " + r.severity, SEVERITY[r.severity]));
    if (game && r.status) head.appendChild(el("span", "tag " + r.status, STATUS[r.status]));
    if (game && r.shot) head.appendChild(el("span", "muted", "SS"));
    if (r.warn) head.appendChild(el("span", "warn", "⚠ " + r.warn));
    row.appendChild(head);

    if (editingId === r.id) {
      const input = el("input", "edit-input");
      input.type = "text";
      input.value = editValue;
      input.addEventListener("click", (e) => e.stopPropagation());
      input.addEventListener("input", () => { editValue = input.value; });
      input.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.isComposing) {
          e.preventDefault();
          post("edit", { id: r.id, text: input.value });
          editingId = null;
          render();
        } else if (e.key === "Escape") {
          e.preventDefault();
          editingId = null;
          render();
        }
      });
      row.appendChild(input);
      setTimeout(() => { if (document.activeElement !== input) input.focus(); }, 0);
    } else if (r.text) {
      row.appendChild(el("div", "text", r.text));
    }

    if (r.ids.indexOf(state.selectedId) >= 0) {
      const tools = el("div", "tools");
      if (game) {
        tools.appendChild(select(["", "DUZELTILDI", "TEKRAR_TEST"], r.status, STATUS,
          (v) => post("status", { id: r.id, value: v })));
        tools.appendChild(select(["", "DUSUK", "ORTA", "KRITIK"], r.severity, SEVERITY,
          (v) => post("severity", { id: r.id, value: v })));
      }
      if (r.canEnd) tools.appendChild(button("Bitiş = şimdi", () => post("bip-end", { id: r.id })));
      if (editingId !== r.id) {
        tools.appendChild(button("Düzenle", () => {
          editingId = r.id;
          editValue = r.text || "";
          render();
        }));
      }
      tools.appendChild(button(confirmDeleteId === r.id ? "Emin misin? Sil" : "Sil", () => {
        if (confirmDeleteId === r.id) {
          confirmDeleteId = null;
          post("delete", { ids: r.ids });
        } else {
          confirmDeleteId = r.id;
          clearTimeout(confirmTimer);
          confirmTimer = setTimeout(() => { confirmDeleteId = null; render(); }, 3000);
          render();
        }
      }));
      row.appendChild(tools);
    }

    row.addEventListener("click", () => post("goto", { id: r.id }));
    return row;
  }

  function renderList(id, list, game) {
    const box = $(id);
    box.textContent = "";
    if (!state.video) {
      box.appendChild(el("div", "empty muted", "Video açık değil."));
      return;
    }
    if (list.length === 0) {
      box.appendChild(el("div", "empty muted", game ? "Bu videoda oyun notu yok." : "Bu videoda kurgu işareti yok."));
      return;
    }
    list.forEach((r) => box.appendChild(renderRow(r, game)));
  }

  function render() {
    $("video").textContent = state.video || "Video açık değil";
    $("video").classList.toggle("muted", !state.video);
    $("reviewed").textContent = state.reviewed ? "İncelenen: " + state.reviewed : "";
    ss.checked = state.ssEnabled !== false;
    const area = $("btn-area");
    area.classList.toggle("on", !!(state.area || state.areaMode));
    area.textContent = state.area ? "Alan seçildi ✕" : state.areaMode ? "Alanı sürükle ✕" : "Alan seç";
    $("btn-loop").classList.toggle("on", !!state.loop);
    showTab(state.tab);
    if (editingId != null && !state.game.concat(state.edit).some((r) => r.id === editingId)) editingId = null;
    renderList("list-game", state.game, true);
    renderList("list-edit", state.edit, false);
  }

  bridge.onMessage("state", (s) => {
    if (!s) return;
    state = s;
    render();
  });

  // ---------- Shortcuts tab ----------

  const KEYS = [
    ["OYUN TESTİ", [
      ["B", "BUG"],
      ["D", "DEĞİŞİKLİK"],
      ["N", "NOT (önce kutuya yazılmalı)"],
      ["Alan seç", "Video durur; videoda sürükle, alan kırmızı çerçeveyle sonraki notun SS'ine çizilir"],
    ]],
    ["YAZI KUTUSUNDAYKEN", [
      ["Enter", "NOT olarak ekle"],
      ["Ctrl+B", "BUG olarak ekle"],
      ["Ctrl+D", "DEĞİŞİKLİK olarak ekle"],
      ["Ctrl+K", "Yazıyla birlikte BİP ekle"],
      ["Esc", "Kutudan çık"],
    ]],
    ["KURGU", [
      ["K", "BİP (küfür)"],
      ["1", "Kesim BAŞLA"],
      ["2", "Kesim BİTİR"],
    ]],
    ["LİSTE (açık sekmenin listesi)", [
      ["Tık", "Nota git (2 sn önce)"],
      ["[  /  ]", "Önceki / sonraki not"],
      ["L", "Seçili notu döngüye al (−3 / +3 sn); duraklatınca ya da atlayınca biter"],
    ]],
    ["İNCELEME", [
      ["I", "Buraya kadar inceledim (konumu kaydeder)"],
      ["Otomatik", "Video açılınca kaldığın yerden devam eder"],
    ]],
    ["IINA", [
      ["Boşluk", "Oynat / durdur"],
      ["← / →", "Geri / ileri sar"],
      ["Eklentiler menüsü", "KareNot: rapor, alan işaretleme, klasör"],
    ]],
  ];

  const table = $("keys");
  KEYS.forEach((group) => {
    const tr = document.createElement("tr");
    const th = document.createElement("th");
    th.colSpan = 2;
    th.textContent = group[0];
    tr.appendChild(th);
    table.appendChild(tr);
    group[1].forEach((row) => {
      const r = document.createElement("tr");
      const k = document.createElement("td");
      k.className = "key";
      k.textContent = row[0];
      const a = document.createElement("td");
      a.textContent = row[1];
      r.appendChild(k);
      r.appendChild(a);
      table.appendChild(r);
    });
  });

  if (!window.iina) {
    bridge._h.state({
      video: "oyun_testi_2026-10-28.mp4", tab: "game", selectedId: 2, ssEnabled: true, reviewed: "00:04:12.0",
      game: [
        { id: 1, ids: [1], t: 65.4, time: "00:01:05.4", kind: "BUG", label: "BUG", color: "#DC2828", text: "Karakter duvardan geçti", status: "", severity: "KRITIK", shot: true },
        { id: 2, ids: [2], t: 80, time: "00:01:20.0", kind: "DEGISIKLIK", label: "DEĞİŞİKLİK", color: "#E68200", text: "UI yazısı taşıyor", status: "DUZELTILDI", severity: "", shot: true },
      ],
      edit: [
        { id: 3, ids: [3, 4], t: 90, time: "00:01:30–00:01:42 · 12 sn", kind: "KES", label: "KES", color: "#1EA046", text: "", warn: "" },
        { id: 5, ids: [5], t: 120, time: "00:02:00.0", kind: "KES_BASLA", label: "KES ▶", color: "#1EA046", text: "", warn: "Bitişi yok" },
      ],
    });
  }

  post("ready");
})();
