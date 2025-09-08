// 编辑器核心模块
import { layoutEditor, DEFAULT_CODE, getSelectedModel, GPT_COMPLETION_SYSTEM_PROMPT } from '../utils/common.js';
import { registerCompletion } from './index.mjs';
import { getSystemSettings } from '../index.js';
import { CodeActionProvider } from './codeActionProvider.js';

export class Editor {
    constructor() {
        this.editor = null;
        this.defaultCode = DEFAULT_CODE;
        this.container = document.getElementById('container');
        this.completion = null;
        this.codeActionProvider = null;

        // 从localStorage读取主题设置，如果没有则默认为dark主题
        this.currentTheme = localStorage.getItem('editorTheme') || 'vs-dark';
        
        // 处理旧版本的主题名称
        if (this.currentTheme === 'vs-dark') {
            this.currentTheme = 'vs-dark-custom';
        }

        // 设置初始主题
        document.body.classList.remove('theme-dark', 'theme-light');
        document.body.classList.add(this.currentTheme === 'vs-dark-custom' ? 'theme-dark' : 'theme-light');
    }

    initialize(containerId) {
        // 定义 Visual Studio Dark 主题
        this.defineVSDarkTheme();
        
        // 添加全屏功能
        this.setupFullscreen();

        // 设置主题切换
        this.setupThemeToggle();

        this.editor = monaco.editor.create(document.getElementById(containerId), {
            value: this.defaultCode,
            language: 'csharp',
            theme: this.currentTheme,
            automaticLayout: true,
            minimap: { enabled: false },
            fontSize: 14,
            lineNumbers: 'on',
            renderWhitespace: 'selection',
            scrollBeyondLastLine: false
        });

        this.completion = registerCompletion(monaco, this.editor, {
            endpoint: "",
            language: "csharp",
            trigger: 'onIdle',
            maxContextLines: 100,
            enableCaching: true,
            onError: error => {
                console.log(error);
            },
            requestHandler: async ({ _endpoint, body }) => {

                // 如果禁用GPT补全，则返回空字符串
                if (getSystemSettings().disableGptComplete) {
                    return {
                        completion: ''
                    };
                }

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
                this.completion.trigger();
            },
        });

        // 初始化 Code Actions Provider
        this.codeActionProvider = new CodeActionProvider(this.editor);


        return this.editor;
    }


    setupFullscreen() {
        // 创建全屏按钮
        const fullscreenButton = document.createElement('button');
        fullscreenButton.className = 'editor-control-button fullscreen-button';
        fullscreenButton.innerHTML = '⛶';
        fullscreenButton.title = '全屏';
        this.container.appendChild(fullscreenButton);

        // 检测是否为移动设备，如果是则隐藏全屏按钮
        // 注：我们已经在CSS中通过媒体查询设置了隐藏，这里做双重保障
        const isMobile = window.matchMedia('(max-width: 768px)').matches;
        if (isMobile) {
            fullscreenButton.style.display = 'none';
        }

        let isFullscreen = false;
        fullscreenButton.addEventListener('click', () => {
            isFullscreen = !isFullscreen;
            const editorContainer = this.container;
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
                const editorContainer = this.container;
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
            themeButton.style.visibility = 'visible';
            // 设置初始图标
            themeButton.innerHTML = this.currentTheme === 'vs-dark' ? '☀️' :
                '<svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg" style="overflow-x: visible;">' +
                '<path d="M16.3592 13.9967C14.1552 17.8141 9.27399 19.122 5.45663 16.918C4.41706 16.3178 3.54192 15.5059 2.87537 14.538C2.65251 14.2143 2.79667 13.7674 3.16661 13.635C6.17301 12.559 7.78322 11.312 8.71759 9.52844C9.70125 7.65076 9.95545 5.59395 9.26732 2.77462C9.17217 2.38477 9.4801 2.01357 9.88082 2.03507C11.1233 2.10173 12.3371 2.45863 13.4378 3.09415C17.2552 5.2981 18.5631 10.1793 16.3592 13.9967Z" fill="#242424" style="overflow-x: visible;"></path>' +
                '</svg>';

            themeButton.addEventListener('click', () => {
                this.toggleTheme();
            });
        }
    }

    defineVSDarkTheme() {
        monaco.editor.defineTheme('vs-dark-custom', {
            base: 'vs-dark',
            inherit: true,
            rules: [
                // 关键字
                { token: 'keyword', foreground: '569CD6' },
                { token: 'keyword.flow', foreground: '569CD6' },
                { token: 'keyword.other', foreground: '569CD6' },
                
                // 类型
                { token: 'type', foreground: '4EC9B0' },
                { token: 'type.identifier', foreground: '4EC9B0' },
                
                // 字符串
                { token: 'string', foreground: 'CE9178' },
                { token: 'string.escape', foreground: 'D7BA7D' },
                
                // 数字
                { token: 'number', foreground: 'B5CEA8' },
                { token: 'number.hex', foreground: 'B5CEA8' },
                { token: 'number.float', foreground: 'B5CEA8' },
                
                // 注释
                { token: 'comment', foreground: '57A64A' },
                { token: 'comment.line', foreground: '57A64A' },
                { token: 'comment.block', foreground: '57A64A' },
                
                // 标识符
                { token: 'identifier', foreground: '9CDCFE' },
                { token: 'variable', foreground: '9CDCFE' },
                { token: 'parameter', foreground: '9CDCFE' },
                
                // 函数/方法
                { token: 'function', foreground: 'DCDCAA' },
                { token: 'method', foreground: 'DCDCAA' },
                
                // 运算符
                { token: 'operator', foreground: 'D4D4D4' },
                { token: 'delimiter', foreground: 'D4D4D4' },
                
                // 属性
                { token: 'property', foreground: '9CDCFE' },
                
                // 命名空间
                { token: 'namespace', foreground: 'FFFFFF' },
                
                // 其他
                { token: 'tag', foreground: '569CD6' },
                { token: 'attribute.name', foreground: '9CDCFE' },
                { token: 'attribute.value', foreground: 'CE9178' }
            ],
            colors: {
                'editor.background': '#1E1E1E',
                'editor.foreground': '#D4D4D4',
                'editor.lineHighlightBackground': '#2A2D2E',
                'editor.selectionBackground': '#264F78',
                'editor.inactiveSelectionBackground': '#3A3D41',
                'editorLineNumber.foreground': '#858585',
                'editorLineNumber.activeForeground': '#C6C6C6',
                'editorCursor.foreground': '#AEAFAD',
                'editor.selectionHighlightBackground': '#ADD6FF26',
                'editor.wordHighlightBackground': '#575757',
                'editor.wordHighlightStrongBackground': '#004972',
                'editorBracketMatch.background': '#0064001a',
                'editorBracketMatch.border': '#888888'
            }
        });
        
        // 如果当前主题是 vs-dark，更新为自定义版本
        if (this.currentTheme === 'vs-dark') {
            this.currentTheme = 'vs-dark-custom';
        }
    }

    toggleTheme() {
        this.currentTheme = this.currentTheme === 'vs-dark-custom' ? 'vs-light' : 'vs-dark-custom';
        monaco.editor.setTheme(this.currentTheme);

        // 更新全局主题
        document.body.classList.toggle('theme-light', this.currentTheme === 'vs-light');
        document.body.classList.toggle('theme-dark', this.currentTheme === 'vs-dark-custom');

        // 保存主题设置到localStorage
        localStorage.setItem('editorTheme', this.currentTheme);

        // 更新按钮图标
        const themeButton = document.getElementById('themeButton');
        if (themeButton) {
            themeButton.innerHTML = this.currentTheme === 'vs-dark-custom' ? '☀️' :
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