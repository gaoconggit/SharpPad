// 编辑器核心模块
import { layoutEditor, DEFAULT_CODE } from '../utils/common.js';
import { registerCompletion } from './index.mjs';

export class Editor {
    constructor() {
        this.editor = null;
        this.defaultCode = DEFAULT_CODE;
        this.currentTheme = 'vs-dark';

        // 设置初始主题
        document.body.classList.add('theme-dark');
    }

    initialize(containerId) {
        this.editor = monaco.editor.create(document.getElementById(containerId), {
            value: this.defaultCode,
            language: 'csharp',
            theme: this.currentTheme
        });

        // 添加全屏功能
        this.setupFullscreen();

        // 设置主题切换
        this.setupThemeToggle();

        const completion = registerCompletion(monaco, this.editor, {
            // This is the endpoint where you set up the monacopilot API handler
            // https://github.com/arshad-yaseen/monacopilot?tab=readme-ov-file#api-handler
            endpoint: "http://localhost:3030/v1/chat/monaco-copilot",
            language: "csharp",
            trigger: 'onIdle',
            maxContextLines: 64,
            enableCaching: false,
            onError: error => {
                console.error(error);
            },
        });

        return this.editor;
    }

    setupFullscreen() {
        // 添加全屏样式
        const style = document.createElement('style');
        style.textContent = `
            .fullscreen-editor {
                position: fixed !important;
                top: 0 !important;
                left: 0 !important;
                width: 100vw !important;
                height: 100vh !important;
                z-index: 9999 !important;
                padding: 0 !important;
                margin: 0 !important;
            }

            .fullscreen-editor #container {
                position: fixed !important;
                top: 0 !important;
                left: 0 !important;
                width: 100vw !important;
                height: 100vh !important;
                margin: 0 !important;
                padding: 0 !important;
                z-index: 9999 !important;
                transform: none !important;
            }

            .fullscreen-editor .monaco-editor {
                width: 100% !important;
                height: 100% !important;
                margin: 0 !important;
                padding: 0 !important;
            }

            .fullscreen-editor .monaco-editor .overflow-guard {
                width: 100% !important;
                height: 100% !important;
            }

            .fullscreen-button {
                position: absolute !important;
                top: 10px !important;
                right: 10px !important;
                z-index: 10000;
                width: 32px !important;
                height: 32px !important;
                padding: 0 !important;
                border: none !important;
                border-radius: 4px !important;
                background-color: #2d2d2d !important;
                color: #fff !important;
                font-size: 18px !important;
                cursor: pointer !important;
                display: flex !important;
                align-items: center !important;
                justify-content: center !important;
                opacity: 0.7 !important;
                transition: all 0.2s ease !important;
            }

            .fullscreen-button:hover {
                opacity: 1 !important;
                background-color: #404040 !important;
            }
        `;
        document.head.appendChild(style);

        // 创建全屏按钮
        const fullscreenButton = document.createElement('button');
        fullscreenButton.className = 'fullscreen-button';
        fullscreenButton.innerHTML = '⛶';
        fullscreenButton.title = '全屏';

        // 将全屏按钮添加到编辑器容器中
        const container = this.editor.getDomNode().parentElement;
        container.appendChild(fullscreenButton);

        let isFullscreen = false;
        fullscreenButton.addEventListener('click', () => {
            isFullscreen = !isFullscreen;
            const editorContainer = this.editor.getDomNode().parentElement;
            if (isFullscreen) {
                editorContainer.classList.add('fullscreen-editor');
                fullscreenButton.innerHTML = '⮌';
                fullscreenButton.title = '退出全屏';
            } else {
                editorContainer.classList.remove('fullscreen-editor');
                fullscreenButton.innerHTML = '⛶';
                fullscreenButton.title = '全屏';
            }
            layoutEditor();
        });

        // ESC 退出全屏
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && isFullscreen) {
                isFullscreen = false;
                const editorContainer = this.editor.getDomNode().parentElement;
                editorContainer.classList.remove('fullscreen-editor');
                fullscreenButton.innerHTML = '⛶';
                fullscreenButton.title = '全屏';
                layoutEditor();
            }
        });
    }

    setupThemeToggle() {
        const themeButton = document.getElementById('themeButton');
        if (themeButton) {
            themeButton.addEventListener('click', () => {
                this.toggleTheme();
            });
        }
    }

    toggleTheme() {
        this.currentTheme = this.currentTheme === 'vs-dark' ? 'vs-light' : 'vs-dark';
        monaco.editor.setTheme(this.currentTheme);

        // 更新全局主题
        document.body.classList.toggle('theme-light', this.currentTheme === 'vs-light');
        document.body.classList.toggle('theme-dark', this.currentTheme === 'vs-dark');

        // 更新按钮图标
        const themeButton = document.getElementById('themeButton');
        if (themeButton) {
            themeButton.innerHTML = this.currentTheme === 'vs-dark' ? '🌓' : '☀️';
        }
    }

    getValue() {
        return this.editor ? this.editor.getValue() : '';
    }

    setValue(value) {
        if (this.editor) {
            this.editor.setValue(value);
        }
    }

    formatDocument() {
        if (this.editor) {
            this.editor.getAction('editor.action.formatDocument').run();
        }
    }

    triggerSuggest() {
        if (this.editor) {
            this.editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
        }
    }
} 