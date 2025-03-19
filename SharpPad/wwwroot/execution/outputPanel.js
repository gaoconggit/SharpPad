import { layoutEditor, showNotification, isMobileDevice, getResponsiveSize } from '../utils/common.js';

export class OutputPanel {
    constructor() {
        this.outputPanel = document.getElementById('outputPanel');
        this.container = document.getElementById('container');
        this.toggleOutput = document.getElementById('toggleOutput');
        this.toggleOutputLayout = document.getElementById('toggleOutputLayout');
        this.outputContent = document.getElementById('outputContent');
        this.formatOutput = document.getElementById('formatOutput');
        this.copyOutput = document.getElementById('copyOutput');
        this.clearOutput = document.getElementById('clearOutput');
        this.minimizedOutputButton = document.querySelector('.minimized-output-button');
        this.chatPanel = document.getElementById('chatPanel');

        this.isResizing = false;
        this.startY = 0;
        this.startX = 0;
        this.startHeight = 0;
        this.startWidth = 0;
        this.rafId = null;
        this.isVertical = false;
        this.lastHorizontalHeight = 200;
        this.lastVerticalWidth = 520;
        
        // 添加拖动边界阈值，用于优化移动端的调整体验
        this.dragThreshold = 5;
        this.dragStarted = false;
        this.lastTouchY = 0;
        this.lastTouchX = 0;
        
        // 添加防抖定时器
        this.resizeDebounceTimer = null;

        this.initializeEventListeners();
    }

    initializeEventListeners() {
        // 调整大小事件
        const resizeHandle = this.outputPanel.querySelector('.resize-handle');
        resizeHandle.addEventListener('mousedown', this.handleMouseDown.bind(this));
        document.addEventListener('mousemove', this.handleMouseMove.bind(this));
        document.addEventListener('mouseup', this.handleMouseUp.bind(this));
        // 防止调整大小时选择文本
        document.addEventListener('selectstart', this.handleSelectStart.bind(this));

        // 工具栏按钮事件
        this.toggleOutput.addEventListener('click', () => {
            this.outputPanel.style.display = 'none';
            this.minimizedOutputButton.style.display = 'block';
            this.container.style.marginBottom = '0';
            
            // 调整其他面板的高度，但不影响聊天窗口
            const fileList = document.getElementById('fileList');
            
            // 为移动设备和桌面设备设置不同的高度
            const fullHeight = isMobileDevice() ? '100%' : '100vh';
            fileList.style.height = fullHeight;
            this.container.style.height = fullHeight;
            // 移除对chatPanel高度的直接设置
            
            layoutEditor();
        });

        // 为toggleOutputLayout添加click和touch事件处理
        const handleToggleOutputLayout = () => {
            this.isVertical = !this.isVertical;
            if (this.isVertical) {
                this.lastHorizontalHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);
                
                this.outputPanel.classList.add('vertical');
                const fileList = document.getElementById('fileList');
                const container = document.getElementById('container');

                // 根据设备类型设置全高
                const fullHeight = isMobileDevice() ? '100%' : '100vh';
                fileList.style.height = fullHeight;
                container.style.height = fullHeight;
                // 移除对聊天窗口高度的设置
                this.outputPanel.style.height = fullHeight;

                // 使用保存的垂直布局宽度，根据设备类型调整
                const verticalWidth = isMobileDevice() ? 
                    Math.min(this.lastVerticalWidth, window.innerWidth * 0.8) : 
                    this.lastVerticalWidth;
                    
                this.outputPanel.style.width = `${verticalWidth}px`;

                // 根据聊天面板的显示状态设置位置，但不改变聊天面板的布局
                if (this.chatPanel.style.display === 'none') {
                    this.outputPanel.classList.add('chat-minimized');
                    this.outputPanel.style.right = '0';
                    
                    // 在移动设备上使用不同的右边距计算
                    if (isMobileDevice()) {
                        container.style.marginRight = `${verticalWidth}px`;
                    } else {
                        container.style.marginRight = `${verticalWidth}px`;
                    }
                } else {
                    this.outputPanel.classList.remove('chat-minimized');
                    
                    // 避免在移动设备上调整聊天面板的位置
                    if (!isMobileDevice()) {
                        this.outputPanel.style.right = '520px';
                        container.style.marginRight = `${520 + verticalWidth}px`;
                    } else {
                        // 移动设备上输出面板保持在右侧，不影响聊天面板
                        this.outputPanel.style.right = '0';
                        container.style.marginRight = `${verticalWidth}px`;
                    }
                }
            } else {
                // 保存当前垂直布局的宽度
                this.lastVerticalWidth = parseInt(getComputedStyle(this.outputPanel).width, 10);
                
                this.outputPanel.classList.remove('vertical', 'chat-minimized');
                
                const height = this.lastHorizontalHeight;
                this.outputPanel.style.height = `${height}px`;
                this.outputPanel.style.right = '0'; // 重置right值
                this.outputPanel.style.width = '100%'; // 重置宽度为100%
                
                // 恢复水平布局时的高度，但不影响聊天窗口
                const fileList = document.getElementById('fileList');
                const container = document.getElementById('container');
                
                // 考虑移动设备高度计算
                const remainingHeight = isMobileDevice() ? 
                    `calc(100% - ${height}px)` : 
                    `calc(100vh - ${height}px)`;

                fileList.style.height = remainingHeight;
                container.style.height = remainingHeight;
                // 移除对聊天面板高度的设置

                // 重置编辑器容器的右边距，不考虑聊天面板
                container.style.marginRight = '0';
            }
            layoutEditor();
        };

        // 添加click事件
        this.toggleOutputLayout.addEventListener('click', handleToggleOutputLayout);
        
        // 添加touch事件支持
        if ('ontouchstart' in window) {
            this.toggleOutputLayout.addEventListener('touchend', (e) => {
                e.preventDefault(); // 防止点击事件被触发
                handleToggleOutputLayout();
            });
        }

        this.minimizedOutputButton.querySelector('.restore-output').addEventListener('click', () => {
            this.outputPanel.style.display = 'flex';
            this.minimizedOutputButton.style.display = 'none';
            
            const fileList = document.getElementById('fileList');
            const container = document.getElementById('container');
            const isMobile = isMobileDevice();

            if (this.isVertical) {
                // 恢复垂直布局
                const fullHeight = isMobile ? '100%' : '100vh';
                fileList.style.height = fullHeight;
                container.style.height = fullHeight;
                // 移除对聊天面板高度的影响
                this.outputPanel.style.height = fullHeight;
                
                // 调整编辑器容器的右边距，不考虑聊天面板
                if (isMobile) {
                    const outputWidth = parseInt(getComputedStyle(this.outputPanel).width, 10);
                    container.style.marginRight = `${outputWidth}px`;
                } else {
                    // 保持编辑器的右边距只受输出面板影响
                    const outputWidth = parseInt(getComputedStyle(this.outputPanel).width, 10);
                    container.style.marginRight = `${outputWidth}px`;
                }
            } else {
                // 恢复水平布局
                const height = parseInt(getComputedStyle(this.outputPanel).height, 10);
                
                // 考虑移动设备高度计算
                const remainingHeight = isMobile ? 
                    `calc(100% - ${height}px)` : 
                    `calc(100vh - ${height}px)`;
                
                fileList.style.height = remainingHeight;
                container.style.height = remainingHeight;
                // 移除对聊天面板高度的影响
                container.style.marginBottom = `${height}px`;
            }
            
            layoutEditor();
        });

        this.formatOutput.addEventListener('click', this.formatOutputContent.bind(this));
        this.copyOutput.addEventListener('click', this.copyOutputContent.bind(this));
        this.clearOutput.addEventListener('click', this.clearOutputContent.bind(this));

        // 窗口大小变化事件
        window.addEventListener('resize', this.handleResize.bind(this));
        
        // 为移动设备添加触摸事件支持
        if ('ontouchstart' in window) {
            resizeHandle.addEventListener('touchstart', this.handleTouchStart.bind(this), { passive: false });
            document.addEventListener('touchmove', this.handleTouchMove.bind(this), { passive: false });
            document.addEventListener('touchend', this.handleTouchEnd.bind(this));
            
            // 添加双击快速调整功能，便于移动设备用户使用
            if (isMobileDevice()) {
                resizeHandle.addEventListener('dblclick', this.handleDoubleTapResize.bind(this));
                // 实现简单的双击检测
                let lastTap = 0;
                resizeHandle.addEventListener('touchend', (e) => {
                    const currentTime = new Date().getTime();
                    const tapLength = currentTime - lastTap;
                    if (tapLength < 300 && tapLength > 0) {
                        this.handleDoubleTapResize(e);
                    }
                    lastTap = currentTime;
                });
            }
        }
    }

    // 触摸事件处理
    handleTouchStart(e) {
        if (!e.target.classList.contains('resize-handle')) return;
        
        e.preventDefault(); // 防止页面滚动
        if (this.outputPanel.classList.contains('collapsed')) return;
        
        this.isResizing = true;
        this.dragStarted = false; // 重置拖动状态
        
        if (this.isVertical) {
            this.startX = e.touches[0].clientX;
            this.lastTouchX = this.startX;
            this.startWidth = parseInt(document.defaultView.getComputedStyle(this.outputPanel).width, 10);
        } else {
            this.startY = e.touches[0].clientY;
            this.lastTouchY = this.startY;
            this.startHeight = parseInt(document.defaultView.getComputedStyle(this.outputPanel).height, 10);
        }
        this.outputPanel.classList.add('resizing');
        
        // 添加视觉提示，特别是在移动设备上
        if (isMobileDevice()) {
            const resizeHandle = this.outputPanel.querySelector('.resize-handle');
            resizeHandle.classList.add('active');
            
            // 显示辅助提示线，帮助用户理解调整方向
            if (!this.isVertical) {
                const helperLine = document.createElement('div');
                helperLine.className = 'resize-helper-line horizontal';
                this.outputPanel.appendChild(helperLine);
            } else {
                const helperLine = document.createElement('div');
                helperLine.className = 'resize-helper-line vertical';
                this.outputPanel.appendChild(helperLine);
            }
        }
    }
    
    handleTouchMove(e) {
        if (!this.isResizing) return;
        
        e.preventDefault(); // 防止页面滚动
        
        // 检查是否超过拖动阈值
        if (!this.dragStarted) {
            if (this.isVertical) {
                if (Math.abs(e.touches[0].clientX - this.startX) > this.dragThreshold) {
                    this.dragStarted = true;
                } else {
                    return; // 如果没有超过阈值，不处理移动
                }
            } else {
                if (Math.abs(e.touches[0].clientY - this.startY) > this.dragThreshold) {
                    this.dragStarted = true;
                } else {
                    return; // 如果没有超过阈值，不处理移动
                }
            }
        }
        
        // 减少计算量，只在必要时更新
        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }
        
        this.rafId = requestAnimationFrame(() => {
            if (this.isVertical) {
                // 优化垂直方向的调整
                const delta = this.startX - e.touches[0].clientX;
                const newWidth = Math.min(
                    Math.max(this.startWidth + delta, 200), 
                    window.innerWidth * (isMobileDevice() ? 0.8 : 0.4)
                );
                
                // 直接设置宽度，减少样式计算
                this.outputPanel.style.width = `${newWidth}px`;
                
                // 批量更新相关元素
                this.lastVerticalWidth = newWidth;
                this.updateContainerMargins(newWidth, true);
                
                // 更新辅助提示线
                this.updateHelperLine(true);
            } else {
                // 优化水平方向的调整
                const delta = this.startY - e.touches[0].clientY;
                const minHeight = isMobileDevice() ? 150 : 100;
                const maxHeight = window.innerHeight * (isMobileDevice() ? 0.7 : 0.8);
                const newHeight = Math.min(Math.max(this.startHeight + delta, minHeight), maxHeight);
                
                // 直接设置高度
                this.outputPanel.style.height = `${newHeight}px`;
                
                // 批量更新相关元素
                this.lastHorizontalHeight = newHeight;
                this.updateContainerMargins(newHeight, false);
                
                // 更新辅助提示线
                this.updateHelperLine(false);
            }
        });
    }
    
    handleTouchEnd() {
        if (this.isResizing) {
            this.isResizing = false;
            this.dragStarted = false;
            this.outputPanel.classList.remove('resizing');
            
            // 移除视觉提示
            if (isMobileDevice()) {
                const resizeHandle = this.outputPanel.querySelector('.resize-handle');
                if (resizeHandle) {
                    resizeHandle.classList.remove('active');
                }
                
                // 移除辅助提示线
                const helperLine = this.outputPanel.querySelector('.resize-helper-line');
                if (helperLine) {
                    this.outputPanel.removeChild(helperLine);
                }
                
                // 恢复平滑过渡效果
                this.outputPanel.style.transition = '';
            }
            
            if (this.rafId) {
                cancelAnimationFrame(this.rafId);
                this.rafId = null;
            }
            
            // 添加防抖，防止过快调整布局
            clearTimeout(this.resizeDebounceTimer);
            this.resizeDebounceTimer = setTimeout(() => {
                // 确保面板大小在合理范围内
                if (!this.isVertical) {
                    const currentHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);
                    if (currentHeight < 100) {
                        this.outputPanel.classList.add('size-transition');
                        this.outputPanel.style.height = '150px';
                        this.lastHorizontalHeight = 150;
                        this.updateHorizontalLayout(150);
                        
                        setTimeout(() => {
                            this.outputPanel.classList.remove('size-transition');
                        }, 300);
                    }
                }
                
                // 最终调整布局
                layoutEditor();
            }, 100);
        }
    }

    handleMouseDown(e) {
        if (!e.target.classList.contains('resize-handle')) return;
        
        // 如果输出面板处于收起状态，不允许调整大小
        if (this.outputPanel.classList.contains('collapsed')) {
            return;
        }

        this.isResizing = true;
        if (this.isVertical) {
            this.startX = e.clientX;
            this.startWidth = parseInt(document.defaultView.getComputedStyle(this.outputPanel).width, 10);
        } else {
            this.startY = e.clientY;
            this.startHeight = parseInt(document.defaultView.getComputedStyle(this.outputPanel).height, 10);
        }
        this.outputPanel.classList.add('resizing');
        document.documentElement.style.cursor = this.isVertical ? 'ew-resize' : 'ns-resize';
        e.preventDefault();
    }

    handleMouseMove(e) {
        if (!this.isResizing) return;

        // 使用requestAnimationFrame来优化性能
        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }

        this.rafId = requestAnimationFrame(() => {
            if (this.isVertical) {
                // 垂直布局调整宽度，保持原有逻辑但优化计算
                const delta = this.startX - e.clientX;
                const maxWidth = window.innerWidth * (isMobileDevice() ? 0.8 : 0.4);
                const newWidth = Math.min(Math.max(this.startWidth + delta, 200), maxWidth);
                
                // 直接设置宽度，减少不必要的计算和样式更新
                this.outputPanel.style.width = `${newWidth}px`;
                
                // 批量更新相关元素布局，减少重复计算
                this.lastVerticalWidth = newWidth;
                this.updateContainerMargins(newWidth, true);
            } else {
                // 水平布局调整高度，使用优化的计算
                const delta = this.startY - e.clientY;
                const maxHeight = window.innerHeight * (isMobileDevice() ? 0.6 : 0.8);
                const newHeight = Math.min(Math.max(this.startHeight + delta, 100), maxHeight);
                
                // 直接设置高度，减少复杂计算
                this.outputPanel.style.height = `${newHeight}px`;
                
                // 批量更新相关元素，避免触发多次重排
                this.lastHorizontalHeight = newHeight;
                this.updateContainerMargins(newHeight, false);
            }
        });
    }
    
    // 更新容器边距的优化方法
    updateContainerMargins(size, isVertical) {
        const fileList = document.getElementById('fileList');
        const container = document.getElementById('container');
        
        if (isVertical) {
            // 垂直布局时更新右边距
            const chatVisible = this.chatPanel.style.display !== 'none';
            const chatWidth = chatVisible ? parseInt(getComputedStyle(this.chatPanel).width, 10) : 0;
            
            if (isMobileDevice()) {
                container.style.marginRight = `${size}px`;
            } else {
                if (chatVisible) {
                    this.outputPanel.style.right = `${chatWidth}px`;
                    container.style.marginRight = `${chatWidth + size}px`;
                } else {
                    this.outputPanel.style.right = '0';
                    container.style.marginRight = `${size}px`;
                }
            }
        } else {
            // 水平布局时更新底部边距和高度
            const remainingHeight = isMobileDevice() ? 
                `calc(100% - ${size}px)` : 
                `calc(100vh - ${size}px)`;
                
            // 批量更新，减少重排和重绘
            fileList.style.height = remainingHeight;
            container.style.height = remainingHeight;
            container.style.marginBottom = `${size}px`;
            
            if (!isMobileDevice() && this.chatPanel.style.display !== 'none') {
                this.chatPanel.style.height = remainingHeight;
            }
        }
    }

    handleMouseUp() {
        if (!this.isResizing) return;

        this.isResizing = false;
        this.outputPanel.classList.remove('resizing');
        document.documentElement.style.cursor = '';

        // 取消任何待处理的动画帧
        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
            this.rafId = null;
        }
    }

    handleSelectStart(e) {
        if (this.isResizing) {
            e.preventDefault();
        }
    }

    handleResize() {
        // 根据设备类型设置不同的最大高度比例
        const maxHeightRatio = isMobileDevice() ? 0.7 : 0.4; // 增加移动设备可用高度比例
        const maxHeight = window.innerHeight * maxHeightRatio;
        const currentHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);

        if (currentHeight > maxHeight) {
            this.outputPanel.style.height = `${maxHeight}px`;
            this.lastHorizontalHeight = maxHeight; // 更新存储的高度值
            this.container.style.marginBottom = `${maxHeight}px`;
            this.updateHorizontalLayout(maxHeight);
        }
        
        // 在窗口大小变化时，确保移动设备上的输出面板宽度不超过屏幕
        if (this.isVertical && isMobileDevice()) {
            const maxWidth = window.innerWidth * 0.8;
            const currentWidth = parseInt(getComputedStyle(this.outputPanel).width, 10);
            if (currentWidth > maxWidth) {
                this.outputPanel.style.width = `${maxWidth}px`;
                this.lastVerticalWidth = maxWidth;
                this.updateVerticalLayout(maxWidth);
            }
        }
    }

    formatOutputContent() {
        try {
            const content = this.outputContent.textContent;
            const formatted = JSON.stringify(JSON.parse(content), null, 2);
            this.outputContent.innerHTML = `<pre class="hljs"><code><div class="lang-label">json</div>${formatted}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
        } catch (error) {
            console.error('Format error:', error);
        }
    }

    async copyOutputContent() {
        try {
            await navigator.clipboard.writeText(this.outputContent.textContent);
            showNotification('已复制到剪贴板', 'success');
        } catch (error) {
            console.error('Copy error:', error);
            showNotification('复制失败', 'error');
        }
    }

    clearOutputContent() {
        this.outputContent.innerHTML = '';
        this.outputContent.className = '';
    }

    // 新增: 双击/双触调整大小功能
    handleDoubleTapResize(e) {
        if (this.isVertical) {
            // 在垂直模式下双击，切换到默认宽度
            const defaultWidth = isMobileDevice() ? 
                Math.min(320, window.innerWidth * 0.6) : 
                this.lastVerticalWidth;
            
            // 添加过渡效果类名
            this.outputPanel.classList.add('size-transition');
            
            this.outputPanel.style.width = `${defaultWidth}px`;
            this.updateVerticalLayout(defaultWidth);
        } else {
            // 在水平模式下双击，切换到预设的三种高度之一
            const currentHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);
            const smallHeight = Math.min(150, window.innerHeight * 0.2);
            const mediumHeight = Math.min(300, window.innerHeight * 0.4);
            const largeHeight = Math.min(450, window.innerHeight * 0.6);
            
            let newHeight;
            if (currentHeight < smallHeight + 20) {
                newHeight = mediumHeight;
            } else if (currentHeight < mediumHeight + 20) {
                newHeight = largeHeight;
            } else {
                newHeight = smallHeight;
            }
            
            // 添加过渡效果类名
            this.outputPanel.classList.add('size-transition');
            
            // 设置新高度
            this.outputPanel.style.height = `${newHeight}px`;
            this.lastHorizontalHeight = newHeight;
            
            // 更新其他元素
            this.updateHorizontalLayout(newHeight);
        }
        
        // 延迟清除过渡效果，以便后续手动调整不受影响
        setTimeout(() => {
            this.outputPanel.classList.remove('size-transition');
        }, 300);
    }

    // 辅助方法：更新辅助线位置
    updateHelperLine(isVertical) {
        if (isMobileDevice()) {
            const helperLine = this.outputPanel.querySelector('.resize-helper-line');
            if (helperLine) {
                if (isVertical) {
                    helperLine.style.left = '0';
                } else {
                    helperLine.style.top = '0';
                }
            }
        }
    }

    // 更新垂直布局的方法
    updateVerticalLayout(width) {
        // 调用优化后的通用方法
        this.updateContainerMargins(width, true);
        layoutEditor();
    }
    
    // 更新水平布局的方法
    updateHorizontalLayout(height) {
        // 调用优化后的通用方法
        this.updateContainerMargins(height, false);
        layoutEditor();
    }
} 