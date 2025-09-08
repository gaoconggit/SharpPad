// Monaco Editor Code Actions Provider - 符合官方API标准的正确实现
import { sendRequest } from '../utils/apiService.js';
import { getCurrentFile } from '../utils/common.js';

export class CodeActionProvider {
    constructor(editor) {
        this.editor = editor;
        this.registerProvider();
        this.setupKeyBindings();
    }

    registerProvider() {
        // 注册 Code Actions Provider
        monaco.languages.registerCodeActionProvider('csharp', {
            provideCodeActions: async (model, range, context, token) => {
                try {
                    // 检查是否被取消
                    if (token.isCancellationRequested) {
                        return { actions: [], dispose: () => {} };
                    }

                    // 准备请求数据
                    const code = model.getValue();
                    const position = model.getOffsetAt(range.getStartPosition());
                    const selectionStart = model.getOffsetAt(range.getStartPosition());
                    const selectionEnd = model.getOffsetAt(range.getEndPosition());
                    const file = getCurrentFile();
                    const packages = file?.nugetConfig?.packages || [];

                    // 调用后端 API
                    const response = await sendRequest('codeActions', {
                        code: code,
                        position: position,
                        selectionStart: selectionStart,
                        selectionEnd: selectionEnd,
                        packages: packages.map(p => ({
                            Id: p.id,
                            Version: p.version
                        }))
                    });

                    if (!response.data || response.data.length === 0) {
                        return { actions: [], dispose: () => {} };
                    }

                    // 转换为 Monaco Editor 标准格式
                    const actions = response.data.map((action, index) => {
                        // 构建 WorkspaceEdit - 符合 Monaco Editor 标准
                        const workspaceEdit = {
                            edits: action.edits.map(edit => ({
                                resource: model.uri,
                                versionId: model.getVersionId(), // 关键：必须包含版本ID
                                textEdit: {
                                    range: new monaco.Range(
                                        edit.startLine,
                                        edit.startColumn,
                                        edit.endLine,
                                        edit.endColumn
                                    ),
                                    text: edit.newText
                                }
                            }))
                        };

                        // 标准 CodeAction 对象
                        const codeAction = {
                            title: action.title,
                            kind: this.mapActionKind(action.kind),
                            isPreferred: action.isPreferred || false,
                            diagnostics: context.markers || [], // 使用上下文中的标记
                            edit: workspaceEdit // 使用标准的 WorkspaceEdit 格式
                        };

                        return codeAction;
                    });

                    return {
                        actions: actions,
                        dispose: () => {}
                    };
                } catch (error) {
                    console.error('Code Actions Provider error:', error);
                    return { actions: [], dispose: () => {} };
                }
            }
        });
    }

    mapActionKind(kind) {
        // 使用标准的 Monaco Editor CodeActionKind
        switch (kind?.toLowerCase()) {
            case 'quickfix':
                return 'quickfix';
            case 'refactor':
                return 'refactor';
            case 'refactor.extract':
                return 'refactor.extract';
            case 'source':
                return 'source';
            case 'source.organizeimports':
                return 'source.organizeImports';
            default:
                return 'quickfix';
        }
    }

    setupKeyBindings() {
        // 注册 Quick Fix 快捷键
        this.editor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.Period,
            () => {
                this.triggerQuickFix();
            }
        );

        // 添加编辑器动作
        monaco.editor.addEditorAction({
            id: 'editor.action.quickFix',
            label: 'Quick Fix',
            alias: 'Quick Fix',
            precondition: null,
            keybindings: [
                monaco.KeyMod.CtrlCmd | monaco.KeyCode.Period,
            ],
            contextMenuGroupId: 'navigation',
            contextMenuOrder: 1.5,
            run: (editor) => {
                this.triggerQuickFix();
            }
        });
    }

    triggerQuickFix() {
        try {
            // 方法1: 使用标准的 Quick Fix 命令
            this.editor.trigger('keyboard', 'editor.action.quickFix', {});
        } catch (error) {
            // 备选方案: 使用 Code Action 命令
            try {
                this.editor.trigger('keyboard', 'editor.action.codeAction', {
                    type: 'auto'
                });
            } catch (fallbackError) {
                console.error('Quick Fix trigger failed:', fallbackError);
            }
        }
    }

}