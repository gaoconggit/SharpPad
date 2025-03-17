// 编辑器核心模块
import { layoutEditor, DEFAULT_CODE, getSelectedModel, GPT_COMPLETION_SYSTEM_PROMPT } from '../utils/common.js';
import { registerCompletion } from './index.mjs';

export class Editor {
    constructor() {
        this.editor = null;
        this.defaultCode = DEFAULT_CODE;
        
        // 从localStorage读取主题设置，如果没有则默认为light主题
        this.currentTheme = localStorage.getItem('editorTheme') || 'vs-light';
        
        // 设置初始主题
        document.body.classList.remove('theme-dark', 'theme-light');
        document.body.classList.add(this.currentTheme === 'vs-dark' ? 'theme-dark' : 'theme-light');
    }

    initialize(containerId) {
        this.editor = monaco.editor.create(document.getElementById(containerId), {
            value: this.defaultCode,
            language: 'csharp',
            theme: this.currentTheme
        });

        // 添加全屏功能
        this.setupFullscreen();

        const completion = registerCompletion(monaco, this.editor, {
            endpoint: "",
            language: "csharp",
            trigger: 'onIdle',
            maxContextLines: 100,
            enableCaching: true,
            onError: error => {
                console.log(error);
            },
            requestHandler: async ({ _endpoint, body }) => {
                var selectedModel = getSelectedModel();
                
                // 构建请求头和请求体
                const headers = {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${selectedModel.apiKey}`
                };
                
                const requestBody = {
                    "model": selectedModel.name,
                    "stream": false,
                    "messages": [
                        {
                            "role": "system",
                            "content": GPT_COMPLETION_SYSTEM_PROMPT.replace("{{language}}", body.completionMetadata.language)
                        },
                        {
                            "role": "user",
                            "content": `Complete the following ${body.completionMetadata.language} code at line ${body.completionMetadata.cursorPosition.lineNumber}, column ${body.completionMetadata.cursorPosition.column}:
${body.completionMetadata.textBeforeCursor}<cursor>${body.completionMetadata.textAfterCursor}`
                        }
                    ],
                    "max_tokens": 300
                };
                
                // 确定请求端点
                let endpoint = selectedModel.endpoint;
                if (selectedModel.useBackend) {
                    // 如果使用后端，将目标端点作为请求头传递
                    headers['x-endpoint'] = selectedModel.endpoint;
                    endpoint = './v1/chat/completions';
                }
                
                const response = await fetch(endpoint, {
                    method: 'POST',
                    headers: headers,
                    body: JSON.stringify(requestBody)
                });

                const data = await response.json();
                return {
                    completion: data.choices[0].message.content,
                };
            }
        });

        monaco.editor.addEditorAction({
            id: 'monacopilot.triggerCompletion',
            label: 'GPT Complete Code',
            contextMenuGroupId: 'navigation',
            keybindings: [
                monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.Space,
            ],
            run: () => {
                completion.trigger();
            },
        });

        return this.editor;
    }

    setupFullscreen() {
        // 添加全屏和主题切换按钮样式
        const style = document.createElement('style');
        style.textContent = `
            .editor-control-button {
                position: absolute !important;
                top: 10px !important;
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

            .editor-control-button:hover {
                opacity: 1 !important;
                background-color: #404040 !important;
            }

            .fullscreen-button {
                right: 10px !important;
            }

            .theme-button {
                right: 50px !important;
            }

            /* 深色主题下的主题按钮样式 */
            .theme-dark .theme-button {
                background-color: #2d2d2d !important;
                color: #fff !important;
            }

            /* 浅色主题下的主题按钮样式 */
            .theme-light .theme-button {
                background-color: rgb(243, 239, 239) !important;
                color: #242424 !important;
            }

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
        `;
        document.head.appendChild(style);

        const container = this.editor.getDomNode().parentElement;

        // 创建全屏按钮
        const fullscreenButton = document.createElement('button');
        fullscreenButton.className = 'editor-control-button fullscreen-button';
        fullscreenButton.innerHTML = '⛶';
        fullscreenButton.title = '全屏';
        container.appendChild(fullscreenButton);

        // 创建主题切换按钮
        const themeButton = document.createElement('button');
        themeButton.className = 'editor-control-button theme-button';
        themeButton.innerHTML = this.currentTheme === 'vs-dark' ? '☀️' :
            '<svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg" style="overflow-x: visible;">' +
            '<path d="M16.3592 13.9967C14.1552 17.8141 9.27399 19.122 5.45663 16.918C4.41706 16.3178 3.54192 15.5059 2.87537 14.538C2.65251 14.2143 2.79667 13.7674 3.16661 13.635C6.17301 12.559 7.78322 11.312 8.71759 9.52844C9.70125 7.65076 9.95545 5.59395 9.26732 2.77462C9.17217 2.38477 9.4801 2.01357 9.88082 2.03507C11.1233 2.10173 12.3371 2.45863 13.4378 3.09415C17.2552 5.2981 18.5631 10.1793 16.3592 13.9967Z" fill="#242424" style="overflow-x: visible;"></path>' +
            '</svg>';
        themeButton.title = '切换主题';
        container.appendChild(themeButton);

        // 设置主题切换事件
        themeButton.addEventListener('click', () => {
            this.toggleTheme();
        });

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

        // 保存主题设置到localStorage
        localStorage.setItem('editorTheme', this.currentTheme);

        // 更新按钮图标
        const themeButton = document.querySelector('.theme-button');
        if (themeButton) {
            themeButton.innerHTML = this.currentTheme === 'vs-dark' ? '☀️' :
                '<svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg" style="overflow-x: visible;">' +
                '<path d="M16.3592 13.9967C14.1552 17.8141 9.27399 19.122 5.45663 16.918C4.41706 16.3178 3.54192 15.5059 2.87537 14.538C2.65251 14.2143 2.79667 13.7674 3.16661 13.635C6.17301 12.559 7.78322 11.312 8.71759 9.52844C9.70125 7.65076 9.95545 5.59395 9.26732 2.77462C9.17217 2.38477 9.4801 2.01357 9.88082 2.03507C11.1233 2.10173 12.3371 2.45863 13.4378 3.09415C17.2552 5.2981 18.5631 10.1793 16.3592 13.9967Z" fill="#242424" style="overflow-x: visible;"></path>' +
                '</svg>';
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