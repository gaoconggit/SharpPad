// AI Edit 模块 - 类似 Cursor/GitHub Copilot 的内联编辑功能
import { getSelectedModel } from '../utils/common.js';

export class AIEditManager {
    constructor(editor) {
        this.editor = editor;
        this.widget = document.getElementById('aiEditWidget');
        this.trigger = document.getElementById('aiEditTrigger');
        this.dialog = document.getElementById('aiEditDialog');
        this.input = document.getElementById('aiEditInput');
        this.submitBtn = document.getElementById('aiEditSubmit');
        this.cancelBtn = document.getElementById('aiEditCancel');
        this.closeBtn = document.getElementById('aiEditClose');
        this.loading = document.getElementById('aiEditLoading');

        this.currentSelection = null;
        this.isProcessing = false;

        this.initializeEventListeners();
    }

    initializeEventListeners() {
        // 监听编辑器选择变化
        this.editor.onDidChangeCursorSelection((e) => {
            const selection = this.editor.getSelection();
            if (!selection.isEmpty()) {
                this.showWidget(selection);
            } else {
                this.hideWidget();
            }
        });

        // 监听编辑器失去焦点
        this.editor.onDidBlurEditorText(() => {
            // 延迟隐藏，允许用户点击按钮
            setTimeout(() => {
                if (!this.dialog.classList.contains('show')) {
                    this.hideWidget();
                }
            }, 200);
        });

        // 触发按钮点击
        this.trigger.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.showDialog();
        });

        // 提交编辑
        this.submitBtn.addEventListener('click', () => this.handleSubmit());

        // 取消编辑
        this.cancelBtn.addEventListener('click', () => this.hideDialog());
        this.closeBtn.addEventListener('click', () => this.hideDialog());

        // Enter提交，Shift+Enter换行
        this.input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.handleSubmit();
            } else if (e.key === 'Escape') {
                e.preventDefault();
                this.hideDialog();
            }
        });

        // Ctrl+Shift+K 快捷键
        this.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyK, () => {
            const selection = this.editor.getSelection();
            if (!selection.isEmpty()) {
                this.currentSelection = selection;
                this.showDialog();
            }
        });

        // 点击对话框外部关闭
        document.addEventListener('click', (e) => {
            if (this.dialog.classList.contains('show') &&
                !this.dialog.contains(e.target) &&
                !this.trigger.contains(e.target)) {
                this.hideDialog();
            }
        });
    }

    showWidget(selection) {
        this.currentSelection = selection;

        // 获取选择区域的位置
        const position = this.editor.getScrolledVisiblePosition({
            lineNumber: selection.endLineNumber,
            column: selection.endColumn
        });

        if (position) {
            const editorDom = this.editor.getDomNode();
            const editorRect = editorDom.getBoundingClientRect();

            // 计算widget位置（选择区域右下角）
            this.widget.style.left = `${editorRect.left + position.left + 10}px`;
            this.widget.style.top = `${editorRect.top + position.top + 20}px`;
            this.widget.style.display = 'block';

            // 触发动画
            setTimeout(() => this.widget.classList.add('show'), 10);
        }
    }

    hideWidget() {
        this.widget.classList.remove('show');
        setTimeout(() => {
            this.widget.style.display = 'none';
        }, 200);
    }

    showDialog() {
        if (!this.currentSelection) return;

        this.hideWidget();

        // 获取选择区域的代码
        const selectedCode = this.editor.getModel().getValueInRange(this.currentSelection);

        // 计算对话框位置（选择区域下方）
        const position = this.editor.getScrolledVisiblePosition({
            lineNumber: this.currentSelection.endLineNumber,
            column: this.currentSelection.endColumn
        });

        if (position) {
            const editorDom = this.editor.getDomNode();
            const editorRect = editorDom.getBoundingClientRect();

            // 计算dialog位置
            let dialogLeft = editorRect.left + position.left;
            let dialogTop = editorRect.top + position.top + 30;

            // 确保对话框不超出视口
            const dialogWidth = 400;
            const dialogHeight = 200;

            if (dialogLeft + dialogWidth > window.innerWidth) {
                dialogLeft = window.innerWidth - dialogWidth - 20;
            }
            if (dialogLeft < 20) {
                dialogLeft = 20;
            }

            if (dialogTop + dialogHeight > window.innerHeight) {
                dialogTop = editorRect.top + position.top - dialogHeight - 10;
            }

            this.dialog.style.left = `${dialogLeft}px`;
            this.dialog.style.top = `${dialogTop}px`;
            this.dialog.style.display = 'block';

            // 触发动画
            setTimeout(() => {
                this.dialog.classList.add('show');
                this.input.focus();
            }, 10);
        }
    }

    hideDialog() {
        this.dialog.classList.remove('show');
        setTimeout(() => {
            this.dialog.style.display = 'none';
            this.input.value = '';
            this.loading.style.display = 'none';
        }, 200);
    }

    async handleSubmit() {
        const instruction = this.input.value.trim();
        if (!instruction || this.isProcessing) return;

        const selectedCode = this.editor.getModel().getValueInRange(this.currentSelection);

        try {
            this.isProcessing = true;
            this.submitBtn.disabled = true;
            this.loading.style.display = 'flex';

            // 调用AI API编辑代码
            const editedCode = await this.editCodeWithAI(selectedCode, instruction);

            // 应用编辑
            this.applyEdit(editedCode);

            // 隐藏对话框
            this.hideDialog();

        } catch (error) {
            console.error('AI编辑失败:', error);
            alert('AI编辑失败: ' + error.message);
        } finally {
            this.isProcessing = false;
            this.submitBtn.disabled = false;
            this.loading.style.display = 'none';
        }
    }

    async editCodeWithAI(code, instruction) {
        const selectedModel = getSelectedModel();

        if (!selectedModel) {
            throw new Error('请先配置AI模型');
        }

        // 构建请求消息
        const messages = [
            {
                role: 'system',
                content: `你是一个代码编辑助手。用户会给你一段代码和编辑指令，你需要按照指令修改代码。
重要规则：
1. 只返回修改后的代码，不要有任何解释、注释或额外文字
2. 保持代码的缩进和格式风格
3. 只修改需要改的部分，其他保持不变
4. 如果指令不清楚，做最合理的修改`
            },
            {
                role: 'user',
                content: `请按照以下指令修改代码：

指令：${instruction}

原代码：
\`\`\`csharp
${code}
\`\`\`

请直接返回修改后的代码，不要有任何解释或格式标记。`
            }
        ];

        // 构建请求参数
        const requestBody = {
            model: selectedModel.name,
            messages: messages,
            stream: false,
            temperature: 0.3  // 降低温度以获得更确定性的输出
        };

        // 构建请求头
        const headers = {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${selectedModel.apiKey}`
        };

        // 确定请求端点
        let endpoint = selectedModel.endpoint;
        if (selectedModel.useBackend) {
            headers['x-endpoint'] = selectedModel.endpoint;
            endpoint = './v1/chat/completions';
        }

        const response = await fetch(endpoint, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(requestBody)
        });

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const data = await response.json();
        let editedCode = data.choices[0].message.content.trim();

        // 清理可能的markdown代码块标记
        editedCode = editedCode.replace(/^```[\w]*\n?/gm, '').replace(/\n?```$/gm, '');

        return editedCode;
    }

    applyEdit(editedCode) {
        if (!this.currentSelection) return;

        // 使用Monaco编辑器的编辑操作
        this.editor.executeEdits('ai-edit', [{
            range: this.currentSelection,
            text: editedCode,
            forceMoveMarkers: true
        }]);

        // 选中新插入的代码
        const lineCount = editedCode.split('\n').length;
        const lastLineLength = editedCode.split('\n').pop().length;

        this.editor.setSelection(new monaco.Range(
            this.currentSelection.startLineNumber,
            this.currentSelection.startColumn,
            this.currentSelection.startLineNumber + lineCount - 1,
            lineCount === 1 ? this.currentSelection.startColumn + lastLineLength : lastLineLength + 1
        ));

        // 聚焦编辑器
        this.editor.focus();
    }
}

// 初始化AI Edit功能
function initializeAIEdit() {
    // 等待编辑器初始化完成
    const checkEditor = setInterval(() => {
        if (window.editor) {
            clearInterval(checkEditor);

            // 创建AI Edit管理器
            const aiEditManager = new AIEditManager(window.editor);

            // 将管理器暴露到全局以便调试
            window.aiEditManager = aiEditManager;

            console.log('AI Edit feature initialized');
        }
    }, 100);

    // 超时保护
    setTimeout(() => {
        clearInterval(checkEditor);
    }, 10000);
}

// 当DOM加载完成后初始化
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeAIEdit);
} else {
    initializeAIEdit();
}
