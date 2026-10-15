/* Agent Arcade website: menu, hero loop, film lightbox, pricing toggles, copy buttons. */
(function () {
  'use strict';
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Mobile menu
  var nav = document.querySelector('.nav'), menu = document.querySelector('.menu-btn');
  if (nav && menu) menu.addEventListener('click', function () {
    var open = nav.classList.toggle('open');
    menu.setAttribute('aria-expanded', open ? 'true' : 'false');
  });

  // Hero: the shared cockpit module on its 12-second loop. Paused off-screen, in a hidden tab, or by the button;
  // with reduced motion it shows one still frame (Claude Code needs you, the phone asks).
  var stage = document.querySelector('[data-hero]');
  if (stage && window.AACockpit) {
    var ck = AACockpit.create(stage.querySelector('.stage-win'), { logo: stage.getAttribute('data-logo') });
    var phoneBox = stage.querySelector('.stage-phone');
    var phone = AACockpit.createPhone(phoneBox);
    var btn = stage.querySelector('.stage-ctl button');
    var paused = reduce, visible = true, clock = 0, last = null;
    function draw(t) {
      var s = AACockpit.heroState(t);
      ck.render(s);
      phone.render(s.phone);
      phoneBox.style.opacity = window.innerWidth <= 768 ? 1 : (0.15 + 0.85 * s.phoneShow);
    }
    function frame(now) {
      if (last != null && !paused && visible && !document.hidden) clock += Math.min(0.1, (now - last) / 1000);
      last = now;
      draw(clock);
      requestAnimationFrame(frame);
    }
    if (reduce) { clock = 6.4; draw(clock); } else requestAnimationFrame(frame);
    if ('IntersectionObserver' in window) new IntersectionObserver(function (es) { visible = es[0].isIntersecting; }).observe(stage);
    if (btn) {
      if (reduce) btn.textContent = 'Play';
      btn.addEventListener('click', function () {
        paused = !paused;
        if (!paused && reduce) { reduce = false; requestAnimationFrame(frame); }
        btn.textContent = paused ? 'Play' : 'Pause';
        btn.setAttribute('aria-pressed', paused ? 'true' : 'false');
      });
    }
  }

  // Film lightbox: never autoplays with sound; Esc or the button closes it.
  document.querySelectorAll('[data-film]').forEach(function (opener) {
    opener.addEventListener('click', function (e) {
      e.preventDefault();
      var box = document.createElement('div');
      box.className = 'lightbox';
      box.setAttribute('role', 'dialog');
      box.setAttribute('aria-modal', 'true');
      box.setAttribute('aria-label', 'Agent Arcade film, 30 seconds');
      box.innerHTML = '<video controls autoplay playsinline preload="auto" poster="' + opener.getAttribute('data-poster') + '">' +
        '<source src="' + opener.getAttribute('data-film') + '" type="video/mp4">' +
        '<track kind="captions" srclang="en" label="English" src="' + opener.getAttribute('data-captions') + '" default></video>' +
        '<button class="btn btn-ghost btn-sm" type="button">Close</button>';
      document.body.appendChild(box);
      var close = box.querySelector('button');
      function done() { box.remove(); document.removeEventListener('keydown', key); opener.focus(); }
      function key(ev) { if (ev.key === 'Escape') done(); if (ev.key === 'Tab') { ev.preventDefault(); (document.activeElement === close ? box.querySelector('video') : close).focus(); } }
      close.addEventListener('click', done);
      box.addEventListener('click', function (ev) { if (ev.target === box) done(); });
      document.addEventListener('keydown', key);
      close.focus();
    });
  });

  // Pricing: monthly/annual and business/personal-buyer views.
  var prices = document.querySelector('[data-prices]');
  if (prices) {
    var view = { period: 'month', buyer: 'business' };
    var TEXT = {
      'month-business': ['EUR 25', 'per user / month, excl. VAT'],
      'year-business': ['EUR 250', 'per user / year, excl. VAT · 2 months free'],
      'month-personal': ['EUR 30.25', 'per month incl. 21% NL VAT'],
      'year-personal': ['EUR 302.50', 'per year incl. 21% NL VAT · 2 months free']
    };
    function show() {
      var t = TEXT[view.period + '-' + view.buyer];
      prices.querySelector('[data-amount]').textContent = t[0];
      prices.querySelector('[data-unit]').textContent = t[1];
      document.querySelectorAll('[data-set]').forEach(function (b) {
        var kv = b.getAttribute('data-set').split('=');
        b.setAttribute('aria-pressed', view[kv[0]] === kv[1] ? 'true' : 'false');
      });
    }
    document.querySelectorAll('[data-set]').forEach(function (b) {
      b.addEventListener('click', function () { var kv = b.getAttribute('data-set').split('='); view[kv[0]] = kv[1]; show(); });
    });
    show();
  }

  // Copy buttons on code blocks.
  document.querySelectorAll('.codeblock').forEach(function (block) {
    var b = document.createElement('button');
    b.type = 'button'; b.className = 'copy'; b.textContent = 'Copy';
    b.addEventListener('click', function () {
      var text = block.querySelector('pre').innerText;
      var ok = function () { b.textContent = 'Copied'; setTimeout(function () { b.textContent = 'Copy'; }, 1500); };
      if (navigator.clipboard) navigator.clipboard.writeText(text).then(ok, function () { select(block); });
      else select(block);
    });
    block.appendChild(b);
  });
  function select(block) { var r = document.createRange(); r.selectNodeContents(block.querySelector('pre')); var s = getSelection(); s.removeAllRanges(); s.addRange(r); }
})();
