import { layoutEditor } from '../utils/common.js';

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
            const width = Math.min(Math.max(this.startWidth + (e.clientX - this.startX), 290), window.innerWidth * 0.4);
            this.fileList.style.width = `${width}px`;

            // 根据聊天面板的状态调整容器宽度
            if (this.chatPanelDisplay !== 'none') {
                this.container.style.width = `calc(100% - ${width}px - ${this.chatPanelWidth}px)`;
                this.container.style.marginRight = `${this.chatPanelWidth}px`;
            } else {
                this.container.style.width = `calc(100% - ${width}px)`;
                this.container.style.marginRight = '0';
            }
            
            this.container.style.marginLeft = `${width}px`;
            layoutEditor();
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
        const chatPanelDisplay = getComputedStyle(this.chatPanel).display;

        if (chatPanelDisplay !== 'none') {
            this.container.style.width = `calc(100% - ${fileListWidth}px - ${chatPanelWidth}px)`;
            this.container.style.marginRight = `${chatPanelWidth}px`;
        } else {
            this.container.style.width = `calc(100% - ${fileListWidth}px)`;
            this.container.style.marginRight = '0';
        }
        this.container.style.marginLeft = `${fileListWidth}px`;
        layoutEditor();
    }
}

// 初始化文件列表调整大小功能
const fileListResizer = new FileListResizer();

// 导出实例以供其他模块使用
export { fileListResizer }; 