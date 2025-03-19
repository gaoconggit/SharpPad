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

        this.initializeEventListeners();
        this.initializeChatPanelObserver();
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

        // 监听聊天面板的显示状态变化
        const chatPanel = document.getElementById('chatPanel');
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                if (mutation.type === 'attributes' && mutation.attributeName === 'style') {
                    if (this.isVertical) {
                        if (chatPanel.style.display === 'none') {
                            this.outputPanel.classList.add('chat-minimized');
                        } else {
                            this.outputPanel.classList.remove('chat-minimized');
                        }
                    }
                }
            });
        });
        observer.observe(chatPanel, { attributes: true });
        
        // 为移动设备添加触摸事件支持
        if ('ontouchstart' in window) {
            resizeHandle.addEventListener('touchstart', this.handleTouchStart.bind(this), { passive: false });
            document.addEventListener('touchmove', this.handleTouchMove.bind(this), { passive: false });
            document.addEventListener('touchend', this.handleTouchEnd.bind(this));
        }
    }

    // 触摸事件处理
    handleTouchStart(e) {
        if (!e.target.classList.contains('resize-handle')) return;
        
        e.preventDefault(); // 防止页面滚动
        if (this.outputPanel.classList.contains('collapsed')) return;
        
        this.isResizing = true;
        if (this.isVertical) {
            this.startX = e.touches[0].clientX;
            this.startWidth = parseInt(document.defaultView.getComputedStyle(this.outputPanel).width, 10);
        } else {
            this.startY = e.touches[0].clientY;
            this.startHeight = parseInt(document.defaultView.getComputedStyle(this.outputPanel).height, 10);
        }
        this.outputPanel.classList.add('resizing');
    }
    
    handleTouchMove(e) {
        if (!this.isResizing) return;
        
        e.preventDefault(); // 防止页面滚动
        
        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }
        
        this.rafId = requestAnimationFrame(() => {
            if (this.isVertical) {
                const delta = this.startX - e.touches[0].clientX;
                const newWidth = Math.min(
                    Math.max(this.startWidth + delta, 200), 
                    window.innerWidth * (isMobileDevice() ? 0.8 : 0.4)
                );
                this.outputPanel.style.width = `${newWidth}px`;
                this.updateVerticalLayout(newWidth);
            } else {
                const delta = e.touches[0].clientY - this.startY;
                const maxHeight = window.innerHeight * (isMobileDevice() ? 0.6 : 0.8);
                const newHeight = Math.min(Math.max(this.startHeight - delta, 100), maxHeight);
                this.outputPanel.style.height = `${newHeight}px`;
                this.updateHorizontalLayout(newHeight);
            }
        });
    }
    
    handleTouchEnd() {
        if (this.isResizing) {
            this.isResizing = false;
            this.outputPanel.classList.remove('resizing');
            
            if (this.rafId) {
                cancelAnimationFrame(this.rafId);
                this.rafId = null;
            }
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

        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }

        this.rafId = requestAnimationFrame(() => {
            if (this.isVertical) {
                const delta = this.startX - e.clientX;
                const maxWidth = window.innerWidth * (isMobileDevice() ? 0.8 : 0.4);
                const newWidth = Math.min(Math.max(this.startWidth + delta, 200), maxWidth);
                this.outputPanel.style.width = `${newWidth}px`;
                this.updateVerticalLayout(newWidth);
            } else {
                const delta = e.clientY - this.startY;
                const maxHeight = window.innerHeight * (isMobileDevice() ? 0.6 : 0.8);
                const newHeight = Math.min(Math.max(this.startHeight - delta, 100), maxHeight);
                this.outputPanel.style.height = `${newHeight}px`;
                this.updateHorizontalLayout(newHeight);
            }
        });
    }
    
    // 更新垂直布局的方法
    updateVerticalLayout(width) {
        const chatPanel = document.getElementById('chatPanel');
        if (isMobileDevice()) {
            // 移动设备的特殊处理
            const chatWidth = parseInt(getComputedStyle(chatPanel).width, 10);
            if (chatPanel.style.display === 'none') {
                this.outputPanel.style.right = '0';
                this.container.style.marginRight = `${width}px`;
            } else {
                this.outputPanel.style.right = `${chatWidth}px`;
                this.container.style.marginRight = `${chatWidth + width}px`;
            }
        } else {
            // 桌面设备的处理
            this.outputPanel.style.right = this.outputPanel.classList.contains('chat-minimized') ? '0' : '520px';
        }
        layoutEditor();
    }
    
    // 更新水平布局的方法
    updateHorizontalLayout(height) {
        // 批量更新其他元素的高度
        const fileList = document.getElementById('fileList');
        const container = document.getElementById('container');
        const chatPanel = document.getElementById('chatPanel');

        // 考虑不同设备类型的高度计算
        const remainingHeight = isMobileDevice() ? 
            `calc(100% - ${height}px)` : 
            `calc(100vh - ${height}px)`;
            
        fileList.style.height = remainingHeight;
        container.style.height = remainingHeight;
        chatPanel.style.height = remainingHeight;
        layoutEditor();
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
        const maxHeightRatio = isMobileDevice() ? 0.6 : 0.4;
        const maxHeight = window.innerHeight * maxHeightRatio;
        const currentHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);

        if (currentHeight > maxHeight) {
            this.outputPanel.style.height = `${maxHeight}px`;
            this.container.style.marginBottom = `${maxHeight}px`;
            this.updateHorizontalLayout(maxHeight);
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

    initializeChatPanelObserver() {
        // 移除ResizeObserver对聊天面板的监视，解除绑定
        // 不再需要监视聊天面板的大小变化
        /*
        const resizeObserver = new ResizeObserver(entries => {
            if (!entries[0]) return;
            
            const chatPanelWidth = entries[0].contentRect.width;
            const outputPanelWidth = parseInt(getComputedStyle(this.outputPanel).width, 10);
            
            // 检测是否为移动设备
            const isMobile = window.matchMedia('(max-width: 768px)').matches;
            
            if (isMobile) {
                // 移动设备上的调整
                if (this.outputPanel.classList.contains('vertical')) {
                    // 如果输出面板是垂直的，调整位置
                    this.outputPanel.style.right = '0';
                    this.container.style.marginRight = '0';
                } else {
                    // 如果输出面板在底部，调整高度关系
                    this.container.style.height = 'calc(50vh - 40vh)';
                }
            } else {
                // 桌面设备上的调整
                // 更新容器的右边距
                this.container.style.marginRight = `${chatPanelWidth + outputPanelWidth}px`;
                
                // 更新输出面板的位置
                this.outputPanel.style.right = `${chatPanelWidth}px`;
            }
        });
        
        resizeObserver.observe(this.chatPanel);
        
        // 保存引用以便稍后清理
        this.chatPanelResizeObserver = resizeObserver;
        */
    }
} 