/* =============================================================================
   myfav1.js — theme switches, sidebar behaviour and the theme settings panel.
   Load in <head> WITHOUT defer/async (after myfav1.css) so the saved theme is on
   <html> before first paint. No dependencies; Bootstrap's bundle handles
   dropdowns, collapses, modals and offcanvas.

   Declarative hooks (work in plain HTML and in Blazor static SSR):
     data-mf-action="toggle-menu"     sidebar button in the topbar
     data-mf-action="toggle-hover"    pin / unpin button inside the sidebar (sm-hover)
     data-mf-action="close-menu"      close button / backdrop of the drawer
     data-mf-action="toggle-theme"    light ↔ dark
     data-mf-action="set" data-mf-key="menuColor" data-mf-value="brand"
     data-mf-action="reset"           back to defaults
     <input type="radio" data-mf-setting="theme" value="dark">   settings panel radios

   API: window.myfav1.get(), .set(key, value), .reset(), .markActive(), .chartColors()
   ========================================================================== */
(function () {
  'use strict';

  var STORAGE_KEY = 'mf-config';
  var ATTR = {
    theme: 'data-bs-theme', menuColor: 'data-menu-color', topbarColor: 'data-topbar-color',
    sidenavSize: 'data-sidenav-size', layoutMode: 'data-layout-mode'
  };
  var html = document.documentElement;
  // The page's own <html> attributes are the defaults (2026-11-07), so each app declares its look in
  // markup (e.g. data-bs-theme="dark"); the constants are the fallback when an attribute is left off.
  // Read before apply() runs, so these are the markup's values, not an earlier visitor's choice.
  var DEFAULTS = {
    theme: html.getAttribute(ATTR.theme) || 'light',               // light | dark
    menuColor: html.getAttribute(ATTR.menuColor) || 'dark',        // dark | light | brand
    topbarColor: html.getAttribute(ATTR.topbarColor) || 'light',   // light | dark | brand
    sidenavSize: html.getAttribute(ATTR.sidenavSize) || 'default', // default | compact | condensed | sm-hover | sm-hover-active | full
    layoutMode: html.getAttribute(ATTR.layoutMode) || 'fluid'      // fluid | detached
  };

  function read() {
    try { return Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}')); }
    catch (e) { return Object.assign({}, DEFAULTS); }
  }
  function write(cfg) {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(cfg)); } catch (e) { /* private mode: keep in memory */ }
  }

  var config = read();
  var collapsedByToggle = false; // topbar toggle collapsed the menu this session (not persisted)

  // Width decides the effective sidebar size: phones get the drawer, tablets the icon rail.
  function effectiveSize() {
    var w = window.innerWidth;
    if (w < 768) return 'full';
    if (collapsedByToggle) return 'condensed';
    if (w <= 1140 && config.sidenavSize !== 'full') return 'condensed';
    return config.sidenavSize;
  }

  function apply() {
    html.setAttribute(ATTR.theme, config.theme);
    html.setAttribute(ATTR.menuColor, config.menuColor);
    html.setAttribute(ATTR.topbarColor, config.topbarColor);
    html.setAttribute(ATTR.layoutMode, config.layoutMode);
    html.setAttribute(ATTR.sidenavSize, effectiveSize());
    syncControls();
  }

  function set(key, value) {
    if (!(key in DEFAULTS)) return;
    config[key] = value;
    if (key === 'sidenavSize') collapsedByToggle = false;
    write(config);
    apply();
    document.dispatchEvent(new CustomEvent('myfav1:change', { detail: { key: key, value: value, config: get() } }));
  }

  function get() { return Object.assign({}, config); }

  function reset() {
    config = Object.assign({}, DEFAULTS);
    collapsedByToggle = false;
    write(config);
    apply();
    document.dispatchEvent(new CustomEvent('myfav1:change', { detail: { key: '*', config: get() } }));
  }

  // Settings panel radios mirror the current config.
  function syncControls() {
    if (!document.body) return;
    document.querySelectorAll('[data-mf-setting]').forEach(function (input) {
      input.checked = String(config[input.getAttribute('data-mf-setting')]) === input.value;
    });
    document.querySelectorAll('[data-mf-action="toggle-theme"]').forEach(function (btn) {
      btn.setAttribute('aria-pressed', config.theme === 'dark' ? 'true' : 'false');
      var icon = btn.querySelector('i');
      if (icon) icon.className = icon.className.replace(/ti-(moon|sun)\b/, config.theme === 'dark' ? 'ti-sun' : 'ti-moon');
    });
  }

  function toggleMenu() {
    var size = html.getAttribute(ATTR.sidenavSize);
    if (size === 'full') { html.classList.toggle('sidebar-enable'); return; }
    if (window.innerWidth <= 1140 && config.sidenavSize !== 'full') {
      // tablet: the rail is forced; open the full menu as a drawer instead
      html.setAttribute(ATTR.sidenavSize, 'full');
      html.classList.add('sidebar-enable');
      return;
    }
    collapsedByToggle = size !== 'condensed' && size !== 'sm-hover';
    if (size === 'sm-hover') { set('sidenavSize', 'sm-hover-active'); return; }
    apply();
  }

  function closeMenu() {
    html.classList.remove('sidebar-enable');
    if (window.innerWidth >= 768) apply();
  }

  // Marks the sidebar link for the current URL and opens its parent groups.
  function markActive() {
    var nav = document.querySelector('.side-nav');
    if (!nav) return;
    var here = location.pathname.replace(/\/+$/, '') || '/';
    var best = null, bestLen = -1;
    nav.querySelectorAll('a.side-nav-link[href]').forEach(function (a) {
      if (a.getAttribute('data-bs-toggle') === 'collapse') return;
      // resolve against <base href> (Blazor uses relative links like "ecommerce/orders")
      var path = new URL(a.getAttribute('href'), document.baseURI).pathname.replace(/\/+$/, '') || '/';
      var match = path === here || (path !== '/' && here.indexOf(path + '/') === 0);
      if (match && path.length > bestLen) { best = a; bestLen = path.length; }
    });
    if (!best) return; // nothing matches: leave server-rendered state (e.g. NavLink) alone
    nav.querySelectorAll('.side-nav-item.active').forEach(function (li) { li.classList.remove('active'); });
    nav.querySelectorAll('a.side-nav-link.active').forEach(function (a) { if (a !== best && !a.hasAttribute('data-mf-keep-active')) a.classList.remove('active'); });
    best.classList.add('active');
    for (var el = best.parentElement; el && el !== nav; el = el.parentElement) {
      if (el.classList.contains('side-nav-item')) el.classList.add('active');
      if (el.classList.contains('collapse')) {
        el.classList.add('show');
        var toggle = nav.querySelector('[data-bs-toggle="collapse"][href="#' + el.id + '"], [data-bs-toggle="collapse"][data-bs-target="#' + el.id + '"]');
        if (toggle) toggle.setAttribute('aria-expanded', 'true');
      }
    }
  }

  // Chart palette from CSS variables, so charts follow the theme tokens.
  function chartColors() {
    var cs = getComputedStyle(html);
    return ['--mf-chart-1', '--mf-chart-2', '--mf-chart-3', '--mf-chart-4', '--bs-warning', '--bs-info']
      .map(function (v) { return cs.getPropertyValue(v).trim(); });
  }

  // --- wiring --------------------------------------------------------------
  apply(); // runs in <head>: attributes are on <html> before the body paints

  document.addEventListener('click', function (e) {
    var el = e.target.closest('[data-mf-action]');
    if (el) {
      var action = el.getAttribute('data-mf-action');
      if (action === 'toggle-menu') { e.preventDefault(); toggleMenu(); }
      else if (action === 'toggle-hover') { e.preventDefault(); set('sidenavSize', html.getAttribute(ATTR.sidenavSize) === 'sm-hover' ? 'sm-hover-active' : 'sm-hover'); }
      else if (action === 'close-menu') { e.preventDefault(); closeMenu(); }
      else if (action === 'toggle-theme') { e.preventDefault(); set('theme', config.theme === 'dark' ? 'light' : 'dark'); }
      else if (action === 'set') { e.preventDefault(); set(el.getAttribute('data-mf-key'), el.getAttribute('data-mf-value')); }
      else if (action === 'reset') { e.preventDefault(); reset(); }
      return;
    }
    // tapping a menu link inside the drawer closes it
    var link = e.target.closest('.sidenav-menu a.side-nav-link:not([data-bs-toggle])');
    if (link && html.classList.contains('sidebar-enable')) closeMenu();
  });

  // Bootstrap tooltips/popovers are created lazily on first hover/focus/click, so they also work on
  // content rendered later (Blazor re-renders, enhanced navigation) without any init call.
  function lazyBootstrap(e) {
    if (!window.bootstrap || !e.target.closest) return;
    var tip = e.target.closest('[data-bs-toggle="tooltip"]');
    if (tip && !window.bootstrap.Tooltip.getInstance(tip)) window.bootstrap.Tooltip.getOrCreateInstance(tip).show();
    if (e.type === 'click') {
      var pop = e.target.closest('[data-bs-toggle="popover"]');
      if (pop && !window.bootstrap.Popover.getInstance(pop)) { e.preventDefault(); window.bootstrap.Popover.getOrCreateInstance(pop).show(); }
    }
  }
  document.addEventListener('mouseover', lazyBootstrap);
  document.addEventListener('focusin', lazyBootstrap);
  document.addEventListener('click', lazyBootstrap);

  document.addEventListener('change', function (e) {
    var input = e.target.closest('[data-mf-setting]');
    if (input && input.checked) set(input.getAttribute('data-mf-setting'), input.value);
  });

  var resizeTimer;
  window.addEventListener('resize', function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () { if (!html.classList.contains('sidebar-enable')) apply(); }, 100);
  });

  // other tabs changed the theme
  window.addEventListener('storage', function (e) { if (e.key === STORAGE_KEY) { config = read(); apply(); } });

  function onPageReady() { syncControls(); markActive(); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', onPageReady);
  else onPageReady();
  window.addEventListener('popstate', markActive);
  // Blazor enhanced navigation swaps the page without a reload.
  function hookBlazor() {
    if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
      window.Blazor.addEventListener('enhancedload', function () {
        html.classList.remove('sidebar-enable');
        document.querySelectorAll('.tooltip, .popover').forEach(function (t) { t.remove(); }); // orphaned by the page swap
        apply(); onPageReady();
      });
      return true;
    }
    return false;
  }
  if (!hookBlazor()) document.addEventListener('DOMContentLoaded', function () { setTimeout(hookBlazor, 0); });

  window.myfav1 = { get: get, set: set, reset: reset, apply: apply, markActive: markActive, chartColors: chartColors, defaults: Object.assign({}, DEFAULTS) };
})();
