// ==========================================
// 1. 鼠标点击特效：量子星尘爆炸（大范围版）
// ==========================================
document.addEventListener('mousedown', function(e) {
    const colors = ['#ffffff', '#87CEFA', '#00FFFF', '#DDA0DD'];
    const particleCount = 15; 

    for(let i = 0; i < particleCount; i++) {
        let particle = document.createElement('div');
        let size = Math.random() * 7 + 5; 

        particle.style.position = 'fixed';
        particle.style.left = e.clientX + 'px';
        particle.style.top = e.clientY + 'px';
        particle.style.width = size + 'px';   
        particle.style.height = size + 'px';  
        particle.style.background = colors[Math.floor(Math.random() * colors.length)];
        particle.style.borderRadius = '50%';
        particle.style.pointerEvents = 'none'; 
        particle.style.boxShadow = `0 0 15px 4px ${particle.style.background}`; 
        particle.style.zIndex = '99999';
        // 🌟 修改点 1：把扩散时间从 0.8s 延长到 1.2s，让大范围扩散更优雅
        particle.style.transition = 'all 1.2s cubic-bezier(0.1, 0.8, 0.3, 1)'; 
        document.body.appendChild(particle);

        setTimeout(() => {
            let angle = Math.random() * 2 * Math.PI;
            // 🌟 修改点 2：半径整整扩大一倍！原来是(40~140)，现在是(80~280)
            let radius = Math.random() * 200 + 80; 
            particle.style.transform = `translate(${Math.cos(angle)*radius}px, ${Math.sin(angle)*radius}px) scale(0)`;
            particle.style.opacity = '0';
        }, 10); 

        // 🌟 修改点 3：配合动画时间，清理内存的延迟也同步加长到 1200ms
        setTimeout(() => particle.remove(), 1200);
    }
});

// ==========================================
// 2. 全局背景特效：纯白极简线段流星
// ==========================================
const style = document.createElement('style');
style.innerHTML = `
.meteor {
    position: fixed;
    width: 100px;
    height: 1.5px;
    pointer-events: none;
    z-index: 0;
}
`;
document.head.appendChild(style);

function createMeteor() {
    let meteor = document.createElement('div');
    meteor.className = 'meteor';
    
    let isLeftToRight = Math.random() > 0.5;
    let startY = Math.random() * window.innerHeight * 0.7;
    let duration = Math.random() * 3000 + 3000; 
    
    let startX, endX, angle;
    
    if (isLeftToRight) {
        startX = -200; 
        endX = window.innerWidth + 200;
        angle = Math.random() * 15 + 10; 
        meteor.style.background = 'linear-gradient(to right, rgba(255,255,255,0) 0%, rgba(255,255,255,1) 100%)';
        meteor.style.boxShadow = '2px 0 3px rgba(255, 255, 255, 0.8)'; 
    } else {
        startX = window.innerWidth + 200; 
        endX = -200;
        angle = - (Math.random() * 15 + 10); 
        meteor.style.background = 'linear-gradient(to left, rgba(255,255,255,0) 0%, rgba(255,255,255,1) 100%)';
        meteor.style.boxShadow = '-2px 0 3px rgba(255, 255, 255, 0.8)';
    }
    
    meteor.style.left = startX + 'px';
    meteor.style.top = startY + 'px';
    meteor.style.transform = `rotate(${angle}deg)`;
    meteor.style.transition = `all ${duration}ms linear`;
    
    document.body.appendChild(meteor);
    
    setTimeout(() => {
        meteor.style.left = endX + 'px';
        let deltaY = Math.abs(endX - startX) * Math.tan(Math.abs(angle) * Math.PI / 180);
        meteor.style.top = (startY + deltaY) + 'px';
    }, 50);
    
    setTimeout(() => {
        meteor.remove();
    }, duration + 200);
}

setInterval(() => {
    let count = Math.floor(Math.random() * 3) + 1; 
    for(let i = 0; i < count; i++) {
        setTimeout(createMeteor, Math.random() * 3000); 
    }
}, 5000);