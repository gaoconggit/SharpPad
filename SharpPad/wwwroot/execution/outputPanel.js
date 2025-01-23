import { layoutEditor, showNotification } from '../utils/common.js';

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
            
            // 调整其他面板的高度
            const fileList = document.getElementById('fileList');
            const chatPanel = document.getElementById('chatPanel');
            fileList.style.height = '100vh';
            this.container.style.height = '100vh';
            chatPanel.style.height = '100vh';
            
            layoutEditor();
        });

        this.toggleOutputLayout.addEventListener('click', () => {
            this.isVertical = !this.isVertical;
            if (this.isVertical) {
                this.lastHorizontalHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);
                
                this.outputPanel.classList.add('vertical');
                // 检查聊天面板是否最小化
                const chatPanel = document.getElementById('chatPanel');
                const fileList = document.getElementById('fileList');
                const container = document.getElementById('container');

                if (chatPanel.style.display === 'none') {
                    this.outputPanel.classList.add('chat-minimized');
                }

                // 重置所有面板的高度为100vh
                fileList.style.height = '100vh';
                container.style.height = '100vh';
                chatPanel.style.height = '100vh';
                this.outputPanel.style.height = '100vh';

                // 根据当前聊天面板的宽度设置位置
                const chatPanelWidth = parseInt(getComputedStyle(chatPanel).width, 10);
                this.outputPanel.style.right = `${chatPanelWidth}px`;
            } else {
                this.outputPanel.classList.remove('vertical', 'chat-minimized');
                
                const height = this.lastHorizontalHeight;
                this.outputPanel.style.height = `${height}px`;
                this.outputPanel.style.right = '0'; // 重置right值
                
                // 恢复水平布局时的高度
                const fileList = document.getElementById('fileList');
                const container = document.getElementById('container');
                const chatPanel = document.getElementById('chatPanel');
                const remainingHeight = `calc(100vh - ${height}px)`;

                fileList.style.height = remainingHeight;
                container.style.height = remainingHeight;
                chatPanel.style.height = remainingHeight;

                // 重置编辑器容器的右边距
                container.style.marginRight = '520px';
            }
            layoutEditor();
        });

        this.minimizedOutputButton.querySelector('.restore-output').addEventListener('click', () => {
            this.outputPanel.style.display = 'flex';
            this.minimizedOutputButton.style.display = 'none';
            
            const fileList = document.getElementById('fileList');
            const container = document.getElementById('container');
            const chatPanel = document.getElementById('chatPanel');

            if (this.isVertical) {
                // 恢复垂直布局
                fileList.style.height = '100vh';
                container.style.height = '100vh';
                chatPanel.style.height = '100vh';
                this.outputPanel.style.height = '100vh';
                
                // 调整编辑器容器的右边距
                container.style.marginRight = chatPanel.style.display === 'none' ? '520px' : '1040px';
            } else {
                // 恢复水平布局
                const height = parseInt(getComputedStyle(this.outputPanel).height, 10);
                const remainingHeight = `calc(100vh - ${height}px)`;
                
                fileList.style.height = remainingHeight;
                container.style.height = remainingHeight;
                chatPanel.style.height = remainingHeight;
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
                const newWidth = Math.min(Math.max(this.startWidth + delta, 200), window.innerWidth * 0.4);
                this.outputPanel.style.width = `${newWidth}px`;
                this.outputPanel.style.right = this.outputPanel.classList.contains('chat-minimized') ? '0' : '520px';
            } else {
                const delta = e.clientY - this.startY;
                const newHeight = Math.min(Math.max(this.startHeight - delta, 100), window.innerHeight * 0.8);
                this.outputPanel.style.height = `${newHeight}px`;

                // 批量更新其他元素的高度
                const fileList = document.getElementById('fileList');
                const container = document.getElementById('container');
                const chatPanel = document.getElementById('chatPanel');

                const remainingHeight = `calc(100vh - ${newHeight}px)`;
                fileList.style.height = remainingHeight;
                container.style.height = remainingHeight;
                chatPanel.style.height = remainingHeight;
            }

            layoutEditor();
        });
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
        const maxHeight = window.innerHeight * 0.4;
        const currentHeight = parseInt(getComputedStyle(this.outputPanel).height, 10);

        if (currentHeight > maxHeight) {
            this.outputPanel.style.height = `${maxHeight}px`;
            this.container.style.marginBottom = `${maxHeight}px`;
            layoutEditor();
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
        // 创建一个 ResizeObserver 来监听聊天面板的大小变化
        const resizeObserver = new ResizeObserver((entries) => {
            if (this.isVertical) {
                const chatPanelWidth = entries[0].contentRect.width;
                this.outputPanel.style.right = `${chatPanelWidth}px`;
            }
        });

        // 开始观察聊天面板
        resizeObserver.observe(this.chatPanel);

        // 保存 observer 引用以便需要时可以断开
        this.chatPanelResizeObserver = resizeObserver;
    }
} 