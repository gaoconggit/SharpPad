import { layoutEditor } from '../utils/common.js';

export class OutputPanel {
    constructor() {
        this.outputPanel = document.getElementById('outputPanel');
        this.container = document.getElementById('container');
        this.toggleOutput = document.getElementById('toggleOutput');
        this.outputContent = document.getElementById('outputContent');
        this.formatOutput = document.getElementById('formatOutput');
        this.copyOutput = document.getElementById('copyOutput');
        this.clearOutput = document.getElementById('clearOutput');
        this.minimizedOutputButton = document.querySelector('.minimized-output-button');

        this.isResizing = false;
        this.startY = 0;
        this.startHeight = 0;
        this.rafId = null;

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
            layoutEditor();
        });

        this.minimizedOutputButton.querySelector('.restore-output').addEventListener('click', () => {
            this.outputPanel.style.display = 'flex';
            this.minimizedOutputButton.style.display = 'none';
            const height = parseInt(getComputedStyle(this.outputPanel).height, 10);
            this.container.style.marginBottom = `${height}px`;
            layoutEditor();
        });

        this.formatOutput.addEventListener('click', this.formatOutputContent.bind(this));
        this.copyOutput.addEventListener('click', this.copyOutputContent.bind(this));
        this.clearOutput.addEventListener('click', this.clearOutputContent.bind(this));

        // 窗口大小变化事件
        window.addEventListener('resize', this.handleResize.bind(this));
    }

    handleMouseDown(e) {
        if (!e.target.classList.contains('resize-handle')) return;
        
        // 如果输出面板处于收起状态，不允许调整大小
        if (this.outputPanel.classList.contains('collapsed')) {
            return;
        }

        this.isResizing = true;
        this.startY = e.clientY;
        this.startHeight = parseInt(document.defaultView.getComputedStyle(this.outputPanel).height, 10);
        this.outputPanel.classList.add('resizing');
        document.documentElement.style.cursor = 'ns-resize';
        e.preventDefault();
    }

    handleMouseMove(e) {
        if (!this.isResizing) return;

        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }

        this.rafId = requestAnimationFrame(() => {
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
            this.outputContent.textContent = formatted;
        } catch (error) {
            console.error('Format error:', error);
        }
    }

    async copyOutputContent() {
        try {
            await navigator.clipboard.writeText(this.outputContent.textContent);
            this.showNotification('已复制到剪贴板', 'success');
        } catch (error) {
            console.error('Copy error:', error);
            this.showNotification('复制失败', 'error');
        }
    }

    clearOutputContent() {
        this.outputContent.innerHTML = '';
        this.outputContent.className = '';
    }

    showNotification(message, type = 'info') {
        const notification = document.getElementById('notification');
        notification.textContent = message;
        notification.style.backgroundColor = type === 'success' ? 'rgba(76, 175, 80, 0.9)' : 'rgba(244, 67, 54, 0.9)';
        notification.style.display = 'block';
        setTimeout(() => {
            notification.style.display = 'none';
        }, 2000);
    }
} 