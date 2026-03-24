document.addEventListener('mousedown', function(e) {
    // 专属星空色系：纯白，浅天空蓝，青色，星芒紫
    const colors = ['#ffffff', '#87CEFA', '#00FFFF', '#DDA0DD'];
    const particleCount = 15; // 粒子数量稍微加了一点

    for(let i = 0; i < particleCount; i++) {
        let particle = document.createElement('div');
        
        // 🌟 核心修改 1：随机生成 5px 到 12px 的大小，让星尘更有层次感
        let size = Math.random() * 7 + 5; 

        // 核心渲染属性
        particle.style.position = 'fixed';
        particle.style.left = e.clientX + 'px';
        particle.style.top = e.clientY + 'px';
        particle.style.width = size + 'px';   // 应用更大的尺寸
        particle.style.height = size + 'px';  // 应用更大的尺寸
        particle.style.background = colors[Math.floor(Math.random() * colors.length)];
        particle.style.borderRadius = '50%';
        particle.style.pointerEvents = 'none'; 
        
        // 🌟 核心修改 2：光晕辐射范围成倍放大
        particle.style.boxShadow = `0 0 15px 4px ${particle.style.background}`; 
        particle.style.zIndex = '99999';
        particle.style.transition = 'all 0.8s cubic-bezier(0.1, 0.8, 0.3, 1)'; 
        document.body.appendChild(particle);

        // 🌟 核心修改 3：爆炸散开的半径拉长，看起来更舒展
        setTimeout(() => {
            let angle = Math.random() * 2 * Math.PI;
            let radius = Math.random() * 100 + 40; // 散射半径调大
            particle.style.transform = `translate(${Math.cos(angle)*radius}px, ${Math.sin(angle)*radius}px) scale(0)`;
            particle.style.opacity = '0';
        }, 10); 

        // 彻底消散后清理内存
        setTimeout(() => particle.remove(), 800);
    }
});