import { layoutEditor, isMobileDevice, getResponsiveSize, setContainerWidth } from '../utils/common.js';

export class FileListResizer {
    constructor() {
        this.fileList = document.getElementById('fileList');
        this.container = document.getElementById('container');
        this.chatPanel = document.getElementById('chatPanel');
        this.isResizing = false;
        this.startX = 0;
        this.startWidth = 0;
        this.rafId = null;

        this.initializeResizeHandle();
    }

    initializeResizeHandle() {
        // 创建调整大小的手柄
        const resizeHandle = document.createElement('div');
        resizeHandle.className = 'file-list-resize-handle';
        this.fileList.appendChild(resizeHandle);

        // 添加事件监听
        resizeHandle.addEventListener('mousedown', this.handleMouseDown.bind(this));
        document.addEventListener('mousemove', this.handleMouseMove.bind(this));
        document.addEventListener('mouseup', this.handleMouseUp.bind(this));
        document.addEventListener('selectstart', this.handleSelectStart.bind(this));
    }

    handleMouseDown(e) {
        this.isResizing = true;
        this.startX = e.clientX;
        this.startWidth = parseInt(document.defaultView.getComputedStyle(this.fileList).width, 10);
        
        // 记录初始状态
        this.chatPanelWidth = parseInt(getComputedStyle(this.chatPanel).width, 10);
        this.chatPanelDisplay = getComputedStyle(this.chatPanel).display;
        
        this.fileList.classList.add('resizing');
        document.documentElement.style.cursor = 'ew-resize';
        e.preventDefault();
    }

    handleMouseMove(e) {
        if (!this.isResizing) return;

        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }

        this.rafId = requestAnimationFrame(() => {
            // 根据设备类型设置最小宽度和最大宽度
            const minWidth = isMobileDevice() ? 200 : 290;
            const maxWidth = window.innerWidth * (isMobileDevice() ? 0.3 : 0.4);
            
            const width = Math.min(Math.max(this.startWidth + (e.clientX - this.startX), minWidth), maxWidth);
            this.fileList.style.width = `${width}px`;

            // 根据聊天面板的状态调整容器宽度
            const isChatVisible = this.chatPanelDisplay !== 'none';
            setContainerWidth(this.container, width, this.chatPanelWidth, isChatVisible);
        });
    }

    handleMouseUp() {
        if (!this.isResizing) return;

        this.isResizing = false;
        this.fileList.classList.remove('resizing');
        document.documentElement.style.cursor = '';

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

    // 更新容器宽度（供外部调用，比如聊天面板显示/隐藏时）
    updateContainerWidth() {
        const fileListWidth = parseInt(getComputedStyle(this.fileList).width, 10);
        const chatPanelWidth = parseInt(getComputedStyle(this.chatPanel).width, 10);
        const isChatVisible = getComputedStyle(this.chatPanel).display !== 'none';
        
        // 使用响应式宽度设置函数
        setContainerWidth(this.container, fileListWidth, chatPanelWidth, isChatVisible);
    }
}

// 初始化文件列表调整大小功能
const fileListResizer = new FileListResizer();

// 导出实例以供其他模块使用
export { fileListResizer }; 