// 教材页共享侧边目录
// 自动扫描 .wrap 下的 h2/h3 生成左侧导航，无需给标题手写 id
(function () {
  'use strict';

  // 把标题文本转成合法 id（保留中文，去掉标点空格）
  function slug(text, index) {
    var s = text.replace(/[\s、，。：（）()·]/g, '').slice(0, 30);
    return 'sec-' + index + '-' + s;
  }

  document.addEventListener('DOMContentLoaded', function () {
    var wrap = document.querySelector('.wrap');
    if (!wrap) return;

    // 只取 h2 做主目录，h3 做次级（可折叠层次）
    var headings = wrap.querySelectorAll('h2, h3');
    if (headings.length < 2) return;

    var toc = document.createElement('nav');
    toc.className = 'toc';

    var title = document.createElement('h4');
    title.textContent = '目录';
    toc.appendChild(title);

    var list = document.createElement('div');
    list.className = 'toc-list';
    toc.appendChild(list);

    var entries = [];

    headings.forEach(function (h, i) {
      // 已有 id 就沿用（如第零章的 ch0），否则自动生成
      if (!h.id) h.id = slug(h.textContent, i);

      var a = document.createElement('a');
      a.href = '#' + h.id;
      a.textContent = h.textContent;
      a.className = h.tagName === 'H2' ? 'toc-h2' : 'toc-h3';
      a.addEventListener('click', function () {
        entries.forEach(function (e) { e.link.classList.remove('active'); });
        a.classList.add('active');
      });
      list.appendChild(a);
      entries.push({ heading: h, link: a });
    });

    document.body.appendChild(toc);

    // 折叠按钮（窄屏 / 想要全宽阅读时用）
    var toggle = document.createElement('button');
    toggle.className = 'toc-toggle';
    toggle.setAttribute('aria-label', '切换目录');
    toggle.textContent = '☰';
    toggle.addEventListener('click', function () {
      document.body.classList.toggle('toc-hidden');
    });
    document.body.appendChild(toggle);

    // 滚动高亮：取当前视口内最靠上的那个标题
    function sync() {
      var best = null;
      var bestTop = -Infinity;
      entries.forEach(function (e) {
        var top = e.heading.getBoundingClientRect().top;
        // 已滚过顶部的标题里，取最靠下的那个（即当前所在章节）
        if (top < 120 && top > bestTop) {
          bestTop = top;
          best = e;
        }
      });
      entries.forEach(function (e) {
        e.link.classList.toggle('active', e === best);
      });
      if (best) {
        // 目录过长时把当前项滚进视野
        var lr = best.link.getBoundingClientRect();
        var cr = list.getBoundingClientRect();
        if (lr.top < cr.top || lr.bottom > cr.bottom) {
          best.link.scrollIntoView({ block: 'nearest' });
        }
      }
    }

    var ticking = false;
    window.addEventListener('scroll', function () {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(function () {
        sync();
        ticking = false;
      });
    }, { passive: true });

    sync();
  });
})();
