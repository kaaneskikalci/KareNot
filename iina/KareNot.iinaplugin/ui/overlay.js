// KareNot video overlay: lets the user drag a red rectangle over the video while area marking is on.
"use strict";

(function () {
  const bridge = window.iina || { postMessage: function () {}, onMessage: function () {} };
  const box = document.getElementById("box");
  const catcher = document.getElementById("catch");
  let start = null;

  function draw(x0, y0, x1, y1) {
    const x = Math.min(x0, x1), y = Math.min(y0, y1);
    box.style.left = x + "px";
    box.style.top = y + "px";
    box.style.width = Math.abs(x1 - x0) + "px";
    box.style.height = Math.abs(y1 - y0) + "px";
    box.style.display = "block";
  }

  function clear() {
    start = null;
    box.style.display = "none";
  }

  catcher.addEventListener("mousedown", (e) => {
    if (e.button !== 0) return;
    e.preventDefault();
    start = { x: e.clientX, y: e.clientY };
    draw(start.x, start.y, start.x, start.y);
  });

  window.addEventListener("mousemove", (e) => {
    if (start) draw(start.x, start.y, e.clientX, e.clientY);
  });

  window.addEventListener("mouseup", (e) => {
    if (!start) return;
    const x0 = start.x, y0 = start.y;
    start = null;
    const w = Math.abs(e.clientX - x0), h = Math.abs(e.clientY - y0);
    if (w < 5 || h < 5) {
      box.style.display = "none";
      return;
    }
    bridge.postMessage("rect", {
      x: Math.min(x0, e.clientX), y: Math.min(y0, e.clientY), w: w, h: h,
      vw: window.innerWidth, vh: window.innerHeight,
    });
  });

  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape") bridge.postMessage("cancel", {});
  });

  bridge.onMessage("mode", (d) => {
    const on = !!(d && d.on);
    document.body.classList.toggle("on", on);
    if (!on) clear();
  });

  bridge.postMessage("ready", {});
})();
