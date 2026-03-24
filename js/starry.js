document.addEventListener('mousedown', function(e) {
    // 专属星空色系：纯白，浅天空蓝，青色，星芒紫
    const colors = ['#ffffff', '#87CEFA', '#00FFFF', '#DDA0DD'];
    const particleCount = 12; // 每次点击产生的粒子/原子数量

    for(let i = 0; i < particleCount; i++) {
        let particle = document.createElement('div');
        // 核心渲染属性
        particle.style.position = 'fixed';
        particle.style.left = e.clientX + 'px';
        particle.style.top = e.clientY + 'px';
        particle.style.width = '3px';
        particle.style.height = '3px';
        particle.style.background = colors[Math.floor(Math.random() * colors.length)];
        particle.style.borderRadius = '50%';
        particle.style.pointerEvents = 'none'; 
        particle.style.boxShadow = `0 0 10px 2px ${particle.style.background}`; // 量子发光晕圈
        particle.style.zIndex = '99999';
        particle.style.transition = 'all 0.8s cubic-bezier(0.1, 0.8, 0.3, 1)'; // 物理减速抛射阻尼感
        document.body.appendChild(particle);

        // 随机散射物理轨迹
        setTimeout(() => {
            let angle = Math.random() * 2 * Math.PI;
            let radius = Math.random() * 60 + 20; // 爆炸散射半径
            particle.style.transform = `translate(${Math.cos(angle)*radius}px, ${Math.sin(angle)*radius}px) scale(0)`;
            particle.style.opacity = '0';
        }, 10); 

        // 彻底消散后清理内存
        setTimeout(() => particle.remove(), 800);
    }
});