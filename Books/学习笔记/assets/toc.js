// 章节页共享：扫描 <section id> + <h2>，生成左侧书签
(function () {
  document.addEventListener('DOMContentLoaded', function () {
    var main = document.querySelector('main');
    if (!main) return;
    var sections = main.querySelectorAll('section[id]');
    if (!sections.length) return;

    main.classList.add('with-toc');

    var toc = document.createElement('nav');
    toc.className = 'toc';
    var h4 = document.createElement('h4');
    h4.textContent = '本页目录';
    toc.appendChild(h4);

    var links = [];
    sections.forEach(function (sec) {
      var heading = sec.querySelector('h2');
      if (!heading) return;
      var a = document.createElement('a');
      a.href = '#' + sec.id;
      a.textContent = heading.textContent;
      a.addEventListener('click', function () {
        links.forEach(function (l) { l.classList.remove('active'); });
        a.classList.add('active');
      });
      toc.appendChild(a);
      links.push(a);
    });

    document.body.insertBefore(toc, document.body.firstChild);

    // 滚动时高亮当前章节
    if ('IntersectionObserver' in window) {
      var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            var id = entry.target.id;
            links.forEach(function (l) {
              l.classList.toggle('active', l.getAttribute('href') === '#' + id);
            });
          }
        });
      }, { rootMargin: '-20% 0px -70% 0px' });
      sections.forEach(function (sec) { observer.observe(sec); });
    }
  });
})();
