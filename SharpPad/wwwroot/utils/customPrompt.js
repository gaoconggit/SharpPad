// 自定义 prompt 对话框,兼容 WKWebView
class CustomPrompt {
    constructor() {
        this.dialog = document.getElementById('customPromptDialog');
        this.title = document.getElementById('customPromptTitle');
        this.label = document.getElementById('customPromptLabel');
        this.input = document.getElementById('customPromptInput');
        this.confirmBtn = document.getElementById('customPromptConfirm');
        this.cancelBtn = document.getElementById('customPromptCancel');
        this.closeBtn = document.getElementById('customPromptClose');
        this.resolveCallback = null;

        this.initializeEvents();
    }

    initializeEvents() {
        // 确定按钮
        this.confirmBtn.addEventListener('click', () => {
            const value = this.input.value;
            this.close();
            if (this.resolveCallback) {
                this.resolveCallback(value);
                this.resolveCallback = null;
            }
        });

        // 取消按钮
        this.cancelBtn.addEventListener('click', () => {
            this.close();
            if (this.resolveCallback) {
                this.resolveCallback(null);
                this.resolveCallback = null;
            }
        });

        // 关闭按钮
        this.closeBtn.addEventListener('click', () => {
            this.close();
            if (this.resolveCallback) {
                this.resolveCallback(null);
                this.resolveCallback = null;
            }
        });

        // 输入框回车确认
        this.input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                this.confirmBtn.click();
            } else if (e.key === 'Escape') {
                e.preventDefault();
                this.cancelBtn.click();
            }
        });

        // 点击背景关闭
        this.dialog.addEventListener('click', (e) => {
            if (e.target === this.dialog) {
                this.cancelBtn.click();
            }
        });
    }

    show(message, defaultValue = '') {
        return new Promise((resolve) => {
            this.resolveCallback = resolve;

            // 设置标题和标签
            this.title.textContent = '输入';
            this.label.textContent = message;

            // 设置默认值
            this.input.value = defaultValue;

            // 显示对话框
            this.dialog.style.display = 'block';

            // 聚焦到输入框并选中文本
            setTimeout(() => {
                this.input.focus();
                this.input.select();
            }, 100);
        });
    }

    close() {
        this.dialog.style.display = 'none';
        this.input.value = '';
    }
}

// 创建全局实例
let customPromptInstance = null;

// 兼容的 prompt 函数
export function customPrompt(message, defaultValue = '') {
    if (!customPromptInstance) {
        customPromptInstance = new CustomPrompt();
    }
    return customPromptInstance.show(message, defaultValue);
}

// 初始化函数
export function initCustomPrompt() {
    if (!customPromptInstance) {
        customPromptInstance = new CustomPrompt();
    }
    return customPromptInstance;
}

// 自定义 confirm 对话框
class CustomConfirm {
    constructor() {
        this.dialog = document.getElementById('customConfirmDialog');
        this.title = document.getElementById('customConfirmTitle');
        this.message = document.getElementById('customConfirmMessage');
        this.okBtn = document.getElementById('customConfirmOk');
        this.cancelBtn = document.getElementById('customConfirmCancel');
        this.closeBtn = document.getElementById('customConfirmClose');
        this.resolveCallback = null;

        this.initializeEvents();
    }

    initializeEvents() {
        // 确定按钮
        this.okBtn.addEventListener('click', () => {
            this.close();
            if (this.resolveCallback) {
                this.resolveCallback(true);
                this.resolveCallback = null;
            }
        });

        // 取消按钮
        this.cancelBtn.addEventListener('click', () => {
            this.close();
            if (this.resolveCallback) {
                this.resolveCallback(false);
                this.resolveCallback = null;
            }
        });

        // 关闭按钮
        this.closeBtn.addEventListener('click', () => {
            this.close();
            if (this.resolveCallback) {
                this.resolveCallback(false);
                this.resolveCallback = null;
            }
        });

        // 键盘事件
        this.dialog.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                this.okBtn.click();
            } else if (e.key === 'Escape') {
                e.preventDefault();
                this.cancelBtn.click();
            }
        });

        // 点击背景关闭
        this.dialog.addEventListener('click', (e) => {
            if (e.target === this.dialog) {
                this.cancelBtn.click();
            }
        });
    }

    show(message) {
        return new Promise((resolve) => {
            this.resolveCallback = resolve;

            // 设置消息
            this.message.textContent = message;

            // 显示对话框
            this.dialog.style.display = 'block';

            // 聚焦到确定按钮
            setTimeout(() => {
                this.okBtn.focus();
            }, 100);
        });
    }

    close() {
        this.dialog.style.display = 'none';
        this.message.textContent = '';
    }
}

// 创建全局实例
let customConfirmInstance = null;

// 兼容的 confirm 函数
export function customConfirm(message) {
    if (!customConfirmInstance) {
        customConfirmInstance = new CustomConfirm();
    }
    return customConfirmInstance.show(message);
}

// 初始化函数
export function initCustomConfirm() {
    if (!customConfirmInstance) {
        customConfirmInstance = new CustomConfirm();
    }
    return customConfirmInstance;
}
