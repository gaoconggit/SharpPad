// 编辑器核心模块
import { layoutEditor, DEFAULT_CODE, getSelectedModel, GPT_COMPLETION_SYSTEM_PROMPT, getCurrentFile } from '../utils/common.js';
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
        this.breakpointDecorationIds = [];
        this.breakpointLines = new Set();
        this.updateBreakpointDecorations = null;
        this.breakpointTooltip = null;
        this.hoverBreakpointDecorationIds = [];
        this.breakpointStoragePrefix = 'sharpPad.breakpoints.';

        // 从localStorage读取主题设置，如果没有则默认为dark主题
        this.currentTheme = localStorage.getItem('editorTheme') || 'vs-dark';
        
        // 处理旧版本的主题名称
        if (this.currentTheme === 'vs-dark') {
            this.currentTheme = 'vs-dark-custom';
        }

        // 处理旧版本的亮色主题名称
        if (this.currentTheme === 'vs-light') {
            this.currentTheme = 'vs-light-custom';
        }
        
        // 设置初始主题
        document.body.classList.remove('theme-dark', 'theme-light');
        document.body.classList.add(this.currentTheme === 'vs-dark-custom' ? 'theme-dark' : 'theme-light');
    }

    initialize(containerId) {
        // 定义 Visual Studio Dark 主题
        this.defineVSDarkTheme();
        
        // 定义 Visual Studio 2022 Light 主题
        this.defineVSLightTheme();
        
        // 添加全屏功能
        this.setupFullscreen();

        // 设置主题切换
        this.setupThemeToggle();

        this.editor = monaco.editor.create(document.getElementById(containerId), {
            value: this.defaultCode,
            language: 'csharp',
            theme: this.currentTheme,
            automaticLayout: true,
            minimap: { enabled: true },
            fontSize: 14,
            lineNumbers: 'on',
            renderWhitespace: 'selection',
            scrollBeyondLastLine: true,
            glyphMargin: true
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
                            "content": `Complete the ${body.completionMetadata.language} code at the <cursor> position.

CURSOR POSITION: Line ${body.completionMetadata.cursorPosition.lineNumber}, Column ${body.completionMetadata.cursorPosition.column}

CODE CONTEXT:
${body.completionMetadata.textBeforeCursor}<cursor>${body.completionMetadata.textAfterCursor}

Provide only the code to insert at the cursor position.`
                        }
                    ]
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

        // 适配桌面端 WebView 缺失系统菜单时的快捷键
        this.setupDesktopShortcuts();
        this.setupDebuggerBreakpoints();
        this.setupDebuggerBreakpointToggle();
        this.setupDebuggerBreakpointHover();

        return this.editor;
    }

    setupDesktopShortcuts() {
        const platform = navigator?.platform || '';
        const isMacLike = platform.includes('Mac') || platform.includes('iPad') || platform.includes('iPhone');
        if (!isMacLike || typeof monaco === 'undefined' || !this.editor) {
            return;
        }

        this.editor.onKeyDown(event => {
            if (!event.metaKey) {
                return;
            }

            const key = event.browserEvent?.key?.toLowerCase();
            if (!key) {
                return;
            }

            const trigger = actionId => {
                event.preventDefault();
                event.stopPropagation();
                this.editor.trigger('desktop-shortcut', actionId);
            };

            switch (key) {
                case 'z':
                    if (event.shiftKey) {
                        trigger('redo');
                    } else {
                        trigger('undo');
                    }
                    break;
                case 'x':
                    trigger('editor.action.clipboardCutAction');
                    document.execCommand('cut');
                    break;
                case 'c':
                    trigger('editor.action.clipboardCopyAction');
                    document.execCommand('copy');
                    break;
                case 'v':
                    trigger('editor.action.clipboardPasteAction');
                    if (navigator.clipboard?.readText) {
                        navigator.clipboard.readText()
                            .then(text => {
                                if (typeof text === 'string') {
                                    const selection = this.editor.getSelection();
                                    const model = this.editor.getModel();
                                    if (model && selection) {
                                        this.editor.executeEdits('desktop-shortcut', [{
                                            range: selection,
                                            text,
                                            forceMoveMarkers: true
                                        }]);
                                    } else {
                                        this.editor.trigger('desktop-shortcut', 'editor.action.clipboardPasteAction');
                                    }
                                }
                            })
                            .catch(() => {
                                this.editor.trigger('desktop-shortcut', 'editor.action.clipboardPasteAction');
                            });
                    }
                    break;
                case 'a':
                    trigger('editor.action.selectAll');
                    break;
                default:
                    break;
            }
        });
    }

    setupFullscreen() {
        // 创建全屏按钮
        const fullscreenButton = document.createElement('button');
        fullscreenButton.className = 'editor-control-button fullscreen-button';
        fullscreenButton.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-label="Fullscreen corners"><path d="M8 3H3v5" /><path d="M16 3h5v5" /><path d="M21 16v5h-5" /><path d="M8 21H3v-5" /></svg>';
        fullscreenButton.title = '全屏';
        this.container.appendChild(fullscreenButton);

        // 检测是否为移动设备，如果是则隐藏全屏按钮
        // 注：我们已经在CSS中通过媒体查询设置了隐藏，这里做双重保障
        const isMobile = window.matchMedia('(max-width: 768px) and (pointer: coarse), (max-width: 480px)').matches;
        if (isMobile) {
            fullscreenButton.style.display = 'none';
        }

        let isFullscreen = false;
        fullscreenButton.addEventListener('click', () => {
            isFullscreen = !isFullscreen;
            const editorContainer = this.container;
            if (isFullscreen) {
                editorContainer.classList.add('fullscreen-editor');
                fullscreenButton.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-label="U-turn arrow"><path d="M9 11L5 15L9 19" /><path d="M5 15h7a5 5 0 1 0 0-10h-1" /></svg>';
                fullscreenButton.title = '退出全屏';
            } else {
                editorContainer.classList.remove('fullscreen-editor');
                fullscreenButton.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-label="Fullscreen corners"><path d="M8 3H3v5" /><path d="M16 3h5v5" /><path d="M21 16v5h-5" /><path d="M8 21H3v-5" /></svg>';
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
                fullscreenButton.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-label="Fullscreen corners"><path d="M8 3H3v5" /><path d="M16 3h5v5" /><path d="M21 16v5h-5" /><path d="M8 21H3v-5" /></svg>';
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
            themeButton.innerHTML = this.currentTheme === 'vs-dark-custom' ? '☀️' :
                '<svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg" style="overflow-x: visible;">' +
                '<path d="M16.3592 13.9967C14.1552 17.8141 9.27399 19.122 5.45663 16.918C4.41706 16.3178 3.54192 15.5059 2.87537 14.538C2.65251 14.2143 2.79667 13.7674 3.16661 13.635C6.17301 12.559 7.78322 11.312 8.71759 9.52844C9.70125 7.65076 9.95545 5.59395 9.26732 2.77462C9.17217 2.38477 9.4801 2.01357 9.88082 2.03507C11.1233 2.10173 12.3371 2.45863 13.4378 3.09415C17.2552 5.2981 18.5631 10.1793 16.3592 13.9967Z" fill="#242424" style="overflow-x: visible;"></path>' +
                '</svg>';

            themeButton.addEventListener('click', () => {
                this.toggleTheme();
            });
        }
    }

    setupDebuggerBreakpoints() {
        if (!this.editor) {
            return;
        }

        addDebuggerBreakpointStyles();

        const updateDecorations = () => {
            const decorations = [];
            const lines = Array.from(this.breakpointLines).sort((a, b) => a - b);
            lines.forEach(lineNumber => {
                decorations.push({
                    range: new monaco.Range(lineNumber, 1, lineNumber, 1),
                    options: {
                        isWholeLine: true,
                        className: 'debugger-breakpoint-line',
                        glyphMarginClassName: 'debugger-breakpoint-glyph'
                    }
                });
            });

            this.breakpointDecorationIds = this.editor.deltaDecorations(
                this.breakpointDecorationIds,
                decorations
            );
        };

        updateDecorations();
        this.updateBreakpointDecorations = updateDecorations;
    }

    setupDebuggerBreakpointToggle() {
        if (!this.editor) {
            return;
        }

        this.editor.onMouseDown(event => {
            if (event.target?.type !== monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN) {
                return;
            }

            const position = event.target.position;
            if (!position) {
                return;
            }

            this.toggleDebuggerBreakpointAtLine(position.lineNumber);
        });
    }

    setupDebuggerBreakpointHover() {
        if (!this.editor) {
            return;
        }

        const updateGhostDecoration = lineNumber => {
            if (!lineNumber || this.breakpointLines.has(lineNumber)) {
                this.hoverBreakpointDecorationIds = this.editor.deltaDecorations(
                    this.hoverBreakpointDecorationIds,
                    []
                );
                return;
            }

            this.hoverBreakpointDecorationIds = this.editor.deltaDecorations(
                this.hoverBreakpointDecorationIds,
                [{
                    range: new monaco.Range(lineNumber, 1, lineNumber, 1),
                    options: {
                        glyphMarginClassName: 'debugger-breakpoint-glyph debugger-breakpoint-ghost'
                    }
                }]
            );
        };

        const showTooltip = (text, x, y) => {
            const tooltip = this.ensureDebuggerBreakpointTooltip();
            tooltip.textContent = text;
            tooltip.style.left = `${x}px`;
            tooltip.style.top = `${y}px`;
            tooltip.classList.add('is-visible');
        };

        const hideTooltip = () => {
            if (this.breakpointTooltip) {
                this.breakpointTooltip.classList.remove('is-visible');
            }
            updateGhostDecoration(null);
        };

        this.editor.onMouseMove(event => {
            const targetType = event.target?.type;
            const isGlyphMargin = targetType === monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN;

            if (!isGlyphMargin || !event.target.position) {
                hideTooltip();
                return;
            }

            const lineNumber = event.target.position.lineNumber;
            const browserEvent = event.event?.browserEvent;
            if (!browserEvent) {
                hideTooltip();
                return;
            }

            const text = this.breakpointLines.has(lineNumber)
                ? 'Click to remove breakpoint'
                : 'Click to add a breakpoint';

            showTooltip(text, browserEvent.clientX + 10, browserEvent.clientY + 10);
            updateGhostDecoration(lineNumber);
        });

        this.editor.onMouseLeave(() => {
            hideTooltip();
        });
    }

    toggleDebuggerBreakpointAtLine(lineNumber) {
        if (this.breakpointLines.has(lineNumber)) {
            this.breakpointLines.delete(lineNumber);
        } else {
            this.breakpointLines.add(lineNumber);
        }

        if (this.updateBreakpointDecorations) {
            this.updateBreakpointDecorations();
        }

        this.saveBreakpointsForActiveFile();

        if (this.hoverBreakpointDecorationIds.length > 0) {
            const hoveredLine = this.breakpointLines.has(lineNumber) ? null : lineNumber;
            if (!hoveredLine) {
                this.hoverBreakpointDecorationIds = this.editor.deltaDecorations(
                    this.hoverBreakpointDecorationIds,
                    []
                );
            }
        }
    }

    getBreakpointLines() {
        return Array.from(this.breakpointLines).sort((a, b) => a - b);
    }

    normalizeBreakpointLines(lines) {
        if (!Array.isArray(lines)) {
            return [];
        }

        return Array.from(new Set(lines.filter(Number.isFinite).map(value => Math.floor(value)).filter(value => value > 0)))
            .sort((a, b) => a - b);
    }

    getBreakpointStorageKey(fileId) {
        if (!fileId) {
            return null;
        }

        return `${this.breakpointStoragePrefix}${fileId}`;
    }

    saveBreakpointsForActiveFile() {
        const currentFile = getCurrentFile();
        if (!currentFile?.id) {
            return;
        }

        const key = this.getBreakpointStorageKey(currentFile.id);
        if (!key) {
            return;
        }

        try {
            const lines = this.getBreakpointLines();
            if (lines.length === 0) {
                localStorage.removeItem(key);
                return;
            }

            localStorage.setItem(key, JSON.stringify(lines));
        } catch (error) {
            console.warn('无法保存断点信息:', error);
        }
    }

    loadBreakpointsForFile(fileId) {
        const key = this.getBreakpointStorageKey(fileId);
        if (!key) {
            this.setBreakpointLines([]);
            return;
        }

        let storedLines = [];
        try {
            const raw = localStorage.getItem(key);
            storedLines = raw ? JSON.parse(raw) : [];
        } catch (error) {
            console.warn('无法读取断点信息:', error);
        }

        this.setBreakpointLines(storedLines);
    }

    setBreakpointLines(lines) {
        this.breakpointLines = new Set(this.normalizeBreakpointLines(lines));

        if (this.updateBreakpointDecorations) {
            this.updateBreakpointDecorations();
        }

        if (this.editor && this.hoverBreakpointDecorationIds.length > 0) {
            this.hoverBreakpointDecorationIds = this.editor.deltaDecorations(
                this.hoverBreakpointDecorationIds,
                []
            );
        }
    }

    ensureDebuggerBreakpointTooltip() {
        if (this.breakpointTooltip) {
            return this.breakpointTooltip;
        }

        const tooltip = document.createElement('div');
        tooltip.className = 'debugger-breakpoint-tooltip';
        document.body.appendChild(tooltip);
        this.breakpointTooltip = tooltip;
        return tooltip;
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

    defineVSLightTheme() {
        monaco.editor.defineTheme('vs-light-custom', {
            base: 'vs',
            inherit: true,
            rules: [
                // 关键字
                { token: 'keyword', foreground: '0000FF' },
                { token: 'keyword.flow', foreground: '0000FF' },
                { token: 'keyword.other', foreground: '0000FF' },
                
                // 类型
                { token: 'type', foreground: '2B91AF' },
                { token: 'type.identifier', foreground: '2B91AF' },
                
                // 字符串
                { token: 'string', foreground: 'A31515' },
                { token: 'string.escape', foreground: 'FF8800' },
                
                // 数字
                { token: 'number', foreground: '098658' },
                { token: 'number.hex', foreground: '098658' },
                { token: 'number.float', foreground: '098658' },
                
                // 注释
                { token: 'comment', foreground: '008000' },
                { token: 'comment.line', foreground: '008000' },
                { token: 'comment.block', foreground: '008000' },
                
                // 标识符
                { token: 'identifier', foreground: '001080' },
                { token: 'variable', foreground: '001080' },
                { token: 'parameter', foreground: '001080' },
                
                // 函数/方法
                { token: 'function', foreground: '795E26' },
                { token: 'method', foreground: '795E26' },
                
                // 运算符
                { token: 'operator', foreground: '000000' },
                { token: 'delimiter', foreground: '000000' },
                
                // 属性
                { token: 'property', foreground: '001080' },
                
                // 命名空间
                { token: 'namespace', foreground: '000000' },
                
                // 其他
                { token: 'tag', foreground: '0000FF' }
            ],
            colors: {
                'editor.foreground': '#1E1E1E',
                'editor.background': '#FFFFFF',
                'editor.selectionBackground': '#ADD6FF',
                'editor.lineHighlightBackground': '#F6F6F6',
                'editorCursor.foreground': '#000000',
                'editorWhitespace.foreground': '#B3B3B3'
            }
        });
    }

    toggleTheme() {
        this.currentTheme = this.currentTheme === 'vs-dark-custom' ? 'vs-light-custom' : 'vs-dark-custom';
        monaco.editor.setTheme(this.currentTheme);

        // 更新全局主题
        document.body.classList.toggle('theme-light', this.currentTheme === 'vs-light-custom');
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

function addDebuggerBreakpointStyles() {
    if (document.getElementById('debugger-breakpoint-style')) {
        return;
    }

    const style = document.createElement('style');
    style.id = 'debugger-breakpoint-style';
    style.textContent = `
        .monaco-editor .debugger-breakpoint-glyph {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 100%;
            height: 100%;
        }

        .monaco-editor .debugger-breakpoint-glyph::before {
            content: '';
            display: block;
            box-sizing: border-box;
            width: 13px;
            height: 13px;
            min-width: 13px;
            min-height: 13px;
            max-width: 13px;
            max-height: 13px;
            aspect-ratio: 1 / 1;
            margin-left: 15px;
            background: #e51400;
            border-radius: 999px;
            box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.35);
        }

        .monaco-editor .debugger-breakpoint-glyph:hover::before {
            box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.35), 0 0 6px rgba(229, 20, 0, 0.6);
        }

        .monaco-editor .debugger-breakpoint-ghost::before {
            background: rgba(229, 20, 0, 0.35);
            box-shadow: 0 0 0 1px rgba(229, 20, 0, 0.35), 0 0 6px rgba(229, 20, 0, 0.4);
        }

        .monaco-editor .debugger-breakpoint-line {
            background-color: rgba(229, 20, 0, 0.06);
        }

        body.theme-light .monaco-editor .debugger-breakpoint-line {
            background-color: rgba(229, 20, 0, 0.1);
        }

        .debugger-breakpoint-tooltip {
            position: fixed;
            z-index: 10000;
            padding: 4px 8px;
            border-radius: 4px;
            background: rgba(30, 30, 30, 0.95);
            color: #e6e6e6;
            font-size: 12px;
            line-height: 1.4;
            pointer-events: none;
            opacity: 0;
            transform: translateY(-2px);
            transition: opacity 0.12s ease, transform 0.12s ease;
            white-space: nowrap;
        }

        body.theme-light .debugger-breakpoint-tooltip {
            background: rgba(250, 250, 250, 0.98);
            color: #1e1e1e;
            border: 1px solid rgba(0, 0, 0, 0.1);
        }

        .debugger-breakpoint-tooltip.is-visible {
            opacity: 1;
            transform: translateY(0);
        }
    `;
    document.head.appendChild(style);
}
