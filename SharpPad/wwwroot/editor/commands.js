// 编辑器命令模块
export class EditorCommands {
    constructor(editor) {
        this.editor = editor;
    }

    registerCommands() {
        // 格式化文档 (Ctrl+K, Ctrl+D)
        this.editor.addCommand(
            monaco.KeyMod.chord(
                monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyK,
                monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyD
            ),
            () => {
                this.editor.getAction('editor.action.formatDocument').run();
            }
        );

        // 复制当前行到下一行 (Ctrl+D)
        this.editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyD,
            () => {
                const copyAction = this.editor.getAction('editor.action.copyLinesDownAction');
                if (copyAction) {
                    copyAction.run();
                }
            }
        );

        // 保存代码 (Ctrl+S)
        this.editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS,
            () => {
                window.fileManager.saveCode(this.editor.getValue());
            }
        );

        // 运行代码 (Ctrl+Enter)
        this.editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
            () => {
                const runButton = document.getElementById('runButton');
                if (runButton) {
                    runButton.click();
                }
            }
        );

        // 触发智能提示 (Ctrl+J)
        this.editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyJ,
            () => {
                this.editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
            }
        );

        // 清除输出 (Alt+C)
        this.editor.addCommand(
            monaco.KeyMod.Alt | monaco.KeyCode.KeyC,
            () => {
                document.getElementById('outputContent').innerHTML = '';
            }
        );

        // 保留全局快捷键处理
        window.addEventListener('keydown', (e) => {
            if (e.altKey && e.key.toLowerCase() === 'c') {
                e.preventDefault();
                document.getElementById('outputContent').innerHTML = '';
            }
        }, true);
    }
} 
