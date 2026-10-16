'use strict';

// The window's shape decides the layout on its own, with no setting: a portrait window (a monitor turned on its side,
// or a window taller than wide) stacks its panes and docks the side panel under them, and a narrow one turns the
// sidebar into a drawer. These are the pure rules (also used by tests/layout.test.mjs); app.js applies them.
//
// Interface (loaded before app.js):
//   Layout.portrait(width, height, was)        whether the window counts as portrait; was = the answer so far
//   Layout.drawer(width)                       whether the sidebar floats over the page instead of taking a column
//   Layout.paneGrid(count, width, height, portrait)
//                                              { cols, rows } for count panes in a stage of width × height CSS px
(() => {
  // Square-ish windows keep the layout they had, so dragging a window's edge near square doesn't flip the layout back
  // and forth (each flip resizes every agent). Height ÷ width: portrait from 1.08, landscape again below 0.95.
  const ENTER_PORTRAIT = 1.08;
  const LEAVE_PORTRAIT = 0.95;

  function portrait(width, height, was = null) {
    if (!(width > 0 && height > 0)) return was === true;
    const ratio = height / width;
    if (was === true) return ratio >= LEAVE_PORTRAIT;
    if (was === false) return ratio >= ENTER_PORTRAIT;
    return ratio > 1;
  }

  // Below this width the 252 px sidebar would leave the panes too little room (SIDEBAR_WIDTH in app.js).
  const DRAWER_BELOW = 900;
  const drawer = width => width > 0 && width < DRAWER_BELOW;

  // The grid's gaps and padding (.grid in app.css), and a comfortable pane: about 60 columns and 15 rows of text at
  // the default size.
  const GAP = 8;
  const PAD = 16;
  const COMFORT_WIDTH = 480;
  const COMFORT_HEIGHT = 300;

  // Landscape keeps the grid people know: 1, 2 side by side, 3 in a row, 2 × 2, then rows of 3. Portrait tries 1 to 3
  // columns and keeps the one whose smallest pane comes closest to comfortable (fewer columns on a tie), which stacks
  // the panes until they would get too short.
  function paneGrid(count, width, height, isPortrait) {
    const n = Math.max(0, Math.floor(Number(count) || 0));
    if (n <= 1) return { cols: 1, rows: 1 };
    if (!isPortrait) {
      const cols = n === 2 || n === 4 ? 2 : 3;
      return { cols, rows: Math.ceil(n / cols) };
    }
    let best = null;
    for (let cols = 1; cols <= Math.min(3, n); cols++) {
      const rows = Math.ceil(n / cols);
      const paneWidth = (width - PAD - GAP * (cols - 1)) / cols;
      const paneHeight = (height - PAD - GAP * (rows - 1)) / rows;
      const score = Math.min(paneWidth / COMFORT_WIDTH, paneHeight / COMFORT_HEIGHT);
      if (!best || score > best.score + 1e-9) best = { cols, rows, score };
    }
    return { cols: best.cols, rows: best.rows };
  }

  const api = { portrait, drawer, paneGrid, ENTER_PORTRAIT, LEAVE_PORTRAIT, DRAWER_BELOW };
  if (typeof window !== 'undefined') window.Layout = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})();
