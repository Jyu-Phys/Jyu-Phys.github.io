// ==========================================
// 2. 全局背景特效：纯白极简线段流星
// ==========================================
const style = document.createElement('style');
style.innerHTML = `
.meteor {
    position: fixed;
    width: 100px; /* 线段长度，100px 显得精干凌厉 */
    height: 1.5px; /* 极细的实心线 */
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
    let duration = Math.random() * 3000 + 3000; // 速度稍微加快一点点，3~6秒
    
    let startX, endX, angle;
    
    if (isLeftToRight) {
        startX = -200; 
        endX = window.innerWidth + 200;
        angle = Math.random() * 15 + 10; 
        // 头部（右侧）纯白，尾部（左侧）完全透明拉丝
        meteor.style.background = 'linear-gradient(to right, rgba(255,255,255,0) 0%, rgba(255,255,255,1) 100%)';
        // 仅在最前端加微弱白光，模拟刺破空气的质感
        meteor.style.boxShadow = '2px 0 3px rgba(255, 255, 255, 0.8)'; 
    } else {
        startX = window.innerWidth + 200; 
        endX = -200;
        angle = - (Math.random() * 15 + 10); 
        // 头部（左侧）纯白，尾部（右侧）完全透明拉丝
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

// 每 5 秒判定一次，每次出 1~3 根纯白线段，保持克制的美感
setInterval(() => {
    let count = Math.floor(Math.random() * 3) + 1; 
    for(let i = 0; i < count; i++) {
        setTimeout(createMeteor, Math.random() * 3000); 
    }
}, 5000);