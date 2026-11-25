(() => {
  'use strict';
  const root = document.documentElement;
  const themeButton = document.getElementById('nav-theme-toggle');
  const themeLabel = document.getElementById('theme-label');
  function updateThemeControl() {
    const dark = root.dataset.theme === 'dark';
    themeLabel.textContent = dark ? '浅色模式' : '深色模式';
    themeButton.setAttribute('aria-pressed', String(dark));
    themeButton.setAttribute('aria-label', dark ? '切换至浅色模式' : '切换至深色模式');
    document.querySelector('meta[name="theme-color"]').content = dark ? '#0e1622' : '#f5f7fb';
  }
  themeButton.addEventListener('click', () => {
    const next = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.dataset.theme = next;
    try { localStorage.setItem('theme', JSON.stringify({ value: next, expiry: Date.now() + 365 * 86400000 })); } catch (_) {}
    updateThemeControl();
  });
  updateThemeControl();

  const cards = [...document.querySelectorAll('.recent-post-item')];
  const contentTotal = document.querySelector('.site-data a[href="#content-inner"] .length-num');
  const categoryTotal = document.querySelector('.site-data a[href="/categories/"] .length-num');
  if (contentTotal) contentTotal.textContent = String(cards.length);
  if (categoryTotal) categoryTotal.textContent = String(document.querySelectorAll('#aside-cat-list .card-category-list-item').length);
  const input = document.getElementById('content-search');
  const clearButton = document.getElementById('clear-search');
  const result = document.getElementById('result-count');
  const empty = document.getElementById('empty-state');
  const filters = [...document.querySelectorAll('.filter-button')];
  let activeFilter = 'all';
  function normalize(value) { return value.normalize('NFKC').toLocaleLowerCase().replace(/\s+/g, ' ').trim(); }
  function classify(card) {
    const info = card.querySelector('.recent-post-info');
    if (info && info.dataset.kind) return info.dataset.kind;
    const title = card.querySelector('.article-title');
    const href = title ? title.getAttribute('href') : '';
    if (href.includes('/skills/')) return 'skills';
    if (card.textContent.includes('超快基础知识')) return 'fundamentals';
    if (/display-color|hello-world/.test(href) || card.textContent.includes('科研脚本')) return 'tools';
    return 'research';
  }
  const catalog = cards.map(card => ({ card, kind: classify(card), text: normalize(card.textContent) }));
  function applyFilters() {
    const terms = normalize(input.value).split(' ').filter(Boolean);
    let count = 0;
    catalog.forEach(item => {
      const match = (activeFilter === 'all' || item.kind === activeFilter) && terms.every(term => item.text.includes(term));
      item.card.hidden = !match;
      if (match) count++;
    });
    filters.forEach(button => {
      const selected = button.dataset.filter === activeFilter;
      button.classList.toggle('is-active', selected);
      button.setAttribute('aria-pressed', String(selected));
    });
    clearButton.hidden = input.value.length === 0;
    empty.hidden = count !== 0;
    result.textContent = terms.length || activeFilter !== 'all' ? `找到 ${count} 项内容` : `${count} 项内容`;
  }
  filters.forEach(button => button.addEventListener('click', () => { activeFilter = button.dataset.filter; applyFilters(); }));
  input.addEventListener('input', applyFilters);
  input.addEventListener('keydown', event => {
    if (event.key === 'Escape') { input.value = ''; applyFilters(); }
  });
  document.querySelector('.search-form').addEventListener('submit', event => event.preventDefault());
  clearButton.addEventListener('click', () => { input.value = ''; applyFilters(); input.focus(); });
  document.getElementById('reset-filters').addEventListener('click', () => {
    input.value = ''; activeFilter = 'all'; applyFilters(); input.focus();
  });
  applyFilters();
})();
