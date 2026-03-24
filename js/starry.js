// ==========================================
// 1. 鼠标点击特效：量子星尘爆炸
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
        particle.style.transition = 'all 0.8s cubic-bezier(0.1, 0.8, 0.3, 1)'; 
        document.body.appendChild(particle);

        setTimeout(() => {
            let angle = Math.random() * 2 * Math.PI;
            let radius = Math.random() * 100 + 40; 
            particle.style.transform = `translate(${Math.cos(angle)*radius}px, ${Math.sin(angle)*radius}px) scale(0)`;
            particle.style.opacity = '0';
        }, 10); 

        setTimeout(() => particle.remove(), 800);
    }
});

// ==========================================
// 2. 全局背景特效：缓慢划过的随机流星群
// ==========================================
// 动态注入流星的 CSS 样式
const style = document.createElement('style');
style.innerHTML = `
.meteor {
    position: fixed;
    width: 150px; /* 流星拖尾长度 */
    height: 2px;
    border-radius: 50%;
    box-shadow: 0 0 10px 2px rgba(135, 206, 250, 0.6); /* 幽蓝色发光 */
    pointer-events: none;
    z-index: 0; /* 藏在文字下面，背景上面 */
}
`;
document.head.appendChild(style);

function createMeteor() {
    let meteor = document.createElement('div');
    meteor.className = 'meteor';
    
    // 随机决定从左往右(true)还是从右往左(false)
    let isLeftToRight = Math.random() > 0.5;
    
    // 随机初始高度（主要集中在屏幕中上半部分）
    let startY = Math.random() * window.innerHeight * 0.7;
    
    // 缓慢飞行的时间：4秒 到 8秒
    let duration = Math.random() * 4000 + 4000; 
    
    let startX, endX, angle;
    
    if (isLeftToRight) {
        startX = -200; // 屏幕左侧外
        endX = window.innerWidth + 200;
        angle = Math.random() * 15 + 10; // 向右下倾斜 10~25度
        // 拖尾渐变：左侧透明，右侧亮白/浅蓝
        meteor.style.background = 'linear-gradient(to right, rgba(255,255,255,0) 0%, rgba(135,206,250,1) 100%)';
    } else {
        startX = window.innerWidth + 200; // 屏幕右侧外
        endX = -200;
        angle = - (Math.random() * 15 + 10); // 向左下倾斜
        // 拖尾渐变：右侧透明，左侧亮白/浅蓝
        meteor.style.background = 'linear-gradient(to left, rgba(255,255,255,0) 0%, rgba(135,206,250,1) 100%)';
    }
    
    meteor.style.left = startX + 'px';
    meteor.style.top = startY + 'px';
    meteor.style.transform = `rotate(${angle}deg)`;
    meteor.style.transition = `all ${duration}ms linear`;
    
    document.body.appendChild(meteor);
    
    // 极短延迟后赋予终点坐标，触发CSS线性动画
    setTimeout(() => {
        meteor.style.left = endX + 'px';
        // 利用三角函数计算 Y 轴终点，确保走直线
        let deltaY = Math.abs(endX - startX) * Math.tan(Math.abs(angle) * Math.PI / 180);
        meteor.style.top = (startY + deltaY) + 'px';
    }, 50);
    
    // 飞出屏幕后清理DOM节点释放内存
    setTimeout(() => {
        meteor.remove();
    }, duration + 200);
}

// 调度中心：每隔 6 秒判定一次流星雨
setInterval(() => {
    // 每次随机出 1 到 5 颗流星
    let count = Math.floor(Math.random() * 5) + 1;
    for(let i = 0; i < count; i++) {
        // 给每一颗流星增加 0~3 秒的随机延迟，让它们错落有致，不要黏在一起飞
        setTimeout(createMeteor, Math.random() * 3000); 
    }
}, 6000);